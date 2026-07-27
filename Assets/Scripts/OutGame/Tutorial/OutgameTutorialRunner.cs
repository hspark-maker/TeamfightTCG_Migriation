using UnityEngine;
using UnityEngine.SceneManagement;

// 아웃게임 첫시작 튜토리얼의 스텝 해석·실행 static 코어(씬 오브젝트·UI를 모른다).
// 불변식: 커밋이 실행보다 앞선다 — 자동구매는 CommitStep 후에 구매하고, 실패했을 때만 롤백한다.
public static class OutgameTutorialRunner
{
    // 개봉 연출 씬. 저작 데이터가 아니라 시스템 고정 경로라 SO 필드가 아닌 상수로 둔다.
    const string PackOpenScene = "CardPack";

    static OutgameTutorialData s_data;

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

    /// <summary>현재 진행도가 가리키는 스텝. 미주입·완료·인덱스 범위 밖이면 false.</summary>
    public static bool TryGetCurrentStep(out OutgameTutorialData.Step _step)
    {
        _step = default;
        if (!IsRunning) return false;

        int t_index = OutgameTutorialProgress.StepIndex;
        if (t_index < 0 || t_index >= StepCount) return false;

        _step = s_data.steps[t_index];
        return true;
    }

    /// <summary>현재 스텝을 진입시킨다. 반환 true = 이 씬에서 앵커에 게이트를 걸어야 함(false면 자동 처리·씬 전환).</summary>
    public static bool EnterCurrentStep()
    {
        if (!TryGetCurrentStep(out var t_step))
        {
            // 인덱스가 시퀀스 밖(저작에서 스텝을 줄인 경우)이면 완료로 닫는다 —
            // 안 그러면 IsRunning은 true인데 실행할 스텝이 없는 림보가 영구히 남는다.
            if (IsRunning && OutgameTutorialProgress.StepIndex >= StepCount)
            {
                // 단, 빈 시퀀스는 저작 미완일 뿐이라 완료로 낙인찍지 않는다(스텝을 채우면 그대로 재개돼야 한다).
                if (StepCount > 0) OutgameTutorialProgress.Complete();
                else Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'에 스텝이 없습니다 — 진행할 수 없습니다.");
            }
            return false;
        }

        int t_index = OutgameTutorialProgress.StepIndex;

        switch (t_step.kind)
        {
            case OutgameTutorialData.EStepKind.AutoPurchase:
                return EnterAutoPurchase(t_step, t_index);

            case OutgameTutorialData.EStepKind.BattleEntry:
                // 클릭 리스너가 아니라 진입 시 미리 시작한다 — PlayBtn의 씬 PersistentCall(StartAiBattle)이
                // 런타임 리스너보다 먼저 LoadScene을 돌려 순서 의존이 생기기 때문. Begin은 멱등이라 재진입도 안전.
                if (t_step.scenario == null)
                    Debug.LogWarning($"[OutgameTutorialRunner] 스텝 {t_index}(BattleEntry)에 시나리오가 미배선 — 일반 전투로 진입합니다.");
                TutorialConfig.Begin(t_step.scenario);
                return true;

            case OutgameTutorialData.EStepKind.WaitClick:
            case OutgameTutorialData.EStepKind.WaitPackOpen:
            case OutgameTutorialData.EStepKind.WaitPurchase:
                // 완료 시점(클릭 또는 결과 신호)에 브리지가 NotifyStepSatisfied로 커밋한다.
                return true;

            default:
                // kind가 추가됐는데 여기 분기를 안 넣으면 조용히 오동작한다 — 게이트를 걸지 않고 경고로 드러낸다.
                Debug.LogWarning($"[OutgameTutorialRunner] 스텝 {t_index}의 처리되지 않은 종류({t_step.kind}) — 진입을 건너뜁니다.");
                return false;
        }
    }

    /// <summary>앵커 클릭 감지 시 브리지가 호출. 다음 인덱스를 커밋하고 시퀀스를 넘어서면 완료 처리한다.</summary>
    public static void NotifyStepSatisfied()
    {
        if (!IsRunning) return;

        CommitAdvance(OutgameTutorialProgress.StepIndex + 1);
    }

    // 팩 구매 → 캐리어 → 개봉 씬 전환. 게이트는 필요 없다(입력 없는 자동 스텝).
    static bool EnterAutoPurchase(OutgameTutorialData.Step _step, int _index)
    {
        // 불변식: 커밋이 실행보다 앞선다. 구매 직후 강제종료 시 "소유는 생겼는데 진행도는 0"이 되어
        // 레거시 마이그레이션이 온보딩을 영구 스킵시키는 구멍을 원천 봉쇄한다. 순서를 바꾸지 말 것.
        OutgameTutorialProgress.CommitStep(_index + 1);

        var t_opened = CardPackOpener.TryPurchase(_step.pack, _step.duplicateRefundGold);
        if (t_opened == null || !t_opened.Success)
        {
            // 실패는 차감 없이 반환되므로(TryPurchase 보장) 커밋만 되돌리면 원상복구된다 — 다음 부트에 재시도.
            OutgameTutorialProgress.CommitStep(_index);

            string t_result = t_opened != null ? t_opened.Result.ToString() : "null";
            string t_pack   = _step.pack != null ? _step.pack.PackId : "null";
            Debug.LogWarning($"[OutgameTutorialRunner] 스텝 {_index} 자동 구매 실패(pack={t_pack}, result={t_result}) — 씬 전환 없이 유지.");
            return false;
        }

        // 마지막 스텝이 자동 구매인 저작도 완료로 닫는다(진행도가 시퀀스 끝에 멈춰 재개 불가가 되지 않게).
        if (_index + 1 >= StepCount) OutgameTutorialProgress.Complete();

        // 전투 진입은 BattleEntry 스텝(로비 PlayBtn)이 담당 → 캐리어의 튜토리얼 시작은 항상 false.
        PackHandoff.Set(t_opened, _step.nextScene, false);
        SceneManager.LoadScene(PackOpenScene);
        return false;
    }

    static void CommitAdvance(int _nextIndex)
    {
        OutgameTutorialProgress.CommitStep(_nextIndex);

        // 완료를 인덱스 비교로 파생시키지 않고 여기서 한 번만 확정한다(스텝을 나중에 추가해도 완료 유저가 되살아나지 않게).
        if (_nextIndex >= StepCount) OutgameTutorialProgress.Complete();
    }
}
