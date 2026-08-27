using System.Collections.Generic;
using UnityEngine;

// 카드팩 스펙시트(CardPack / CardPackDrop) 런타임 조회 창구.
// 시트를 못 읽거나 packId가 없으면 조회가 실패로 떨어지고 CardPackData가 인스펙터 값으로 폴백한다.
public static class PackSpec
{
    static bool s_loaded;
    static bool s_warnedCatalogNotReady;
    static bool s_warnedUnresolvedDrops;
    static readonly Dictionary<string, CardPack> s_packs = new Dictionary<string, CardPack>();
    static readonly Dictionary<string, List<CardPackDrop>> s_drops = new Dictionary<string, List<CardPackDrop>>();

    // 초기화에서 1회. 지연 로드도 되지만 상점 진입 프레임에 파싱이 걸리지 않게 미리 당긴다.
    public static void Init() => EnsureLoaded();

    public static bool TryGetPack(string _packId, out CardPack _row)
    {
        EnsureLoaded();
        _row = null;
        return !string.IsNullOrEmpty(_packId) && s_packs.TryGetValue(_packId, out _row);
    }

    // 이 팩에서 뽑을 수 있는 카드와 가중치. 랭크 오버라이드는 만족하는 등급 중 가장 높은 하나만 적용된다
    // (하위 등급과 합산하지 않는다 — CardPackData.ResolvePool과 같은 규약).
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
                Debug.LogWarning("[PackSpec] CardCatalog 초기화 전에 드롭 풀을 조회했다. 정상 부팅에서는 InitializationInstaller가 CardCatalog.SetSource 후 PackSpec을 초기화해야 한다.");
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
                    s_packs[t_row.packId] = t_row;

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
