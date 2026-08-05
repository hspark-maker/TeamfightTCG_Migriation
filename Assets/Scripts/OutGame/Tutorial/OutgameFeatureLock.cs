using System;
using System.Collections.Generic;

// 튜토리얼 진행 좌표에서 파생되는 기능 해금의 단일 창구(저장하지 않음)
// 불변식: 스텝이 지목하는 앵커의 기능은 그 스텝까지의 누적 unlocks에 포함되어야 한다(어기면 게이트가 무한 대기)
public static class OutgameFeatureLock
{
    // 해금 집합이 달라졌을 때 발화
    public static event Action OnChanged;

    // 디버그 전용 전체 해금
    public static bool ForceUnlockAllForDebug
    {
        get => s_forceUnlockAll;
        set
        {
            if (s_forceUnlockAll == value) return;

            s_forceUnlockAll = value;
            s_valid          = false;
            Refresh();
        }
    }

    static readonly HashSet<EOutgameFeature> s_unlocked = new HashSet<EOutgameFeature>();

    static bool s_forceUnlockAll;
    static bool s_all;
    static bool s_valid;

    static int  s_chapter;
    static int  s_step;
    static bool s_running;

    // 해당 기능이 열려 있는가(None은 항상 열림)
    public static bool IsUnlocked(EOutgameFeature _feature)
    {
        if (_feature == EOutgameFeature.None) return true;

        Recalculate();
        return s_all || s_unlocked.Contains(_feature);
    }

    // 진행 변화를 지금 반영하고, 달라졌으면 OnChanged 발화
    public static void Refresh()
    {
        if (!Recalculate()) return;

        OnChanged?.Invoke();
    }

    static bool Recalculate()
    {
        int  t_chapter = OutgameTutorialProgress.ChapterIndex;
        int  t_step    = OutgameTutorialProgress.StepIndex;
        bool t_running = OutgameTutorialRunner.IsRunning;

        if (s_valid && s_chapter == t_chapter && s_step == t_step && s_running == t_running) return false;

        s_chapter = t_chapter;
        s_step    = t_step;
        s_running = t_running;
        s_valid   = true;

        s_unlocked.Clear();

        s_all = s_forceUnlockAll || !t_running;
        if (s_all) return true;

        foreach (var t_row in OutgameTutorialRunner.EnumerateUpTo(t_chapter, t_step))
            Collect(t_row);

        if (s_unlocked.Count == 0 && !HasAnyAuthoredUnlock()) s_all = true;

        return true;
    }

    static bool HasAnyAuthoredUnlock()
    {
        foreach (var t_row in OutgameTutorialRunner.EnumerateUpTo(int.MaxValue, int.MaxValue))
        {
            var t_unlocks = t_row != null ? t_row.Unlocks : null;
            if (t_unlocks == null) continue;

            for (int t_i = 0; t_i < t_unlocks.Count; t_i++)
                if (t_unlocks[t_i] != EOutgameFeature.None) return true;
        }

        return false;
    }

    static void Collect(TutorialStepDef _step)
    {
        var t_unlocks = _step != null ? _step.Unlocks : null;
        if (t_unlocks == null) return;

        for (int t_i = 0; t_i < t_unlocks.Count; t_i++)
            if (t_unlocks[t_i] != EOutgameFeature.None) s_unlocked.Add(t_unlocks[t_i]);
    }
}
