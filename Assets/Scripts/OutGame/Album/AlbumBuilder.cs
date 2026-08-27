using System.Collections.Generic;
using UnityEngine;

// 앨범 저작 def를 런타임 뷰로 조립한다 — 조립은 여기, 조회·판정은 CardAlbum
internal static class AlbumBuilder
{
    // 저작 SO → 테마 목록. 미배선·빈 저작이면 빈 목록(앨범은 저작물이라 자동 생성 fallback을 두지 않는다)
    public static IReadOnlyList<AlbumTheme> Build(CardAlbumConfig _source)
    {
        if (_source == null || _source.ThemeDefCount == 0)
        {
            Debug.LogWarning("[CardAlbum] 앨범 SO 미배선 또는 테마 0개 — 앨범이 비어 있다(부트 초기화(InitializationRunner) 배선 확인).");
            return System.Array.Empty<AlbumTheme>();
        }

        var t_defs = _source.Themes;
        var t_themes = new List<AlbumTheme>(t_defs.Count);
        for (int t_i = 0; t_i < t_defs.Count; t_i++)
        {
            t_themes.Add(BuildTheme(t_i, t_defs[t_i]));
        }

        return t_themes.AsReadOnly();
    }

    static AlbumTheme BuildTheme(int _index, AlbumThemeDef _def)
    {
        // displayName·인덱스 폴백 금지 — 여기서 키가 흔들리면 수령 낙인이 통째로 소실된다
        bool t_stable = !string.IsNullOrEmpty(_def.themeId);
        if (!t_stable)
            Debug.LogError($"[CardAlbum] themeId 미저작(index {_index}, '{_def.displayName}') — 이 테마의 보상은 영구 Locked다.");

        var t_pages = new List<AlbumPage>();
        var t_ids = new List<int>();

        var t_pageDefs = _def.pages;
        int t_pageCount = t_pageDefs != null ? t_pageDefs.Count : 0;
        for (int t_p = 0; t_p < t_pageCount; t_p++)
        {
            var t_page = BuildPage(_def.themeId, t_stable, t_p, t_pageDefs[t_p]);
            t_pages.Add(t_page);

            t_ids.AddRange(t_page.CardIds);
        }

        return new AlbumTheme(
            _def.themeId, _def.displayName, _def.icon, _def.frame, _def.namePlate, _def.cellPrefab,
            ResolveRewards(_def.themeId, null, _def.rewards), t_pages.AsReadOnly(), t_ids.AsReadOnly(), t_stable);
    }

    static AlbumPage BuildPage(string _themeKey, bool _themeStable, int _index, AlbumPageDef _def)
    {
        bool t_stable = _themeStable && !string.IsNullOrEmpty(_def.pageId);
        if (string.IsNullOrEmpty(_def.pageId))
            Debug.LogError($"[CardAlbum] pageId 미저작(테마 '{_themeKey}' index {_index}) — 이 페이지의 보상은 영구 Locked다.");

        var t_source = _def.CardIds;
        int t_slots = t_source != null ? t_source.Count : 0;

        var t_ids = new List<int>(t_slots);
        for (int t_c = 0; t_c < t_slots; t_c++)
        {
            t_ids.Add(t_source[t_c]);
        }

        return new AlbumPage(
            _def.pageId, _index, ResolveRewards(_themeKey, _def.pageId, _def.rewards), _themeKey, t_stable,
            t_ids.AsReadOnly());
    }

    // 값의 진실원은 스펙시트다 — 시트에 그 키의 줄이 없을 때만 SO 저작값으로 떨어진다
    static IReadOnlyList<AlbumRewardDef> ResolveRewards(string _themeId, string _pageId, List<AlbumRewardDef> _authored)
        => AlbumSpec.TryGetRewards(_themeId, _pageId, out List<AlbumRewardDef> t_spec)
            ? t_spec
            : NormalizeRewards(_authored);

    // def의 List는 null일 수 있다 — 소비자가 매번 null 가드하지 않게 빈 목록으로 정규화
    static IReadOnlyList<AlbumRewardDef> NormalizeRewards(List<AlbumRewardDef> _rewards)
        => _rewards != null ? _rewards : (IReadOnlyList<AlbumRewardDef>)System.Array.Empty<AlbumRewardDef>();
}
