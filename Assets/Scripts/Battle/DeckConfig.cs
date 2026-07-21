using System.Collections.Generic;
using System.Linq;

public static class DeckConfig
{
    public static List<CardData> PlayerDeck { get; private set; }

    public static void Set(IEnumerable<CardData> _deck)
    {
        PlayerDeck = new List<CardData>(_deck.Where(d => d != null));
    }

    public static bool HasDeck => PlayerDeck != null && PlayerDeck.Count == DeckSaveManager.DECK_SIZE && PlayerDeck.All(d => d != null);

    public static bool IsMultiplayer { get; private set; }

    public static void SetMultiplayer(bool _value) => IsMultiplayer = _value;
}
