using System;
using UnityEngine;

// 트리거 발화 튜토리얼의 해석 static 코어(씬 오브젝트·UI를 모른다). 온보딩 러너와 병렬 축이며
// 스텝 행 정의·실행기(TutorialStepDef/TutorialStepExecutor)는 온보딩과 같은 것을 그대로 쓴다.
// 진행 좌표는 메모리에만 둔다 — 앱을 끄면 처음부터고, 세이브에는 완주 시점에 트리거 키 1개만 남는다.
public static class TriggeredTutorialRunner
{
    static TriggeredTutorialData  s_data;
    static TriggeredTutorialEntry s_active;
    static int                    s_index;

    /// <summary>트리거가 실제로 발화했을 때. 세션 중간에 시작되므로 브리지가 Start의 pull만으로는 잡을 수 없다.</summary>
    public static event Action OnActivated;

    public static bool IsRunning => s_active != null;

    /// <summary>실행 중인 묶음 안에서의 스텝 순번. 자동 스텝이 좌표를 진짜로 밀었는지 브리지가 판정하는 근거다.</summary>
    public static int StepIndex => s_index;

    /// <summary>씬마다 브리지가 호출하는 멱등 주입. 첫 주입만 유효하다(에셋이 갈리면 실행 중 좌표가 다른 목록을 가리킨다).</summary>
    public static void EnsureData(TriggeredTutorialData _data)
    {
        if (_data == null) return;          // 미배선 브리지가 기존 주입을 지우지 않게.
        if (s_data == _data) return;

        if (s_data != null)
        {
            Debug.LogWarning($"[TriggeredTutorialRunner] 다른 트리거 튜토리얼 데이터 주입 시도('{_data.name}' ≠ 기존 '{s_data.name}') — 기존 유지.");
            return;
        }

        s_data = _data;
    }

    /// <summary>트리거 발화. 아래 무시 조건은 전부 정상 경로라 경고하지 않는다(발화 지점이 탭 전환이라 매번 불린다).</summary>
    public static void Fire(EOutgameTutorialTrigger _trigger)
    {
        if (_trigger == EOutgameTutorialTrigger.None) return;
        if (s_data == null) return;
        if (IsRunning) return;                                          // 실행 중인 트리거 런을 다른 트리거가 끊지 않는다
        if (OutgameTutorialRunner.IsRunning) return;                    // 온보딩 우선(게이트를 서로 뺏지 않게)
        if (OutgameTutorialProgress.IsTriggerDone(_trigger)) return;    // 계정당 1회

        if (!TryGetEntry(_trigger, out var t_entry) || t_entry.StepCount == 0) return;

        s_active = t_entry;
        s_index  = 0;

        OnActivated?.Invoke();
    }

    /// <summary>현재 좌표가 가리키는 스텝. 미실행·범위 밖·빈 칸이면 false.</summary>
    public static bool TryGetCurrentStep(out TutorialStepDef _step)
    {
        _step = null;
        if (!IsRunning) return false;

        return s_active.TryGetStep(s_index, out _step);
    }

    /// <summary>현재 스텝을 진입시킨다. 반환 true = 이 씬에서 앵커에 게이트를 걸어야 함.
    /// 실행할 스텝이 없으면(저작 미완·빈 칸) 림보로 남기지 않고 그 자리에서 닫는다.</summary>
    public static bool EnterCurrentStep()
    {
        if (!TryGetCurrentStep(out var t_step))
        {
            // 낙인을 찍고 닫는다 — 안 닫으면 탭을 누를 때마다 같은 빈 칸을 다시 시도한다.
            // 되돌릴 수 없으므로(다시 보려면 디버그 리셋) 저작 실수를 조용히 삼키지 않고 드러낸다.
            Debug.LogWarning($"[TriggeredTutorialRunner] '{s_active.Label}'({s_active.Trigger})의 스텝 {s_index}이(가) 비어 있습니다 — 완주로 닫습니다.");
            Finish();
            return false;
        }

        bool t_isLast = s_index + 1 >= s_active.StepCount;

        // 챕터 축은 트리거엔 없으므로 항상 0. 싱크는 메모리 — 실행기가 커밋해도 온보딩의 영속 좌표를 건드리지 않는다.
        return TutorialStepExecutor.Enter(t_step,
            new OutgameTutorialStepContext(0, s_index, 0, s_index + 1, t_isLast, MemoryProgressSink.Instance));
    }

    /// <summary>스텝 완료를 감지한 브리지가 호출. 마지막이었으면 완주 낙인까지 찍는다.</summary>
    public static void NotifyStepSatisfied()
    {
        if (!IsRunning) return;

        s_index++;
        if (s_index >= s_active.StepCount) Finish();
    }

    /// <summary>낙인 없이 실행만 끊는다(세이브 재로드·디버그 리셋용) — 다음 발화 때 처음부터 다시 돈다.</summary>
    public static void Abort()
    {
        s_active = null;
        s_index  = 0;
    }

    // 같은 트리거를 여러 엔트리에 저작하면 첫 항목만 쓴다(뒤 항목은 영영 발화하지 않는다).
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

    // 완주 낙인을 찍는 유일한 자리. NotifyStepSatisfied 경로와 스텝의 CompleteIfLast 경로 양쪽에서 불릴 수 있어
    // 재진입 방어가 필요하다(두 번째 호출은 s_active가 이미 비어 낙인 대상을 알 수 없다).
    static void Finish()
    {
        if (s_active == null) return;

        OutgameTutorialProgress.MarkTriggerDone(s_active.Trigger);

        s_active = null;
        s_index  = 0;
    }

    // 트리거 런의 진행 좌표는 메모리에만 산다 — 같은 스텝 SO를 온보딩과 공유해도 세이브가 오염되지 않는다.
    sealed class MemoryProgressSink : ITutorialProgressSink
    {
        public static readonly ITutorialProgressSink Instance = new MemoryProgressSink();

        MemoryProgressSink() { }

        public void Commit(int _chapter, int _step) => s_index = _step;   // 챕터 축이 없어 _chapter는 무시한다

        public void Complete() => Finish();
    }
}
