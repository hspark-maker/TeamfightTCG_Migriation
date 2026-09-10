using System;

/// <summary>Rank-gated, persistent adventure introduction independent of basic onboarding.</summary>
public static class AdventureTutorialRunner
{
    static OutgameTutorialChapter s_chapter;
    static OutgameTutorialData s_data;
    static int s_oldChapterIndex;
    static bool s_initialized;
    static bool s_sessionActive;
    public static event Action OnChanged;
    static TutorialSaveData Slot => DataSaveManager.Data.Tutorial;
    public static bool IsUnlocked => s_initialized && Slot != null && Slot.AdventureUnlocked;
    public static bool IsRunning => s_sessionActive && IsUnlocked && Slot.AdventureIntroStarted && !Slot.AdventureIntroCompleted;
    public static bool NeedsInvitation => IsUnlocked && OutgameTutorialProgress.IsCompleted
        && !Slot.AdventureIntroStarted && !Slot.AdventureIntroCompleted && !Slot.AdventureIntroDeferred;

    public static void EnsureData(OutgameTutorialData data)
    {
        if (data == null) return;
        s_data = data;
        s_chapter = data.adventureIntroduction;
        s_oldChapterIndex = data.chapters.Count;
    }

    public static void Initialize()
    {
        if (s_chapter == null) return;
        if (DataSaveManager.Data.Tutorial == null) DataSaveManager.Data.Tutorial = new TutorialSaveData();
        s_initialized = true;
        var slot = Slot;
        if (MigrateLegacyProgress(slot, s_chapter, s_oldChapterIndex,
            DataSaveManager.Data.Adventure?.ClearedNodeIds?.Count > 0)) Save();
        s_sessionActive = slot.AdventureIntroStarted && !slot.AdventureIntroCompleted;
        // Neither the map nor match deck survives application restart.
        int resumeId = ResumeStepId(s_chapter, Slot.AdventureIntroStepId);
        if (IsRunning && resumeId != Slot.AdventureIntroStepId)
        {
            Slot.AdventureIntroStepId = resumeId;
            Save();
        }
        RankManager.OnChanged -= RefreshUnlock;
        RankManager.OnChanged += RefreshUnlock;
        RefreshUnlock();
    }

    /// <summary>Pure migration: modifies only the supplied value object, never the live save.</summary>
    public static bool MigrateLegacyProgress(TutorialSaveData slot, OutgameTutorialChapter chapter,
        int oldChapterIndex, bool hasProgress)
    {
        if (slot == null || chapter == null || oldChapterIndex <= 0 || slot.AdventureFlowVersion != 0) return false;
        int moved = -1;
        for (int i = 0; i < chapter.StepCount; i++)
            if (chapter.TryGetStep(i, out var step) && step.StepId == slot.StepId) { moved = i; break; }
        bool wasAdventure = moved >= 0 || slot.ChapterIndex >= oldChapterIndex;
        if (slot.OutgameCompleted || wasAdventure || hasProgress)
        {
            slot.AdventureUnlocked = true;
            slot.AdventureIntroCompleted = slot.OutgameCompleted || hasProgress;
            slot.AdventureIntroStarted = !slot.AdventureIntroCompleted;
            int index = moved >= 0 ? moved : Math.Max(0, slot.ChapterStepIndex - 1);
            slot.AdventureIntroStepId = chapter.TryGetStep(index, out var current) ? current.StepId : 0;
            slot.OutgameCompleted = true;
        }
        slot.AdventureFlowVersion = 1;
        return true;
    }

    public static bool MeetsUnlockTier(bool isRanked, int bestTierIndex, RankTier tier)
        => isRanked && tier.Index >= 0 && tier.Division >= 1
            && tier.Division <= RankConfig.DivisionsPerGrade && bestTierIndex >= tier.Index;

    public static int ResumeStepId(OutgameTutorialChapter chapter, int stepId)
    {
        if (chapter == null) return stepId;
        int reopenId = 0;
        for (int i = 0; i < chapter.StepCount; i++)
        {
            if (!chapter.TryGetStep(i, out var step)) continue;
            if (step.Anchor == EOutgameTutorialAnchor.AdventureButton) reopenId = step.StepId;
            if (step.StepId == stepId) return reopenId > 0 ? reopenId : stepId;
        }
        return stepId;
    }

    public static void RefreshUnlock()
    {
        if (!s_initialized || Slot.AdventureUnlocked || !RankManager.IsRanked) return;
        for (int i = 0; RankManager.TryGetTier(i, out var tier); i++)
        {
            if (tier.Grade != s_data.adventureUnlockGrade || tier.Division != s_data.adventureUnlockDivision) continue;
            if (!MeetsUnlockTier(RankManager.IsRanked, RankManager.BestTierIndex, tier)) return;
            Slot.AdventureUnlocked = true;
            Save();
            OutgameFeatureLock.RefreshAdventure();
            OnChanged?.Invoke();
            return;
        }
    }

    public static bool TryBegin()
    {
        if (!NeedsInvitation || s_chapter == null) return false;
        Slot.AdventureIntroStarted = true;
        s_sessionActive = true;
        Slot.AdventureIntroStepId = StepIdAt(0);
        Save();
        OnChanged?.Invoke();
        return true;
    }

    public static void NotifyMapOpened()
    {
        if (!IsRunning && IsUnlocked && OutgameTutorialProgress.IsCompleted && !Slot.AdventureIntroCompleted)
        {
            s_sessionActive = true;
            Slot.AdventureIntroStarted = true;
            Slot.AdventureIntroStepId = 37;
            Save();
            OnChanged?.Invoke();
        }
    }

    public static void Pause()
    {
        if (!s_sessionActive) return;
        s_sessionActive = false;
        OnChanged?.Invoke();
    }

    public static void DeferInvitation()
    {
        if (!NeedsInvitation) return;
        Slot.AdventureIntroDeferred = true;
        Save();
        OnChanged?.Invoke();
    }

    public static bool TryGetCurrentStep(out TutorialStepDef step)
    {
        step = null;
        if (!IsRunning || s_chapter == null) return false;
        int index = FindIndex(Slot.AdventureIntroStepId);
        return s_chapter.TryGetStep(index < 0 ? 0 : index, out step);
    }

    public static void Advance()
    {
        if (!IsRunning) return;
        int next = FindIndex(Slot.AdventureIntroStepId) + 1;
        if (next >= s_chapter.StepCount)
        {
            Slot.AdventureIntroCompleted = true;
            OutgameTutorialProgress.MarkTriggerDone(EOutgameTutorialTrigger.AdventureMapFirstOpen);
        }
        else Slot.AdventureIntroStepId = StepIdAt(next);
        Save();
        OnChanged?.Invoke();
    }

    public static void NotifyBattleLaunched()
    {
        if (!IsRunning) return;
        Slot.AdventureIntroCompleted = true;
        OutgameTutorialProgress.MarkTriggerDone(EOutgameTutorialTrigger.AdventureMapFirstOpen);
        Save();
        OnChanged?.Invoke();
    }

    static int FindIndex(int id)
    {
        if (id <= 0 || s_chapter == null) return -1;
        for (int i = 0; i < s_chapter.StepCount; i++)
            if (s_chapter.TryGetStep(i, out var step) && step.StepId == id) return i;
        return -1;
    }
    static int StepIdAt(int index) => s_chapter != null && s_chapter.TryGetStep(index, out var step) ? step.StepId : 0;
    static void Save() => DataSaveManager.Save();
}
