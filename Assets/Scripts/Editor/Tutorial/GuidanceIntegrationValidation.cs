#if UNITY_EDITOR
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using UnityEditor;
using UnityEngine;

/// <summary>실제 세이브·서버를 변경하지 않는 안내 저장 계약 회귀 검사.</summary>
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
            AdventureFlowVersion = 2, AdventureUnlocked = true,
            CompletedTriggers = new System.Collections.Generic.List<string> { "KeywordGrowthFirstOpen", "AdventureMapFirstOpen" },
            SynergyIntroduction = new SynergyIntroductionSaveData { DeckSlot = 2, SynergyId = "Caretaker" },
        };
        string json = JsonConvert.SerializeObject(original, settings);
        var fields = JObject.Parse(json);
        Require((int)fields["synergyIntroduction"]["deckSlot"] == 2, "Synergy target wire key missing.");
        Require(fields["completedTriggers"] is JArray t_triggers && t_triggers.Count == 2, "Completed trigger wire key missing.");
        var restored = JsonConvert.DeserializeObject<TutorialSaveData>(json, settings);
        Require(restored.AdventureFlowVersion == 2 && restored.AdventureUnlocked
            && restored.CompletedTriggers.Contains("AdventureMapFirstOpen")
            && restored.SynergyIntroduction.SynergyId == "Caretaker",
            "Guidance state did not survive snapshot round trip.");
        var legacy = JsonConvert.DeserializeObject<TutorialSaveData>("{\"outgameCompleted\":true}", settings);
        Require(legacy.SynergyIntroduction != null && legacy.SynergyIntroduction.DeckSlot == -1,
            "Missing legacy fields must retain migration defaults.");

        Debug.Log("[GuidanceIntegrationValidation] PASS: save round trip, legacy defaults.");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
