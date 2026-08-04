using System;
using System.Collections.Generic;

// 튜토리얼 진행에 따른 기능 해금의 단일 창구(static).
// 해금 상태를 저장하지 않는다 — 진행 좌표에서 매번 파생시킨다. 덕분에 세이브 스키마가 늘지 않고,
// 디버그 좌표 점프·스텝 저작 변경이 별도 조치 없이 곧바로 반영된다.
//
// 의존은 한 방향이다: 이쪽이 Progress·Runner를 읽는다. 그쪽은 이 클래스를 모른다.
//
// 잠금과 튜토리얼 딤은 역할이 다르다 — 잠금은 "아직 안 배운 기능"을 상시 막고,
// 딤(OutgameTutorialGateUI)은 그 스텝 동안 타깃 외 입력을 국소적으로 막는다.
// 불변식: 스텝이 지목하는 앵커의 기능은 그 스텝까지의 누적 unlocks에 반드시 포함되어야 한다.
// 어기면 딤은 걸렸는데 타깃은 잠긴 상태가 되어 게이트가 무한 대기한다(GateUI가 원인을 경고로 지목한다).
public static class OutgameFeatureLock
{
    /// <summary>해금 집합이 달라졌을 때 발화. 잠금 표시는 이걸 구독해 갱신한다.</summary>
    public static event Action OnChanged;

    /// <summary>디버그 전용 전체 해금. 잠금 때문에 QA가 막히지 않게 하는 우회로.</summary>
    public static bool ForceUnlockAllForDebug
    {
        get => s_forceUnlockAll;
        set
        {
            if (s_forceUnlockAll == value) return;

            s_forceUnlockAll = value;
            s_valid          = false;   // 좌표는 그대로라 스냅샷 비교로는 안 잡힌다
            Refresh();
        }
    }

    static readonly HashSet<EOutgameFeature> s_unlocked = new HashSet<EOutgameFeature>();

    static bool s_forceUnlockAll;
    static bool s_all;        // 전부 열린 상태(완료·데이터 미주입·디버그). 이때 s_unlocked는 비어 있다
    static bool s_valid;

    // 마지막으로 계산한 진행 스냅샷. 이게 바뀔 때만 다시 센다.
    static int  s_chapter;
    static int  s_step;
    static bool s_running;

    public static bool IsUnlocked(EOutgameFeature _feature)
    {
        if (_feature == EOutgameFeature.None) return true;   // 미지정 = 잠글 대상이 아니다

        Recalculate();
        return s_all || s_unlocked.Contains(_feature);
    }

    /// <summary>진행 변화를 지금 반영하고, 실제로 달라졌으면 OnChanged를 발화한다.
    /// 스텝 적용의 단일 창구(OutgameTutorialBridge)가 부른다 — Runner.OnStepChanged는
    /// NotifyStepSatisfied에서만 발화해 자동 스텝이 스스로 커밋하는 경로를 놓친다.</summary>
    public static void Refresh()
    {
        if (!Recalculate()) return;

        OnChanged?.Invoke();
    }

    // 스냅샷이 그대로면 아무것도 하지 않는다. 반환 = 이번에 다시 셌는가.
    // IsRunning까지 스냅샷에 넣는 이유: 데이터 주입(브리지 Awake)으로 잠금이 "전부 열림"에서
    // 실제 판정으로 바뀌는데, 좌표만 보면 그 전환을 놓친다.
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

        // 튜토리얼이 돌지 않으면 잠글 근거가 없다 — 완료 유저·튜토리얼 미배선 씬은 전부 열린다.
        s_all = s_forceUnlockAll || !t_running;
        if (s_all) return true;

        foreach (var t_stepAsset in OutgameTutorialRunner.EnumerateUpTo(t_chapter, t_step))
            Collect(t_stepAsset);

        // 저작 미완(시퀀스 전체에 unlocks가 하나도 없음)은 "잠금을 아직 안 쓴다"로 본다.
        // 없으면 저작을 시작하기 전에 화면이 통째로 잠겨 튜토리얼 자체가 막힌다
        // — 러너가 빈 시퀀스에 완료 낙인을 찍지 않는 것과 같은 방어 축이다.
        // 한 칸이라도 저작되면 즉시 실제 판정으로 넘어간다.
        if (s_unlocked.Count == 0 && !HasAnyAuthoredUnlock()) s_all = true;

        return true;
    }

    static bool HasAnyAuthoredUnlock()
    {
        foreach (var t_stepAsset in OutgameTutorialRunner.EnumerateUpTo(int.MaxValue, int.MaxValue))
        {
            var t_unlocks = t_stepAsset != null ? t_stepAsset.Unlocks : null;
            if (t_unlocks == null) continue;

            for (int t_i = 0; t_i < t_unlocks.Count; t_i++)
                if (t_unlocks[t_i] != EOutgameFeature.None) return true;
        }

        return false;
    }

    static void Collect(OutgameTutorialStep _step)
    {
        var t_unlocks = _step != null ? _step.Unlocks : null;
        if (t_unlocks == null) return;

        for (int t_i = 0; t_i < t_unlocks.Count; t_i++)
            if (t_unlocks[t_i] != EOutgameFeature.None) s_unlocked.Add(t_unlocks[t_i]);
    }
}
