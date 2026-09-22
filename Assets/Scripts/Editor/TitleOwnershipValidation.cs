using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>칭호 CSV와 실제 선택 화면을 저장·통신 없이 검증한다.</summary>
public static class TitleOwnershipValidation
{
    const BindingFlags FIELDS = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/검증/칭호 서버 소유 회귀")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside Play Mode.");
        string t_csv = File.ReadAllText("docs/SpecData/Title_sheet.csv");
        Require(SpecFirestoreUploader.ListTables(out string t_error).Contains("Title"), t_error ?? "Missing Title table.");
        Require(SpecFirestoreUploader.TryParseAccountCsv("Title", t_csv, out IList t_rows, out t_error), t_error);
        Require(t_rows.Count == 8, "Expected eight test titles.");
        Require(!SpecFirestoreUploader.TryParseAccountCsv("Title", t_csv.Replace("2,test_blue_traveler", "2,test_first_step"), out _, out _), "Duplicate title accepted.");
        Require(!SpecFirestoreUploader.TryParseAccountCsv("Title", t_csv.Replace(
            "1,test_first_step", "0,test_first_step"), out _, out _), "Zero title ID accepted.");
        Require(!SpecFirestoreUploader.TryParseAccountCsv("Title", t_csv.Replace(
            "1,test_first_step", "1, test_first_step"), out _, out _), "Title ID whitespace accepted.");
        Require(!SpecFirestoreUploader.TryParseAccountCsv("Title",
            "id,titleId,eventKey\nint,string,string\n1,test_first_step,WinBattle", out _, out _),
            "Legacy title condition column accepted.");
        Require(SpecFirestoreUploader.TryParseAccountCsv("Achievement",
            File.ReadAllText("docs/SpecData/Achievement_sheet.csv"), out IList t_achievements, out t_error), t_error);
        Require(t_achievements.Count > 0 && t_achievements[0].GetType().GetField("rewardCurrency") == null &&
            t_achievements[0].GetType().GetField("rewardAmount") == null, "Achievement still carries inline rewards.");
        Require(SpecFirestoreUploader.TryParseAccountCsv("Reward",
            File.ReadAllText("docs/SpecData/Reward_sheet.csv"), out IList t_rewards, out t_error), t_error);
        bool t_hasTitleReward = false;
        foreach (Reward t_reward in t_rewards)
            if (t_reward.ownerType == "Achievement" && t_reward.rewardType == "Title" && t_reward.amount == 1)
                t_hasTitleReward = true;
        Require(t_hasTitleReward && ServerOwnedRewardOwners.Contains("Achievement"), "Achievement Reward rows missing or read locally.");
        ValidateView();
        Debug.Log("[TitleOwnershipValidation] PASS: CSV, all locked entries, preview, server ownership refresh, draft/scroll preservation, restore and reward DTO.");
    }

    static void ValidateView()
    {
        ProfileSaveData t_original = DataSaveManager.Data.Profile;
        TitleCatalog t_catalog = TitleManager.Catalog;
        Scene t_scene = EditorSceneManager.NewPreviewScene();
        ProfileTitleTab t_tab = null;
        ProfileEditPanel t_panel = null;
        int t_saves = 0;
        void OnSaved(ESaveUploadTiming _) => t_saves++;
        try
        {
            DataSaveManager.Data.Profile = new ProfileSaveData { Nickname = "keep", EquippedTitleId = "test_first_step",
                OwnedTitleIds = new List<string> { "test_first_step", "future_title" } };
            TitleManager.SetCatalog(AssetDatabase.LoadAssetAtPath<TitleCatalog>("Assets/SO/Profile/TitleCatalog.asset"));
            TitleManager.NotifyRehydrated();
            DataSaveManager.OnSaved += OnSaved;
            GameObject t_root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Assets/Prefabs/UI/PooledUI/Profile/ProfileEditPanel.prefab"));
            SceneManager.MoveGameObjectToScene(t_root, t_scene);
            t_root.SetActive(false);
            t_panel = t_root.GetComponent<ProfileEditPanel>();
            t_tab = t_root.GetComponentInChildren<ProfileTitleTab>(true);
            Require(t_tab != null, "Missing title tab.");
            t_tab.BeginEdit(null);
            t_tab.Show();
            var t_cells = Get<List<TitleItemCell>>(t_tab, "m_cells");
            Require(t_cells.Count == 8, "Locked titles must remain visible.");
            TitleItemCell t_locked = t_cells.Find(t => t.Id == "test_blue_traveler");
            Require(Get<GameObject>(t_locked, "lockedMark").activeSelf, "Unowned title not locked.");
            typeof(ProfileTitleTab).GetMethod("Select", FIELDS).Invoke(t_tab, new object[] { "test_blue_traveler" });
            Require(!t_tab.IsDirty && Get<string>(t_tab, "m_selectedId") == "test_blue_traveler", "Locked preview changed equipment.");
            var t_scroll = Get<ScrollRect>(t_tab, "scrollRect");
            Vector2 t_position = new Vector2(0, 21);
            t_scroll.content.anchoredPosition = t_position;
            var t_remote = JsonConvert.DeserializeObject<ProfileSaveData>(JsonConvert.SerializeObject(DataSaveManager.Data.Profile));
            t_remote.OwnedTitleIds.Add("test_blue_traveler");
            DataSaveManager.Data.Profile = t_remote;
            TitleManager.NotifyRehydrated();
            Require(!Get<GameObject>(t_locked, "lockedMark").activeSelf, "Visible title did not unlock.");
            Require(t_cells.Count == 8 && t_scroll.content.anchoredPosition == t_position, "Grant rebuilt or moved list.");
            Require(Get<string>(t_tab, "m_draftTitleId") == "test_first_step" && Get<string>(t_tab, "m_selectedId") == "test_blue_traveler", "Grant reverted draft or preview.");
            Require(TitleManager.IsOwned("future_title") && !TitleManager.CanEquip("future_title"), "Unknown title ownership lost.");
            typeof(ProfileTitleTab).GetMethod("Select", FIELDS).Invoke(t_tab, new object[] { "test_blue_traveler" });
            Require(t_tab.IsDirty && TitleManager.EquippedId == "test_first_step", "Selection saved before commit.");
            t_tab.Hide();
            t_tab.Show();
            Require(Get<string>(t_tab, "m_draftTitleId") == "test_blue_traveler", "Tab switch lost draft.");
            Assembly t_runtime = typeof(TitleManager).Assembly;
            Type t_resultType = t_runtime.GetType("ServerCommandResult", true);
            object t_result = JsonConvert.DeserializeObject("{\"titles\":[{\"titleId\":\"test_blue_traveler\",\"isNew\":true}]}", t_resultType);
            var t_titles = (List<GrantedTitle>)t_resultType.GetProperty("Titles").GetValue(t_result);
            Type t_display = t_runtime.GetType("RewardItemDisplay", true);
            var t_outcome = (RewardClaimOutcome)t_display.GetMethod("ToOutcome", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { Array.Empty<CurrencyGain>(), null, null, null, t_titles });
            var t_line = new RewardLine(t_outcome.Titles[0]);
            Require(t_outcome.HasItems && t_line.Type == ERewardType.Title && t_line.Icon != null && t_line.IsNewItem == true, "Title reward result lost.");
            Require((string)t_display.GetMethod("NameOf", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { "Title", t_line.RewardId }) == "푸른 여행자", "Title reward name missing.");
            Require(t_saves == 0, "Ownership refresh or preview triggered a save.");
        }
        finally
        {
            DataSaveManager.OnSaved -= OnSaved;
            if (t_tab != null) t_tab.Hide();
            if (t_panel != null) typeof(ProfileEditPanel).GetField("m_sessionOpen", FIELDS).SetValue(t_panel, false);
            EditorSceneManager.ClosePreviewScene(t_scene);
            DataSaveManager.Data.Profile = t_original;
            TitleManager.SetCatalog(t_catalog);
            TitleManager.NotifyRehydrated();
        }
    }

    static T Get<T>(object _owner, string _name) => (T)_owner.GetType().GetField(_name, FIELDS).GetValue(_owner);
    static void Require(bool _condition, string _message)
    {
        if (!_condition) throw new InvalidOperationException(_message);
    }
}
