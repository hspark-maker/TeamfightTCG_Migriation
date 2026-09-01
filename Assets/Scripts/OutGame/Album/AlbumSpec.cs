using System;
using System.Collections.Generic;
using UnityEngine;

// 앨범과 스펙시트의 이음매 — 구조(테마→페이지→칸)와 보상 키를 시트에서 읽는다.
// 서버가 수령 자격을 재는 표와 같은 표를 읽어야 완성 모수가 갈리지 않는다.
public static class AlbumSpec
{
    public static bool TryGetRewards(string _themeId, string _pageId, out List<AlbumRewardDef> _rewards)
        => RewardSpec.TryGetRewards(ERewardOwnerType.Album, OwnerIdOf(_themeId, _pageId), out _rewards);

    public static void Init() => RewardSpec.Init();

    /// <summary>스펙시트에서 앨범 구조를 읽는다. 테마 순서·페이지 순서·칸 순서가 확정된 상태로 나온다.
    /// 시트를 못 읽거나 두 표 중 하나라도 비면 false — 앨범은 저작물이라 자동 생성 폴백을 두지 않는다.</summary>
    public static bool TryReadStructure(out IReadOnlyList<AlbumSpecTheme> _themes)
    {
        _themes = Array.Empty<AlbumSpecTheme>();

        SpecDataManager t_manager = SpecSource.Manager;
        if (t_manager == null)
        {
            Debug.LogError("[AlbumSpec] SpecData를 읽지 못해 앨범 구조를 만들 수 없다 — 앨범이 비어 있다.");
            return false;
        }

        IReadOnlyList<AlbumThemeInfo> t_themeRows = t_manager.AlbumThemeInfo?.All;
        if (t_themeRows == null || t_themeRows.Count == 0)
        {
            Debug.LogError("[AlbumSpec] AlbumThemeInfo 표가 비었다 — 앨범이 비어 있다.");
            return false;
        }

        IReadOnlyList<AlbumEntry> t_entryRows = t_manager.AlbumEntry?.All;
        if (t_entryRows == null || t_entryRows.Count == 0)
        {
            Debug.LogError("[AlbumSpec] AlbumEntry 표가 비었다 — 앨범이 비어 있다.");
            return false;
        }

        _themes = ReadThemes(t_themeRows, t_entryRows);
        return true;
    }

    static string OwnerIdOf(string _themeId, string _pageId)
    {
        if (string.IsNullOrEmpty(_themeId)) return "b";
        if (string.IsNullOrEmpty(_pageId)) return "t:" + _themeId;
        return "p:" + _themeId + "/" + _pageId;
    }

    // 값 해석 실패는 조용히 넘기지 않는다 — 한 줄이 조용히 빠지면 클라 완성 모수만 서버와 갈린다(SpecSource.LoadCards와 같은 규약)
    static IReadOnlyList<AlbumSpecTheme> ReadThemes(
        IReadOnlyList<AlbumThemeInfo> _themeRows, IReadOnlyList<AlbumEntry> _entryRows)
    {
        var t_sorted = new List<AlbumThemeInfo>(_themeRows.Count);
        var t_known = new HashSet<string>(StringComparer.Ordinal);

        foreach (AlbumThemeInfo t_row in _themeRows)
        {
            if (t_row == null) throw new InvalidOperationException("[AlbumSpec] AlbumThemeInfo 표에 null 행이 있다.");

            string t_themeId = Key(t_row.themeId);
            if (t_themeId.Length == 0)
                throw new InvalidOperationException($"[AlbumSpec] AlbumThemeInfo {t_row.id}의 themeId가 비었다 — 테마 낙인 키를 만들 수 없다.");
            if (!t_known.Add(t_themeId))
                throw new InvalidOperationException($"[AlbumSpec] themeId '{t_themeId}'가 중복이다(AlbumThemeInfo {t_row.id}) — 낙인이 한 테마로 합쳐진다.");

            t_sorted.Add(t_row);
        }

        t_sorted.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : a.id.CompareTo(b.id));

        Dictionary<string, List<PageDraft>> t_pages = ReadPages(_entryRows, t_known);

        var t_themes = new List<AlbumSpecTheme>(t_sorted.Count);
        foreach (AlbumThemeInfo t_row in t_sorted)
        {
            string t_themeId = Key(t_row.themeId);
            t_themes.Add(new AlbumSpecTheme(
                t_themeId, t_row.displayName, t_row.description, t_row.locked != 0, Materialize(t_pages, t_themeId)));
        }
        return t_themes.AsReadOnly();
    }

    // 페이지 순서 = 그 테마 안에서 AlbumEntry가 처음 등장하는 순서, 칸 순서 = order(동률이면 id)
    static Dictionary<string, List<PageDraft>> ReadPages(IReadOnlyList<AlbumEntry> _entryRows, HashSet<string> _knownThemes)
    {
        var t_byTheme = new Dictionary<string, List<PageDraft>>(StringComparer.Ordinal);
        var t_byKey = new Dictionary<string, PageDraft>(StringComparer.Ordinal);

        foreach (AlbumEntry t_row in _entryRows)
        {
            if (t_row == null) throw new InvalidOperationException("[AlbumSpec] AlbumEntry 표에 null 행이 있다.");

            string t_themeId = Key(t_row.themeId);
            string t_pageId = Key(t_row.pageId);
            if (t_themeId.Length == 0)
                throw new InvalidOperationException($"[AlbumSpec] AlbumEntry {t_row.id}의 themeId가 비었다.");
            if (t_pageId.Length == 0)
                throw new InvalidOperationException($"[AlbumSpec] AlbumEntry {t_row.id}의 pageId가 비었다 — 페이지 낙인 키를 만들 수 없다.");
            if (!_knownThemes.Contains(t_themeId))
                throw new InvalidOperationException($"[AlbumSpec] AlbumEntry {t_row.id}가 AlbumThemeInfo에 없는 themeId '{t_themeId}'를 가리킨다.");

            if (!t_byTheme.TryGetValue(t_themeId, out List<PageDraft> t_list))
            {
                t_list = new List<PageDraft>();
                t_byTheme.Add(t_themeId, t_list);
            }

            string t_key = t_themeId + "\n" + t_pageId;
            if (!t_byKey.TryGetValue(t_key, out PageDraft t_page))
            {
                t_page = new PageDraft(t_pageId);
                t_byKey.Add(t_key, t_page);
                t_list.Add(t_page);
            }
            t_page.Cells.Add(t_row);
        }
        return t_byTheme;
    }

    static IReadOnlyList<AlbumSpecPage> Materialize(Dictionary<string, List<PageDraft>> _pages, string _themeId)
    {
        if (!_pages.TryGetValue(_themeId, out List<PageDraft> t_drafts)) return Array.Empty<AlbumSpecPage>();

        var t_result = new List<AlbumSpecPage>(t_drafts.Count);
        foreach (PageDraft t_draft in t_drafts)
        {
            t_draft.Cells.Sort((a, b) => a.order != b.order ? a.order.CompareTo(b.order) : a.id.CompareTo(b.id));

            var t_ids = new List<int>(t_draft.Cells.Count);
            foreach (AlbumEntry t_cell in t_draft.Cells) t_ids.Add(t_cell.cardId);

            t_result.Add(new AlbumSpecPage(t_draft.PageId, t_ids.AsReadOnly()));
        }
        return t_result.AsReadOnly();
    }

    static string Key(string _value) => _value != null ? _value.Trim() : string.Empty;

    sealed class PageDraft
    {
        public readonly string PageId;
        public readonly List<AlbumEntry> Cells = new List<AlbumEntry>();

        public PageDraft(string _pageId) => PageId = _pageId;
    }
}

// 스펙시트가 정한 테마 하나 — 표시 속성과 페이지 순서. 그림(스킨)은 담지 않는다(그건 CardAlbumConfig 소관)
public sealed class AlbumSpecTheme
{
    public string ThemeId { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public bool IsLocked { get; }
    public IReadOnlyList<AlbumSpecPage> Pages { get; }

    internal AlbumSpecTheme(
        string _themeId, string _displayName, string _description, bool _locked, IReadOnlyList<AlbumSpecPage> _pages)
    {
        ThemeId = _themeId;
        DisplayName = _displayName ?? string.Empty;
        Description = _description ?? string.Empty;
        IsLocked = _locked;
        Pages = _pages;
    }
}

// 스펙시트가 정한 페이지 하나 — 칸 순서 그대로의 카드 ID
public sealed class AlbumSpecPage
{
    public string PageId { get; }
    public IReadOnlyList<int> CardIds { get; }

    internal AlbumSpecPage(string _pageId, IReadOnlyList<int> _cardIds)
    {
        PageId = _pageId;
        CardIds = _cardIds;
    }
}
