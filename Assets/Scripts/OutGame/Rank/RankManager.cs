using System;
using UnityEngine;

// 랭크(표시용 티어 진행도)의 static 단일 창구.
// 티어는 points의 순수 파생이라 세이브엔 points만 둔다 — 도달 티어를 따로 저장하면 이중 진실원이 된다.
// 메모리 캐시를 두지 않고 슬롯을 직접 읽는다(DataSaveManager.Load가 Data를 교체하므로 캐시는 stale 위험).
public static class RankManager
{
    static RankConfig s_config;

    public static long Points => Slot.points;

    // 미배선(전투 씬 직접 Play 등)에서도 동작하도록 기본 인스턴스로 fallback한다.
    static RankConfig Config
        => s_config != null ? s_config : (s_config = ScriptableObject.CreateInstance<RankConfig>());

    // 슬롯 접근 단일 지점. 손상·구 세이브로 노드가 비어도 크래시 대신 기본값으로 살아난다.
    static RankSaveData Slot
    {
        get
        {
            var t_data = DataSaveManager.Data;
            if (t_data.rank == null) t_data.rank = new RankSaveData();
            return t_data.rank;
        }
    }

    // UI 1회 스냅샷. 다음 티어가 없으면 NextRequired = Points로 둬서 소비처의 0 나눗셈·음수 잔여를 원천 차단한다.
    public static RankInfo GetInfo()
    {
        var t_config = Config;
        long t_points = Points;

        int t_index = t_config.ResolveTierIndex(t_points);
        t_config.TryGetTier(t_index, out RankTier t_tier);                          // 실패해도 RankTier.None이라 그대로 진행한다.
        bool t_hasNext = t_config.TryGetTier(t_index + 1, out RankTier t_next);

        return new RankInfo(
            t_index,
            t_tier.Grade,
            t_tier.Division,
            t_tier.DisplayName,
            t_tier.Badge,                                                           // 뱃지는 null 허용(HUD가 non-null일 때만 교체).
            t_points,
            t_hasNext ? t_next.RequiredPoints : t_points,
            !t_hasNext);
    }

    // 강등이 없도록 "가감 전" 티어의 임계치를 하한으로 클램프한다(해석을 가감 뒤로 미루면 하한도 내려가 강등이 성립).
    // 전투 씬 → 로비 씬 왕복을 견뎌야 하므로 지연 flush에 맡기지 않고 즉시 Save.
    public static void ApplyBattleResult(bool _won)
    {
        var t_config = Config;
        var t_slot = Slot;

        long t_points = t_slot.points;
        long t_delta = _won ? t_config.winPoints : -t_config.losePoints;

        int t_index = t_config.ResolveTierIndex(t_points);
        // 임계치 오설정(음수)·조회 실패는 하한 0으로 떨어뜨린다.
        long t_floor = t_config.TryGetTier(t_index, out RankTier t_tier) ? Math.Max(t_tier.RequiredPoints, 0) : 0;

        t_slot.points = Math.Max(t_points + t_delta, t_floor);
        Save();
    }

    /// <summary>부트스트랩에서 실제 애셋 주입(선택). null이면 기본 유지.</summary>
    public static void SetConfig(RankConfig _config)
    {
        if (_config != null) s_config = _config;
    }

    // 디버그 전용: 포인트만 0으로 되돌린다(티어는 파생이라 따로 리셋할 대상이 없다).
    public static void ResetForDebug()
    {
        Slot.points = 0;
        Save();
    }

    // 포인트는 슬롯에 직접 쓰이므로 영속화만 한다(별도 캐시 flush 없음).
    static void Save() => DataSaveManager.Save();
}

// 랭크 표시 1회 스냅샷(UI용). 포인트가 바뀌면 값이 달라지므로 표시 시점마다 GetInfo로 다시 받는다.
public readonly struct RankInfo
{
    public readonly int TierIndex;      // 도달 티어 인덱스(0 = 최하위)
    public readonly ERankGrade Grade;   // 도달 등급
    public readonly int Division;       // 등급 내 단계(1~4, 1이 최하위)
    public readonly string DisplayName; // 티어 표시명(항상 non-null)
    public readonly Sprite Badge;       // 등급 뱃지(없을 수 있음)
    public readonly long Points;        // 현재 랭크 포인트
    public readonly long NextRequired;  // 다음 티어 진입 임계치. 최대 티어면 Points와 같다(오름차순 저작이면 "남은 = NextRequired - Points"가 항상 성립).
    public readonly bool IsMaxTier;     // 다음 티어 없음

    public RankInfo(int _tierIndex, ERankGrade _grade, int _division, string _displayName, Sprite _badge, long _points, long _nextRequired, bool _isMaxTier)
    {
        TierIndex = _tierIndex;
        Grade = _grade;
        Division = _division;
        DisplayName = _displayName;
        Badge = _badge;
        Points = _points;
        NextRequired = _nextRequired;
        IsMaxTier = _isMaxTier;
    }
}
