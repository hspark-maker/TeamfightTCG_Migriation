using System.Collections.Generic;

/// <summary>
/// 팩 개봉 결과 코드. Success 외는 모두 실패(차감 없음 또는 방어 실패).
/// UI(F-19/F-20)가 실패 사유별로 다른 메시지를 띄울 수 있게 구분한다.
/// </summary>
public enum EPackOpenResult
{
    Success,           // 정상 개봉
    PackNotFound,      // packId 불일치 / 상점에 없음 (차감 없음)
    InsufficientGold,  // 잔액 부족 (차감 없음)
    EmptyPool,         // 팩 풀이 비어 드로우 불가 (차감 없음)
    SpendFailed,       // CanAfford 통과 후 Spend 실패 (방어, 차감 없음)
}

/// <summary>
/// 개봉으로 뽑힌 카드 1장의 스냅샷(불변). isNew는 Grant 시점에만 알 수 있으므로 여기 고정한다
/// (개봉 후엔 전 카드가 IsOwned=true라 UI가 사후 판정 불가). refund는 중복일 때 환급된 Gold(신규면 0).
/// </summary>
public readonly struct DrawnCard
{
    public readonly CardData Card;
    public readonly bool IsNew;   // Grant가 true면 신규 획득, false면 중복
    public readonly long Refund;  // 중복 시 환급된 Gold(신규면 0)

    public DrawnCard(CardData _card, bool _isNew, long _refund)
    {
        Card = _card;
        IsNew = _isNew;
        Refund = _refund;
    }
}

/// <summary>
/// 팩 개봉 결과 스냅샷. 성공 시 뽑힌 카드들과 총 환급액을 담고, 실패 시 사유 코드만 담는다.
/// List를 품는 응집 결과값이라 class로 둔다(값 모델링 선호: 흩어진 static 필드 금지, 명확한 필드 묶음).
/// UI가 신규/중복·환급을 연출하는 유일한 진실원.
/// </summary>
public class @OpenedPack
{
    public readonly EPackOpenResult Result;
    public readonly string PackId;               // 요청 packId(성공/실패 무관 기록)
    public readonly IReadOnlyList<DrawnCard> Cards;
    public readonly long TotalRefund;            // 중복 환급 합계(파생: Cards의 Refund 합)

    public bool Success => Result == EPackOpenResult.Success;
    public int Count => Cards != null ? Cards.Count : 0;

    OpenedPack(EPackOpenResult _result, string _packId, IReadOnlyList<DrawnCard> _cards, long _totalRefund)
    {
        Result = _result;
        PackId = _packId;
        Cards = _cards;
        TotalRefund = _totalRefund;
    }

    /// <summary>성공 결과 조립. 총 환급액은 카드들의 Refund 합으로 파생한다.</summary>
    public static OpenedPack CreateSuccess(string _packId, List<DrawnCard> _cards)
    {
        long t_totalRefund = 0;
        if (_cards != null)
        {
            for (int t_i = 0; t_i < _cards.Count; t_i++) t_totalRefund += _cards[t_i].Refund;
        }
        var t_cards = _cards != null ? _cards.AsReadOnly() : (IReadOnlyList<DrawnCard>)System.Array.Empty<DrawnCard>();
        return new OpenedPack(EPackOpenResult.Success, _packId, t_cards, t_totalRefund);
    }

    /// <summary>실패 결과 조립. 카드 없음·환급 0. 사유 코드로 UI가 분기한다.</summary>
    public static OpenedPack CreateFailure(EPackOpenResult _result, string _packId)
    {
        return new OpenedPack(_result, _packId, System.Array.Empty<DrawnCard>(), 0);
    }
}
