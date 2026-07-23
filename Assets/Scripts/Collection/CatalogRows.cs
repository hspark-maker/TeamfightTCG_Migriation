using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 도감 행의 읽기전용 static 파사드. 고정 배치 소스에서 행 구조를 파생하고 완성 여부를 조회한다.
/// 배치 소스 주입점 + fallback(GameTiming/BattleTimingConfig 관용구):
///   - SetLayout(CollectionLayoutConfig) 주입 시 그 배치 순서 사용.
///   - 미배선(또는 빈 배치) 시 CardCatalog.All 순서로 fallback → 씬 배선 없이도 동작.
/// 행 구조는 배치 소스 시그니처가 같으면 캐시 재사용하되, 소유 조회는 캐시하지 않고 매 호출 실시간이다.
/// 의존 방향: Collection → CardCatalog(읽기)/OwnershipManager(읽기)/CollectionLayoutConfig(읽기). 역참조 없음.
/// </summary>
public static class CatalogRows
{
    // 행당 열 수. 카드 수가 3의 배수가 아니면 마지막 행은 부분행(1~2칸)으로 정식 포함된다.
    public const int ColumnsPerRow = 3;

    // 주입된 배치 SO(선택). null이면 CardCatalog.All로 fallback.
    static CollectionLayoutConfig s_layout;

    // 파생 행 구조 캐시(소유 상태는 담지 않음).
    static List<CatalogRow> s_rows;
    static IReadOnlyList<CatalogRow> s_rowsReadonly;
    static Dictionary<string, CatalogRow> s_rowByKey;
    static LayoutSignature s_signature;
    static bool s_hasCache;

    /// <summary>배치 SO 주입(선택). 부트/배선에서 호출. null도 허용(fallback 복귀).</summary>
    public static void SetLayout(CollectionLayoutConfig _layout)
    {
        s_layout = _layout;
        Invalidate();
    }

    /// <summary>배치 소스가 바뀌었을 때 행 구조 재빌드를 강제(에디터 authoring·카탈로그 재주입 대응).</summary>
    public static void Invalidate()
    {
        s_hasCache = false;
        s_rows = null;
        s_rowsReadonly = null;
        s_rowByKey = null;
    }

    // ── 공개 조회 API ─────────────────────────────────────────

    /// <summary>배치 순서대로 파생된 행 목록(읽기 전용).</summary>
    public static IReadOnlyList<CatalogRow> Rows
    {
        get { EnsureBuilt(); return s_rowsReadonly; }
    }

    public static int RowCount
    {
        get { EnsureBuilt(); return s_rows.Count; }
    }

    /// <summary>행 안정 키로 행 조회. 미존재·빈 키·미authoring이면 false(예외 없음).</summary>
    public static bool TryGetRow(string _rowKey, out CatalogRow _row)
    {
        EnsureBuilt();
        if (string.IsNullOrEmpty(_rowKey)) { _row = null; return false; }
        return s_rowByKey.TryGetValue(_rowKey, out _row);
    }

    /// <summary>행 완성 = 행의 모든 자리를 소유. 소유는 실시간 조회(캐시 없음). 빈/미해결 행은 미완성.</summary>
    public static bool IsRowComplete(CatalogRow _row)
    {
        if (_row == null) return false;

        var t_keys = _row.CardKeys;
        if (t_keys == null || t_keys.Count == 0) return false;

        for (int t_i = 0; t_i < t_keys.Count; t_i++)
        {
            // 미해결 슬롯(null 키)은 소유 불가 → 행이 영원히 미완성. IsOwned(null)==false로 자연 처리.
            if (!OwnershipManager.IsOwned(t_keys[t_i])) return false;
        }
        return true;
    }

    /// <summary>행 키로 완성 조회. 미존재 키는 false.</summary>
    public static bool IsRowComplete(string _rowKey)
    {
        return TryGetRow(_rowKey, out var t_row) && IsRowComplete(t_row);
    }

    /// <summary>모든 행 완성 여부. 빈 도감(행 0개)은 완성이 아니다.</summary>
    public static bool IsAllComplete
    {
        get
        {
            EnsureBuilt();
            if (s_rows.Count == 0) return false;

            for (int t_i = 0; t_i < s_rows.Count; t_i++)
            {
                if (!IsRowComplete(s_rows[t_i])) return false;
            }
            return true;
        }
    }

    // ── 드리프트 진단 (에디터/디버그 전용, 정상 흐름 예외 없음) ──

    /// <summary>
    /// 배치 SO ↔ 카탈로그 드리프트를 로그로 진단한다(디버그 전용).
    /// 카탈로그엔 있는데 배치 누락 / 배치엔 있는데 카탈로그 미존재를 각각 보고. 정상 흐름 예외를 던지지 않는다.
    /// </summary>
    public static void ValidateLayout()
    {
        if (!CardCatalog.IsReady)
        {
            Debug.LogWarning("[CatalogRows] CardCatalog 미준비 — 드리프트 검증을 생략한다(부트 순서 확인).");
            return;
        }

        EnsureBuilt();

        var t_placed = new HashSet<string>();
        foreach (var t_row in s_rows)
        {
            foreach (var t_key in t_row.CardKeys)
            {
                if (!string.IsNullOrEmpty(t_key)) t_placed.Add(t_key);
            }
        }

        var t_catalog = new HashSet<string>();
        foreach (var t_card in CardCatalog.All)
        {
            var t_key = CardCatalog.KeyOf(t_card);
            if (!string.IsNullOrEmpty(t_key)) t_catalog.Add(t_key);
        }

        var t_missingFromLayout = new List<string>(); // 카탈로그엔 있는데 배치 안 됨
        foreach (var t_key in t_catalog)
        {
            if (!t_placed.Contains(t_key)) t_missingFromLayout.Add(t_key);
        }

        var t_notInCatalog = new List<string>();      // 배치엔 있는데 카탈로그에 없음
        foreach (var t_key in t_placed)
        {
            if (!t_catalog.Contains(t_key)) t_notInCatalog.Add(t_key);
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

    // ── 내부: 행 구조 파생 ─────────────────────────────────────

    // 시그니처가 유지되면 캐시 재사용. 소유 상태는 여기 캐시하지 않는다(조회 시 실시간).
    static void EnsureBuilt()
    {
        var t_source = ResolveSource(out var t_sourceObj);
        var t_sig = new LayoutSignature(t_sourceObj, t_source.Count);

        if (s_hasCache && s_signature.Equals(t_sig)) return;

        Build(t_source);
        s_signature = t_sig;
        s_hasCache = true;
    }

    // 배치 소스 결정: 배선된 배치 SO(자리 1개 이상) 우선, 아니면 CardCatalog.All fallback.
    // 배선됐지만 비어 있는 SO(미authoring)도 fallback으로 취급 → 컬렉션이 통째로 빈 상태로 고착되지 않게.
    static IReadOnlyList<CardData> ResolveSource(out Object _sourceObj)
    {
        if (s_layout != null && s_layout.SlotCount > 0)
        {
            _sourceObj = s_layout;
            return s_layout.Slots;
        }

        _sourceObj = null;
        return CardCatalog.All;
    }

    static void Build(IReadOnlyList<CardData> _source)
    {
        s_rows = new List<CatalogRow>();
        s_rowByKey = new Dictionary<string, CatalogRow>();

        int t_count = _source != null ? _source.Count : 0;
        int t_rowIndex = 0;

        for (int t_i = 0; t_i < t_count; t_i += ColumnsPerRow)
        {
            int t_end = Mathf.Min(t_i + ColumnsPerRow, t_count); // 부분행 포함(모든 자리는 정확히 한 행에 소속)
            var t_cards = new List<CardData>(t_end - t_i);
            var t_keys = new List<string>(t_end - t_i);

            for (int t_s = t_i; t_s < t_end; t_s++)
            {
                var t_card = _source[t_s];             // 배치가 참조하는 CardData(카탈로그와 동일 에셋)
                t_cards.Add(t_card);
                t_keys.Add(CardCatalog.KeyOf(t_card)); // 미authoring 슬롯(null 카드)은 null 키
            }

            // 행 안정 키 = 행 첫 자리 카드의 안정 키.
            string t_rowKey = t_keys.Count > 0 ? t_keys[0] : null;

            var t_row = new CatalogRow(t_rowKey, t_rowIndex, t_cards.AsReadOnly(), t_keys.AsReadOnly());
            s_rows.Add(t_row);

            // 빈 키 행은 키 조회 색인에서 제외(null 충돌 방지). Rows 열거에는 그대로 포함.
            if (!string.IsNullOrEmpty(t_rowKey) && !s_rowByKey.ContainsKey(t_rowKey))
                s_rowByKey.Add(t_rowKey, t_row);

            t_rowIndex++;
        }

        s_rowsReadonly = s_rows.AsReadOnly();
    }

    // 배치 소스 시그니처: 소스 객체 동일성 + 자리 수. 같으면 행 구조 캐시 재사용.
    // (고정 배치 전제 — 개수 불변인 authoring 변경은 Invalidate()/SetLayout으로 반영.)
    readonly struct LayoutSignature : System.IEquatable<LayoutSignature>
    {
        readonly Object m_source; // 배치 SO 또는 null(fallback)
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
