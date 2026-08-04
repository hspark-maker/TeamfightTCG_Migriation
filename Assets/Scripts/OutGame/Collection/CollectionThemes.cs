using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 도감 테마의 읽기전용 static 파사드. 테마 authoring 소스에서 테마 구조를 파생하고 소유 진행도를 조회한다.
/// </summary>
public static class CollectionThemes
{
    // 미배선이면 빈 목록. 자동 청크 fallback을 두지 않는다 — 테마는 저작물이라 가짜 테마가 생긴다.
    static CollectionThemeConfig s_source;

    // 파생 테마 구조 캐시(소유 상태는 담지 않음).
    static List<CollectionTheme> s_themes;
    static IReadOnlyList<CollectionTheme> s_themesReadonly;
    static Dictionary<string, CollectionTheme> s_themeByKey;
    static ThemeSignature s_signature;
    static bool s_hasCache;

    /// <summary>테마 SO 주입(선택). 부트/배선에서 호출. null도 허용(빈 목록).</summary>
    public static void SetSource(CollectionThemeConfig _config)
    {
        s_source = _config;
        Invalidate();
    }

    /// <summary>테마 소스가 바뀌었을 때 테마 구조 재빌드를 강제(에디터 authoring·카탈로그 재주입 대응).</summary>
    public static void Invalidate()
    {
        s_hasCache = false;
        s_themes = null;
        s_themesReadonly = null;
        s_themeByKey = null;
    }

    // ── 공개 조회 API ─────────────────────────────────────────

    /// <summary>테마 목록(읽기 전용, 순서 = authoring 순서).</summary>
    public static IReadOnlyList<CollectionTheme> Themes
    {
        get { EnsureBuilt(); return s_themesReadonly; }
    }

    public static int ThemeCount
    {
        get { EnsureBuilt(); return s_themes.Count; }
    }

    /// <summary>테마 안정 키로 테마 조회. 미존재·빈 키·미authoring이면 false(예외 없음).</summary>
    public static bool TryGetTheme(string _themeKey, out CollectionTheme _theme)
    {
        EnsureBuilt();
        if (string.IsNullOrEmpty(_themeKey)) { _theme = null; return false; }
        return s_themeByKey.TryGetValue(_themeKey, out _theme);
    }

    /// <summary>테마 내 소유 장수. 소유는 실시간 조회(캐시 없음).</summary>
    public static int OwnedCountOf(CollectionTheme _theme)
    {
        if (_theme == null) return 0;

        var t_keys = _theme.CardKeys;
        if (t_keys == null) return 0;

        int t_owned = 0;
        for (int t_i = 0; t_i < t_keys.Count; t_i++)
        {
            // 미해결 슬롯(null 키)은 IsOwned(null)==false로 자연히 미소유 처리된다.
            if (OwnershipManager.IsOwned(t_keys[t_i])) t_owned++;
        }
        return t_owned;
    }

    /// <summary>테마 완성 = 테마의 모든 카드를 소유. 빈 테마는 미완성(IsRowComplete와 동일 규약).</summary>
    public static bool IsComplete(CollectionTheme _theme)
    {
        if (_theme == null) return false;

        var t_keys = _theme.CardKeys;
        if (t_keys == null || t_keys.Count == 0) return false;

        return OwnedCountOf(_theme) == t_keys.Count;
    }

    // ── 드리프트 진단 (에디터/디버그 전용, 정상 흐름 예외 없음) ──

    /// <summary>테마 authoring ↔ 카탈로그 드리프트를 로그로 진단(디버그 전용, 예외 없음).</summary>
    public static void ValidateThemes()
    {
        if (!CardCatalog.IsReady)
        {
            Debug.LogWarning("[CollectionThemes] CardCatalog 미준비 — 드리프트 검증을 생략한다(부트 순서 확인).");
            return;
        }

        EnsureBuilt();

        var t_placed = new HashSet<string>();
        foreach (var t_theme in s_themes)
        {
            foreach (var t_key in t_theme.CardKeys)
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

        var t_missingFromThemes = new List<string>(); // 카탈로그엔 있는데 테마에 배치 안 됨
        foreach (var t_key in t_catalog)
        {
            if (!t_placed.Contains(t_key)) t_missingFromThemes.Add(t_key);
        }

        var t_notInCatalog = new List<string>();      // 테마엔 있는데 카탈로그에 없음
        foreach (var t_key in t_placed)
        {
            if (!t_catalog.Contains(t_key)) t_notInCatalog.Add(t_key);
        }

        if (t_missingFromThemes.Count == 0 && t_notInCatalog.Count == 0)
        {
            Debug.Log($"[CollectionThemes] 테마 검증 통과 — 테마 {t_placed.Count}장 / 카탈로그 {t_catalog.Count}장, 드리프트 없음.");
            return;
        }

        if (t_missingFromThemes.Count > 0)
            Debug.LogWarning($"[CollectionThemes] 카탈로그엔 있으나 테마 배치 누락 {t_missingFromThemes.Count}장: {string.Join(", ", t_missingFromThemes)}");
        if (t_notInCatalog.Count > 0)
            Debug.LogWarning($"[CollectionThemes] 테마엔 있으나 카탈로그 미존재 {t_notInCatalog.Count}장: {string.Join(", ", t_notInCatalog)}");
    }

    // ── 내부: 테마 구조 파생 ───────────────────────────────────

    // 시그니처가 유지되면 캐시 재사용. 소유 상태는 캐시하지 않는다(조회 시 실시간).
    static void EnsureBuilt()
    {
        bool t_fromSource = s_source != null && s_source.ThemeDefCount > 0;
        Object t_sourceObj = t_fromSource ? (Object)s_source : null;
        int t_count = t_fromSource ? s_source.ThemeDefCount : 0;
        var t_sig = new ThemeSignature(t_sourceObj, t_count);

        if (s_hasCache && s_signature.Equals(t_sig)) return;

        if (t_fromSource) BuildFromSource();
        else BuildEmpty();

        s_signature = t_sig;
        s_hasCache = true;
    }

    // 각 def → 1 CollectionTheme.
    static void BuildFromSource()
    {
        BeginBuild();

        var t_defs = s_source.Themes;

        for (int t_i = 0; t_i < t_defs.Count; t_i++)
        {
            var t_def = t_defs[t_i];

            var t_source = t_def.cards;
            int t_slots = t_source != null ? t_source.Count : 0;

            var t_cards = new List<CardData>(t_slots);
            var t_keys = new List<string>(t_slots);
            for (int t_c = 0; t_c < t_slots; t_c++)
            {
                t_cards.Add(t_source[t_c]);
                t_keys.Add(CardCatalog.KeyOf(t_source[t_c]));
            }

            AddTheme(t_i, t_def, t_cards, t_keys);
        }

        EndBuild();
    }

    // 경고는 재빌드 시점(소스 변화 시)에만 나가므로 스팸되지 않는다.
    static void BuildEmpty()
    {
        BeginBuild();
        EndBuild();

        Debug.LogWarning("[CollectionThemes] 테마 SO 미배선 또는 테마 0개 — 테마 목록이 비어 있다(BootInstaller 배선 확인).");
    }

    // ── 빌드 공용 헬퍼 ─────────────────────────────────────────

    static void BeginBuild()
    {
        s_themes = new List<CollectionTheme>();
        s_themeByKey = new Dictionary<string, CollectionTheme>();
    }

    static void AddTheme(int _index, CollectionThemeDef _def, List<CardData> _cards, List<string> _keys)
    {
        var t_theme = new CollectionTheme(
            ResolveKey(_index, _def), _def.displayName, _def.icon, _index, _cards.AsReadOnly(), _keys.AsReadOnly());
        s_themes.Add(t_theme);

        // 빈 키/중복 키 테마는 조회 색인에서 제외(null·충돌 방지). Themes 열거에는 그대로 포함.
        if (!string.IsNullOrEmpty(t_theme.Key) && !s_themeByKey.ContainsKey(t_theme.Key))
            s_themeByKey.Add(t_theme.Key, t_theme);
    }

    // 행과 달리 "첫 카드 키"를 안 쓴다 — 구성 카드가 바뀌어도 테마 정체성은 유지돼야 한다.
    static string ResolveKey(int _index, CollectionThemeDef _def)
    {
        if (!string.IsNullOrEmpty(_def.themeId)) return _def.themeId;
        if (!string.IsNullOrEmpty(_def.displayName)) return _def.displayName;
        return $"theme_{_index}";
    }

    static void EndBuild()
    {
        s_themesReadonly = s_themes.AsReadOnly();
    }

    // 소스 객체 동일성 + 요소 수. 개수 불변인 authoring 값 변경은 Invalidate()/SetSource로 반영해야 한다.
    readonly struct ThemeSignature : System.IEquatable<ThemeSignature>
    {
        readonly Object m_source; // 테마 SO 또는 null(미배선)
        readonly int m_count;

        public ThemeSignature(Object _source, int _count)
        {
            m_source = _source;
            m_count = _count;
        }

        public bool Equals(ThemeSignature _other)
        {
            return m_source == _other.m_source && m_count == _other.m_count;
        }
    }
}
