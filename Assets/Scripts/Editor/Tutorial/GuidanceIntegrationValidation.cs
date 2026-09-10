#if UNITY_EDITOR
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEditor;
using UnityEngine;

/// <summary>실제 세이브·서버를 변경하지 않는 안내 저장 및 팝업 계약 회귀 검사.</summary>
public static class GuidanceIntegrationValidation
{
    [MenuItem("Tools/Tutorial/Validate Guidance Integration")]
    public static void Run()
    {
        Require(!EditorApplication.isPlaying, "Stop play mode before isolated validation.");
        var sequence = AssetDatabase.LoadAssetAtPath<OutgameTutorialData>(
            "Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset");
        var issues = TutorialValidator.Validate(sequence);
        int errors = 0;
        foreach (var issue in issues)
            if (issue.Level == ETutorialIssueLevel.Error)
            {
                errors++;
                Debug.LogError($"[GuidanceIntegrationValidation] {issue.Coord}: {issue.Message}");
            }
        Require(errors == 0, "Tutorial authoring has blocking errors.");
        var settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };
        var original = new TutorialSaveData
        {
            ChapterIndex = 2, ChapterStepIndex = 12, StepId = 38,
            AdventureFlowVersion = 1, AdventureUnlocked = true, AdventureIntroStarted = true,
            AdventureIntroStepId = 37, AdventureIntroDeferred = true,
            GuideNotices = new GuideNoticeSaveData { Version = 1, Guide1Pending = true, Guide2Shown = true },
            SynergyIntroduction = new SynergyIntroductionSaveData { DeckSlot = 2, SynergyId = "Caretaker" },
        };
        string json = JsonConvert.SerializeObject(original, settings);
        var fields = JObject.Parse(json);
        Require((bool)fields["guideNotices"]["guide1Pending"], "Guide pending wire key missing.");
        Require((int)fields["synergyIntroduction"]["deckSlot"] == 2, "Synergy target wire key missing.");
        var restored = JsonConvert.DeserializeObject<TutorialSaveData>(json, settings);
        Require(restored.GuideNotices.Guide1Pending && restored.GuideNotices.Guide2Shown
            && restored.AdventureIntroStepId == 37 && restored.AdventureIntroDeferred
            && restored.SynergyIntroduction.SynergyId == "Caretaker",
            "Guidance state did not survive snapshot round trip.");
        var legacy = JsonConvert.DeserializeObject<TutorialSaveData>("{\"outgameCompleted\":true}", settings);
        Require(legacy.GuideNotices != null && legacy.GuideNotices.Version == 0
            && legacy.SynergyIntroduction != null && legacy.SynergyIntroduction.DeckSlot == -1,
            "Missing legacy fields must retain migration defaults.");

        GameObject contents = PrefabUtility.LoadPrefabContents(
            "Assets/Assets/Prefabs/UI/PooledUI/GuideMissionNoticePopup.prefab");
        try
        {
            var popup = contents.GetComponent<GuideMissionNoticePopup>();
            Require(popup != null, "Guide notice component missing.");
            int completed = 0;
            popup.Initialization(new GuideMissionNoticeData { MissionId = "guide.01", OnCompleted = () => completed++ });
            popup.isShow = true;
            popup.Yield();
            Require(!popup.isShow && completed == 0, "Yield must preserve pending notice.");
            popup.Close();
            Require(completed == 0, "Hidden notice must not complete.");
            popup.isShow = true;
            popup.Close();
            popup.Close();
            Require(completed == 1, "User close must complete exactly once.");
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        Debug.Log("[GuidanceIntegrationValidation] PASS: save round trip, legacy defaults, popup yield/close once.");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
