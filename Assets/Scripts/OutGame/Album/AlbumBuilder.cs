using System;
using System.Collections.Generic;
using UnityEngine;

// 스펙시트 앨범 구조를 런타임 뷰로 조립한다 — 조립은 여기, 조회·판정은 CardAlbum.
// 구조·표시 텍스트의 진실원은 시트(서버가 수령 자격을 재는 표와 같은 표)고, SO는 그림 4종만 준다.
internal static class AlbumBuilder
{
    // 시트를 못 읽으면 빈 목록(앨범은 저작물이라 자동 생성 fallback을 두지 않는다)
    public static IReadOnlyList<AlbumTheme> Build(CardAlbumConfig _skinSource)
    {
        if (!AlbumSpec.TryReadStructure(out IReadOnlyList<AlbumSpecTheme> t_specThemes))
            return Array.Empty<AlbumTheme>();

        Dictionary<string, AlbumThemeSkin> t_skins = SkinsOf(_skinSource);

        var t_themes = new List<AlbumTheme>(t_specThemes.Count);
        for (int t_i = 0; t_i < t_specThemes.Count; t_i++)
        {
            // 시트에 있는 테마의 스킨이 SO에 없으면 그림 없이 진행한다(셀 프리팹 저작값이 그대로 남는다)
            t_skins.TryGetValue(t_specThemes[t_i].ThemeId, out AlbumThemeSkin t_skin);
            t_themes.Add(BuildTheme(t_specThemes[t_i], t_skin));
        }

        return t_themes.AsReadOnly();
    }

    static AlbumTheme BuildTheme(AlbumSpecTheme _spec, AlbumThemeSkin _skin)
    {
        var t_pages = new List<AlbumPage>(_spec.Pages.Count);
        var t_ids = new List<int>();
        var t_seen = new HashSet<int>();

        // 도감 번호는 페이지가 아니라 테마 내 통번호다 — 여기서 한 번 확정해 AlbumPage.FirstNumber로만 노출한다
        int t_firstNumber = 1;
        for (int t_p = 0; t_p < _spec.Pages.Count; t_p++)
        {
            AlbumPage t_page = BuildPage(_spec.ThemeId, _spec.Pages[t_p], t_p, t_firstNumber);
            t_pages.Add(t_page);
            t_firstNumber += t_page.CardIds.Count;

            // 테마 모수는 서버 albumScopeCardIds와 같은 규칙 — 평탄화 후 중복 제거(첫 등장 순서 보존)
            for (int t_c = 0; t_c < t_page.CardIds.Count; t_c++)
            {
                int t_id = t_page.CardIds[t_c];
                if (t_seen.Add(t_id)) t_ids.Add(t_id);
            }
        }

        // themeId·pageId는 AlbumSpec이 빈 값을 통과시키지 않는다 — 낙인 키는 항상 안정이다
        return new AlbumTheme(
            _spec.ThemeId, _spec.DisplayName, _spec.Description, _spec.IsLocked,
            _skin.icon, _skin.frame, _skin.namePlate, _skin.cellPrefab,
            ResolveRewards(_spec.ThemeId, null), t_pages.AsReadOnly(), t_ids.AsReadOnly(), true);
    }

    static AlbumPage BuildPage(string _themeKey, AlbumSpecPage _spec, int _index, int _firstNumber)
        => new AlbumPage(
            _spec.PageId, _index, ResolveRewards(_themeKey, _spec.PageId), _themeKey, true, _spec.CardIds, _firstNumber);

    // 값의 진실원은 스펙시트다 — 시트에 그 키의 줄이 없으면 보상 미저작으로 본다
    static IReadOnlyList<AlbumRewardDef> ResolveRewards(string _themeId, string _pageId)
        => AlbumSpec.TryGetRewards(_themeId, _pageId, out List<AlbumRewardDef> t_spec)
            ? t_spec
            : (IReadOnlyList<AlbumRewardDef>)Array.Empty<AlbumRewardDef>();

    // 그림 4종(Icon·Frame·NamePlate·CellPrefab)만 SO에서 온다. themeId로 잇는다 — 저작 순서는 더 이상 축이 아니다
    static Dictionary<string, AlbumThemeSkin> SkinsOf(CardAlbumConfig _source)
    {
        var t_map = new Dictionary<string, AlbumThemeSkin>(StringComparer.Ordinal);
        if (_source == null)
        {
            Debug.LogWarning("[CardAlbum] 앨범 스킨 SO 미배선 — 테마 그림이 셀 프리팹 저작값으로 남는다(초기화 배선 확인).");
            return t_map;
        }

        IReadOnlyList<AlbumThemeSkin> t_defs = _source.Themes;
        for (int t_i = 0; t_i < t_defs.Count; t_i++)
        {
            string t_key = t_defs[t_i].themeId != null ? t_defs[t_i].themeId.Trim() : string.Empty;
            if (t_key.Length == 0 || t_map.ContainsKey(t_key)) continue;
            t_map.Add(t_key, t_defs[t_i]);
        }
        return t_map;
    }
}
