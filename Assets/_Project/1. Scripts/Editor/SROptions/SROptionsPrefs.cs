#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;

namespace HeroSiege.Editor.SROptionsUI
{
    // 즐겨찾기 · 최근 사용 · 접힘 상태 · 선택 카테고리 · 라벨 폭을 EditorPrefs에 저장한다.
    // 도메인 리로드와 에디터 재시작을 건너 유지된다.
    internal static class SROptionsPrefs
    {
        private const string FavoriteKey = "HeroSiege.SROptionsUI.Favorites";
        private const string RecentKey = "HeroSiege.SROptionsUI.Recent";
        private const string CategoryKey = "HeroSiege.SROptionsUI.Category";
        private const string LabelWidthKey = "HeroSiege.SROptionsUI.LabelWidth";
        private const string FoldPrefix = "HeroSiege.SROptionsUI.Fold.";

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

        // 즐겨찾기를 beforeId 바로 앞으로 옮긴다. beforeId가 null이거나 목록에 없으면 맨 뒤로 간다
        public static void MoveFavorite(string id, string beforeId)
        {
            LoadFavoritesIfNeeded();

            if (favoriteSet.Contains(id) == false) return;
            if (id == beforeId) return;

            favoriteList.Remove(id);

            int index = beforeId == null ? favoriteList.Count : favoriteList.IndexOf(beforeId);
            if (index < 0) index = favoriteList.Count;

            favoriteList.Insert(index, id);
            SaveFavorites();
        }

        // 최근 사용 목록의 맨 앞으로 올린다
        public static void PushRecent(string id)
        {
            LoadRecentsIfNeeded();

            recentList.Remove(id);
            recentList.Insert(0, id);

            if (recentList.Count > RecentLimit)
                recentList.RemoveRange(RecentLimit, recentList.Count - RecentLimit);

            EditorPrefs.SetString(RecentKey, string.Join(Separator.ToString(), recentList));
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
            return EditorPrefs.GetString(CategoryKey, fallback);
        }

        // 선택된 카테고리 이름을 저장한다
        public static void SetSelectedCategory(string category)
        {
            EditorPrefs.SetString(CategoryKey, category);
        }

        // 라벨 컬럼 폭 저장값을 읽는다
        public static float GetLabelWidth()
        {
            return EditorPrefs.GetFloat(LabelWidthKey, 150f);
        }

        // 라벨 컬럼 폭을 저장한다
        public static void SetLabelWidth(float width)
        {
            EditorPrefs.SetFloat(LabelWidthKey, width);
        }

        // 폴드아웃이 열려 있었는지 저장값에서 읽는다
        public static bool IsFoldOpen(string key, bool fallback)
        {
            return EditorPrefs.GetBool(FoldPrefix + key, fallback);
        }

        // 폴드아웃 펼침 상태를 저장한다
        public static void SetFoldOpen(string key, bool isOpen)
        {
            EditorPrefs.SetBool(FoldPrefix + key, isOpen);
        }

        // 즐겨찾기 목록을 저장 순서 그대로 EditorPrefs에 기록한다
        private static void SaveFavorites()
        {
            EditorPrefs.SetString(FavoriteKey, string.Join(Separator.ToString(), favoriteList));
        }

        // EditorPrefs에 저장된 즐겨찾기 문자열을 처음 접근할 때만 파싱해 캐시한다
        private static void LoadFavoritesIfNeeded()
        {
            if (favoriteList != null) return;

            favoriteList = new List<string>();
            favoriteSet = new HashSet<string>();

            foreach (var entry in Split(EditorPrefs.GetString(FavoriteKey, string.Empty)))
            {
                if (favoriteSet.Add(entry)) favoriteList.Add(entry);
            }
        }

        // EditorPrefs에 저장된 최근 사용 문자열을 처음 접근할 때만 파싱해 캐시한다
        private static void LoadRecentsIfNeeded()
        {
            if (recentList != null) return;

            recentList = new List<string>();
            foreach (var entry in Split(EditorPrefs.GetString(RecentKey, string.Empty)))
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
