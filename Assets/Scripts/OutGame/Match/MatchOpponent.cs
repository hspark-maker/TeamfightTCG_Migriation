using System.Collections.Generic;

// 매칭이 확정한 상대 1명(표시 프로필 + 상대 덱)
public readonly struct MatchOpponent
{
    public readonly MatchProfile Profile;
    public readonly IReadOnlyList<int> Deck;

    /// <summary>상대 덱이 쓸 카드 레벨(0 = 미저작). 덱과 같은 추첨에서 함께 나와야 —
    /// 덱 화면에 그린 상대와 실제 전투의 상대가 세기까지 같아진다.</summary>
    public readonly int CardLevel;

    // 실제 매칭으로 갈아끼우면 상대 덱이 배틀 씬(SyncInitialDecks)에서 도착해 여기는 빈 채로 온다 — 덱 미리보기는 이 판정으로 갈린다.
    public bool IsValid => Deck != null && Deck.Count > 0;

    public MatchOpponent(MatchProfile _profile, IReadOnlyList<int> _deck, int _cardLevel = 0)
    {
        Profile   = _profile;
        Deck      = _deck;
        CardLevel = _cardLevel;
    }
}
