using System;
using UnityEngine;

// 트리거 발화 튜토리얼의 해석 static 코어(씬 오브젝트·UI를 모른다)
// 진행 좌표는 메모리에만 둔다 — 세이브에는 완주 시점에 트리거 키 1개만 남는다
public static class TriggeredTutorialRunner
{
    static TriggeredTutorialData  s_data;
    static TriggeredTutorialEntry s_active;
    static int                    s_index;

    // 졸업 낙인보다 먼저 열린 게이트(승급 연출 관람 구간). 낙인은 그 연출이 끝나야 찍히는데
    // 알림 점은 연출과 나란히 떠야 해서, 그 사이를 이 래치가 잇는다.
    static bool s_openedAtFinale;

    // "지금 안내를 세워도 되는 무대인가"의 판정. 조건이 씬·UI 사정이라 여기 적지 않고 브리지가 등록한다.
    static Func<bool> s_stageGuard;

    // 트리거가 실제로 발화했을 때(세션 중간에 시작되므로 브리지가 pull만으로는 잡을 수 없다)
    public static event Action OnActivated;

    // 남은 트리거 목록이 달라졌을 때(주입·발화·완주·중단) — 알림 점이 이걸 보고 다시 그린다
    public static event Action OnChanged;

    // "지금 다시 물어봐라" — 발화를 걸어 둔 쪽(탭)이 자기 트리거로 Fire를 재시도한다.
    // OnChanged와 나누는 이유: 저쪽은 "남은 목록이 달라졌다"라서 그리기용이고, 이쪽은 발화용이다.
    public static event Action OnRetryRequested;

    public static bool IsRunning => s_active != null;

    // 실행 중인 묶음 안에서의 스텝 순번
    public static int StepIndex => s_index;

    // 지금 도는 런이 이 트리거의 것인가(성장 곡선이 "안내가 대주는 구간인가"를 묻는 창구)
    public static bool IsRunningTrigger(EOutgameTutorialTrigger _trigger)
        => s_active != null && s_active.Trigger == _trigger;

    // 온보딩 졸업 전에는 트리거 튜토리얼이 통째로 잠긴다 — 게이트는 하나뿐이라 두 안내가 겹치면 서로를 가로채고,
    // 첫시작 동선 밖의 탭으로 부르는 점은 아직 못 가는 곳을 가리킨다.
    static bool IsOpen => OutgameTutorialProgress.IsCompleted || s_openedAtFinale;

    // 이 트리거로 아직 볼 것이 남았는가. 판정은 Fire의 무시 조건과 같아야 한다 —
    // UI가 규칙을 복제하지 않도록 "띄울지"의 답을 여기서만 낸다(데이터 미주입이면 false).
    public static bool HasPending(EOutgameTutorialTrigger _trigger)
    {
        if (_trigger == EOutgameTutorialTrigger.None) return false;
        if (!IsOpen) return false;
        if (OutgameTutorialProgress.IsTriggerDone(_trigger)) return false;

        return TryGetEntry(_trigger, out var t_entry) && t_entry.StepCount > 0;
    }

    /// <summary>무대가 비어 안내를 세워도 되는가. 미등록이면 항상 열린 것으로 본다.
    /// 되묻는 쪽이 저마다 조건을 복제하지 않도록 답을 여기 한 곳에서만 낸다.</summary>
    public static bool IsStageClear => s_stageGuard == null || s_stageGuard();

    /// <summary>무대 판정을 등록한다(씬 브리지가 자기 수명 동안만). 러너는 조건을 모른 채 물어보기만 한다.</summary>
    public static void SetStageGuard(Func<bool> _guard) => s_stageGuard = _guard;

    /// <summary>발화를 걸어 둔 쪽에 "지금 다시 물어봐라"를 방송한다. Fire의 거절은 대부분 일시적인데
    /// (데이터 미주입·다른 안내 진행 중·온보딩 미졸업·무대가 다른 연출에 점유됨) 그 자리에서 버리면 기회가 영영 사라진다.
    /// 보류 큐를 두지 않는 이유는 발화 조건이 곧 현재 상태라서다 — 되물으면 답이 다시 나온다.</summary>
    public static void RequestRetry() => OnRetryRequested?.Invoke();

    // 온보딩 졸업으로 게이트가 열리면 그 전까지 전부 false였던 HasPending의 답이 한꺼번에 뒤집힌다 —
    // 이미 그린 쪽(알림 점)에 다시 물어보게 하고, 그 순간 이미 서 있는 탭도 되묻게 한다.
    public static void NotifyOnboardingCompleted()
    {
        OnChanged?.Invoke();
        RequestRetry();
    }

    /// <summary>온보딩이 마지막 스텝(승급 연출을 관람만 하는 자리)에 들어섰다 — 가르칠 것은 끝났고 낙인만 남았다.
    /// 알림 점이 그 연출과 나란히 뜨도록 게이트를 여기서 미리 연다(기능 해금이 UnlocksAll로 하는 일과 같은 취지).</summary>
    public static void NotifyOnboardingFinale()
    {
        if (s_openedAtFinale) return;

        s_openedAtFinale = true;
        OnChanged?.Invoke();
        RequestRetry();
    }

    // 씬마다 브리지가 호출하는 멱등 주입(첫 주입만 유효)
    public static void EnsureData(TriggeredTutorialData _data)
    {
        if (_data == null) return;
        if (s_data == _data) return;

        if (s_data != null)
        {
            Debug.LogWarning($"[TriggeredTutorialRunner] 다른 트리거 튜토리얼 데이터 주입 시도('{_data.name}' ≠ 기존 '{s_data.name}') — 기존 유지.");
            return;
        }

        s_data = _data;

        // 주입 전의 HasPending은 전부 false다 — 그 답을 이미 그린 쪽에 다시 물어보게 한다.
        OnChanged?.Invoke();

        // 주입보다 먼저 터진 발화는 s_data가 없어 버려졌다 — 이제 답할 수 있으니 되묻게 한다.
        RequestRetry();
    }

    // 트리거 발화(아래 무시 조건은 전부 정상 경로라 경고하지 않는다).
    // 거절해도 기회는 남는다 — 부르는 쪽이 되풀이해 묻거나(패널 열기·강화 정산) RequestRetry가 되묻게 한다.
    public static void Fire(EOutgameTutorialTrigger _trigger)
    {
        if (_trigger == EOutgameTutorialTrigger.None) return;
        if (s_data == null) return;
        if (IsRunning) return;
        if (!IsOpen) return;
        if (OutgameTutorialProgress.IsTriggerDone(_trigger)) return;

        if (!TryGetEntry(_trigger, out var t_entry) || t_entry.StepCount == 0) return;

        s_active = t_entry;
        s_index  = 0;

        OnActivated?.Invoke();
        OnChanged?.Invoke();
    }

    // 현재 좌표가 가리키는 스텝(미실행·범위 밖·빈 칸이면 false)
    public static bool TryGetCurrentStep(out TutorialStepDef _step)
    {
        _step = null;
        if (!IsRunning) return false;

        return s_active.TryGetStep(s_index, out _step);
    }

    // 지금 서 있는 스텝이 _action인가. OutgameTutorialRunner와 같은 조회 창구다 —
    // 화면·규칙 쪽이 튜토 좌표를 직접 해석하지 않게 한다(강화 비용이 "지금이 안내가 시킨 강화인가"를 묻는다).
    public static bool IsCurrentAction(EOutgameTutorialAction _action)
        => TryGetCurrentStep(out var t_step) && t_step.Action == _action;

    // 현재 스텝 진입 — 결말은 반환값이 말한다(OutgameTutorialRunner와 같은 규약)
    public static EOutgameTutorialStepResult EnterCurrentStep()
    {
        if (!TryGetCurrentStep(out var t_step))
        {
            Debug.LogWarning($"[TriggeredTutorialRunner] '{s_active.Label}'({s_active.Trigger})의 스텝 {s_index}이(가) 비어 있습니다 — 완주로 닫습니다.");
            Finish();
            return EOutgameTutorialStepResult.Advanced;
        }

        bool t_isLast = s_index + 1 >= s_active.StepCount;

        return TutorialStepExecutor.Enter(t_step,
            new OutgameTutorialStepContext(0, s_index, 0, s_index + 1, t_isLast, MemoryProgressSink.Instance));
    }

    // 스텝 완료를 감지한 브리지가 호출 — 마지막이었으면 완주 낙인까지 찍는다
    public static void NotifyStepSatisfied()
    {
        if (!IsRunning) return;

        s_index++;
        if (s_index >= s_active.StepCount) Finish();
    }

    // 낙인 없이 실행만 끊는다(세이브 재로드·디버그 리셋용)
    public static void Abort()
    {
        s_active = null;
        s_index  = 0;

        // 되감기로 온보딩이 다시 진행 중이 되면 미리 연 문도 함께 닫혀야 한다 — 남으면 튜토 도중에 점이 뜬다.
        s_openedAtFinale = false;

        // 디버그 낙인 초기화가 이 경로로 들어온다 — 걷힌 트리거를 알림 점이 다시 집어야 한다.
        OnChanged?.Invoke();
        RequestRetry();
    }

    static bool TryGetEntry(EOutgameTutorialTrigger _trigger, out TriggeredTutorialEntry _entry)
    {
        _entry = null;
        if (s_data == null || s_data.entries == null) return false;

        for (int t_i = 0; t_i < s_data.entries.Count; t_i++)
        {
            var t_candidate = s_data.entries[t_i];
            if (t_candidate == null || t_candidate.Trigger != _trigger) continue;

            _entry = t_candidate;
            return true;
        }

        return false;
    }

    static void Finish()
    {
        if (s_active == null) return;

        OutgameTutorialProgress.MarkTriggerDone(s_active.Trigger);

        s_active = null;
        s_index  = 0;

        OnChanged?.Invoke();
    }

    // 트리거 런의 진행 좌표를 메모리에만 두는 싱크(챕터 축이 없어 _chapter는 무시)
    sealed class MemoryProgressSink : ITutorialProgressSink
    {
        public static readonly ITutorialProgressSink Instance = new MemoryProgressSink();

        MemoryProgressSink() { }

        public void Commit(int _chapter, int _step) => s_index = _step;

        public void Complete() => Finish();
    }
}
