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

    // 지금 스텝이 일시로 닫아 둔 기능(누적하지 않는다 — 스텝이 넘어가면 저절로 빈다)
    static readonly HashSet<EOutgameFeature> s_locked = new HashSet<EOutgameFeature>();

    static bool s_forceUnlockAll;
    static bool s_all;
    static bool s_valid;

    static int  s_chapter;
    static int  s_step;
    static bool s_running;

    // 해당 기능이 열려 있는가(None은 항상 열림)
    // ⚠ 조회도 반드시 Refresh를 거친다 — 조회가 캐시만 조용히 갱신하면 뒤이은 Refresh가 "변화 없음"으로 보고
    //   알림을 삼켜, 잠김 룩이 옛 상태에 고착된다(진행으로만 열리는 잠금이라 영영 안 풀린다).
    //   구독자가 이 안에서 다시 조회해도 그때는 캐시가 최신이라 재귀는 한 단계에서 멎는다.
    public static bool IsUnlocked(EOutgameFeature _feature)
    {
        if (_feature == EOutgameFeature.None) return true;

        Refresh();

        // 일시 잠금이 해금보다 우선한다 — 이미 열린 기능도 그 스텝 동안은 닫아 옆길을 막는다
        if (s_locked.Contains(_feature)) return false;

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
        s_locked.Clear();

        s_all = s_forceUnlockAll || !t_running;

        // 일시 잠금은 해금 계산과 무관하게 지금 스텝 하나만 본다(전체 해금 상태에서도 걸린다).
        // 디버그 전체 해금은 예외 — 그때는 아무것도 막지 않아야 검증이 된다.
        if (!s_forceUnlockAll && t_running && OutgameTutorialRunner.TryGetCurrentStep(out var t_current))
            CollectLocks(t_current);

        if (s_all) return true;

        foreach (var t_row in OutgameTutorialRunner.EnumerateUpTo(t_chapter, t_step))
        {
            // 저작이 "여기서부터 전부"라고 못 박은 스텝을 지났으면 남은 목록을 볼 것도 없다 —
            // 안내는 계속 돌지만 게임의 문은 그 자리에서 열린다(졸업까지 기다리지 않는다).
            if (t_row != null && t_row.UnlocksAll) s_all = true;

            Collect(t_row);
        }

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

    static void CollectLocks(TutorialStepDef _step)
    {
        var t_locks = _step != null ? _step.Locks : null;
        if (t_locks == null) return;

        for (int t_i = 0; t_i < t_locks.Count; t_i++)
            if (t_locks[t_i] != EOutgameFeature.None) s_locked.Add(t_locks[t_i]);
    }
}
