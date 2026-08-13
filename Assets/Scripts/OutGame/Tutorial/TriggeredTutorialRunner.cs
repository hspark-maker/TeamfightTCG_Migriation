using System;
using UnityEngine;

// 트리거 발화 튜토리얼의 해석 static 코어(씬 오브젝트·UI를 모른다)
// 진행 좌표는 메모리에만 둔다 — 세이브에는 완주 시점에 트리거 키 1개만 남는다
public static class TriggeredTutorialRunner
{
    static TriggeredTutorialData  s_data;
    static TriggeredTutorialEntry s_active;
    static int                    s_index;

    // 트리거가 실제로 발화했을 때(세션 중간에 시작되므로 브리지가 pull만으로는 잡을 수 없다)
    public static event Action OnActivated;

    // 남은 트리거 목록이 달라졌을 때(주입·발화·완주·중단) — 알림 점이 이걸 보고 다시 그린다
    public static event Action OnChanged;

    public static bool IsRunning => s_active != null;

    // 실행 중인 묶음 안에서의 스텝 순번
    public static int StepIndex => s_index;

    // 온보딩 졸업 전에는 트리거 튜토리얼이 통째로 잠긴다 — 게이트는 하나뿐이라 두 안내가 겹치면 서로를 가로채고,
    // 첫시작 동선 밖의 탭으로 부르는 점은 아직 못 가는 곳을 가리킨다.
    static bool IsOpen => OutgameTutorialProgress.IsCompleted;

    // 이 트리거로 아직 볼 것이 남았는가. 판정은 Fire의 무시 조건과 같아야 한다 —
    // UI가 규칙을 복제하지 않도록 "띄울지"의 답을 여기서만 낸다(데이터 미주입이면 false).
    public static bool HasPending(EOutgameTutorialTrigger _trigger)
    {
        if (_trigger == EOutgameTutorialTrigger.None) return false;
        if (!IsOpen) return false;
        if (OutgameTutorialProgress.IsTriggerDone(_trigger)) return false;

        return TryGetEntry(_trigger, out var t_entry) && t_entry.StepCount > 0;
    }

    // 온보딩 졸업으로 게이트가 열리면 그 전까지 전부 false였던 HasPending의 답이 한꺼번에 뒤집힌다 —
    // 이미 그린 쪽(알림 점)에 다시 물어보게 한다.
    public static void NotifyOnboardingCompleted() => OnChanged?.Invoke();

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
    }

    // 트리거 발화(아래 무시 조건은 전부 정상 경로라 경고하지 않는다)
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

    // 현재 스텝 진입 — 반환 true = 이 씬에서 앵커에 게이트를 걸어야 함
    public static bool EnterCurrentStep()
    {
        if (!TryGetCurrentStep(out var t_step))
        {
            Debug.LogWarning($"[TriggeredTutorialRunner] '{s_active.Label}'({s_active.Trigger})의 스텝 {s_index}이(가) 비어 있습니다 — 완주로 닫습니다.");
            Finish();
            return false;
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

        // 디버그 낙인 초기화가 이 경로로 들어온다 — 걷힌 트리거를 알림 점이 다시 집어야 한다.
        OnChanged?.Invoke();
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
