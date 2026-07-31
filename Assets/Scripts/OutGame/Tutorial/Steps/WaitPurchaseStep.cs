using UnityEngine;

/// <summary>상점 구매 대기. 앵커에 딤만 걸고 구매 "성공"으로 완료한다 — 클릭 자체는 완료가 아니다(골드 부족 시 실패).</summary>
[CreateAssetMenu(fileName = "Step_WaitPurchase", menuName = "Card Battle/Outgame Tutorial/Step/Wait Purchase")]
public class WaitPurchaseStep : OutgameTutorialStep
{
    [Tooltip("딤을 걸 구매 버튼")]
    [SerializeField] EOutgameTutorialAnchor anchor;

    [Tooltip("상점 진열·판매 대상을 이 팩으로 덮어쓴다. 미지정이면 상점 기본 진열")]
    [SerializeField] CardPackData pack;

    [Tooltip("중복 카드 1장당 환급 골드")]
    [SerializeField] long duplicateRefundGold;

    public override EOutgameTutorialAnchor Anchor => anchor;
    public override EOutgameTutorialCompletion Completion => EOutgameTutorialCompletion.Purchase;

    public override bool Enter(OutgameTutorialStepContext _context) => true;

    public override bool TryGetForcedPack(out CardPackData _pack, out long _refundGold)
    {
        _pack       = pack;
        _refundGold = duplicateRefundGold;
        return pack != null;
    }
}
