using System.Collections.Generic;

// 개봉 결과(환급 재화·획득 카드)를 로비 씬까지 실어 나르는 씬 캐리어
public static class CardPackRewardHandoff
{
    static readonly CurrencyGainBucket s_pendingRefund = new CurrencyGainBucket();
    static readonly List<CardData> s_pendingCards = new List<CardData>();

    // 로비에서 연출할 개봉 결과가 실려 있는지
    public static bool HasPending => !s_pendingRefund.IsEmpty || s_pendingCards.Count > 0;

    // 개봉 결과를 싣는다 — 로비 도달 전 연속 개봉도 남도록 누적
    public static void Set(CurrencyGain _refund, IReadOnlyList<CardData> _cards)
    {
        s_pendingRefund.Add(_refund);

        if (_cards == null) return;

        for (int t_i = 0; t_i < _cards.Count; t_i++)
        {
            var t_card = _cards[t_i];
            if (t_card == null) continue;

            s_pendingCards.Add(t_card);
        }
    }

    // 개봉 결과를 꺼내고 홀더를 비운다 (1회 소비). 환급분은 _into에 합쳐진다
    public static bool TryConsume(CurrencyGainBucket _into, out IReadOnlyList<CardData> _cards)
    {
        if (!HasPending)
        {
            _cards = null;
            return false;
        }

        _into?.Add(s_pendingRefund);
        _cards = new List<CardData>(s_pendingCards);

        s_pendingRefund.Clear();
        s_pendingCards.Clear();
        return true;
    }
}
