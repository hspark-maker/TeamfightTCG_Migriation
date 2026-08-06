using System.Collections.Generic;
using UnityEngine;

// 도감 행의 읽기전용 static 파사드
public static class CatalogRows
{
    static CollectionLayoutConfig s_layout;

    static CollectionLayoutConfig s_fallbackConfig;

    static List<CatalogRow> s_rows;
    static IReadOnlyList<CatalogRow> s_rowsReadonly;
    static Dictionary<int, CatalogRow> s_rowById;
    static LayoutSignature s_signature;
    static bool s_hasCache;

    static CollectionLayoutConfig Tuning
        => s_layout != null
            ? s_layout
            : (s_fallbackConfig != null ? s_fallbackConfig : (s_fallbackConfig = ScriptableObject.CreateInstance<CollectionLayoutConfig>()));

    // 배치 SO 주입 — null이면 카탈로그 청크 fallback
    public static void SetLayout(CollectionLayoutConfig _layout)
    {
        s_layout = _layout;
        Invalidate();
    }

    // 행 구조 캐시 무효화 — 다음 조회에서 재빌드
    public static void Invalidate()
    {
        s_hasCache = false;
        s_rows = null;
        s_rowsReadonly = null;
        s_rowById = null;
    }

    // 행 목록(읽기 전용, 순서 = authoring 순서)
    public static IReadOnlyList<CatalogRow> Rows
    {
        get { EnsureBuilt(); return s_rowsReadonly; }
    }

    public static int RowCount
    {
        get { EnsureBuilt(); return s_rows.Count; }
    }

    // 행 안정 키(대표 카드 번호)로 행 조회
    public static bool TryGetRow(int _rowId, out CatalogRow _row)
    {
        EnsureBuilt();
        if (_rowId <= 0) { _row = null; return false; }
        return s_rowById.TryGetValue(_rowId, out _row);
    }

    // 행 완성 = 행의 모든 카드 소유(빈 행은 미완성)
    public static bool IsRowComplete(CatalogRow _row)
    {
        if (_row == null) return false;

        var t_ids = _row.CardIds;
        if (t_ids == null || t_ids.Count == 0) return false;

        for (int t_i = 0; t_i < t_ids.Count; t_i++)
        {
            if (!OwnershipManager.IsOwned(t_ids[t_i])) return false;
        }
        return true;
    }

    // 행 키로 완성 조회
    public static bool IsRowComplete(int _rowId)
    {
        return TryGetRow(_rowId, out var t_row) && IsRowComplete(t_row);
    }

    // 행 authoring ↔ 카탈로그 드리프트 로그 진단(디버그 전용)
    public static void ValidateLayout()
    {
        if (!CardCatalog.IsReady)
        {
            Debug.LogWarning("[CatalogRows] CardCatalog 미준비 — 드리프트 검증을 생략한다(부트 순서 확인).");
            return;
        }

        EnsureBuilt();

        var t_placed = new HashSet<int>();
        foreach (var t_row in s_rows)
        {
            foreach (var t_id in t_row.CardIds)
            {
                if (t_id > 0) t_placed.Add(t_id);
            }
        }

        var t_catalog = new HashSet<int>();
        foreach (var t_card in CardCatalog.All)
        {
            int t_id = CardCatalog.IdOf(t_card);
            if (t_id > 0) t_catalog.Add(t_id);
        }

        var t_missingFromLayout = new List<int>();
        foreach (var t_id in t_catalog)
        {
            if (!t_placed.Contains(t_id)) t_missingFromLayout.Add(t_id);
        }

        var t_notInCatalog = new List<int>();
        foreach (var t_id in t_placed)
        {
            if (!t_catalog.Contains(t_id)) t_notInCatalog.Add(t_id);
        }

        if (t_missingFromLayout.Count == 0 && t_notInCatalog.Count == 0)
        {
            Debug.Log($"[CatalogRows] 배치 검증 통과 — 배치 {t_placed.Count}장 / 카탈로그 {t_catalog.Count}장, 드리프트 없음.");
            return;
        }

        if (t_missingFromLayout.Count > 0)
            Debug.LogWarning($"[CatalogRows] 카탈로그엔 있으나 배치 누락 {t_missingFromLayout.Count}장: {string.Join(", ", t_missingFromLayout)}");
        if (t_notInCatalog.Count > 0)
            Debug.LogWarning($"[CatalogRows] 배치엔 있으나 카탈로그 미존재 {t_notInCatalog.Count}장: {string.Join(", ", t_notInCatalog)}");
    }

    static void EnsureBuilt()
    {
        bool t_fromLayout = s_layout != null && s_layout.RowDefCount > 0;
        Object t_sourceObj = t_fromLayout ? (Object)s_layout : null;
        int t_count = t_fromLayout ? s_layout.RowDefCount : (CardCatalog.IsReady ? CardCatalog.Count : 0);
        var t_sig = new LayoutSignature(t_sourceObj, t_count);

        if (s_hasCache && s_signature.Equals(t_sig)) return;

        if (t_fromLayout) BuildFromLayout();
        else BuildFallback();

        s_signature = t_sig;
        s_hasCache = true;
    }

    static void BuildFromLayout()
    {
        BeginBuild();

        var t_defs = s_layout.Rows;
        int t_rowIndex = 0;

        for (int t_i = 0; t_i < t_defs.Count; t_i++)
        {
            var t_def = t_defs[t_i];

            var t_source = t_def.cards;
            int t_slots = t_source != null ? t_source.Count : 0;

            var t_cards = new List<CardData>(t_slots);
            var t_ids = new List<int>(t_slots);
            for (int t_c = 0; t_c < t_slots; t_c++)
            {
                t_cards.Add(t_source[t_c]);
                t_ids.Add(CardCatalog.IdOf(t_source[t_c]));
            }

            ResolveTuning(t_def, out float t_cycleSeconds, out ECurrencyType t_rewardType, out long t_cap);
            AddRow(t_rowIndex++, t_cards, t_ids, t_cycleSeconds, t_rewardType, t_cap);
        }

        EndBuild();
    }

    static void BuildFallback()
    {
        BeginBuild();

        var t_source = CardCatalog.IsReady ? CardCatalog.All : (IReadOnlyList<CardData>)System.Array.Empty<CardData>();
        int t_count = t_source.Count;
        int t_rowIndex = 0;

        var t_cfg = Tuning;
        float t_cycleSeconds = t_cfg.DefaultProductionCycleSeconds;
        ECurrencyType t_rewardType = t_cfg.DefaultRewardType;
        long t_cap = t_cfg.DefaultCap;
        int t_perRow = t_cfg.DefaultCardsPerRow;

        for (int t_i = 0; t_i < t_count; t_i += t_perRow)
        {
            int t_slots = System.Math.Min(t_perRow, t_count - t_i);
            var t_cards = new List<CardData>(t_slots);
            var t_ids = new List<int>(t_slots);

            for (int t_c = 0; t_c < t_slots; t_c++)
            {
                var t_card = t_source[t_i + t_c];
                t_cards.Add(t_card);
                t_ids.Add(CardCatalog.IdOf(t_card));
            }

            AddRow(t_rowIndex++, t_cards, t_ids, t_cycleSeconds, t_rewardType, t_cap);
        }

        EndBuild();
    }

    // ECurrencyType은 Gold=0이라 "미설정" 센티널이 없어 rewardType은 오버라이드 판정 없이 그대로 쓴다
    static void ResolveTuning(CollectionRowDef _def, out float _cycleSeconds, out ECurrencyType _rewardType, out long _cap)
    {
        var t_cfg = Tuning;

        _cycleSeconds = _def.productionCycleSeconds > 0f ? _def.productionCycleSeconds : t_cfg.DefaultProductionCycleSeconds;
        _rewardType = _def.rewardType;
        _cap = _def.cap > 0 ? _def.cap : t_cfg.DefaultCap;
    }

    static void BeginBuild()
    {
        s_rows = new List<CatalogRow>();
        s_rowById = new Dictionary<int, CatalogRow>();
    }

    static void AddRow(int _index, List<CardData> _cards, List<int> _ids, float _cycleSeconds, ECurrencyType _rewardType, long _cap)
    {
        int t_rowId = 0;
        for (int t_i = 0; t_i < _ids.Count; t_i++)
        {
            if (_ids[t_i] > 0) { t_rowId = _ids[t_i]; break; }
        }

        var t_row = new CatalogRow(t_rowId, _index, _cards.AsReadOnly(), _ids.AsReadOnly(), _cycleSeconds, _rewardType, _cap);
        s_rows.Add(t_row);

        if (t_rowId > 0 && !s_rowById.ContainsKey(t_rowId))
            s_rowById.Add(t_rowId, t_row);
    }

    static void EndBuild()
    {
        s_rowsReadonly = s_rows.AsReadOnly();
    }

    // 행 소스 시그니처 — 같으면 행 구조 캐시 재사용
    readonly struct LayoutSignature : System.IEquatable<LayoutSignature>
    {
        readonly Object m_source;
        readonly int m_count;

        public LayoutSignature(Object _source, int _count)
        {
            m_source = _source;
            m_count = _count;
        }

        public bool Equals(LayoutSignature _other)
        {
            return m_source == _other.m_source && m_count == _other.m_count;
        }
    }
}
