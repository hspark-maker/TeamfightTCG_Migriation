#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>실제 저작 데이터와 순수 진행 판정만 읽는 검사 — 라이브 세이브·서버는 건드리지 않는다.</summary>
public static class AdventureTutorialValidation
{
    const string ADVENTURE_KEY = "AdventureMapFirstOpen";

    [MenuItem("Tools/Tutorial/Validate Adventure Progression")]
    public static void Run()
    {
        var data = AssetDatabase.LoadAssetAtPath<OutgameTutorialData>(
            "Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset");
        Require(data != null, "Tutorial data is missing");

        OutgameTutorialChapter chapter = null;
        for (int i = 0; i < data.chapters.Count; i++)
            if (data.chapters[i] != null && data.chapters[i].Trigger == EOutgameTutorialTrigger.AdventureMapFirstOpen) chapter = data.chapters[i];
        Require(chapter != null && chapter.IsGuided, "Adventure introduction chapter (guided, AdventureMapFirstOpen) is missing");
        const int oldChapter = 5;

        // v0 — 옛 온보딩의 모험 도입 챕터 도중이던 세이브: 해금은 되찾되 낙인은 없다(자율 챕터가 다시 부른다).
        foreach (int id in new[] { 34, 35, 44 })
        {
            var slot = new TutorialSaveData { StepId = id, ChapterIndex = oldChapter };
            Require(AdventureUnlock.MigrateLegacyProgress(slot, chapter, false), "Migration not applied");
            Require(slot.OutgameCompleted && slot.AdventureUnlocked, "Legacy active flow lost its unlock");
            Require(!slot.CompletedTriggers.Contains(ADVENTURE_KEY), "Unfinished legacy intro was marked complete");
            Require(slot.AdventureFlowVersion == 2, "Flow version not bumped");
            Require(!AdventureUnlock.MigrateLegacyProgress(slot, chapter, false), "Migration repeated");
        }
        var coordinate = new TutorialSaveData { ChapterIndex = oldChapter, ChapterStepIndex = 4 };
        AdventureUnlock.MigrateLegacyProgress(coordinate, chapter, false);
        Require(coordinate.AdventureUnlocked && !coordinate.CompletedTriggers.Contains(ADVENTURE_KEY), "ID-less legacy coordinate drifted");

        // v0 — 졸업했거나 정점을 이미 치른 계정은 도입을 완주한 것으로 본다.
        var completed = new TutorialSaveData { OutgameCompleted = true };
        AdventureUnlock.MigrateLegacyProgress(completed, chapter, false);
        Require(completed.AdventureUnlocked && completed.CompletedTriggers.Contains(ADVENTURE_KEY), "Legacy access revoked");
        var played = new TutorialSaveData();
        AdventureUnlock.MigrateLegacyProgress(played, chapter, true);
        Require(played.AdventureUnlocked && played.CompletedTriggers.Contains(ADVENTURE_KEY), "Existing adventure progress ignored");

        // v0 — 신규 계정은 아무것도 승계하지 않는다.
        var fresh = new TutorialSaveData();
        AdventureUnlock.MigrateLegacyProgress(fresh, chapter, false);
        Require(!fresh.AdventureUnlocked && !fresh.OutgameCompleted && fresh.AdventureFlowVersion == 2
                && !fresh.CompletedTriggers.Contains(ADVENTURE_KEY), "Fresh player was grandfathered");
        var absent = new TutorialSaveData();
        Require(!AdventureUnlock.MigrateLegacyProgress(absent, null, false) && !absent.AdventureUnlocked,
                "Missing data must not unlock fresh accounts");

        // v1 — 별도 플래그였던 도입 완주가 낙인으로 옮겨진다.
        var v1Done = new TutorialSaveData { AdventureFlowVersion = 1, AdventureUnlocked = true, AdventureIntroCompleted = true };
        Require(AdventureUnlock.MigrateLegacyProgress(v1Done, chapter, false), "v1 migration not applied");
        Require(v1Done.CompletedTriggers.Contains(ADVENTURE_KEY) && v1Done.AdventureFlowVersion == 2, "v1 completion mark lost");
        var v1Open = new TutorialSaveData { AdventureFlowVersion = 1, AdventureUnlocked = true };
        Require(AdventureUnlock.MigrateLegacyProgress(v1Open, chapter, false), "v1 migration not applied");
        Require(!v1Open.CompletedTriggers.Contains(ADVENTURE_KEY) && v1Open.AdventureUnlocked, "v1 unfinished intro was marked complete");
        Require(!AdventureUnlock.MigrateLegacyProgress(v1Done, chapter, false), "v2 migrated again");

        Debug.Log("[AdventureTutorialValidation] PASS: v0/v1 migration, idempotency and missing data.");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
