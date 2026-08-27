#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

// 앨범 저작 ↔ 카탈로그 드리프트·키 안정성 로그 진단(에디터 수동 실행 전용)
internal static class AlbumValidator
{
    // CardAlbumConfig의 [ContextMenu]가 유일한 진입점
    public static void Validate()
    {
        if (!CardCatalog.IsReady)
        {
            Debug.LogWarning("[CardAlbum] CardCatalog 미준비 — 앨범 검증을 생략한다(초기화 순서 확인).");
            return;
        }

        var t_themes = CardAlbum.Themes;

        int t_unstable = 0;
        int t_unassigned = 0;
        var t_rewardKeys = new HashSet<string>();
        var t_placed = new HashSet<int>();
        var t_dupCards = new List<int>();

        foreach (var t_theme in t_themes)
        {
            if (!t_theme.HasStableKey) t_unstable++;
            else if (!t_rewardKeys.Add(t_theme.RewardKey))
                Debug.LogError($"[CardAlbum] 테마 키 중복 '{t_theme.Key}' — 낙인이 한 테마로 합쳐진다.");

            foreach (var t_page in t_theme.Pages)
            {
                if (!t_page.HasStableKey) t_unstable++;
                else if (!t_rewardKeys.Add(t_page.RewardKey))
                    Debug.LogError($"[CardAlbum] 페이지 낙인 키 중복 '{t_page.RewardKey}' — 낙인이 한 페이지로 합쳐진다.");

                if (CardAlbum.TotalCountOf(t_page) == 0)
                    Debug.LogWarning($"[CardAlbum] 카드 0장 페이지 '{t_theme.Key}/{t_page.Key}' — 영구 미완성이다.");

                foreach (int t_id in t_page.CardIds)
                {
                    // 미부여 id는 전부 0이라 중복·미존재 진단이 오염된다 — 카드명으로 따로 보고
                    if (t_id <= 0)
                    {
                        t_unassigned++;
                        Debug.LogError($"[CardAlbum] id 미부여 카드 {t_id} (페이지 '{t_theme.Key}/{t_page.Key}') — 소유 불가라 페이지가 영구 미완성이다.");
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
        foreach (int t_id in CardCatalog.AllIds)
        {
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
            Debug.Log($"[CardAlbum] 앨범 검증 통과 — 테마 {t_themes.Count}개 / 배치 {t_placed.Count}장, 드리프트 없음.");
    }
}
#endif
