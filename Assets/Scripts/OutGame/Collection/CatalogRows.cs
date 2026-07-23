using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 도감 행의 읽기전용 static 파사드. 행 authoring 소스에서 행 구조·해석된 튜닝을 파생하고 완성 여부를 조회한다.
/// </summary>
public static class CatalogRows
{
    // 행당 카드 수(고정). fallback 청크 폭이자 정상 행의 카드 슬롯 수.
    public const int ColumnsPerRow = 3;

    // 주입된 배치 SO(선택). null이거나 rows 0개면 CardCatalog.All fallback + 전역 기본 튜닝.
    static CollectionLayoutConfig s_layout;

    // 전역 기본 튜닝 소스: 배선 SO 미존재 시 코드 기본값(SO field initializer) 제공용 lazy 인스턴스.
    static CollectionLayoutConfig s_fallbackConfig;

    // 파생 행 구조 캐시(소유/생산 상태는 담지 않음).
    static List<CatalogRow> s_rows;
    static IReadOnlyList<CatalogRow> s_rowsReadonly;
    static Dictionary<string, CatalogRow> s_rowByKey;
    static LayoutSignature s_signature;
    static bool s_hasCache;

    // 전역 기본 튜닝 소스. 배선 SO 우선, 없으면 코드 기본값 lazy 인스턴스(GameTiming.Battle 관용구).
    static CollectionLayoutConfig Tuning
        => s_layout != null
            ? s_layout
            : (s_fallbackConfig != null ? s_fallbackConfig : (s_fallbackConfig = ScriptableObject.CreateInstance<CollectionLayoutConfig>()));

    /// <summary>배치 SO 주입(선택). 부트/배선에서 호출. null도 허용(fallback 복귀).</summary>
    public static void SetLayout(CollectionLayoutConfig _layout)
    {
        s_layout = _layout;
        Invalidate();
    }

    /// <summary>행 소스가 바뀌었을 때 행 구조 재빌드를 강제(에디터 authoring·카탈로그 재주입 대응).</summary>
    public static void Invalidate()
    {
        s_hasCache = false;
        s_rows = null;
        s_rowsReadonly = null;
        s_rowByKey = null;
    }

    // ── 공개 조회 API ─────────────────────────────────────────

    /// <summary>행 목록(읽기 전용, 순서 = authoring 순서).</summary>
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

    /// <summary>행 완성 = 행의 모든 카드를 소유. 소유는 실시간 조회(캐시 없음). 빈/미해결 행은 미완성.</summary>
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

    /// <summary>도감 전체 완성 1회성 보상 종류(전역 튜닝). 배선 SO 우선, 미배선 시 코드 기본값(fallback 인스턴스).</summary>
    public static ECurrencyType CompletionRewardType => Tuning.CompletionRewardType;

    /// <summary>도감 전체 완성 1회성 보상량(전역 튜닝). 배선 SO 우선, 미배선 시 코드 기본값(fallback 인스턴스).</summary>
    public static long CompletionRewardAmount => Tuning.CompletionRewardAmount;

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
    /// 행 authoring ↔ 카탈로그 드리프트를 로그로 진단한다(디버그 전용).
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
        bool t_fromLayout = s_layout != null && s_layout.RowDefCount > 0;
        // 시그니처: 소스 객체 동일성 + 소스 요소 수. 소스 스왑/개수 변화 시 재빌드.
        Object t_sourceObj = t_fromLayout ? (Object)s_layout : null;
        int t_count = t_fromLayout ? s_layout.RowDefCount : (CardCatalog.IsReady ? CardCatalog.Count : 0);
        var t_sig = new LayoutSignature(t_sourceObj, t_count);

        if (s_hasCache && s_signature.Equals(t_sig)) return;

        if (t_fromLayout) BuildFromLayout();
        else BuildFallback();

        s_signature = t_sig;
        s_hasCache = true;
    }

    // 배선 SO의 rows 리스트를 그대로 행으로 파생(각 def → 1 CatalogRow, 튜닝 해석 적용).
    static void BuildFromLayout()
    {
        BeginBuild();

        var t_defs = s_layout.Rows;
        int t_rowIndex = 0;

        for (int t_i = 0; t_i < t_defs.Count; t_i++)
        {
            var t_def = t_defs[t_i];

            var t_cards = new List<CardData>(ColumnsPerRow) { t_def.card1, t_def.card2, t_def.card3 };
            var t_keys = new List<string>(ColumnsPerRow)
            {
                CardCatalog.KeyOf(t_def.card1),
                CardCatalog.KeyOf(t_def.card2),
                CardCatalog.KeyOf(t_def.card3),
            };

            ResolveTuning(t_def, out float t_perHour, out ECurrencyType t_rewardType, out long t_cap);
            AddRow(t_rowIndex++, t_cards, t_keys, t_perHour, t_rewardType, t_cap);
        }

        EndBuild();
    }

    // fallback: CardCatalog.All을 3장씩 청크. 튜닝은 전역 기본값(Tuning)만 적용. 마지막 부분 청크는 null 패딩.
    static void BuildFallback()
    {
        BeginBuild();

        var t_source = CardCatalog.IsReady ? CardCatalog.All : (IReadOnlyList<CardData>)System.Array.Empty<CardData>();
        int t_count = t_source.Count;
        int t_rowIndex = 0;

        // 전역 기본 튜닝(1회 해석 — fallback 행은 모두 동일 기본값).
        var t_cfg = Tuning;
        float t_perHour = t_cfg.DefaultProductionPerHour;
        ECurrencyType t_rewardType = t_cfg.DefaultRewardType;
        long t_cap = t_cfg.DefaultCap;

        for (int t_i = 0; t_i < t_count; t_i += ColumnsPerRow)
        {
            var t_cards = new List<CardData>(ColumnsPerRow);
            var t_keys = new List<string>(ColumnsPerRow);

            // 3장 슬롯 고정 — 모자란 마지막 행은 null로 패딩(Cards.Count 항상 3).
            for (int t_c = 0; t_c < ColumnsPerRow; t_c++)
            {
                int t_idx = t_i + t_c;
                var t_card = t_idx < t_count ? t_source[t_idx] : null;
                t_cards.Add(t_card);
                t_keys.Add(CardCatalog.KeyOf(t_card));
            }

            AddRow(t_rowIndex++, t_cards, t_keys, t_perHour, t_rewardType, t_cap);
        }

        EndBuild();
    }

    // 행 튜닝 해석: 전역 기본값 ↔ 행 오버라이드.
    //  - productionPerHour>0 → 그 값, 아니면 전역 기본.
    //  - cap>0               → 그 값, 아니면 전역 기본.
    //  - rewardType          → authored 행 값을 그대로 정본으로 사용.
    //    (ECurrencyType은 Gold=0이라 "미설정" 센티널이 없고, 예전의 amount 오버라이드 신호도
    //     사라졌으므로 행의 rewardType 필드를 직접 최종값으로 쓴다. 전역 DefaultRewardType은
    //     def가 없는 fallback 청크 행에서만 쓰인다.)
    static void ResolveTuning(CollectionRowDef _def, out float _perHour, out ECurrencyType _rewardType, out long _cap)
    {
        var t_cfg = Tuning;

        _perHour = _def.productionPerHour > 0f ? _def.productionPerHour : t_cfg.DefaultProductionPerHour;
        _rewardType = _def.rewardType;
        _cap = _def.cap > 0 ? _def.cap : t_cfg.DefaultCap;
    }

    // ── 빌드 공용 헬퍼 ─────────────────────────────────────────

    static void BeginBuild()
    {
        s_rows = new List<CatalogRow>();
        s_rowByKey = new Dictionary<string, CatalogRow>();
    }

    static void AddRow(int _index, List<CardData> _cards, List<string> _keys, float _perHour, ECurrencyType _rewardType, long _cap)
    {
        // 행 안정 키 = 첫 non-null 카드 키(3장 중 앞선 것부터). 전부 미해결이면 null.
        string t_rowKey = null;
        for (int t_i = 0; t_i < _keys.Count; t_i++)
        {
            if (!string.IsNullOrEmpty(_keys[t_i])) { t_rowKey = _keys[t_i]; break; }
        }

        var t_row = new CatalogRow(t_rowKey, _index, _cards.AsReadOnly(), _keys.AsReadOnly(), _perHour, _rewardType, _cap);
        s_rows.Add(t_row);

        // 빈 키/중복 키 행은 조회 색인에서 제외(null·충돌 방지). Rows 열거에는 그대로 포함.
        if (!string.IsNullOrEmpty(t_rowKey) && !s_rowByKey.ContainsKey(t_rowKey))
            s_rowByKey.Add(t_rowKey, t_row);
    }

    static void EndBuild()
    {
        s_rowsReadonly = s_rows.AsReadOnly();
    }

    // 행 소스 시그니처: 소스 객체 동일성 + 요소 수. 같으면 행 구조 캐시 재사용.
    // (authoring 값 변경은 개수 불변이어도 Invalidate()/SetLayout으로 반영해야 한다.)
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
