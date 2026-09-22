using System.Collections.Generic;

// 팩 개봉 결과 코드
public enum EPackOpenResult
{
    Success,
    PackNotFound,
    InsufficientGold,
    EmptyPool,
    SpendFailed,
    NotReady,
    RankLocked,

    // 서버에 닿지 못해 결과를 알 수 없다. 팩·재화 사정이 아니라 연결 문제라 안내 표면이 갈린다.
    NetworkFailed,
}

// 개봉으로 뽑힌 카드 1장의 스냅샷(불변)
public readonly struct DrawnCard
{
    public readonly int CardId;
    public readonly bool IsNew;
    public readonly long Refund;

    // 환급 재화 종류. 액수만으로는 무슨 재화인지 답할 수 없어 뷰가 아이콘을 고를 근거가 없다.
    public readonly ECurrencyType RefundType;

    // 신규 여부만 표시하는 지급. 중복 카드에 별도 보상을 만들지 않는다.
    public DrawnCard(int _cardId, bool _isNew)
    {
        CardId = _cardId;
        IsNew = _isNew;
        Refund = 0L;
        RefundType = ECurrencyType.Gold;
    }

    // 환급이 있으면 재화 종류가 필수다 — 기본값을 열어두면 조각 환급이 골드 코인으로 조용히 표시된다.
    public DrawnCard(int _cardId, bool _isNew, long _refund, ECurrencyType _refundType)
    {
        CardId = _cardId;
        IsNew = _isNew;
        Refund = _refund;
        RefundType = _refundType;
    }
}

// 팩 개봉 결과 스냅샷 (성공: 뽑힌 카드·총 환급 / 실패: 사유 코드)
public class OpenedPack
{
    public readonly EPackOpenResult Result;
    public readonly IReadOnlyList<DrawnCard> Cards;
    public readonly CurrencyGain TotalRefund;

    public bool Success => Result == EPackOpenResult.Success;

    // 성공 결과 조립 — 총 환급액은 카드별 Refund 합(재화 종류는 결제 재화와 별개로 팩이 저작한다)
    public static OpenedPack CreateSuccess(List<DrawnCard> _cards, ECurrencyType _refundType)
    {
        long t_totalRefund = 0;
        if (_cards != null)
        {
            for (int t_i = 0; t_i < _cards.Count; t_i++) t_totalRefund += _cards[t_i].Refund;
        }

        return CreateSuccess(_cards, new CurrencyGain(_refundType, t_totalRefund));
    }

    // 서버가 확정한 중복 보상 합계. 낱장 연출에 가상의 환급액을 배분하지 않는다.
    public static OpenedPack CreateSuccess(List<DrawnCard> _cards, CurrencyGain _totalRefund)
    {
        var t_cards = _cards != null ? _cards.AsReadOnly() : (IReadOnlyList<DrawnCard>)System.Array.Empty<DrawnCard>();
        return new OpenedPack(EPackOpenResult.Success, t_cards, _totalRefund);
    }

    // 실패 결과 조립 — 카드 없음·환급 0
    public static OpenedPack CreateFailure(EPackOpenResult _result)
        => new OpenedPack(_result, System.Array.Empty<DrawnCard>(), CurrencyGain.None);

    OpenedPack(EPackOpenResult _result, IReadOnlyList<DrawnCard> _cards, CurrencyGain _totalRefund)
    {
        Result = _result;
        Cards = _cards;
        TotalRefund = _totalRefund;
    }
}
