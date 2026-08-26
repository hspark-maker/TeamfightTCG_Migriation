using System.Collections.Generic;

// 매칭이 확정한 상대 1명(표시 프로필 + 상대 덱)
public readonly struct MatchOpponent
{
    public readonly MatchProfile Profile;
    public readonly IReadOnlyList<int> Deck;

    // 실제 매칭으로 갈아끼우면 상대 덱이 배틀 씬(SyncInitialDecks)에서 도착해 여기는 빈 채로 온다 — 덱 미리보기는 이 판정으로 갈린다.
    public bool IsValid => Deck != null && Deck.Count > 0;

    public MatchOpponent(MatchProfile _profile, IReadOnlyList<int> _deck)
    {
        Profile = _profile;
        Deck    = _deck;
    }
}
