using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "AIDeckConfig", menuName = "Card Battle/AI Deck Config")]
public class AIDeckConfig : ScriptableObject
{
    [System.Serializable]
    public class DeckEntry
    {
        public string deckName;
        public List<CardData> cards;

        [Tooltip("등장 시작 티어 인덱스(등급×4 + 단계-1). 0 = 브론즈 1")]
        public int fromTier;
        [Tooltip("등장 종료 티어(포함). 0 = 제한 없음, 시작 티어보다 작으면 비활성")]
        public int toTier;
        [Tooltip("같은 티어 안에서의 등장 가중치. 0 이하면 1")]
        public int weight;

        public int ToTierOrMax => this.toTier == 0 ? int.MaxValue : this.toTier;
        public int WeightOrOne => this.weight > 0 ? this.weight : 1;
    }

    public List<DeckEntry> decks;

    public List<CardData> GetRandomDeck()
    {
        if (this.decks == null || this.decks.Count == 0) return new List<CardData>();

        var t_candidates = new List<DeckEntry>();
        foreach (DeckEntry t_entry in this.decks)
            if (HasValidCards(t_entry)) t_candidates.Add(t_entry);

        if (t_candidates.Count == 0) return new List<CardData>();
        return t_candidates[Random.Range(0, t_candidates.Count)].cards;
    }

    public List<CardData> GetDeckForTier(int _tier)
    {
        if (this.decks == null || this.decks.Count == 0) return new List<CardData>();

        for (int t_tier = Mathf.Max(0, _tier); t_tier >= 0; t_tier--)
        {
            List<CardData> t_deck = PickWeighted(t_tier);
            if (t_deck != null) return t_deck;
        }

        return GetRandomDeck();
    }

    List<CardData> PickWeighted(int _tier)
    {
        int t_totalWeight = 0;
        foreach (DeckEntry t_entry in this.decks)
        {
            if (!IsAvailableAt(t_entry, _tier)) continue;
            t_totalWeight += t_entry.WeightOrOne;
        }

        if (t_totalWeight <= 0) return null;

        int t_roll = Random.Range(0, t_totalWeight);
        foreach (DeckEntry t_entry in this.decks)
        {
            if (!IsAvailableAt(t_entry, _tier)) continue;

            t_roll -= t_entry.WeightOrOne;
            if (t_roll < 0) return t_entry.cards;
        }

        return null;
    }

    static bool IsAvailableAt(DeckEntry _entry, int _tier)
        => HasValidCards(_entry)
        && _entry.fromTier <= _tier
        && _tier <= _entry.ToTierOrMax;

    static bool HasValidCards(DeckEntry _entry)
    {
        if (_entry?.cards == null || _entry.cards.Count != DeckSaveManager.DECK_SIZE) return false;
        foreach (CardData t_card in _entry.cards)
            if (t_card == null) return false;
        return true;
    }
}
