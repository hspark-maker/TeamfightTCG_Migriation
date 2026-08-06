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

    /// <summary>현재 티어에서 AI가 쓸 카드 레벨. 난이도 축의 유일한 조회 지점 —
    /// 설정(RankConfig)을 밖으로 내보내지 않으려고 여기서 파생해 준다.</summary>
    public static int AiCardLevel => Config.AiCardLevelAt(Config.ResolveTierIndex(Points));

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

    /// <summary>티어를 _index로 바로 옮긴다(디버그 전용). 포인트를 그 티어의 진입 임계치에 맞춘다 —
    /// 티어는 points의 순수 파생이라 티어를 직접 쓸 곳이 없고, 임계치에 세워야 표시·보상이 다 맞는다.
    /// 범위 밖은 양끝으로 클램프. 반환값 = 실제로 도달한 티어 인덱스.</summary>
    public static int SetTierForDebug(int _index)
    {
        var t_config = Config;
        int t_last   = t_config.TierCount - 1;
        if (t_last < 0) return 0;

        int t_target = _index < 0 ? 0 : (_index > t_last ? t_last : _index);
        if (!t_config.TryGetTier(t_target, out RankTier t_tier)) return t_config.ResolveTierIndex(Points);

        Slot.points = t_tier.RequiredPoints;
        Save();

        return t_config.ResolveTierIndex(Slot.points);
    }

    /// <summary>티어를 _step만큼 올린다(음수면 내린다). 디버그 전용.</summary>
    public static int StepTierForDebug(int _step)
        => SetTierForDebug(Config.ResolveTierIndex(Points) + _step);

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
