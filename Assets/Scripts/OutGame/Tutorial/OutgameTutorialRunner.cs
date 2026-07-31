using System;
using UnityEngine;

// 아웃게임 첫시작 튜토리얼의 시퀀스 해석 static 코어(씬 오브젝트·UI를 모른다).
// 스텝이 "무엇을 하는지"는 스텝 SO가 안다 — 러너는 어느 칸인지 짚고, 진행도 커밋 창구를 넘겨줄 뿐.
public static class OutgameTutorialRunner
{
    static OutgameTutorialData s_data;

    /// <summary>진행도가 다음 스텝으로 넘어갈 때 발화. 진열 대상이 스텝에 따라 달라지는 화면(상점)이 갱신 시점을 잡는다.</summary>
    public static event Action OnStepChanged;

    /// <summary>데이터가 주입됐고 아직 완료 전이면 진행 중. 완료는 항상 진행도의 스칼라가 우선한다.</summary>
    public static bool IsRunning => s_data != null && !OutgameTutorialProgress.IsCompleted;

    // 미주입·빈 시퀀스는 0. 완료 판정을 전부 이 값 하나로 모은다.
    static int StepCount => s_data != null && s_data.steps != null ? s_data.steps.Count : 0;

    /// <summary>씬마다 브리지가 호출하는 멱등 주입. 첫 주입만 유효하다(에셋이 갈리면 진행 인덱스가 다른 시퀀스를 가리킨다).</summary>
    public static void EnsureData(OutgameTutorialData _data)
    {
        if (_data == null) return;          // 미배선 브리지가 기존 주입을 지우지 않게.
        if (s_data == _data) return;

        if (s_data != null)
        {
            Debug.LogWarning($"[OutgameTutorialRunner] 다른 튜토리얼 데이터 주입 시도('{_data.name}' ≠ 기존 '{s_data.name}') — 기존 유지.");
            return;
        }

        s_data = _data;
    }

    /// <summary>현재 진행도가 가리키는 스텝. 미주입·완료·인덱스 범위 밖·빈 칸이면 false.</summary>
    public static bool TryGetCurrentStep(out OutgameTutorialStep _step)
    {
        _step = null;
        if (!IsRunning) return false;

        int t_index = OutgameTutorialProgress.StepIndex;
        if (t_index < 0 || t_index >= StepCount) return false;

        _step = s_data.steps[t_index];
        return _step != null;   // 미배선 칸은 실행할 스텝이 없는 것과 같다.
    }

    /// <summary>현재 스텝을 진입시킨다. 반환 true = 이 씬에서 앵커에 게이트를 걸어야 함(false면 자동 처리·씬 전환).</summary>
    public static bool EnterCurrentStep()
    {
        if (!TryGetCurrentStep(out var t_step))
        {
            CloseOrWarnOnMissingStep();
            return false;
        }

        int t_index = OutgameTutorialProgress.StepIndex;
        return t_step.Enter(new OutgameTutorialStepContext(t_index, t_index + 1 >= StepCount));
    }

    /// <summary>튜토리얼이 이번 스텝에서 팔 팩을 지정했으면 true. 상점은 진열·가격·구매 대상을 이걸로 덮어써
    /// 튜토리얼 중 구매 결과가 저작대로 고정되게 한다. 미지정이면 false → 상점 기본 진열.</summary>
    public static bool TryGetForcedPack(out CardPackData _pack, out long _refundGold)
    {
        _pack       = null;
        _refundGold = 0;

        return TryGetCurrentStep(out var t_step) && t_step.TryGetForcedPack(out _pack, out _refundGold);
    }

    /// <summary>스텝 완료를 감지한 브리지가 호출. 다음 인덱스를 커밋하고 시퀀스를 넘어서면 완료 처리한다.</summary>
    public static void NotifyStepSatisfied()
    {
        if (!IsRunning) return;

        int t_next = OutgameTutorialProgress.StepIndex + 1;
        OutgameTutorialProgress.CommitStep(t_next);

        // 완료를 인덱스 비교로 파생시키지 않고 여기서 한 번만 확정한다(스텝을 나중에 추가해도 완료 유저가 되살아나지 않게).
        if (t_next >= StepCount) OutgameTutorialProgress.Complete();

        // 게이트를 걸기 전에 알린다 — 구매 스텝의 진열 교체가 끝난 뒤 게이트가 그 버튼 상태를 읽어야 한다.
        OnStepChanged?.Invoke();
    }

    // 실행할 스텝이 없는 상태를 정리한다. 인덱스가 시퀀스 밖(저작에서 스텝을 줄인 경우)이면 완료로 닫는다 —
    // 안 그러면 IsRunning은 true인데 실행할 스텝이 없는 림보가 영구히 남는다.
    static void CloseOrWarnOnMissingStep()
    {
        if (!IsRunning) return;

        int t_index = OutgameTutorialProgress.StepIndex;
        if (t_index < StepCount)
        {
            // 범위 안인데 못 꺼냈다 = 그 칸이 비어 있다. 저작 실수라 완료로 닫지 않고 드러낸다.
            Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'의 스텝 {t_index}이(가) 비어 있습니다 — 진행할 수 없습니다.");
            return;
        }

        // 단, 빈 시퀀스는 저작 미완일 뿐이라 완료로 낙인찍지 않는다(스텝을 채우면 그대로 재개돼야 한다).
        if (StepCount > 0) OutgameTutorialProgress.Complete();
        else Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'에 스텝이 없습니다 — 진행할 수 없습니다.");
    }
}
