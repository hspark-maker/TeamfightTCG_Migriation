using System.Collections.Generic;

// 팩 개봉 결과 코드 (Success 외는 모두 실패)
public enum EPackOpenResult
{
    Success,
    PackNotFound,
    InsufficientGold,
    EmptyPool,
    SpendFailed,
}

// 개봉으로 뽑힌 카드 1장의 스냅샷(불변)
public readonly struct DrawnCard
{
    public readonly CardData Card;
    public readonly bool IsNew;
    public readonly long Refund;

    public DrawnCard(CardData _card, bool _isNew, long _refund)
    {
        Card = _card;
        IsNew = _isNew;
        Refund = _refund;
    }
}

// 팩 개봉 결과 스냅샷 (성공: 뽑힌 카드·총 환급 / 실패: 사유 코드)
public class @OpenedPack
{
    public readonly EPackOpenResult Result;
    public readonly string PackId;
    public readonly IReadOnlyList<DrawnCard> Cards;
    public readonly CurrencyGain TotalRefund;

    public bool Success => Result == EPackOpenResult.Success;
    public int Count => Cards != null ? Cards.Count : 0;

    OpenedPack(EPackOpenResult _result, string _packId, IReadOnlyList<DrawnCard> _cards, CurrencyGain _totalRefund)
    {
        Result = _result;
        PackId = _packId;
        Cards = _cards;
        TotalRefund = _totalRefund;
    }

    // 성공 결과 조립 — 총 환급액은 카드들의 Refund 합(종류는 팩 단위 속성이라 결제 재화를 그대로 쓴다)
    public static OpenedPack CreateSuccess(string _packId, List<DrawnCard> _cards, ECurrencyType _refundType)
    {
        long t_totalRefund = 0;
        if (_cards != null)
        {
            for (int t_i = 0; t_i < _cards.Count; t_i++) t_totalRefund += _cards[t_i].Refund;
        }
        var t_cards = _cards != null ? _cards.AsReadOnly() : (IReadOnlyList<DrawnCard>)System.Array.Empty<DrawnCard>();
        return new OpenedPack(EPackOpenResult.Success, _packId, t_cards, new CurrencyGain(_refundType, t_totalRefund));
    }

    // 실패 결과 조립 — 카드 없음·환급 0
    public static OpenedPack CreateFailure(EPackOpenResult _result, string _packId)
    {
        return new OpenedPack(_result, _packId, System.Array.Empty<DrawnCard>(), CurrencyGain.None);
    }
}
