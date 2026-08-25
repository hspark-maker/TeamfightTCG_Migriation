using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 연출용 페이크 매칭 — 잠시 찾는 척한 뒤 내 티어의 AI 덱과 임의 프로필로 상대를 만든다.
public class FakeMatchmaker : IMatchmaker
{
    readonly AIDeckConfig        m_decks;
    readonly OpponentProfilePool m_pool;
    readonly float               m_minSeconds;
    readonly float               m_maxSeconds;

    public FakeMatchmaker(AIDeckConfig _decks, OpponentProfilePool _pool, float _minSeconds = 2.0f, float _maxSeconds = 3.5f)
    {
        m_decks      = _decks;
        m_pool       = _pool;
        m_minSeconds = _minSeconds;
        m_maxSeconds = _maxSeconds;
    }

    public async UniTask<MatchOpponent?> FindOpponentAsync(CancellationToken _ct)
    {
        // UnityEngine.Random을 명시한다 — 전투 결정론 RNG(MatchRandom)를 연출이 오염시키지 않게.
        float t_wait = UnityEngine.Random.Range(Mathf.Min(m_minSeconds, m_maxSeconds), Mathf.Max(m_minSeconds, m_maxSeconds));

        // 취소를 예외가 아니라 null로 새어 나가게 한다(IMatchmaker 계약).
        bool t_canceled = await UniTask.Delay(TimeSpan.FromSeconds(t_wait), cancellationToken: _ct).SuppressCancellationThrow();
        if (t_canceled) return null;

        // 상대는 내 티어에서 뽑는다 — 표시만 흔들면 "실버 상대인데 브론즈 덱"이라는 거짓말이 된다.
        int t_tier = RankManager.TierIndex;

        var t_profile = MatchProfile.OfOpponent(
            m_pool != null ? m_pool.PickName() : OpponentProfilePool.FALLBACK_NAME,
            m_pool != null ? m_pool.PickAvatar() : null);

        IReadOnlyList<CardData> t_deck = m_decks != null ? m_decks.GetDeckForTier(t_tier) : null;

        return new MatchOpponent(t_profile, t_deck);
    }
}
