using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "AIDeckConfig", menuName = "Card Battle/AI Deck Config")]
public class AIDeckConfig : ScriptableObject
{
    [System.Serializable]
    public class DeckEntry
    {
        public string deckName;
        [CardId] public List<int> cardIds;
        public IReadOnlyList<int> CardIds => cardIds;

        [Tooltip("등장 시작 티어 인덱스(등급×4 + 단계-1). 0 = 브론즈 1")]
        public int fromTier;
        [Tooltip("등장 종료 티어(포함). 0 = 제한 없음, 시작 티어보다 작으면 비활성")]
        public int toTier;
        [Tooltip("같은 티어 안에서의 등장 가중치. 0 이하면 1")]
        public int weight;

        [Tooltip("이 덱 카드가 쓸 레벨의 하한. 0 = 미저작(바닥 레벨 고정)")]
        public int fromLevel;
        [Tooltip("이 덱 카드가 쓸 레벨의 상한(포함). 0 = 미저작(바닥 레벨 고정)")]
        public int toLevel;

        public int ToTierOrMax => this.toTier == 0 ? int.MaxValue : this.toTier;
        public int WeightOrOne => this.weight > 0 ? this.weight : 1;

        /// <summary>레벨 범위가 저작됐는가. 한쪽만 채운 반쪽 저작은 미저작으로 본다 —
        /// 그래야 시트 빈칸이 조용히 1레벨 덱을 만렙으로 만들지 않는다.</summary>
        public bool HasAuthoredLevel => this.fromLevel > 0 && this.toLevel > 0;

        /// <summary>이번 판에 이 덱이 쓸 레벨 하나를 뽑는다. 미저작이면 0(= 호출부가 바닥으로 떨어뜨린다).
        /// <see cref="UnityEngine.Random"/>이다 — MatchRandom을 소비하면 멀티 셔플 시드가 밀린다.</summary>
        public int RollLevel()
        {
            if (!HasAuthoredLevel) return 0;

            int t_min = Mathf.Min(this.fromLevel, this.toLevel);
            int t_max = Mathf.Max(this.fromLevel, this.toLevel);
            long t_width = (long)t_max - t_min + 1L;
            return (int)(t_min + RollBelow(t_width));
        }
    }

    [Tooltip("레거시 저작값. 런타임은 AIDeck 서버 표만 사용하며 이 목록으로 폴백하지 않는다.")]
    public List<DeckEntry> decks;

    public List<int> GetRandomDeck() => GetRandomDeck(out _);

    /// <summary><paramref name="_cardLevel"/>은 뽑힌 덱의 저작 레벨 범위에서 굴린 값 하나다.
    /// 0이면 미저작 — 호출부가 바닥 레벨로 떨어뜨린다. 덱 하나당 한 번만 굴린다(카드마다 흔들리지 않게).</summary>
    public List<int> GetRandomDeck(out int _cardLevel)
    {
        IReadOnlyList<DeckEntry> t_decks = ResolveDecks();
        DeckEntry t_entry = PickRandom(t_decks);
        return TakeDeck(t_entry, out _cardLevel);
    }

    DeckEntry PickRandom(IReadOnlyList<DeckEntry> _decks)
    {
        if (_decks == null || _decks.Count == 0) return null;

        var t_candidates = new List<DeckEntry>();
        foreach (DeckEntry t_entry in _decks)
            if (HasValidCards(t_entry)) t_candidates.Add(t_entry);

        if (t_candidates.Count == 0) return null;
        return t_candidates[Random.Range(0, t_candidates.Count)];
    }

    /// <summary>뽑힌 덱에서 카드 목록과 레벨을 함께 꺼낸다 — 레벨 추첨이 덱당 정확히 1회가 되는 지점.</summary>
    static List<int> TakeDeck(DeckEntry _entry, out int _cardLevel)
    {
        if (_entry == null)
        {
            _cardLevel = 0;
            return new List<int>();
        }

        _cardLevel = _entry.RollLevel();
        return new List<int>(_entry.cardIds);
    }

    public List<int> GetDeckForTier(int _tier) => GetDeckForTier(_tier, out _);

    /// <summary><paramref name="_cardLevel"/>은 뽑힌 덱의 저작 레벨 범위에서 굴린 값 하나다(0 = 미저작).</summary>
    public List<int> GetDeckForTier(int _tier, out int _cardLevel)
    {
        IReadOnlyList<DeckEntry> t_decks = ResolveDecks();
        if (t_decks == null || t_decks.Count == 0)
        {
            _cardLevel = 0;
            return new List<int>();
        }

        for (int t_tier = Mathf.Max(0, _tier); t_tier >= 0; t_tier--)
        {
            DeckEntry t_entry = PickWeighted(t_decks, t_tier);
            if (t_entry != null) return TakeDeck(t_entry, out _cardLevel);
        }

        return TakeDeck(PickRandom(t_decks), out _cardLevel);
    }

    IReadOnlyList<DeckEntry> ResolveDecks()
    {
        AIDeckSpec.TryGetDecks(out IReadOnlyList<DeckEntry> t_spec);
        return t_spec;
    }

    static DeckEntry PickWeighted(IReadOnlyList<DeckEntry> _decks, int _tier)
    {
        long t_totalWeight = 0;
        foreach (DeckEntry t_entry in _decks)
        {
            if (!IsAvailableAt(t_entry, _tier)) continue;
            t_totalWeight += t_entry.WeightOrOne;
        }

        if (t_totalWeight <= 0) return null;

        long t_roll = RollBelow(t_totalWeight);
        foreach (DeckEntry t_entry in _decks)
        {
            if (!IsAvailableAt(t_entry, _tier)) continue;

            t_roll -= t_entry.WeightOrOne;
            if (t_roll < 0) return t_entry;
        }

        return null;
    }

    /// <summary>Unity RNG만 사용해 [0, max) long을 뽑는다. float 경유 편향과 int inclusive 상한 overflow를 피한다.</summary>
    static long RollBelow(long _maxExclusive)
    {
        if (_maxExclusive <= 0) return 0;
        const ulong RANGE = 1UL << 63;
        ulong t_bound = (ulong)_maxExclusive;
        ulong t_limit = RANGE - RANGE % t_bound;
        ulong t_value;
        do
        {
            t_value = ((ulong)Random.Range(0, 1 << 21) << 42) |
                      ((ulong)Random.Range(0, 1 << 21) << 21) |
                       (ulong)Random.Range(0, 1 << 21);
        }
        while (t_value >= t_limit);
        return (long)(t_value % t_bound);
    }

    static bool IsAvailableAt(DeckEntry _entry, int _tier)
        => HasValidCards(_entry)
        && _entry.fromTier <= _tier
        && _tier <= _entry.ToTierOrMax;

    static bool HasValidCards(DeckEntry _entry)
    {
        if (_entry?.cardIds == null || _entry.cardIds.Count != DeckSaveManager.DECK_SIZE) return false;
        foreach (int t_cardId in _entry.cardIds)
            if (t_cardId <= 0) return false;
        return true;
    }

}
