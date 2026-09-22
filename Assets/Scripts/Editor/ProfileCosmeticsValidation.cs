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

/// <summary>실제 프로필 프리팹과 CSV를 외부 저장 없이 검증한다.</summary>
public static class ProfileCosmeticsValidation
{
    const BindingFlags INSTANCE_FIELDS = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/검증/프로필 꾸미기 회귀")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Run outside Play Mode.");
        ValidateCsv();
        ValidateProfileList();
        Debug.Log("[ProfileCosmeticsValidation] PASS: CSV validation, real prefab ownership refresh, draft preservation, reopen and save restoration.");
    }

    static void ValidateCsv()
    {
        string t_csv = File.ReadAllText("docs/SpecData/CosmeticItem_sheet.csv");
        Require(SpecFirestoreUploader.ListTables(out string t_listError).Contains("CosmeticItem"), t_listError ?? "CosmeticItem missing from uploader.");
        Require(SpecFirestoreUploader.TryParseAccountCsv("CosmeticItem", t_csv, out IList t_rows, out string t_error), t_error);
        Require(t_rows.Count == 31, "Expected approved 31 cosmetics.");
        foreach (string t_invalid in new[]
        {
            t_csv.Replace("1,Avatar,avatar_00,1", "1,Avatar,avatar_00,2"),
            t_csv.Replace("2,Avatar,avatar_01,1", "2,Avatar,avatar_00,1"),
            t_csv.Replace("11,Emote,1,1", "11,Emote,2147483648,1"),
            t_csv.Replace("1,Avatar,avatar_00,1", "1,Title,avatar_00,1"),
        })
            Require(!SpecFirestoreUploader.TryParseAccountCsv("CosmeticItem", t_invalid, out _, out _), "Invalid CSV accepted.");
    }

    static void ValidateProfileList()
    {
        ProfileSaveData t_original = DataSaveManager.Data.Profile;
        ProfileConfig t_config = ProfileManager.Config;
        EmoteCatalog t_emotes = ProfileManager.EmoteCatalog;
        Scene t_scene = EditorSceneManager.NewPreviewScene();
        ProfileEditPanel t_panel = null;
        Action t_refresh = null;
        int t_saves = 0;
        void OnSaved(ESaveUploadTiming _) => t_saves++;
        try
        {
            ProfileManager.SetConfig(AssetDatabase.LoadAssetAtPath<ProfileConfig>("Assets/SO/Profile/ProfileConfig.asset"));
            ProfileManager.SetEmoteCatalog(AssetDatabase.LoadAssetAtPath<EmoteCatalog>("Assets/SO/EmoteCatalog.asset"));
            var t_profile = new ProfileSaveData
            {
                Nickname = "saved", AvatarId = "avatar_00", FrameId = "frame_default",
                EmoteIds = new List<int> { 1, 0, 0, 0, 0, 0 },
                OwnedAvatarIds = new List<string> { "avatar_00", "avatar_02" },
                OwnedFrameIds = new List<string> { "frame_default" }, OwnedEmoteIds = new List<int> { 1 },
            };
            DataSaveManager.Data.Profile = t_profile;
            ProfileManager.Init();
            DataSaveManager.OnSaved += OnSaved;
            GameObject t_prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/UI/PooledUI/ProfileEditPanel.prefab");
            GameObject t_root = UnityEngine.Object.Instantiate(t_prefab);
            SceneManager.MoveGameObjectToScene(t_root, t_scene);
            t_root.SetActive(false);
            t_panel = t_root.GetComponent<ProfileEditPanel>();
            Require(t_panel != null, "Missing profile panel component.");
            foreach (string t_field in new[] { "avatarContent", "frameContent", "emoteContent", "equippedEmoteContent" })
            {
                Transform t_content = Get<Transform>(t_panel, t_field);
                Require(t_content != null, "Missing content: " + t_field);
                var t_template = t_field.StartsWith("avatar") || t_field.StartsWith("frame")
                    ? (Component)Get<ProfileItemCell>(t_panel, "cellPrefab") : Get<EmoteItemCell>(t_panel, "emoteCellPrefab");
                for (int t_i = t_content.childCount - 1; t_i >= 0; t_i--)
                {
                    GameObject t_child = t_content.GetChild(t_i).gameObject;
                    if (t_template != null && t_child == t_template.gameObject) t_child.SetActive(false);
                    else UnityEngine.Object.DestroyImmediate(t_child);
                }
            }
            Set(t_panel, "m_built", true);
            Set(t_panel, "m_sessionOpen", true);
            Set(t_panel, "m_currentTab", 2);
            Set(t_panel, "m_draftNickname", "편집중");
            Set(t_panel, "m_draftAvatarId", "avatar_02");
            Set(t_panel, "m_draftFrameId", "frame_default");
            Set(t_panel, "m_pendingEmoteId", 1);
            Get<List<int>>(t_panel, "m_draftEmoteIds").AddRange(t_profile.EmoteIds);
            Call(t_panel, "Build");
            var t_before = Get<List<ProfileItemCell>>(t_panel, "m_avatarCells")[0];
            Require(Get<IList>(t_panel, "m_avatarCells").Count == 2, "Unowned avatar was shown.");
            Require(Get<IList>(t_panel, "m_emoteCells").Count == 1, "Unowned emote was shown.");
            Require(Get<IList>(t_panel, "m_equippedEmoteCells").Count == 6, "Equipment slots changed.");
            var t_scroll = Get<ScrollRect>(t_panel, "avatarScroll");
            Vector2 t_scrollPosition = new Vector2(0, 20);
            t_scroll.content.anchoredPosition = t_scrollPosition;
            t_refresh = () => Call(t_panel, "OnOwnershipChanged");
            ProfileManager.OnOwnershipChanged += t_refresh;
            t_profile.OwnedAvatarIds.Add("avatar_01");
            t_profile.OwnedFrameIds.Add("frame_Sun");
            t_profile.OwnedEmoteIds.Add(2);
            ProfileManager.NotifyOwnershipRehydrated();
            ProfileManager.NotifyOwnershipRehydrated();
            var t_cells = Get<List<ProfileItemCell>>(t_panel, "m_avatarCells");
            Require(t_cells.Count == 3 && t_cells[0] == t_before, "Cells were duplicated or rebuilt.");
            Require(t_cells.Find(t_cell => t_cell.Id == "avatar_01").transform.GetSiblingIndex() == 1, "Catalog order changed.");
            Require(Get<IList>(t_panel, "m_frameCells").Count == 2 && Get<IList>(t_panel, "m_emoteCells").Count == 2, "Grant did not add cells.");
            Require(Get<string>(t_panel, "m_draftNickname") == "편집중" && Get<string>(t_panel, "m_draftAvatarId") == "avatar_02", "Draft overwritten.");
            Require(Get<int>(t_panel, "m_currentTab") == 2 && Get<int>(t_panel, "m_pendingEmoteId") == 1, "Tab or pick overwritten.");
            Require(t_scroll.content.anchoredPosition == t_scrollPosition, "Scroll moved.");
            Require(ProfileManager.AvatarId == "avatar_00" && t_saves == 0, "Grant equipped or saved.");
            var t_line = new RewardLine(new GrantedCosmetic { ItemType = "Frame", ItemId = "frame_Sun", IsNew = true });
            Require(t_line.Icon != null && t_line.Type == ERewardType.Frame && t_line.IsNewItem == true, "Cosmetic result lost art or state.");
            var t_missingLine = new RewardLine(new GrantedCosmetic { ItemType = "Avatar", ItemId = "future", IsNew = false });
            Require(t_missingLine.Icon == null && t_missingLine.RewardId == "future", "Unknown cosmetic result lost.");
            Call(t_panel, "Build");
            Require(t_cells.Count == 3, "Reopen duplicated cells.");
            DataSaveManager.Data.Profile = JsonConvert.DeserializeObject<ProfileSaveData>(JsonConvert.SerializeObject(t_profile));
            ProfileManager.Init();
            Require(ProfileManager.IsAvatarOwned("avatar_01") && ProfileManager.IsEmoteOwned(2), "Ownership did not survive save restoration.");
        }
        finally
        {
            DataSaveManager.OnSaved -= OnSaved;
            if (t_refresh != null) ProfileManager.OnOwnershipChanged -= t_refresh;
            if (t_panel != null) Set(t_panel, "m_sessionOpen", false);
            EditorSceneManager.ClosePreviewScene(t_scene);
            DataSaveManager.Data.Profile = t_original;
            ProfileManager.SetConfig(t_config);
            ProfileManager.SetEmoteCatalog(t_emotes);
            ProfileManager.Init();
        }
    }

    static T Get<T>(object _owner, string _name) => (T)_owner.GetType().GetField(_name, INSTANCE_FIELDS).GetValue(_owner);
    static void Set(object _owner, string _name, object _value) => _owner.GetType().GetField(_name, INSTANCE_FIELDS).SetValue(_owner, _value);
    static void Call(object _owner, string _name) => _owner.GetType().GetMethod(_name, INSTANCE_FIELDS).Invoke(_owner, null);
    static void Require(bool _condition, string _message)
    {
        if (!_condition) throw new InvalidOperationException(_message);
    }
}
