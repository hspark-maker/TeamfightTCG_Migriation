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
    }

    public List<DeckEntry> decks;

    public List<CardData> GetRandomDeck()
    {
        if (this.decks == null || this.decks.Count == 0) return new List<CardData>();
        return this.decks[Random.Range(0, this.decks.Count)].cards;
    }
}
