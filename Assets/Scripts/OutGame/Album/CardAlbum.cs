using System.Collections.Generic;
using UnityEngine;

// 카드 앨범 구조의 읽기전용 static 파사드 — 진행도(소유 파생)와 완성 판정 모수는 여기서만 산출한다
public static class CardAlbum
{
    // 미배선이면 빈 앨범 — 앨범은 저작물이라 자동 생성 fallback을 두지 않는다
    static CardAlbumConfig s_source;

    static List<AlbumTheme> s_themes;
    static IReadOnlyList<AlbumTheme> s_themesReadonly;
    static Dictionary<string, AlbumTheme> s_themeByKey;
    static Dictionary<string, AlbumPage> s_pageByRewardKey;
    static AlbumSignature s_signature;
    static bool s_hasCache;

    // 테마 목록(읽기 전용, 순서 = 저작 순서)
    public static IReadOnlyList<AlbumTheme> Themes
    {
        get { EnsureBuilt(); return s_themesReadonly; }
    }

    public static int ThemeCount
    {
        get { EnsureBuilt(); return s_themes.Count; }
    }

    // 앨범 전체 완성 보상 저작값(미배선이면 빈 목록)
    public static IReadOnlyList<AlbumRewardDef> AlbumRewards
    {
        get
        {
            EnsureBuilt();
            return s_source != null
                ? s_source.AlbumRewards
                : (IReadOnlyList<AlbumRewardDef>)System.Array.Empty<AlbumRewardDef>();
        }
    }

    public static int CompletedThemeCount
    {
        get
        {
            EnsureBuilt();
            int t_count = 0;
            for (int t_i = 0; t_i < s_themes.Count; t_i++)
            {
                if (IsComplete(s_themes[t_i])) t_count++;
            }
            return t_count;
        }
    }

    // 빈 앨범·빈 테마 포함 시 미완성 — 저작 실수가 보상으로 새지 않게 보수적으로 판정
    public static bool IsAlbumComplete => ThemeCount > 0 && CompletedThemeCount == ThemeCount;

    // 앨범 SO 주입 — null이면 빈 앨범
    public static void SetSource(CardAlbumConfig _config)
    {
        s_source = _config;
        Invalidate();
    }

    // 구조 캐시 무효화 — 다음 조회에서 재빌드
    public static void Invalidate()
    {
        s_hasCache = false;
        s_themes = null;
        s_themesReadonly = null;
        s_themeByKey = null;
        s_pageByRewardKey = null;
    }

    public static bool TryGetTheme(string _themeKey, out AlbumTheme _theme)
    {
        EnsureBuilt();
        if (string.IsNullOrEmpty(_themeKey)) { _theme = null; return false; }
        return s_themeByKey.TryGetValue(_themeKey, out _theme);
    }

    // 페이지는 낙인 키(RewardKey)로 조회한다 — 페이지 키 단독은 테마 간 유일성이 없다
    public static bool TryGetPage(string _rewardKey, out AlbumPage _page)
    {
        EnsureBuilt();
        if (string.IsNullOrEmpty(_rewardKey)) { _page = null; return false; }
        return s_pageByRewardKey.TryGetValue(_rewardKey, out _page);
    }

    public static int OwnedCountOf(AlbumTheme _theme)
        => _theme != null ? OwnedIn(_theme.CardIds) : 0;

    public static int OwnedCountOf(AlbumPage _page)
        => _page != null ? OwnedIn(_page.CardIds) : 0;

    // 완성 판정 분모(null 슬롯 제외) — 다른 곳에서 Cards.Count로 재산출 금지
    public static int TotalCountOf(AlbumTheme _theme)
        => _theme != null && _theme.CardIds != null ? _theme.CardIds.Count : 0;

    public static int TotalCountOf(AlbumPage _page)
        => _page != null && _page.CardIds != null ? _page.CardIds.Count : 0;

    // 카드 0장 테마는 미완성(저작 누락이 완성으로 새지 않게)
    public static bool IsComplete(AlbumTheme _theme)
    {
        int t_total = TotalCountOf(_theme);
        return t_total > 0 && OwnedCountOf(_theme) == t_total;
    }

    public static bool IsComplete(AlbumPage _page)
    {
        int t_total = TotalCountOf(_page);
        return t_total > 0 && OwnedCountOf(_page) == t_total;
    }

    // 저작 ↔ 카탈로그 드리프트·키 안정성 로그 진단(디버그 전용)
    public static void ValidateAlbum()
    {
        if (!CardCatalog.IsReady)
        {
            Debug.LogWarning("[CardAlbum] CardCatalog 미준비 — 앨범 검증을 생략한다(부트 순서 확인).");
            return;
        }

        EnsureBuilt();

        int t_unstable = 0;
        int t_unassigned = 0;
        var t_rewardKeys = new HashSet<string>();
        var t_placed = new HashSet<int>();
        var t_dupCards = new List<int>();

        foreach (var t_theme in s_themes)
        {
            if (!t_theme.HasStableKey) t_unstable++;
            else if (!t_rewardKeys.Add(t_theme.RewardKey))
                Debug.LogError($"[CardAlbum] 테마 키 중복 '{t_theme.Key}' — 낙인이 한 테마로 합쳐진다.");

            foreach (var t_page in t_theme.Pages)
            {
                if (!t_page.HasStableKey) t_unstable++;
                else if (!t_rewardKeys.Add(t_page.RewardKey))
                    Debug.LogError($"[CardAlbum] 페이지 낙인 키 중복 '{t_page.RewardKey}' — 낙인이 한 페이지로 합쳐진다.");

                if (TotalCountOf(t_page) == 0)
                    Debug.LogWarning($"[CardAlbum] 카드 0장 페이지 '{t_theme.Key}/{t_page.Key}' — 영구 미완성이다.");

                foreach (var t_card in t_page.Cards)
                {
                    if (t_card == null) continue;

                    int t_id = CardCatalog.IdOf(t_card);
                    // 미부여 id는 전부 0이라 중복·미존재 진단이 오염된다 — 카드명으로 따로 보고
                    if (t_id <= 0)
                    {
                        t_unassigned++;
                        Debug.LogError($"[CardAlbum] id 미부여 카드 '{t_card.name}' (페이지 '{t_theme.Key}/{t_page.Key}') — 소유 불가라 페이지가 영구 미완성이다.");
                        continue;
                    }
                    if (!t_placed.Add(t_id)) t_dupCards.Add(t_id);
                }
            }
        }

        if (t_unstable > 0)
            Debug.LogError($"[CardAlbum] 안정 키 미저작 {t_unstable}건 — 해당 보상은 영구 Locked다(themeId/pageId 저작 필요).");
        if (t_dupCards.Count > 0)
            Debug.LogWarning($"[CardAlbum] 카드 중복 배치 {t_dupCards.Count}건: {string.Join(", ", t_dupCards)}");

        var t_missing = new List<int>();
        foreach (var t_card in CardCatalog.All)
        {
            var t_id = CardCatalog.IdOf(t_card);
            if (t_id > 0 && !t_placed.Contains(t_id)) t_missing.Add(t_id);
        }
        var t_notInCatalog = new List<int>();
        foreach (var t_id in t_placed)
        {
            if (!CardCatalog.Contains(t_id)) t_notInCatalog.Add(t_id);
        }

        if (t_missing.Count > 0)
            Debug.LogWarning($"[CardAlbum] 카탈로그엔 있으나 앨범 배치 누락 {t_missing.Count}장: {string.Join(", ", t_missing)}");
        if (t_notInCatalog.Count > 0)
            Debug.LogWarning($"[CardAlbum] 앨범엔 있으나 카탈로그 미존재 {t_notInCatalog.Count}장: {string.Join(", ", t_notInCatalog)}");

        if (t_unstable == 0 && t_unassigned == 0 && t_dupCards.Count == 0 && t_missing.Count == 0 && t_notInCatalog.Count == 0)
            Debug.Log($"[CardAlbum] 앨범 검증 통과 — 테마 {s_themes.Count}개 / 배치 {t_placed.Count}장, 드리프트 없음.");
    }

    static int OwnedIn(IReadOnlyList<int> _ids)
    {
        if (_ids == null) return 0;

        int t_owned = 0;
        for (int t_i = 0; t_i < _ids.Count; t_i++)
        {
            if (OwnershipManager.IsOwned(_ids[t_i])) t_owned++;
        }
        return t_owned;
    }

    static void EnsureBuilt()
    {
        bool t_fromSource = s_source != null && s_source.ThemeDefCount > 0;
        Object t_sourceObj = t_fromSource ? (Object)s_source : null;
        int t_count = t_fromSource ? s_source.ThemeDefCount : 0;
        var t_sig = new AlbumSignature(t_sourceObj, t_count);

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
            AddTheme(t_i, t_defs[t_i]);
        }

        EndBuild();
    }

    static void BuildEmpty()
    {
        BeginBuild();
        EndBuild();

        Debug.LogWarning("[CardAlbum] 앨범 SO 미배선 또는 테마 0개 — 앨범이 비어 있다(BootInstaller 배선 확인).");
    }

    static void BeginBuild()
    {
        s_themes = new List<AlbumTheme>();
        s_themeByKey = new Dictionary<string, AlbumTheme>();
        s_pageByRewardKey = new Dictionary<string, AlbumPage>();
    }

    static void AddTheme(int _index, AlbumThemeDef _def)
    {
        // displayName·인덱스 폴백 금지 — 여기서 키가 흔들리면 수령 낙인이 통째로 소실된다
        bool t_stable = !string.IsNullOrEmpty(_def.themeId);
        if (!t_stable)
            Debug.LogError($"[CardAlbum] themeId 미저작(index {_index}, '{_def.displayName}') — 이 테마의 보상은 영구 Locked다.");

        var t_pages = new List<AlbumPage>();
        var t_cards = new List<CardData>();
        var t_ids = new List<int>();

        var t_pageDefs = _def.pages;
        int t_pageCount = t_pageDefs != null ? t_pageDefs.Count : 0;
        for (int t_p = 0; t_p < t_pageCount; t_p++)
        {
            var t_page = BuildPage(_def.themeId, t_stable, t_p, t_pageDefs[t_p]);
            t_pages.Add(t_page);

            for (int t_c = 0; t_c < t_page.Cards.Count; t_c++)
            {
                if (t_page.Cards[t_c] != null) t_cards.Add(t_page.Cards[t_c]);
            }
            for (int t_c = 0; t_c < t_page.CardIds.Count; t_c++)
            {
                t_ids.Add(t_page.CardIds[t_c]);
            }

            if (t_page.RewardKey != null && !s_pageByRewardKey.ContainsKey(t_page.RewardKey))
                s_pageByRewardKey.Add(t_page.RewardKey, t_page);
        }

        var t_theme = new AlbumTheme(
            _def.themeId, _def.displayName, _def.icon, _index, NormalizeRewards(_def.rewards),
            t_pages.AsReadOnly(), t_cards.AsReadOnly(), t_ids.AsReadOnly(), t_stable);
        s_themes.Add(t_theme);

        if (t_stable && !s_themeByKey.ContainsKey(t_theme.Key))
            s_themeByKey.Add(t_theme.Key, t_theme);
    }

    static AlbumPage BuildPage(string _themeKey, bool _themeStable, int _index, AlbumPageDef _def)
    {
        bool t_stable = _themeStable && !string.IsNullOrEmpty(_def.pageId);
        if (string.IsNullOrEmpty(_def.pageId))
            Debug.LogError($"[CardAlbum] pageId 미저작(테마 '{_themeKey}' index {_index}) — 이 페이지의 보상은 영구 Locked다.");

        var t_source = _def.cards;
        int t_slots = t_source != null ? t_source.Count : 0;

        var t_cards = new List<CardData>(t_slots);
        var t_ids = new List<int>(t_slots);
        for (int t_c = 0; t_c < t_slots; t_c++)
        {
            t_cards.Add(t_source[t_c]);
            if (t_source[t_c] != null) t_ids.Add(CardCatalog.IdOf(t_source[t_c]));
        }

        return new AlbumPage(
            _def.pageId, _index, NormalizeRewards(_def.rewards), _themeKey, t_stable,
            t_cards.AsReadOnly(), t_ids.AsReadOnly());
    }

    // def의 List는 null일 수 있다 — 소비자가 매번 null 가드하지 않게 빈 목록으로 정규화
    static IReadOnlyList<AlbumRewardDef> NormalizeRewards(List<AlbumRewardDef> _rewards)
        => _rewards != null ? _rewards : (IReadOnlyList<AlbumRewardDef>)System.Array.Empty<AlbumRewardDef>();

    static void EndBuild()
    {
        s_themesReadonly = s_themes.AsReadOnly();
    }

    // 앨범 소스 시그니처 — 같으면 구조 캐시 재사용
    readonly struct AlbumSignature : System.IEquatable<AlbumSignature>
    {
        readonly Object m_source;
        readonly int m_count;

        public AlbumSignature(Object _source, int _count)
        {
            m_source = _source;
            m_count = _count;
        }

        public bool Equals(AlbumSignature _other)
        {
            return m_source == _other.m_source && m_count == _other.m_count;
        }
    }
}
