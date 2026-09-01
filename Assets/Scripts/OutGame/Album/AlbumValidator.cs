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
            Debug.LogError("[CardAlbum] SpecData를 읽지 못해 앨범 검증을 할 수 없다.");
            return;
        }

        IReadOnlyList<AlbumThemeInfo> t_themeRows = t_manager.AlbumThemeInfo?.All;
        IReadOnlyList<AlbumEntry> t_entryRows = t_manager.AlbumEntry?.All;
        if (t_themeRows == null || t_themeRows.Count == 0)
        {
            Debug.LogError("[CardAlbum] AlbumThemeInfo 표가 비었다 — 앨범이 통째로 비어 있다.");
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
                Debug.LogError($"[CardAlbum] AlbumThemeInfo {t_row.id}의 themeId가 비었다 — 완성 보상 낙인 키를 만들 수 없다.");
                t_errors++;
                continue;
            }
            if (!t_themeById.ContainsKey(t_themeId)) t_themeById.Add(t_themeId, t_row);
            else
            {
                Debug.LogError($"[CardAlbum] themeId 중복 '{t_themeId}' (AlbumThemeInfo {t_row.id}) — 낙인이 한 테마로 합쳐진다.");
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
                Debug.LogError($"[CardAlbum] AlbumEntry {t_row.id}의 themeId·pageId가 비었다 — 페이지 낙인 키를 만들 수 없다.");
                t_errors++;
                continue;
            }

            if (!t_themeById.ContainsKey(t_themeId))
            {
                Debug.LogError($"[CardAlbum] AlbumEntry {t_row.id}가 AlbumThemeInfo에 없는 themeId '{t_themeId}'를 가리킨다 — 갈 곳 없는 칸이다.");
                t_errors++;
                continue;
            }

            t_cellCountByTheme.TryGetValue(t_themeId, out int t_count);
            t_cellCountByTheme[t_themeId] = t_count + 1;

            string t_pageKey = t_themeId + "/" + t_pageId;
            if (!t_orderKeys.Add(t_pageKey + "#" + t_row.order))
            {
                Debug.LogWarning($"[CardAlbum] order 중복 '{t_pageKey}' order {t_row.order} — 칸 순서가 행 번호로 갈린다(저작 의도 확인).");
                t_warnings++;
            }

            t_placed.Add(t_row.cardId);
            if (t_pageOfCard.TryGetValue(t_row.cardId, out string t_first))
            {
                if (!string.Equals(t_first, t_pageKey, StringComparison.Ordinal))
                {
                    Debug.LogWarning($"[CardAlbum] 카드 {t_row.cardId}가 '{t_first}'와 '{t_pageKey}' 두 곳에 배치됐다.");
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
                Debug.LogError($"[CardAlbum] 열린 테마 '{t_pair.Key}'에 칸이 0개다 — 영구 미완성이라 앨범 전체 보상이 봉인된다.");
                t_errors++;
            }
            else if (t_locked && t_cells > 0)
            {
                Debug.LogWarning($"[CardAlbum] 준비 중 테마 '{t_pair.Key}'에 칸이 {t_cells}개 저작돼 있다 — 흑백+자물쇠로만 뜨고 완성 모수에서 빠진다(의도 확인).");
                t_warnings++;
            }
        }

        t_errors += ValidateCards(t_pageOfCard, ref t_warnings);
        ValidateSkins(_skinSource, t_themeById, ref t_warnings);
        t_errors += ValidateAlbumReward();

        if (t_errors == 0 && t_warnings == 0)
        {
            Debug.Log($"[CardAlbum] 앨범 검증 통과 — 테마 {t_themeById.Count}개 / 칸 {t_placed.Count}개, 드리프트 없음.");
            return;
        }
        Debug.Log($"[CardAlbum] 앨범 검증 종료 — 오류 {t_errors}건, 경고 {t_warnings}건.");
    }

    // 카탈로그는 런타임 초기화에서만 채워진다 — 에디터 정지 상태에서는 이 대조를 건너뛴다
    static int ValidateCards(Dictionary<int, string> _pageOfCard, ref int _warnings)
    {
        if (!CardCatalog.IsReady)
        {
            Debug.LogWarning("[CardAlbum] CardCatalog 미준비 — 카드 실재·누락 대조를 생략한다(플레이 중에 다시 실행할 것).");
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
            Debug.LogError($"[CardAlbum] 카탈로그에 없는 카드 {t_notInCatalog.Count}장이 칸을 차지한다: " +
                           $"{string.Join(", ", t_notInCatalog)} — 소유로 채울 수 없어 그 페이지가 영구 미완성이다.");
            t_errors++;
        }

        var t_missing = new List<int>();
        foreach (int t_id in CardCatalog.AllIds)
        {
            if (!_pageOfCard.ContainsKey(t_id)) t_missing.Add(t_id);
        }
        if (t_missing.Count > 0)
        {
            Debug.LogWarning($"[CardAlbum] 카탈로그엔 있으나 어느 칸에도 없는 카드 {t_missing.Count}장: {string.Join(", ", t_missing)}");
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
            Debug.LogWarning("[CardAlbum] 스킨 SO가 없어 테마 그림 대조를 생략한다.");
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
                Debug.LogWarning($"[CardAlbum] 스킨 {t_i}번의 themeId가 비었다 — 어느 테마에도 붙지 않는다.");
                _warnings++;
                continue;
            }
            if (!t_skinIds.Add(t_themeId))
            {
                Debug.LogWarning($"[CardAlbum] 스킨 themeId 중복 '{t_themeId}' — 뒤쪽 줄은 무시된다.");
                _warnings++;
                continue;
            }
            if (!_themeById.ContainsKey(t_themeId))
            {
                Debug.LogWarning($"[CardAlbum] 스킨 '{t_themeId}'가 AlbumThemeInfo에 없는 테마를 가리킨다 — 쓰이지 않는 저작이다.");
                _warnings++;
            }
        }

        foreach (string t_themeId in _themeById.Keys)
        {
            if (t_skinIds.Contains(t_themeId)) continue;
            Debug.LogWarning($"[CardAlbum] 테마 '{t_themeId}'의 스킨이 없다 — 셀 프리팹 저작 그림으로 그려진다.");
            _warnings++;
        }
    }

    // 앨범 완주 보상만 저작 폴백이 없다 — Reward 표에 줄이 없으면 조용히 빈 목록이 된다
    static int ValidateAlbumReward()
    {
        if (AlbumSpec.TryGetRewards(null, null, out List<AlbumRewardDef> t_rewards) && t_rewards.Count > 0) return 0;

        Debug.LogError("[CardAlbum] Reward 표에 ownerType=Album, ownerId=\"b\" 행이 없다 — 앨범 완주 보상이 빈 목록이 된다.");
        return 1;
    }

    static string Key(string _value) => _value != null ? _value.Trim() : string.Empty;
}
#endif
