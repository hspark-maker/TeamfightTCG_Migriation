using System.Collections.Generic;
using UnityEngine;

// AIDeck 표를 기존 DeckEntry 모양으로 조립한다.
// 덱 크기가 DeckSaveManager.DECK_SIZE로 고정이라 카드 칸은 자식 표가 아니라 card1~card6 컬럼이다 —
// 조인도 slot 검증도 없고, 한 행이 곧 한 덱이다.
// 표를 못 읽으면 false를 반환해 AIDeckConfig가 구 SO로 원자 폴백한다.
public static class AIDeckSpec
{
    /// <summary>표가 가진 카드 칸 컬럼 수(card1~card6). <see cref="DeckSaveManager.DECK_SIZE"/>와 같아야 한다 —
    /// 칸을 컬럼으로 편 대가로 생긴 결합이라, 어긋나면 조용히 폴백하지 말고 소리를 내야 한다.</summary>
    const int CARD_COLUMNS = 6;

    static bool s_loaded;
    static bool s_ready;
    // 조립에 쓴 스냅샷. SpecSource.Reload()가 새 스냅샷을 만들면 참조가 달라져 자동으로 다시 조립한다 —
    // 반대로 SpecSource가 이쪽을 부르게 하면 OutGame -> Battle 역참조가 생긴다.
    static SpecDataManager s_source;
    static readonly List<AIDeckConfig.DeckEntry> s_decks = new List<AIDeckConfig.DeckEntry>();

    public static void Init() => EnsureLoaded();

    public static bool TryGetDecks(out IReadOnlyList<AIDeckConfig.DeckEntry> _decks)
    {
        EnsureLoaded();
        // 실패분은 내보내지 않는다 — 반환값을 안 보는 호출자가 생겨도 빈 cardIds 덱이 새지 않게.
        _decks = s_ready ? s_decks : System.Array.Empty<AIDeckConfig.DeckEntry>();
        return s_ready;
    }

    static void EnsureLoaded()
    {
        SpecDataManager t_manager = SpecSource.Manager;
        if (s_loaded && ReferenceEquals(s_source, t_manager)) return;
        s_loaded = true;
        s_source = t_manager;
        s_ready = false;
        s_decks.Clear();

        IReadOnlyList<AIDeck> t_rows = t_manager?.AIDeck?.All;
        if (t_rows == null || t_rows.Count == 0) return;

        if (DeckSaveManager.DECK_SIZE != CARD_COLUMNS)
        {
            Debug.LogError($"[AIDeckSpec] 덱 크기 {DeckSaveManager.DECK_SIZE}가 표의 카드 칸 수 {CARD_COLUMNS}와 " +
                           "달라 구 SO로 폴백한다. AIDeck 시트의 칸 컬럼을 맞춰라.");
            return;
        }

        var t_sorted = new List<AIDeck>(t_rows);
        // 아래 Sort의 비교자가 a.id를 역참조하므로 null 행은 정렬 전에 걸러야 한다.
        foreach (AIDeck t_row in t_sorted)
            if (t_row == null)
            {
                Debug.LogError("[AIDeckSpec] AIDeck 표에 null 행이 있어 구 SO로 폴백한다.");
                return;
            }
        t_sorted.Sort((a, b) => a.id.CompareTo(b.id));

        bool t_valid = true;
        var t_seenDeckIds = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (AIDeck t_row in t_sorted)
        {
            if (string.IsNullOrEmpty(t_row.deckId) || !t_seenDeckIds.Add(t_row.deckId))
            {
                t_valid = false;
                Debug.LogError($"[AIDeckSpec] 비어 있거나 중복인 deckId를 제외한다: '{t_row.deckId}'.");
                continue;
            }
            if (t_row.fromTier < 0 || (t_row.toTier != 0 && t_row.toTier < t_row.fromTier))
            {
                t_valid = false;
                Debug.LogError($"[AIDeckSpec] '{t_row.deckId}'의 티어 구간이 유효하지 않아 구 SO로 폴백한다.");
            }

            List<int> t_cardIds = BuildCards(t_row);
            if (t_cardIds == null)
            {
                t_valid = false;
                Debug.LogError($"[AIDeckSpec] '{t_row.deckId}'의 카드 칸에 0 이하 id가 있어 후보에서 제외된다.");
                t_cardIds = new List<int>();
            }

            s_decks.Add(new AIDeckConfig.DeckEntry
            {
                deckName = t_row.deckName,
                cardIds = t_cardIds,
                fromTier = t_row.fromTier,
                toTier = t_row.toTier,
                weight = t_row.weight,
                fromLevel = t_row.fromLevel,
                toLevel = t_row.toLevel,
            });
        }

        s_ready = t_valid && s_decks.Count == t_rows.Count;
    }

    /// <summary>칸 컬럼을 덱 순서 그대로 편다. 하나라도 0 이하면 null — 부분 덱을 만들지 않는다.</summary>
    static List<int> BuildCards(AIDeck _row)
    {
        var t_cardIds = new List<int>(DeckSaveManager.DECK_SIZE)
        {
            _row.card1, _row.card2, _row.card3, _row.card4, _row.card5, _row.card6,
        };
        foreach (int t_cardId in t_cardIds)
            if (t_cardId <= 0) return null;
        return t_cardIds;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_loaded = false;
        s_ready = false;
        s_source = null;
        s_decks.Clear();
    }
}
