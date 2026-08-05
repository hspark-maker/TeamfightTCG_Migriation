using System;
using UnityEngine;

// 랭크(표시용 티어 진행도)의 static 단일 창구 — 티어는 points의 순수 파생이라 세이브엔 points만 둔다
public static class RankManager
{
    static RankConfig s_config;

    // 현재 랭크 포인트
    public static long Points => Slot.points;

    static RankConfig Config
        => s_config != null ? s_config : (s_config = ScriptableObject.CreateInstance<RankConfig>());

    static RankSaveData Slot
    {
        get
        {
            var t_data = DataSaveManager.Data;
            if (t_data.rank == null) t_data.rank = new RankSaveData();
            return t_data.rank;
        }
    }

    // 랭크 표시용 1회 스냅샷
    public static RankInfo GetInfo()
    {
        var t_config = Config;
        long t_points = Points;

        int t_index = t_config.ResolveTierIndex(t_points);
        t_config.TryGetTier(t_index, out RankTier t_tier);
        bool t_hasNext = t_config.TryGetTier(t_index + 1, out RankTier t_next);

        return new RankInfo(
            t_index,
            t_tier.Grade,
            t_tier.Division,
            t_tier.DisplayName,
            t_tier.Badge,
            t_points,
            t_hasNext ? t_next.RequiredPoints : t_points,
            !t_hasNext);
    }

    // 전투 1회 정산(가감 전 티어 임계치를 하한으로 클램프해 강등을 막는다) + 즉시 저장
    public static RankApplyResult ApplyBattleResult(bool _won)
    {
        var t_config = Config;
        var t_slot = Slot;

        long t_points = t_slot.points;
        long t_delta = _won ? t_config.winPoints : -t_config.losePoints;

        int t_index = t_config.ResolveTierIndex(t_points);
        long t_floor = t_config.TryGetTier(t_index, out RankTier t_tier) ? Math.Max(t_tier.RequiredPoints, 0) : 0;

        t_slot.points = Math.Max(t_points + t_delta, t_floor);
        Save();

        return new RankApplyResult(
            t_slot.points - t_points,
            t_index,
            t_config.ResolveTierIndex(t_slot.points));
    }

    // 부트스트랩에서 실제 애셋 주입(선택). null이면 기본 유지
    public static void SetConfig(RankConfig _config)
    {
        if (_config != null) s_config = _config;
    }

    // 포인트만 0으로 되돌린다(디버그 전용)
    public static void ResetForDebug()
    {
        Slot.points = 0;
        Save();
    }

    static void Save() => DataSaveManager.Save();
}

// 전투 1회 정산 결과
public readonly struct RankApplyResult
{
    // 클램프 뒤 실제 증감(하한에 걸리면 요청 델타보다 작다)
    public readonly long Delta;
    public readonly int PrevTierIndex;
    public readonly int TierIndex;

    // 이번 정산으로 티어가 올랐는지
    public bool IsTierUp => this.TierIndex > this.PrevTierIndex;

    public RankApplyResult(long _delta, int _prevTierIndex, int _tierIndex)
    {
        Delta = _delta;
        PrevTierIndex = _prevTierIndex;
        TierIndex = _tierIndex;
    }
}

// 랭크 표시 1회 스냅샷(UI용)
public readonly struct RankInfo
{
    public readonly int TierIndex;
    public readonly ERankGrade Grade;
    public readonly int Division;
    public readonly string DisplayName;
    public readonly Sprite Badge;
    public readonly long Points;
    // 다음 티어 진입 임계치(최대 티어면 Points와 같다 — 0 나눗셈·음수 잔여 차단)
    public readonly long NextRequired;
    public readonly bool IsMaxTier;

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
