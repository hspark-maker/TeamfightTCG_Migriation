using UnityEngine;

/// <summary>입력 없이 팩을 구매해 개봉 오버레이를 여는 스텝. 씬은 그대로 두고 로비 위에 개봉을 얹는다.</summary>
[CreateAssetMenu(fileName = "Step_AutoPurchase", menuName = "Card Battle/Outgame Tutorial/Step/Auto Purchase")]
public class AutoPurchaseStep : OutgameTutorialStep
{
    [Tooltip("자동으로 구매할 카드팩")]
    [SerializeField] CardPackData pack;

    [Tooltip("중복 카드 1장당 환급 골드")]
    [SerializeField] long duplicateRefundGold;

    [Tooltip("획득 후 이동할 씬. 비우거나 지금 씬과 같으면 오버레이만 닫고 제자리에 남는다(일반값). " +
             "다른 씬을 넣으면 실제로 그 씬을 로드한다 — 전투 진입은 BattleEntry 스텝의 몫이니 보통 비워 둔다.")]
    [SerializeField] string nextScene;

    public override EOutgameTutorialCompletion Completion => EOutgameTutorialCompletion.Auto;
    public override bool LeavesScene => false;

    public override bool Enter(OutgameTutorialStepContext _context)
    {
        // 열 화면이 없으면 사지 않는다 — 결제 뒤엔 되돌릴 수 없어(아래 TryOpen 실패 주석 참조)
        // 커밋 전에 끊는 것이 유일한 안전판이다. 다음 부트에 재시도된다.
        if (PackOpenOverlay.Instance == null)
        {
            Debug.LogWarning($"[AutoPurchaseStep] 스텝 {_context.ChapterIndex}-{_context.StepIndex} 개봉 오버레이 미배치 — 구매 보류(로비 씬 배선 확인).");
            return false;
        }

        // 불변식: 커밋이 실행보다 앞선다. 구매 직후 강제종료 시 "소유는 생겼는데 진행도는 0"이 되어
        // 레거시 마이그레이션이 온보딩을 영구 스킵시키는 구멍을 원천 봉쇄한다. 순서를 바꾸지 말 것.
        _context.CommitAdvance();

        var t_opened = CardPackOpener.TryPurchase(pack, duplicateRefundGold);
        if (t_opened == null || !t_opened.Success)
        {
            // 실패는 차감 없이 반환되므로(TryPurchase 보장) 커밋만 되돌리면 원상복구된다 — 다음 부트에 재시도.
            _context.Rollback();

            string t_result = t_opened != null ? t_opened.Result.ToString() : "null";
            string t_pack   = pack != null ? pack.PackId : "null";
            Debug.LogWarning($"[AutoPurchaseStep] 스텝 {_context.ChapterIndex}-{_context.StepIndex} 자동 구매 실패(pack={t_pack}, result={t_result}) — 개봉 없이 유지.");
            return false;
        }

        // 마지막 스텝이 자동 구매인 저작도 완료로 닫는다(진행도가 시퀀스 끝에 멈춰 재개 불가가 되지 않게).
        _context.CompleteIfLast();

        // 전투 진입은 BattleEntry 스텝(로비 PlayBtn)이 담당 → 캐리어의 튜토리얼 시작은 항상 false.
        PackHandoff.Set(t_opened, pack, nextScene, false);

        // 오버레이가 안 열려도 Rollback하지 않는다 — 구매는 이미 원자 영속돼 되돌릴 수 없고,
        // 진행도만 되돌리면 다음 부트에 같은 팩을 또 사서 골드가 이중으로 나간다.
        if (!PackOpenOverlay.TryOpen())
        {
            string t_openedPack = pack != null ? pack.PackId : "null";
            Debug.LogWarning($"[AutoPurchaseStep] 스텝 {_context.ChapterIndex}-{_context.StepIndex} 개봉 오버레이 열기 실패(pack={t_openedPack}) — 구매는 유지, 개봉 연출만 생략.");
        }
        return false;
    }
}
