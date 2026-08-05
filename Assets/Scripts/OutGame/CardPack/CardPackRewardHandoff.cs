using System.Collections.Generic;

// 개봉 결과(환급 골드·획득 카드)를 로비 씬까지 실어 나르는 씬 캐리어
public static class CardPackRewardHandoff
{
    static long s_pendingRefundGold;
    static readonly List<CardData> s_pendingCards = new List<CardData>();

    // 로비에서 연출할 개봉 결과가 실려 있는지
    public static bool HasPending => s_pendingRefundGold > 0 || s_pendingCards.Count > 0;

    // 개봉 결과를 싣는다 — 로비 도달 전 연속 개봉도 남도록 누적
    public static void Set(long _refundGold, IReadOnlyList<CardData> _cards)
    {
        if (_refundGold > 0) s_pendingRefundGold += _refundGold;

        if (_cards == null) return;

        for (int t_i = 0; t_i < _cards.Count; t_i++)
        {
            var t_card = _cards[t_i];
            if (t_card == null) continue;

            s_pendingCards.Add(t_card);
        }
    }

    // 개봉 결과를 꺼내고 홀더를 비운다 (1회 소비)
    public static bool TryConsume(out long _refundGold, out IReadOnlyList<CardData> _cards)
    {
        if (!HasPending)
        {
            _refundGold = 0;
            _cards = null;
            return false;
        }

        _refundGold = s_pendingRefundGold;
        _cards = new List<CardData>(s_pendingCards);

        s_pendingRefundGold = 0;
        s_pendingCards.Clear();
        return true;
    }
}
