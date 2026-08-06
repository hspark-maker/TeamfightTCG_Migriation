using System.Collections.Generic;
using UnityEngine;

// 도감 테마의 읽기전용 static 파사드
public static class CollectionThemes
{
    // 미배선이면 빈 목록 — 가짜 테마가 생기지 않도록 자동 청크 fallback을 두지 않는다
    static CollectionThemeConfig s_source;

    static List<CollectionTheme> s_themes;
    static IReadOnlyList<CollectionTheme> s_themesReadonly;
    static Dictionary<string, CollectionTheme> s_themeByKey;
    static ThemeSignature s_signature;
    static bool s_hasCache;

    // 테마 SO 주입 — null이면 빈 목록
    public static void SetSource(CollectionThemeConfig _config)
    {
        s_source = _config;
        Invalidate();
    }

    // 테마 구조 캐시 무효화 — 다음 조회에서 재빌드
    public static void Invalidate()
    {
        s_hasCache = false;
        s_themes = null;
        s_themesReadonly = null;
        s_themeByKey = null;
    }

    // 테마 목록(읽기 전용, 순서 = authoring 순서)
    public static IReadOnlyList<CollectionTheme> Themes
    {
        get { EnsureBuilt(); return s_themesReadonly; }
    }

    public static int ThemeCount
    {
        get { EnsureBuilt(); return s_themes.Count; }
    }

    // 테마 안정 키로 테마 조회
    public static bool TryGetTheme(string _themeKey, out CollectionTheme _theme)
    {
        EnsureBuilt();
        if (string.IsNullOrEmpty(_themeKey)) { _theme = null; return false; }
        return s_themeByKey.TryGetValue(_themeKey, out _theme);
    }

    // 테마 내 소유 장수
    public static int OwnedCountOf(CollectionTheme _theme)
    {
        if (_theme == null) return 0;

        var t_ids = _theme.CardIds;
        if (t_ids == null) return 0;

        int t_owned = 0;
        for (int t_i = 0; t_i < t_ids.Count; t_i++)
        {
            if (OwnershipManager.IsOwned(t_ids[t_i])) t_owned++;
        }
        return t_owned;
    }

    // 테마 완성 = 테마의 모든 카드 소유(빈 테마는 미완성)
    public static bool IsComplete(CollectionTheme _theme)
    {
        if (_theme == null) return false;

        var t_ids = _theme.CardIds;
        if (t_ids == null || t_ids.Count == 0) return false;

        return OwnedCountOf(_theme) == t_ids.Count;
    }

    // 테마 authoring ↔ 카탈로그 드리프트 로그 진단(디버그 전용)
    public static void ValidateThemes()
    {
        if (!CardCatalog.IsReady)
        {
            Debug.LogWarning("[CollectionThemes] CardCatalog 미준비 — 드리프트 검증을 생략한다(부트 순서 확인).");
            return;
        }

        EnsureBuilt();

        var t_placed = new HashSet<int>();
        foreach (var t_theme in s_themes)
        {
            foreach (var t_id in t_theme.CardIds)
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

        var t_missingFromThemes = new List<int>();
        foreach (var t_id in t_catalog)
        {
            if (!t_placed.Contains(t_id)) t_missingFromThemes.Add(t_id);
        }

        var t_notInCatalog = new List<int>();
        foreach (var t_id in t_placed)
        {
            if (!t_catalog.Contains(t_id)) t_notInCatalog.Add(t_id);
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
            var t_ids = new List<int>(t_slots);
            for (int t_c = 0; t_c < t_slots; t_c++)
            {
                t_cards.Add(t_source[t_c]);
                t_ids.Add(CardCatalog.IdOf(t_source[t_c]));
            }

            AddTheme(t_i, t_def, t_cards, t_ids);
        }

        EndBuild();
    }

    static void BuildEmpty()
    {
        BeginBuild();
        EndBuild();

        Debug.LogWarning("[CollectionThemes] 테마 SO 미배선 또는 테마 0개 — 테마 목록이 비어 있다(BootInstaller 배선 확인).");
    }

    static void BeginBuild()
    {
        s_themes = new List<CollectionTheme>();
        s_themeByKey = new Dictionary<string, CollectionTheme>();
    }

    static void AddTheme(int _index, CollectionThemeDef _def, List<CardData> _cards, List<int> _ids)
    {
        var t_theme = new CollectionTheme(
            ResolveKey(_index, _def), _def.displayName, _def.icon, _index, _cards.AsReadOnly(), _ids.AsReadOnly());
        s_themes.Add(t_theme);

        if (!string.IsNullOrEmpty(t_theme.Key) && !s_themeByKey.ContainsKey(t_theme.Key))
            s_themeByKey.Add(t_theme.Key, t_theme);
    }

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

    // 테마 소스 시그니처 — 같으면 테마 구조 캐시 재사용
    readonly struct ThemeSignature : System.IEquatable<ThemeSignature>
    {
        readonly Object m_source;
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
