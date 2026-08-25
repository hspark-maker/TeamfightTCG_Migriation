using System;
using System.Collections.Generic;

// 앨범 스펙시트(AlbumReward) 런타임 조회 창구.
// 시트를 못 읽거나 해당 키가 시트에 없으면 조회가 실패로 떨어지고 저작 SO 값으로 폴백한다.
public static class AlbumSpec
{
    static bool s_loaded;
    static readonly Dictionary<string, List<AlbumRewardDef>> s_rewards =
        new Dictionary<string, List<AlbumRewardDef>>(StringComparer.Ordinal);

    /// <summary>계층 규약은 시트와 같다 — 둘 다 비면 앨범 전체, pageId만 비면 테마 완성, 둘 다 있으면 페이지.</summary>
    public static bool TryGetRewards(string _themeId, string _pageId, out List<AlbumRewardDef> _rewards)
    {
        EnsureLoaded();
        return s_rewards.TryGetValue(KeyOf(_themeId, _pageId), out _rewards);
    }

    // 부트에서 1회. 앨범 탭 진입 프레임에 파싱이 걸리지 않게 미리 당긴다.
    public static void Init() => EnsureLoaded();

    // 두 축을 한 키로 — 개행은 저작 문자열에 나올 수 없어 구분자로 안전하다
    static string KeyOf(string _themeId, string _pageId)
        => string.Concat(_themeId ?? string.Empty, "\n", _pageId ?? string.Empty);

    static void EnsureLoaded()
    {
        if (s_loaded) return;
        s_loaded = true;   // 실패해도 매 조회마다 재파싱하지 않는다(폴백으로 계속 돈다).

        SpecDataManager t_manager = SpecSource.Manager;
        if (t_manager == null) return;   // 못 읽은 경고는 SpecSource가 이미 냈다

        IReadOnlyList<AlbumReward> t_rows = t_manager.AlbumReward?.All;
        if (t_rows == null) return;

        // 표시 순서는 order가 정한다 — 기획자가 시트 행을 옮겨도 팝업 줄 순서가 흔들리지 않게.
        var t_sorted = new List<AlbumReward>(t_rows);
        t_sorted.Sort((_a, _b) => _a.order.CompareTo(_b.order));

        for (int t_i = 0; t_i < t_sorted.Count; t_i++)
        {
            AlbumReward t_row = t_sorted[t_i];
            if (t_row == null) continue;

            if (!RewardSpec.TryConvert(t_row.currency, t_row.amount, $"AlbumReward id {t_row.id}", out AlbumRewardDef t_def))
                continue;

            string t_key = KeyOf(t_row.themeId, t_row.pageId);
            if (!s_rewards.TryGetValue(t_key, out List<AlbumRewardDef> t_list))
                s_rewards[t_key] = t_list = new List<AlbumRewardDef>();

            t_list.Add(t_def);
        }
    }
}
