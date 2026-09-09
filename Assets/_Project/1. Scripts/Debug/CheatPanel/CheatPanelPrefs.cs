#if !DISABLE_SRDEBUGGER
using System.Collections.Generic;
using UnityEngine;

namespace HeroSiege.Debugging.CheatPanel
{
    // 즐겨찾기 · 최근 사용 · 선택 카테고리를 PlayerPrefs에 저장한다.
    // 에디터 창의 SROptionsPrefs와 같은 역할이지만 빌드에서도 살아남아야 해 PlayerPrefs를 쓴다.
    // 쓸 때마다 바로 Save한다 — 앱이 정상 종료로 끝나는 일이 드물어 자동 flush에 기대면 유실된다.
    // 저장하는 것은 항목 id(카테고리/서브카테고리/표시명)라, SROptions에서 이름이나 카테고리가
    // 바뀐 항목은 다음 실행에 조용히 빠진다. 그대로면 재시작해도 같은 목록이 나온다
    public static class CheatPanelPrefs
    {
        private const string FavoriteKey = "HeroSiege.CheatPanel.Favorites";
        private const string RecentKey = "HeroSiege.CheatPanel.Recent";
        private const string CategoryKey = "HeroSiege.CheatPanel.Category";

        private const int RecentLimit = 12;
        private const char Separator = '\n';

        // 즐겨찾기는 사용자가 정한 순서가 의미라 리스트가 원본이고, 집합은 포함 검사 캐시다
        private static List<string> favoriteList;
        private static HashSet<string> favoriteSet;
        private static List<string> recentList;

        // 즐겨찾기 여부
        public static bool IsFavorite(string id)
        {
            LoadFavoritesIfNeeded();
            return favoriteSet.Contains(id);
        }

        // 즐겨찾기 토글 후 변경된 상태를 돌려준다. 새로 켜면 목록 맨 뒤에 붙는다
        public static bool ToggleFavorite(string id)
        {
            LoadFavoritesIfNeeded();

            bool added = favoriteSet.Add(id);

            if (added) favoriteList.Add(id);
            else
            {
                favoriteSet.Remove(id);
                favoriteList.Remove(id);
            }

            SaveFavorites();
            return added;
        }

        // 즐겨찾기 id 목록을 사용자가 정한 순서대로 반환한다(지연 로드)
        public static IReadOnlyList<string> GetFavorites()
        {
            LoadFavoritesIfNeeded();
            return favoriteList;
        }

        // 최근 사용 목록의 맨 앞으로 올린다
        public static void PushRecent(string id)
        {
            LoadRecentsIfNeeded();

            recentList.Remove(id);
            recentList.Insert(0, id);

            if (recentList.Count > RecentLimit)
                recentList.RemoveRange(RecentLimit, recentList.Count - RecentLimit);

            PlayerPrefs.SetString(RecentKey, string.Join(Separator.ToString(), recentList));
            PlayerPrefs.Save();
        }

        // 최근 사용 id 목록을 최신순으로 반환한다(지연 로드)
        public static IReadOnlyList<string> GetRecents()
        {
            LoadRecentsIfNeeded();
            return recentList;
        }

        // 마지막으로 선택했던 카테고리 이름을 저장값에서 읽는다
        public static string GetSelectedCategory(string fallback)
        {
            return PlayerPrefs.GetString(CategoryKey, fallback);
        }

        // 선택된 카테고리 이름을 저장한다
        public static void SetSelectedCategory(string category)
        {
            PlayerPrefs.SetString(CategoryKey, category);
            PlayerPrefs.Save();
        }

        // 즐겨찾기 목록을 저장 순서 그대로 기록한다
        private static void SaveFavorites()
        {
            PlayerPrefs.SetString(FavoriteKey, string.Join(Separator.ToString(), favoriteList));
            PlayerPrefs.Save();
        }

        // 저장된 즐겨찾기 문자열을 처음 접근할 때만 파싱해 캐시한다
        private static void LoadFavoritesIfNeeded()
        {
            if (favoriteList != null) return;

            favoriteList = new List<string>();
            favoriteSet = new HashSet<string>();

            foreach (var entry in Split(PlayerPrefs.GetString(FavoriteKey, string.Empty)))
            {
                if (favoriteSet.Add(entry)) favoriteList.Add(entry);
            }
        }

        // 저장된 최근 사용 문자열을 처음 접근할 때만 파싱해 캐시한다
        private static void LoadRecentsIfNeeded()
        {
            if (recentList != null) return;

            recentList = new List<string>();
            foreach (var entry in Split(PlayerPrefs.GetString(RecentKey, string.Empty)))
                recentList.Add(entry);
        }

        // 구분자로 이어 붙인 문자열을 빈 항목 없이 되돌린다
        private static IEnumerable<string> Split(string raw)
        {
            if (string.IsNullOrEmpty(raw)) yield break;

            foreach (var entry in raw.Split(Separator))
            {
                if (string.IsNullOrEmpty(entry)) continue;
                yield return entry;
            }
        }
    }
}
#endif
