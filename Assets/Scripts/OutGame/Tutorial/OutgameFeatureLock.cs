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

    // 진행이 막혔다는 판정(세이브하지 않는다 — 같은 좌표면 다음 초기화에 같은 판정이 다시 선다)
    static bool s_stalled;

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

    // 남은 기능이 전부 열렸는가(저작의 UnlocksAll·정지 판정·디버그 전체 해금 중 하나라도 섰으면 참).
    // 안내는 아직 돌고 있어도 게임의 문은 이미 다 열린 구간이라는 뜻이다 —
    // "안내 중이라 막는다"는 예외를 그 구간에서 걷어야 할 때 이걸 본다(일시 잠금은 여기에 섞지 않는다).
    public static bool AllUnlocked
    {
        get
        {
            Refresh();
            return s_all;
        }
    }

    // 진행 변화를 지금 반영하고, 달라졌으면 OnChanged 발화
    public static void Refresh()
    {
        if (!Recalculate()) return;

        OnChanged?.Invoke();
    }

    /// <summary>튜토리얼이 더 나아갈 수 없다고 판정됐다 — 남은 기능을 전부 연다(멱등).
    ///
    /// 해금이 진행 좌표에서 파생되는 탓에 진행 정지가 곧 기능 영구 잠금이 된다. 그러면 복구 수단(덱 편성·상점)까지
    /// 함께 잠겨 유저가 스스로 빠져나올 길이 없다. 안내는 멈추더라도 게임은 계속할 수 있어야 한다.</summary>
    public static void NotifyStalled()
    {
        if (s_stalled) return;

        s_stalled = true;
        s_valid   = false;
        Refresh();
    }

    /// <summary>정지 판정을 되돌린다 — 되감기·디버그 점프로 진행을 다시 세울 때의 짝이다.
    /// 이 판정은 세이브가 아니라 static에 있어, 걷지 않으면 세이브를 밀어도 전 기능이 열린 채로 남아
    /// 잠금 저작을 검증할 수 없다.</summary>
    public static void ClearStall()
    {
        if (!s_stalled) return;

        s_stalled = false;
        s_valid   = false;
        Refresh();
    }

    // ⚠ 이 계산에는 에디터 거울이 있다 — Editor/Tutorial/TutorialSequenceState가 저작 검증을 위해
    //   같은 규칙(자기 칸 포함 누적 · locks 우선 · 저작 unlock 없으면 전체 개방)을 플레이 없이 다시 편다.
    //   여기를 고치면 그쪽도 함께 고쳐라. 어긋나면 저작 검증기가 멀쩡한 저작을 오류로 찍는다.
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

        s_all = s_forceUnlockAll || s_stalled || !t_running;

        // 일시 잠금은 해금 계산과 무관하게 지금 스텝 하나만 본다(전체 해금 상태에서도 걸린다).
        // 디버그 전체 해금과 정지 판정은 예외 — 막힌 스텝이 닫아 둔 옆길까지 걷어야 탈출로가 실제로 열린다.
        if (!s_forceUnlockAll && !s_stalled && t_running && OutgameTutorialRunner.TryGetCurrentStep(out var t_current))
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
