using System.Collections.Generic;
using UnityEngine;

// AIDeck/AIDeckCard 부모·자식 표를 기존 DeckEntry 모양으로 조립한다.
// 표를 못 읽으면 false를 반환해 AIDeckConfig가 구 SO로 원자 폴백한다.
public static class AIDeckSpec
{
    static bool s_loaded;
    static readonly List<AIDeckConfig.DeckEntry> s_decks = new List<AIDeckConfig.DeckEntry>();

    public static void Init() => EnsureLoaded();

    public static bool TryGetDecks(out IReadOnlyList<AIDeckConfig.DeckEntry> _decks)
    {
        EnsureLoaded();
        _decks = s_decks;
        return s_decks.Count > 0;
    }

    static void EnsureLoaded()
    {
        if (s_loaded) return;
        s_loaded = true;

        SpecDataManager t_manager = SpecSource.Manager;
        IReadOnlyList<AIDeck> t_deckRows = t_manager?.AIDeck?.All;
        IReadOnlyList<AIDeckCard> t_cardRows = t_manager?.AIDeckCard?.All;
        if (t_deckRows == null || t_deckRows.Count == 0 || t_cardRows == null || t_cardRows.Count == 0)
            return;

        var t_cardsByDeck = new Dictionary<string, List<AIDeckCard>>(System.StringComparer.Ordinal);
        foreach (AIDeckCard t_row in t_cardRows)
        {
            if (t_row == null || string.IsNullOrEmpty(t_row.deckId)) continue;
            if (!t_cardsByDeck.TryGetValue(t_row.deckId, out List<AIDeckCard> t_rows))
                t_cardsByDeck[t_row.deckId] = t_rows = new List<AIDeckCard>();
            t_rows.Add(t_row);
        }

        var t_sortedDecks = new List<AIDeck>(t_deckRows);
        t_sortedDecks.Sort((a, b) => a.id.CompareTo(b.id));
        var t_seenDeckIds = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (AIDeck t_row in t_sortedDecks)
        {
            if (t_row == null || string.IsNullOrEmpty(t_row.deckId) || !t_seenDeckIds.Add(t_row.deckId))
            {
                Debug.LogError($"[AIDeckSpec] 비어 있거나 중복인 deckId를 제외한다: '{t_row?.deckId}'.");
                continue;
            }

            var t_cardIds = new List<int>();
            bool t_validSlots = TryBuildCards(t_row.deckId, t_cardsByDeck, t_cardIds);
            if (!t_validSlots)
                Debug.LogError($"[AIDeckSpec] '{t_row.deckId}'의 카드 칸이 0~5 각 1개가 아니라 후보에서 제외된다.");

            s_decks.Add(new AIDeckConfig.DeckEntry
            {
                deckName = t_row.deckName,
                cardIds = t_cardIds,
                fromTier = t_row.fromTier,
                toTier = t_row.toTier,
                weight = t_row.weight,
            });
        }
    }

    static bool TryBuildCards(
        string _deckId,
        Dictionary<string, List<AIDeckCard>> _cardsByDeck,
        List<int> _cardIds)
    {
        if (!_cardsByDeck.TryGetValue(_deckId, out List<AIDeckCard> t_rows)) return false;

        var t_slots = new int[DeckSaveManager.DECK_SIZE];
        var t_seen = new bool[DeckSaveManager.DECK_SIZE];
        bool t_valid = t_rows.Count == DeckSaveManager.DECK_SIZE;
        foreach (AIDeckCard t_row in t_rows)
        {
            if (t_row.slot < 0 || t_row.slot >= DeckSaveManager.DECK_SIZE || t_seen[t_row.slot])
            {
                t_valid = false;
                continue;
            }
            t_seen[t_row.slot] = true;
            t_slots[t_row.slot] = t_row.cardId;
            if (t_row.cardId <= 0) t_valid = false;
        }

        for (int t_slot = 0; t_slot < t_slots.Length; t_slot++)
        {
            if (!t_seen[t_slot]) t_valid = false;
            _cardIds.Add(t_slots[t_slot]);
        }
        if (!t_valid) _cardIds.Clear();
        return t_valid;
    }
}
