#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

// 스펙시트 앨범 표(AlbumThemeInfo·AlbumEntry) ↔ 카탈로그·스킨·보상 드리프트 진단(에디터 수동 실행 전용)
internal static class AlbumValidator
{
    // CardAlbumConfig의 [ContextMenu]가 유일한 진입점
    public static void Validate(CardAlbumConfig _skinSource)
    {
        SpecDataManager t_manager = SpecSource.Manager;
        if (t_manager == null)
        {
            Debug.LogError("[CardAlbum] Could not read SpecData, so the album cannot be validated.");
            return;
        }

        IReadOnlyList<AlbumThemeInfo> t_themeRows = t_manager.AlbumThemeInfo?.All;
        IReadOnlyList<AlbumEntry> t_entryRows = t_manager.AlbumEntry?.All;
        if (t_themeRows == null || t_themeRows.Count == 0)
        {
            Debug.LogError("[CardAlbum] The AlbumThemeInfo table is empty — the whole album is empty.");
            return;
        }

        int t_errors = 0;
        int t_warnings = 0;

        var t_themeById = new Dictionary<string, AlbumThemeInfo>(StringComparer.Ordinal);
        foreach (AlbumThemeInfo t_row in t_themeRows)
        {
            if (t_row == null) continue;
            string t_themeId = Key(t_row.themeId);
            if (t_themeId.Length == 0)
            {
                Debug.LogError($"[CardAlbum] AlbumThemeInfo {t_row.id} has an empty themeId — the completion reward mark key cannot be built.");
                t_errors++;
                continue;
            }
            if (!t_themeById.ContainsKey(t_themeId)) t_themeById.Add(t_themeId, t_row);
            else
            {
                Debug.LogError($"[CardAlbum] Duplicate themeId '{t_themeId}' (AlbumThemeInfo {t_row.id}) — the marks collapse into a single theme.");
                t_errors++;
            }
        }

        var t_cellCountByTheme = new Dictionary<string, int>(StringComparer.Ordinal);
        var t_orderKeys = new HashSet<string>(StringComparer.Ordinal);
        var t_pageOfCard = new Dictionary<int, string>();
        var t_placed = new List<int>();

        int t_entryCount = t_entryRows != null ? t_entryRows.Count : 0;
        for (int t_i = 0; t_i < t_entryCount; t_i++)
        {
            AlbumEntry t_row = t_entryRows[t_i];
            if (t_row == null) continue;

            string t_themeId = Key(t_row.themeId);
            string t_pageId = Key(t_row.pageId);
            if (t_themeId.Length == 0 || t_pageId.Length == 0)
            {
                Debug.LogError($"[CardAlbum] AlbumEntry {t_row.id} has an empty themeId/pageId — the page mark key cannot be built.");
                t_errors++;
                continue;
            }

            if (!t_themeById.ContainsKey(t_themeId))
            {
                Debug.LogError($"[CardAlbum] AlbumEntry {t_row.id} points at themeId '{t_themeId}', which is not in AlbumThemeInfo — this cell has nowhere to go.");
                t_errors++;
                continue;
            }

            t_cellCountByTheme.TryGetValue(t_themeId, out int t_count);
            t_cellCountByTheme[t_themeId] = t_count + 1;

            string t_pageKey = t_themeId + "/" + t_pageId;
            if (!t_orderKeys.Add(t_pageKey + "#" + t_row.order))
            {
                Debug.LogWarning($"[CardAlbum] Duplicate order '{t_pageKey}' order {t_row.order} — cell order falls back to the row number (check the authoring intent).");
                t_warnings++;
            }

            t_placed.Add(t_row.cardId);
            if (t_pageOfCard.TryGetValue(t_row.cardId, out string t_first))
            {
                if (!string.Equals(t_first, t_pageKey, StringComparison.Ordinal))
                {
                    Debug.LogWarning($"[CardAlbum] Card {t_row.cardId} is placed in both '{t_first}' and '{t_pageKey}'.");
                    t_warnings++;
                }
            }
            else t_pageOfCard.Add(t_row.cardId, t_pageKey);
        }

        foreach (KeyValuePair<string, AlbumThemeInfo> t_pair in t_themeById)
        {
            t_cellCountByTheme.TryGetValue(t_pair.Key, out int t_cells);
            bool t_locked = t_pair.Value.locked != 0;

            if (!t_locked && t_cells == 0)
            {
                Debug.LogError($"[CardAlbum] Open theme '{t_pair.Key}' has zero cells — it can never be completed, which seals the whole-album reward.");
                t_errors++;
            }
            else if (t_locked && t_cells > 0)
            {
                Debug.LogWarning($"[CardAlbum] Upcoming theme '{t_pair.Key}' has {t_cells} cell(s) authored — they only show greyed out with a lock and are excluded from the completion denominator (confirm this is intended).");
                t_warnings++;
            }
        }

        t_errors += ValidateCards(t_pageOfCard, ref t_warnings);
        ValidateSkins(_skinSource, t_themeById, ref t_warnings);
        t_errors += ValidateAlbumReward();

        if (t_errors == 0 && t_warnings == 0)
        {
            Debug.Log($"[CardAlbum] Album validation passed — {t_themeById.Count} theme(s) / {t_placed.Count} cell(s), no drift.");
            return;
        }
        Debug.Log($"[CardAlbum] Album validation finished — {t_errors} error(s), {t_warnings} warning(s).");
    }

    // 카탈로그는 런타임 초기화에서만 채워진다 — 에디터 정지 상태에서는 이 대조를 건너뛴다
    static int ValidateCards(Dictionary<int, string> _pageOfCard, ref int _warnings)
    {
        if (!CardCatalog.IsReady)
        {
            Debug.LogWarning("[CardAlbum] CardCatalog is not ready — skipping the card existence/missing cross-check (run it again during play).");
            _warnings++;
            return 0;
        }

        int t_errors = 0;
        var t_notInCatalog = new List<int>();
        foreach (KeyValuePair<int, string> t_pair in _pageOfCard)
        {
            if (!CardCatalog.Contains(t_pair.Key)) t_notInCatalog.Add(t_pair.Key);
        }
        if (t_notInCatalog.Count > 0)
        {
            Debug.LogError($"[CardAlbum] {t_notInCatalog.Count} card(s) that are not in the catalog occupy cells: " +
                            $"{string.Join(", ", t_notInCatalog)} — they can never be filled by ownership, so that page is permanently incomplete.");
            t_errors++;
        }

        var t_missing = new List<int>();
        foreach (int t_id in CardCatalog.AllIds)
        {
            if (!_pageOfCard.ContainsKey(t_id)) t_missing.Add(t_id);
        }
        if (t_missing.Count > 0)
        {
            Debug.LogWarning($"[CardAlbum] {t_missing.Count} card(s) exist in the catalog but are in no cell: {string.Join(", ", t_missing)}");
            _warnings++;
        }
        return t_errors;
    }

    // 그림만 공급하는 축이라 결함은 전부 경고 — 스킨이 없으면 셀 프리팹 저작값으로 그려질 뿐이다
    static void ValidateSkins(
        CardAlbumConfig _skinSource, Dictionary<string, AlbumThemeInfo> _themeById, ref int _warnings)
    {
        if (_skinSource == null)
        {
            Debug.LogWarning("[CardAlbum] There is no skin SO, so the theme art cross-check is skipped.");
            _warnings++;
            return;
        }

        var t_skinIds = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlyList<AlbumThemeSkin> t_skins = _skinSource.Themes;
        for (int t_i = 0; t_i < t_skins.Count; t_i++)
        {
            string t_themeId = Key(t_skins[t_i].themeId);
            if (t_themeId.Length == 0)
            {
                Debug.LogWarning($"[CardAlbum] Skin #{t_i} has an empty themeId — it attaches to no theme.");
                _warnings++;
                continue;
            }
            if (!t_skinIds.Add(t_themeId))
            {
                Debug.LogWarning($"[CardAlbum] Duplicate skin themeId '{t_themeId}' — the later entries are ignored.");
                _warnings++;
                continue;
            }
            if (!_themeById.ContainsKey(t_themeId))
            {
                Debug.LogWarning($"[CardAlbum] Skin '{t_themeId}' points at a theme that is not in AlbumThemeInfo — this authoring is unused.");
                _warnings++;
            }
        }

        foreach (string t_themeId in _themeById.Keys)
        {
            if (t_skinIds.Contains(t_themeId)) continue;
            Debug.LogWarning($"[CardAlbum] Theme '{t_themeId}' has no skin — it is drawn with the cell prefab authored art.");
            _warnings++;
        }
    }

    // 앨범 완주 보상만 저작 폴백이 없다 — Reward 표에 줄이 없으면 조용히 빈 목록이 된다
    static int ValidateAlbumReward()
    {
        if (AlbumSpec.TryGetRewards(null, null, out List<AlbumRewardDef> t_rewards) && t_rewards.Count > 0) return 0;

        Debug.LogError("[CardAlbum] The Reward table has no row with ownerType=Album, ownerId=\"b\" — the album completion reward becomes an empty list.");
        return 1;
    }

    static string Key(string _value) => _value != null ? _value.Trim() : string.Empty;
}
#endif
