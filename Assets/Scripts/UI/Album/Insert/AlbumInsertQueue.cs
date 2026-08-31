using System.Collections.Generic;
using UnityEngine;

// 획득한 신규 카드를 삽입 세션까지 실어 나르는 휘발성 캐리어(CardPackRewardHandoff와 같은 모양).
// 저장하지 않는다 — 세션 도중 앱을 끄면 카드는 그냥 꽂힌 상태가 된다(설계상 허용).
public static class AlbumInsertQueue
{
    static readonly List<int> s_pending = new List<int>();

    public static bool HasPending => s_pending.Count > 0;

    // 세션 도달 전 연속 개봉도 남도록 누적한다.
    public static void Enqueue(IReadOnlyList<int> _cards)
    {
        if (_cards == null) return;

        for (int t_i = 0; t_i < _cards.Count; t_i++)
        {
            var t_card = _cards[t_i];
            if (t_card <= 0) continue;

            s_pending.Add(t_card);
        }
    }

    // 1회 소비 — 꺼내면 홀더는 비워진다.
    public static bool TryConsume(out IReadOnlyList<int> _cards)
    {
        if (s_pending.Count == 0)
        {
            _cards = null;
            return false;
        }

        _cards = new List<int>(s_pending);
        s_pending.Clear();
        return true;
    }

    public static void Clear()
    {
        s_pending.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay()
    {
        s_pending.Clear();
    }
}
