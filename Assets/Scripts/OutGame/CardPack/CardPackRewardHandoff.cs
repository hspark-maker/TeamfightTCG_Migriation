using System.Collections.Generic;

// 카드팩 개봉 한 세션의 결과를 로비 씬까지 실어 나르는 씬 캐리어.
// 환급 골드와 획득 카드를 한 캐리어에 담는다 — 같은 시점·같은 소스라 갈라놓으면 로비에서 짝을 다시 맞춰야 한다.
// 환급 지급(CurrencyManager.Earn)·소유 부여는 CardPackOpener.TryPurchase가 이미 원자 영속했다 →
// 여기 실린 값은 "이번에 무엇을 얻었는지"의 연출 표시량뿐이다(지급·저장을 다시 하지 않는다).
// CardData는 ScriptableObject(에셋)라 씬 언로드로 파괴되지 않는다 → static 참조가 씬을 넘어가도 살아 있다.
// PackHandoff의 "1회 소비 후 홀더 비움" 규약을 그대로 따른다.
public static class CardPackRewardHandoff
{
    // 대기 중인 중복 환급 골드 합계.
    static long s_pendingRefundGold;
    // 대기 중인 획득 카드. 둘 다 비어 있으면 곧 pending 없음이라 별도 플래그를 두지 않는다.
    static readonly List<CardData> s_pendingCards = new List<CardData>();

    /// <summary>로비에서 연출할 개봉 결과가 실려 있는지. 골드만·카드만 있어도 연출거리가 된다.</summary>
    public static bool HasPending => s_pendingRefundGold > 0 || s_pendingCards.Count > 0;

    /// <summary>개봉 결과를 싣는다. 로비 도달 전 개봉이 연달아 나도 다 남도록 누적(골드 합산 · 카드 append)한다.
    /// 둘 다 비어 있으면 아무 일도 하지 않는다(호출부에 빈 개봉 분기를 강요하지 않는다).</summary>
    public static void Set(long _refundGold, IReadOnlyList<CardData> _cards)
    {
        if (_refundGold > 0) s_pendingRefundGold += _refundGold;   // 0 이하는 골드 연출거리가 없다(카드는 그대로 처리).

        if (_cards == null) return;

        for (int t_i = 0; t_i < _cards.Count; t_i++)
        {
            var t_card = _cards[t_i];
            if (t_card == null) continue;   // null 원소는 연출에서 빈 칸이 되므로 싣지 않는다.

            s_pendingCards.Add(t_card);
        }
    }

    /// <summary>개봉 결과를 꺼내고 홀더를 비운다(1회 소비). 없으면 0 + null + false.</summary>
    public static bool TryConsume(out long _refundGold, out IReadOnlyList<CardData> _cards)
    {
        if (!HasPending)
        {
            _refundGold = 0;
            _cards = null;
            return false;
        }

        _refundGold = s_pendingRefundGold;
        // 내부 리스트를 그대로 넘기면 아래 Clear가 호출자가 쥔 목록까지 비운다 → 사본을 넘긴다.
        _cards = new List<CardData>(s_pendingCards);

        s_pendingRefundGold = 0;
        s_pendingCards.Clear();
        return true;
    }
}
