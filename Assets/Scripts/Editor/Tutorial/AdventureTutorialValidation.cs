#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>Read-only validation of real authored data and pure progression decisions.</summary>
public static class AdventureTutorialValidation
{
    [MenuItem("Tools/Tutorial/Validate Adventure Progression")]
    public static void Run()
    {
        var data = AssetDatabase.LoadAssetAtPath<OutgameTutorialData>(
            "Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset");
        Require(data != null && data.adventureIntroduction != null, "Adventure chapter is missing");
        var chapter = data.adventureIntroduction;
        int oldChapter = data.chapters.Count;

        foreach (int id in new[] { 34, 35, 44 })
        {
            var slot = new TutorialSaveData { StepId = id, ChapterIndex = oldChapter };
            Require(AdventureTutorialRunner.MigrateLegacyProgress(slot, chapter, oldChapter, false), "Migration not applied");
            Require(slot.OutgameCompleted && slot.AdventureUnlocked && slot.AdventureIntroStarted
                && !slot.AdventureIntroCompleted, "Legacy active flow was lost");
            Require(slot.AdventureIntroStepId == (id == 34 ? 35 : id), "Legacy step ID not preserved");
            Require(!AdventureTutorialRunner.MigrateLegacyProgress(slot, chapter, oldChapter, false), "Migration repeated");
        }
        var coordinate = new TutorialSaveData { ChapterIndex = oldChapter, ChapterStepIndex = 4 };
        AdventureTutorialRunner.MigrateLegacyProgress(coordinate, chapter, oldChapter, false);
        Require(coordinate.AdventureIntroStepId == 44, "ID-less legacy coordinate drifted");
        foreach (int id in new[] { 37, 44, 17, 18, 40, 22 })
            Require(AdventureTutorialRunner.ResumeStepId(chapter, id) == 36, "Restart waits for an unopened surface");
        foreach (int id in new[] { 35, 36 })
            Require(AdventureTutorialRunner.ResumeStepId(chapter, id) == id, "Restart rewound visible lobby instruction");

        var completed = new TutorialSaveData { OutgameCompleted = true };
        AdventureTutorialRunner.MigrateLegacyProgress(completed, chapter, oldChapter, false);
        Require(completed.AdventureUnlocked && completed.AdventureIntroCompleted, "Legacy access revoked");
        var played = new TutorialSaveData();
        AdventureTutorialRunner.MigrateLegacyProgress(played, chapter, oldChapter, true);
        Require(played.AdventureUnlocked && played.AdventureIntroCompleted, "Existing adventure progress ignored");
        var fresh = new TutorialSaveData();
        AdventureTutorialRunner.MigrateLegacyProgress(fresh, chapter, oldChapter, false);
        Require(!fresh.AdventureUnlocked && !fresh.OutgameCompleted && fresh.AdventureFlowVersion == 1,
            "Fresh player was grandfathered");
        var absent = new TutorialSaveData();
        Require(!AdventureTutorialRunner.MigrateLegacyProgress(absent, null, 0, false)
            && !absent.AdventureUnlocked, "Missing data must not unlock fresh accounts");

        var config = ScriptableObject.CreateInstance<RankConfig>();
        try
        {
            RankTier threshold = RankTier.None;
            for (int i = 0; config.TryGetTier(i, out var tier); i++)
                if (tier.Grade == data.adventureUnlockGrade && tier.Division == data.adventureUnlockDivision) threshold = tier;
            Require(threshold.Division >= 1, "Configured adventure unlock rank not authored");
            int below = config.ResolveTierIndex(threshold.RequiredPoints - 1);
            int at = config.ResolveTierIndex(threshold.RequiredPoints);
            Require(!AdventureTutorialRunner.MeetsUnlockTier(true, below, threshold), "Rank below threshold unlocked");
            Require(AdventureTutorialRunner.MeetsUnlockTier(true, at, threshold), "Configured rank locked");
            Require(AdventureTutorialRunner.MeetsUnlockTier(true, at + 1, threshold), "Rank above threshold locked");
            Require(!AdventureTutorialRunner.MeetsUnlockTier(true, at, RankTier.None), "Missing threshold unlocked");
            Require(!AdventureTutorialRunner.MeetsUnlockTier(false, at, threshold), "Unranked player unlocked");
        }
        finally { UnityEngine.Object.DestroyImmediate(config); }
        Debug.Log($"[AdventureTutorialValidation] PASS: migration, idempotency, missing data, {data.adventureUnlockGrade} {data.adventureUnlockDivision} threshold.");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
