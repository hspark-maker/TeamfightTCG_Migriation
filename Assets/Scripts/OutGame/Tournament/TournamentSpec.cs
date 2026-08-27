using System;
using System.Collections.Generic;

// 토너먼트 스펙시트(TournamentReward) 런타임 조회 창구.
// 시트를 못 읽거나 ownerKey가 시트에 없으면 조회가 실패로 떨어지고 TournamentConfig가 SO 저작값으로 폴백한다.
public static class TournamentSpec
{
    static bool s_loaded;
    static readonly Dictionary<string, List<AlbumRewardDef>> s_rewards =
        new Dictionary<string, List<AlbumRewardDef>>(StringComparer.Ordinal);

    /// <summary>정점 nodeId 또는 챕터 chapterId의 보상. 시트에 그 키가 없으면 false.</summary>
    public static bool TryGetRewards(string _ownerKey, out List<AlbumRewardDef> _rewards)
    {
        EnsureLoaded();
        _rewards = null;
        return !string.IsNullOrEmpty(_ownerKey) && s_rewards.TryGetValue(_ownerKey, out _rewards);
    }

    // 초기화에서 1회. 맵 진입 프레임에 파싱이 걸리지 않게 미리 당긴다.
    public static void Init() => EnsureLoaded();

    static void EnsureLoaded()
    {
        if (s_loaded) return;
        s_loaded = true;   // 실패해도 매 조회마다 재파싱하지 않는다(폴백으로 계속 돈다).

        SpecDataManager t_manager = SpecSource.Manager;
        if (t_manager == null) return;   // 못 읽은 경고는 SpecSource가 이미 냈다

        IReadOnlyList<TournamentReward> t_rows = t_manager.TournamentReward?.All;
        if (t_rows == null) return;

        // 표시 순서는 order가 정한다 — 기획자가 시트 행을 옮겨도 팝업 줄 순서가 흔들리지 않게.
        var t_sorted = new List<TournamentReward>(t_rows);
        t_sorted.Sort((_a, _b) => _a.order.CompareTo(_b.order));

        for (int t_i = 0; t_i < t_sorted.Count; t_i++)
        {
            TournamentReward t_row = t_sorted[t_i];
            if (t_row == null || string.IsNullOrEmpty(t_row.ownerKey)) continue;

            if (!RewardSpec.TryConvert(t_row.currency, t_row.amount, $"TournamentReward id {t_row.id}", out AlbumRewardDef t_def))
                continue;

            if (!s_rewards.TryGetValue(t_row.ownerKey, out List<AlbumRewardDef> t_list))
                s_rewards[t_row.ownerKey] = t_list = new List<AlbumRewardDef>();

            t_list.Add(t_def);
        }
    }
}
