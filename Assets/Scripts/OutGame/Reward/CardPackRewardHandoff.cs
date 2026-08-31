using System.Collections.Generic;

// 개봉 결과(환급 재화·획득 카드)를 로비 씬까지 실어 나르는 씬 캐리어
public static class CardPackRewardHandoff
{
    static readonly CurrencyGainBucket s_pendingRefund = new CurrencyGainBucket();
    static readonly List<int> s_pendingCards = new List<int>();

    public static bool HasPending => !s_pendingRefund.IsEmpty || s_pendingCards.Count > 0;

    // 개봉 결과를 싣는다 — 로비 도달 전 연속 개봉도 남도록 누적
    public static void Set(CurrencyGain _refund, IReadOnlyList<int> _cards)
    {
        s_pendingRefund.Add(_refund);

        if (_cards == null) return;

        for (int t_i = 0; t_i < _cards.Count; t_i++)
        {
            int t_card = _cards[t_i];
            if (!CardCatalog.Contains(t_card)) continue;

            s_pendingCards.Add(t_card);
        }
    }

    // 개봉 결과를 꺼내고 홀더를 비운다 (1회 소비). 환급분은 _into에 합쳐진다
    public static bool TryConsume(CurrencyGainBucket _into, out IReadOnlyList<int> _cards)
    {
        if (!HasPending)
        {
            _cards = null;
            return false;
        }

        _into?.Add(s_pendingRefund);
        _cards = new List<int>(s_pendingCards);

        s_pendingRefund.Clear();
        s_pendingCards.Clear();
        return true;
    }
}
