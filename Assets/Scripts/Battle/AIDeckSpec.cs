using System.Collections.Generic;
using UnityEngine;

// AIDeck 표를 기존 DeckEntry 모양으로 조립한다.
// 덱 크기가 DeckSaveManager.DECK_SIZE로 고정이라 카드 칸은 자식 표가 아니라 card1~card6 컬럼이다 —
// 조인도 slot 검증도 없고, 한 행이 곧 한 덱이다.
// 표를 못 읽으면 false를 반환하고 초기화가 복구 화면으로 전환한다. SO 수치 폴백은 허용하지 않는다.
public static class AIDeckSpec
{
    /// <summary>표가 가진 카드 칸 컬럼 수(card1~card6). <see cref="DeckSaveManager.DECK_SIZE"/>와 같아야 한다 —
    /// 칸을 컬럼으로 편 대가로 생긴 결합이라, 어긋나면 조용히 폴백하지 말고 소리를 내야 한다.</summary>
    const int CARD_COLUMNS = 6;

    static bool s_loaded;
    static bool s_ready;
    static string s_error;
    // 실패가 '데이터 손상'인지 '앱이 콘텐츠보다 낡음'인지 가른다 — 후자는 재시도로 안 풀리므로
    // 초기화가 복구 화면이 아니라 업데이트 안내로 가야 한다.
    static bool s_updateRequired;
    // 조립에 쓴 스냅샷. SpecSource.Reload()가 새 스냅샷을 만들면 참조가 달라져 자동으로 다시 조립한다 —
    // 반대로 SpecSource가 이쪽을 부르게 하면 OutGame -> Battle 역참조가 생긴다.
    static SpecDataManager s_source;
    static readonly List<AIDeckConfig.DeckEntry> s_decks = new List<AIDeckConfig.DeckEntry>();
    static readonly Dictionary<string, AIDeckConfig.DeckEntry> s_decksById =
        new Dictionary<string, AIDeckConfig.DeckEntry>(System.StringComparer.Ordinal);

    public static void Init() => EnsureLoaded();

    /// <summary>표가 이 앱이 모르는 카드를 참조해 실패했다 = 앱 업데이트가 필요하다.</summary>
    public static bool UpdateRequired
    {
        get { EnsureLoaded(); return s_updateRequired; }
    }

    public static bool TryGetDecks(out IReadOnlyList<AIDeckConfig.DeckEntry> _decks)
    {
        EnsureLoaded();
        // 실패분은 내보내지 않는다 — 반환값을 안 보는 호출자가 생겨도 빈 cardIds 덱이 새지 않게.
        _decks = s_ready ? s_decks : System.Array.Empty<AIDeckConfig.DeckEntry>();
        return s_ready;
    }

    /// <summary>안정 키로 덱 한 벌을 찾는다. 모험처럼 등장 풀과 무관하게 고정 덱을 참조하는 경로가 쓴다.</summary>
    public static bool TryGetDeck(string _deckId, out IReadOnlyList<int> _cardIds)
    {
        EnsureLoaded();
        if (s_ready && !string.IsNullOrEmpty(_deckId) &&
            s_decksById.TryGetValue(_deckId, out AIDeckConfig.DeckEntry t_entry))
        {
            _cardIds = t_entry.CardIds;
            return true;
        }

        _cardIds = System.Array.Empty<int>();
        return false;
    }

    public static bool TryValidateRequired(out string _error)
    {
        EnsureLoaded();
        _error = s_ready ? null : s_error ?? "AIDeck 서버 표를 사용할 수 없다.";
        return s_ready;
    }

    static void EnsureLoaded()
    {
        SpecDataManager t_manager = SpecSource.Manager;
        if (s_loaded && ReferenceEquals(s_source, t_manager)) return;
        if (!CardCatalog.IsReady)
        {
            // 래치하지 않는다 — 여기서 굳으면 카탈로그가 선 뒤에도 같은 스냅샷인 한 다시 조립하지 않아
            // 그 세션 내내 AI 덱이 빈 채로 남는다. 순서가 어긋난 호출은 조립을 미루고 다음 호출에 맡긴다.
            Debug.LogError("[AIDeckSpec] CardCatalog보다 먼저 AIDeck 조립을 요청했다 — 조립을 미룬다.");
            return;
        }
        s_loaded = true;
        s_source = t_manager;
        s_ready = false;
        s_error = null;
        s_updateRequired = false;
        s_decks.Clear();
        s_decksById.Clear();

        IReadOnlyList<AIDeck> t_rows = t_manager?.AIDeck?.All;
        if (t_rows == null || t_rows.Count == 0)
        {
            Fail("AIDeck 서버 표가 비어 있다.");
            return;
        }

        if (DeckSaveManager.DECK_SIZE != CARD_COLUMNS)
        {
            Fail($"덱 크기 {DeckSaveManager.DECK_SIZE}가 표의 카드 칸 수 {CARD_COLUMNS}와 다르다.");
            return;
        }
        var t_sorted = new List<AIDeck>(t_rows);
        // 아래 Sort의 비교자가 a.id를 역참조하므로 null 행은 정렬 전에 걸러야 한다.
        foreach (AIDeck t_row in t_sorted)
            if (t_row == null)
            {
                Fail("AIDeck 서버 표에 null 행이 있다.");
                return;
            }
        t_sorted.Sort((a, b) => a.id.CompareTo(b.id));

        bool t_valid = true;
        var t_seenDeckIds = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (AIDeck t_row in t_sorted)
        {
            if (string.IsNullOrEmpty(t_row.deckId) || !t_seenDeckIds.Add(t_row.deckId))
            {
                Skip($"비어 있거나 중복인 deckId를 제외한다: '{t_row.deckId}'.");
                continue;
            }
            List<int> t_cardIds = BuildCards(t_row);
            if (t_cardIds == null)
            {
                Skip($"'{t_row.deckId}'의 카드 칸에 0 이하 id가 있어 제외한다.");
                continue;
            }

            // 여기서 던지지 않는다 — EnsureLoaded는 전투 경로에서도 불리고, 던지면 s_loaded만 세운 채
            // 빠져나가 같은 스냅샷에 대한 다음 호출이 이 실패를 손상(재시도 가능)으로 오분류한다.
            bool t_rowValid = true;
            foreach (int t_cardId in t_cardIds)
                if (!CardCatalog.Contains(t_cardId))
                {
                    t_valid = false;
                    t_rowValid = false;
                    s_updateRequired = true;
                    Fail($"'{t_row.deckId}'가 이 앱에 없는 카드 ID {t_cardId}를 참조한다.");
                }

            if (!t_rowValid) continue;

            var t_entry = new AIDeckConfig.DeckEntry
            {
                deckName = t_row.deckName,
                cardIds = t_cardIds,
                fromTier = t_row.fromTier,
                toTier = t_row.toTier,
                weight = t_row.weight,
                fromLevel = t_row.fromLevel,
                toLevel = t_row.toLevel,
            };
            s_decks.Add(t_entry);
            s_decksById.Add(t_row.deckId, t_entry);
        }

        s_ready = t_valid && s_decks.Count > 0;
        if (!s_ready && string.IsNullOrEmpty(s_error)) Fail("AIDeck 서버 표 검증에 실패했다.");
    }

    static void Fail(string _message)
    {
        if (string.IsNullOrEmpty(s_error)) s_error = _message;
        Debug.LogError("[AIDeckSpec] " + _message);
    }

    static void Skip(string _message) => Debug.LogWarning("[AIDeckSpec] " + _message);

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
        s_error = null;
        s_updateRequired = false;
        s_source = null;
        s_decks.Clear();
        s_decksById.Clear();
    }
}
