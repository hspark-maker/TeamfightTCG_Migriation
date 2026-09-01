using System.Collections.Generic;
using UnityEngine;

// 카드팩의 단일 진실원인 CardPack / CardPackDrop 표 런타임 조회 창구.
// 표를 못 읽거나 packId가 없으면 조회가 실패하며 SO 폴백은 없다.
public static class PackSpec
{
    static bool s_loaded;
    static bool s_warnedCatalogNotReady;
    static bool s_warnedUnresolvedDrops;
    static readonly Dictionary<string, CardPack> s_packs = new Dictionary<string, CardPack>();
    static readonly Dictionary<string, List<CardPackDrop>> s_drops = new Dictionary<string, List<CardPackDrop>>();
    static readonly List<string> s_allPackIds = new List<string>();
    static readonly List<string> s_shopPackIds = new List<string>();

    // 초기화에서 1회. 지연 로드도 되지만 상점 진입 프레임에 파싱이 걸리지 않게 미리 당긴다.
    public static void Init() => EnsureLoaded();

    public static bool TryGetPack(string _packId, out CardPack _row)
    {
        EnsureLoaded();
        _row = null;
        return !string.IsNullOrEmpty(_packId) && s_packs.TryGetValue(_packId, out _row);
    }

    public static IReadOnlyList<string> ShopPackIds
    {
        get { EnsureLoaded(); return s_shopPackIds; }
    }

    public static IReadOnlyList<string> AllPackIds
    {
        get { EnsureLoaded(); return s_allPackIds; }
    }

    public static string DisplayName(string _packId)
        => TryGetPack(_packId, out CardPack t_row) ? t_row.displayName : string.Empty;

    public static Sprite Art(string _packId)
        => TryGetPack(_packId, out CardPack t_row) ? PackArtCache.Get(t_row.artKey) : null;

    public static ECurrencyType PriceType(string _packId)
        => TryGetPack(_packId, out CardPack t_row) && CurrencyCode.TryParse(t_row.priceType, out ECurrencyType t_type)
            ? t_type
            : ECurrencyType.Gold;

    public static long Price(string _packId)
        => TryGetPack(_packId, out CardPack t_row) ? t_row.price : 0L;

    public static int DrawCount(string _packId)
        => TryGetPack(_packId, out CardPack t_row) ? Mathf.Max(1, t_row.drawCount) : 0;

    public static bool UniqueDraw(string _packId)
        => TryGetPack(_packId, out CardPack t_row) && t_row.uniqueDraw != 0;

    public static ECurrencyType RefundType(string _packId)
        => TryGetPack(_packId, out CardPack t_row) && CurrencyCode.TryParse(t_row.refundType, out ECurrencyType t_type)
            ? t_type
            : ECurrencyType.Gold;

    public static bool TryGetMinRankGrade(string _packId, out ERankGrade _grade)
    {
        _grade = default;
        if (!TryGetPack(_packId, out CardPack t_row) || string.IsNullOrWhiteSpace(t_row.minRankGrade)) return false;
        if (System.Enum.TryParse(t_row.minRankGrade, true, out ERankGrade t_grade)
            && System.Enum.IsDefined(typeof(ERankGrade), t_grade))
        {
            _grade = t_grade;
            return true;
        }

        Debug.LogWarning($"[PackSpec] {_packId}.minRankGrade 값이 올바르지 않습니다: '{t_row.minRankGrade}'");
        return false;
    }

    public static IReadOnlyList<int> ResolveCardIds(string _packId, ERankGrade _grade)
    {
        List<WeightedCard> t_weighted = ResolveDrops(_packId, _grade);
        var t_result = new List<int>(t_weighted.Count);
        for (int t_i = 0; t_i < t_weighted.Count; t_i++)
            if (t_weighted[t_i].cardId > 0) t_result.Add(t_weighted[t_i].cardId);
        return t_result;
    }

    // 이 팩에서 뽑을 수 있는 카드와 가중치. 랭크 오버라이드는 만족하는 등급 중 가장 높은 하나만 적용된다
    // (하위 등급과 합산하지 않는다).
    public static List<WeightedCard> ResolveDrops(string _packId, ERankGrade _grade)
    {
        EnsureLoaded();

        var t_result = new List<WeightedCard>();
        if (string.IsNullOrEmpty(_packId) || !s_drops.TryGetValue(_packId, out List<CardPackDrop> t_rows))
            return t_result;

        ERankGrade t_best = ERankGrade.Bronze;
        bool t_found = false;
        foreach (CardPackDrop t_row in t_rows)
        {
            ERankGrade t_grade = ParseGrade(t_row.minGrade);
            if (t_grade > _grade) continue;
            if (!t_found || t_grade > t_best) { t_best = t_grade; t_found = true; }
        }
        if (!t_found) return t_result;

        if (!CardCatalog.IsReady)
        {
            if (!s_warnedCatalogNotReady)
            {
                s_warnedCatalogNotReady = true;
                Debug.LogWarning("[PackSpec] CardCatalog 초기화 전에 드롭 풀을 조회했다. 정상 부팅에서는 초기화(InitializationRunner)가 CardCatalog.SetSource 후 PackSpec을 초기화해야 한다.");
            }
            return t_result;
        }

        int t_selectedRowCount = 0;
        foreach (CardPackDrop t_row in t_rows)
        {
            if (ParseGrade(t_row.minGrade) != t_best) continue;
            t_selectedRowCount++;
            if (!CardCatalog.Contains(t_row.cardId)) continue;

            t_result.Add(new WeightedCard { cardId = t_row.cardId, weight = Mathf.Max(1, t_row.weight) });
        }

        if (t_result.Count != t_selectedRowCount && !s_warnedUnresolvedDrops)
        {
            s_warnedUnresolvedDrops = true;
            Debug.LogWarning($"[PackSpec] '{_packId}'/{t_best} 드롭 {t_selectedRowCount}행 중 {t_result.Count}행만 CardCatalog에서 해석했다. 누락 cardId는 SO 폴백으로 숨기지 말고 Card/CardPackDrop 표를 맞춰야 한다.");
        }
        return t_result;
    }

    static void EnsureLoaded()
    {
        if (s_loaded) return;
        s_loaded = true;   // 실패해도 매 조회마다 재파싱하지 않는다(폴백으로 계속 돈다).

        SpecDataManager t_manager = SpecSource.Manager;
        if (t_manager == null) return;   // 못 읽은 경고는 SpecSource가 이미 냈다. 팩은 SO 인스펙터 값으로 돈다.

        IReadOnlyList<CardPack> t_packRows = t_manager.CardPack?.All;
        if (t_packRows != null)
            foreach (CardPack t_row in t_packRows)
                if (t_row != null && !string.IsNullOrEmpty(t_row.packId))
                {
                    s_packs[t_row.packId] = t_row;
                    s_allPackIds.Add(t_row.packId);
                }

        if (t_packRows != null)
        {
            var t_shopRows = new List<CardPack>();
            foreach (CardPack t_row in t_packRows)
                if (t_row != null && !string.IsNullOrEmpty(t_row.packId) && t_row.sortOrder > 0)
                    t_shopRows.Add(t_row);
            t_shopRows.Sort((a, b) => a.sortOrder != b.sortOrder
                ? a.sortOrder.CompareTo(b.sortOrder)
                : a.id.CompareTo(b.id));
            foreach (CardPack t_row in t_shopRows) s_shopPackIds.Add(t_row.packId);
        }

        IReadOnlyList<CardPackDrop> t_dropRows = t_manager.CardPackDrop?.All;
        if (t_dropRows != null)
            foreach (CardPackDrop t_row in t_dropRows)
            {
                if (t_row == null || string.IsNullOrEmpty(t_row.packId)) continue;
                if (!s_drops.TryGetValue(t_row.packId, out List<CardPackDrop> t_list))
                    s_drops[t_row.packId] = t_list = new List<CardPackDrop>();
                t_list.Add(t_row);
            }
    }

    static ERankGrade ParseGrade(string _value)
        => System.Enum.TryParse(_value, out ERankGrade t_grade) ? t_grade : ERankGrade.Bronze;
}
