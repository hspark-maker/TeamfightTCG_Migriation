using System;
using UnityEngine;

// 랭크(표시용 티어 진행도)의 static 단일 창구 — 티어는 points의 순수 파생이라 세이브엔 points만 둔다
public static class RankManager
{
    static RankConfig s_config;

    // 현재 랭크 포인트
    public static long Points => Slot.points;

    /// <summary>첫 티어에 도달했는가. 튜토리얼 졸업 전(언랭크)과 브론즈 1을 가르는 유일한 판정 —
    /// 티어 인덱스는 미도달도 0으로 폴백하므로 인덱스로는 구분되지 않는다.</summary>
    public static bool IsRanked => Points >= Config.FirstTierPoints;

    /// <summary>다음 판이 승급전(단판 관문)인가. 일반 전투 천장이 '등급 천장 - 1'이라
    /// "등급 마지막 단계를 꽉 채웠지만 아직 승급은 아닌" 상태가 points 하나로 표현된다(세이브 필드 없음).</summary>
    public static bool IsPromoPending => PromoPendingAt(Points);

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

    // 현재 포인트가 가리키는 티어 인덱스(티어는 points의 순수 파생)
    public static int TierIndex => Config.ResolveTierIndex(Points);

    // 현재 티어의 등급. 등급만 필요한 호출부가 GetInfo() 체인을 늘어놓지 않게 하는 단일 창구.
    public static ERankGrade CurrentGrade => GetInfo().Grade;

    /// <summary>현재 티어의 AI 카드 레벨. **티어 기준값이지 실제 카드 레벨이 아니다**(카드별 값은 <see cref="AiCardLevelOf"/>).
    /// 난이도 축의 유일한 조회 지점 — 설정(RankConfig)을 밖으로 내보내지 않으려고 여기서 파생해 준다.</summary>
    public static int AiCardLevel => Config.AiCardLevelAt(TierIndex);

    /// <summary>현재 티어에서 카드 _card 한 장이 쓸 AI 레벨. 티어 기준 레벨 주변으로 카드마다 흩어지되,
    /// 강화 곡선 만렙을 넘지 않는다(넘으면 곡선에 없는 레벨이라 보너스가 멈춘 것처럼 보인다).</summary>
    public static int AiCardLevelOf(CardData _card)
    {
        int t_base  = Config.AiCardLevelAt(TierIndex);
        int t_level = _card == null
            ? t_base
            : KeepUnlocks(_card, Config.AiCardLevelForCard(TierIndex, CardCatalog.IdOf(_card)), t_base);

        int t_max = CardGrowthManager.MaxLevel;
        return t_max > 0 && t_level > t_max ? t_max : t_level;
    }

    /// <summary>티어 스냅샷 하나를 얻는다. 설정(RankConfig)을 밖으로 내보내지 않으면서
    /// 연출이 "도달한 등급"의 배지·표시명을 물을 수 있는 유일한 창구다.</summary>
    public static bool TryGetTier(int _index, out RankTier _tier) => Config.TryGetTier(_index, out _tier);

    /// <summary>등급 하나의 표시명·배지(단계 숫자 없이). 등급 단위 안내 문구가
    /// RankConfig를 직접 열지 않게 하는 창구다 — 미저작 등급이면 false + 빈 값.</summary>
    public static bool TryGetGradeDisplay(ERankGrade _grade, out string _name, out Sprite _badge)
    {
        _name  = string.Empty;
        _badge = null;

        var t_grades = Config.grades;
        if (t_grades == null) return false;

        for (int t_i = 0; t_i < t_grades.Count; t_i++)
        {
            RankGradeConfig t_entry = t_grades[t_i];
            if (t_entry == null || t_entry.grade != _grade) continue;

            _name  = t_entry.displayName;
            _badge = t_entry.badge;
            return true;
        }

        return false;
    }

    // 랭크 표시용 1회 스냅샷
    public static RankInfo GetInfo() => GetInfoAt(Points);

    /// <summary>임의의 포인트 값이 그리는 표시 스냅샷. 연출이 '전투 직전' 화면을 물을 때 쓴다 —
    /// 정산은 이미 끝나 GetInfo는 최종 상태만 돌려준다.</summary>
    public static RankInfo GetInfoAt(long _points)
    {
        var t_config = Config;
        long t_points = _points;

        int t_index = t_config.ResolveTierIndex(t_points);
        t_config.TryGetTier(t_index, out RankTier t_tier);
        bool t_hasNext = t_config.TryGetTier(t_index + 1, out RankTier t_next);

        // 미도달이면 표시명·배지·다음 목표를 언랭크 기준으로 바꿔 준다 — 등급은 첫 티어 것을 그대로 쓴다.
        bool t_unranked = t_points < t_config.FirstTierPoints;

        // 언랭크 배지가 미저작이면 첫 등급 배지로 폴백한다(빈 배지보다 낫다).
        Sprite t_badge = t_unranked && t_config.unrankedBadge != null ? t_config.unrankedBadge : t_tier.Badge;

        return new RankInfo(
            t_index,
            t_tier.Grade,
            t_tier.Division,
            t_unranked ? t_config.unrankedDisplayName : t_tier.DisplayName,
            t_badge,
            t_points,
            t_unranked ? 0 : t_tier.RequiredPoints,
            t_unranked ? t_config.FirstTierPoints : (t_hasNext ? t_next.RequiredPoints : t_points),
            !t_unranked && !t_hasNext,
            t_unranked);
    }

    /// <summary>언랭크(첫 티어 미도달) 상태의 표시값. 승급 연출이 '오르기 직전'으로 되돌릴 때 쓴다 —
    /// 그 시점엔 정산이 끝나 GetInfo가 이미 도달 상태를 돌려주므로 언랭크 표시를 따로 물어야 한다.
    /// 폴백 규칙(언랭크 배지 미저작 → 첫 등급 배지)은 GetInfo와 같다.</summary>
    public static void GetUnrankedDisplay(out string _displayName, out Sprite _badge)
    {
        var t_config = Config;
        t_config.TryGetTier(0, out RankTier t_first);

        _displayName = t_config.unrankedDisplayName;
        _badge       = t_config.unrankedBadge != null ? t_config.unrankedBadge : t_first.Badge;
    }

    /// <summary>다음 등급의 배지. 진행 호 끝에 세우는 승급 목표 표시가 쓴다 —
    /// 등급 테이블(RankConfig.grades)을 UI로 내보내지 않으려고 여기서 파생해 준다.
    /// 최대 등급이거나 배지가 미저작이면 false(그때는 목표를 감춘다).</summary>
    public static bool TryGetNextGradeBadge(out Sprite _badge)
    {
        _badge = null;

        var t_config = Config;
        if (t_config.grades == null) return false;

        int t_next = TierIndex / RankConfig.DivisionsPerGrade + 1;
        if (t_next >= t_config.grades.Count) return false;

        RankGradeConfig t_grade = t_config.grades[t_next];
        _badge = t_grade != null ? t_grade.badge : null;
        return _badge != null;
    }

    /// <summary>첫 티어(브론즈 1)로 진입시킨다 — 튜토리얼 졸업 보상. 이미 도달했으면 false(멱등).
    /// 반환 결과는 PrevTierIndex가 -1이라 IsTierUp이 참이 된다(진입 연출이 티어 상승과 같은 길을 탄다).</summary>
    public static bool TryEnterFirstTier(out RankApplyResult _result)
    {
        _result = default;
        if (IsRanked) return false;

        var t_slot = Slot;
        long t_points = t_slot.points;

        t_slot.points = Config.FirstTierPoints;
        Save();

        _result = new RankApplyResult(
            t_slot.points - t_points,
            -1,
            Config.ResolveTierIndex(t_slot.points),
            false,
            PromoPendingAt(t_slot.points));
        return true;
    }

    /// <summary>전투 1회 정산 + 즉시 저장. _tutorial = 이 전투가 튜토리얼 시나리오 전투인가
    /// (호출자가 TutorialConfig.IsActive를 넘긴다 — 랭크가 튜토리얼 도메인을 직접 보지 않게).</summary>
    public static RankApplyResult ApplyBattleResult(bool _won, bool _tutorial)
    {
        var t_config = Config;
        var t_slot = Slot;

        long t_points = t_slot.points;
        long t_delta = _won ? t_config.winPoints : -t_config.losePoints;

        int t_index = t_config.ResolveTierIndex(t_points);

        // 튜토리얼 전투는 승급전 경로를 타지 않는다 — 튜토는 첫 티어를 넘지 못하므로 관문에 설 일도 없다.
        if (!_tutorial && PromoPendingAt(t_points))
            return ApplyPromoResult(t_slot, t_points, t_index, _won);

        // 단계 강등이 없다 — 바닥은 현재 단계 진입선(한 번 켠 별은 꺼지지 않는다).
        // 언랭크(첫 티어 미도달)만 0 — 언랭크는 "튜토리얼 중"이라는 뜻을 이미 갖고 있다.
        long t_floor = IsRanked ? t_config.DivisionFloorPoints(t_points) : 0;

        // 일반 전투는 다음 등급 진입선 바로 아래에서 멈춘다 — 등급을 넘는 일은 승급전(위 분기) 한 판만 한다.
        // 최고 등급이면 GradeCeilingPoints가 long.MaxValue라 사실상 천장이 없다.
        long t_ceiling = t_config.GradeCeilingPoints(t_points) - 1;

        // 튜토리얼 전투는 첫 티어도 넘지 못한다 — 랭크 진입은 졸업(TryEnterFirstTier)만이 결정한다.
        // 마지막 튜토 전투의 승점까지 살도록 졸업은 그 전투 뒤로 미뤄져 있다(OutgameTutorialRunner.NotifyStepSatisfied).
        // 그래도 천장을 현재 포인트 아래로는 내리지 않는다 — 이미 랭크에 오른 세이브로 튜토 전투를 돌면(디버그 승급 등)
        // 고정 천장이 곧 강등이 된다.
        if (_tutorial) t_ceiling = Math.Min(t_ceiling, Math.Max(t_config.FirstTierPoints - 1, t_points));

        t_slot.points = Math.Min(Math.Max(t_points + t_delta, t_floor), t_ceiling);
        Save();

        return new RankApplyResult(
            t_slot.points - t_points,
            t_index,
            t_config.ResolveTierIndex(t_slot.points),
            false,
            PromoPendingAt(t_slot.points));
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

    /// <summary>승급전 대기선에 바로 세운다(디버그 전용). SetTierForDebug는 티어 임계치에 세우므로
    /// 대기선(다음 등급 진입선 - 1)에는 도달할 방법이 없어 따로 둔다.
    /// 언랭크이거나 최고 등급이면 아무것도 하지 않고 false.</summary>
    public static bool SetPromoStandbyForDebug()
    {
        if (!IsRanked) return false;

        long t_ceiling = Config.GradeCeilingPoints(Points);
        if (t_ceiling == long.MaxValue) return false;

        Slot.points = t_ceiling - 1;
        Save();

        return true;
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

    /// <summary>승급전 한 판의 정산 — 승리는 다음 등급 진입선, 패배는 현 단계 절반으로 **스냅**한다.
    /// 가감이 아닌 이유: 승급전은 점수판이 아니라 관문이다(가감이면 승급 직후 진행률이 어정쩡하게 남고,
    /// 승/패 점수가 비대칭이라 패배 복귀선도 정확히 절반이 되지 않는다).</summary>
    static RankApplyResult ApplyPromoResult(RankSaveData _slot, long _points, int _index, bool _won)
    {
        var t_config = Config;

        long t_ceiling = t_config.GradeCeilingPoints(_points);
        long t_floor   = t_config.DivisionFloorPoints(_points);

        // 승급전은 등급 마지막 단계에서만 서므로 천장 - 바닥 = 그 등급의 pointsPerDivision이다.
        _slot.points = _won ? t_ceiling : t_floor + (t_ceiling - t_floor) / 2;
        Save();

        return new RankApplyResult(
            _slot.points - _points,
            _index,
            t_config.ResolveTierIndex(_slot.points),
            true,
            PromoPendingAt(_slot.points));
    }

    // _points가 승급전 대기선(다음 등급 진입선 - 1)인가. 최고 등급은 천장이 long.MaxValue라 늘 false.
    static bool PromoPendingAt(long _points)
        => _points >= Config.FirstTierPoints && _points == Config.GradeCeilingPoints(_points) - 1;

    /// <summary>하향 편차는 체력만 깎는다 — 기준 레벨이 이미 연 시너지·키워드를 도로 잠그면 카드 정체성이 사라진다
    /// (시너지가 꺼진 카드는 집계에서 빠져 3장 요구 시너지가 성립조차 못 한다). 해금이 기준과 같아지는 가장 낮은 레벨까지만 내린다.</summary>
    static int KeepUnlocks(CardData _card, int _level, int _base)
    {
        if (_level >= _base) return _level;

        CardGrowthConfig t_growth = CardGrowthManager.Config;
        bool             t_synergy  = t_growth.SynergyUnlockedAt(_base);
        CardKeyword      t_keywords = t_growth.UnlockedKeywordsAt(_card, _base);

        for (int t_lv = _level; t_lv < _base; t_lv++)
        {
            if (t_growth.SynergyUnlockedAt(t_lv) == t_synergy && t_growth.UnlockedKeywordsAt(_card, t_lv) == t_keywords)
                return t_lv;
        }
        return _base;
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

    // 이 전투가 승급전이었는지(정산 전 상태)
    public readonly bool PrevPromoPending;

    // 이 전투로 승급전 대기에 들어갔는지(정산 후 상태)
    public readonly bool PromoPending;

    // 이번 정산으로 티어가 올랐는지
    public bool IsTierUp => this.TierIndex > this.PrevTierIndex;

    // 내렸는지. 첫 진입 센티널(PrevTierIndex = -1)은 여기 걸리지 않는다.
    public bool IsTierDown => this.TierIndex < this.PrevTierIndex;

    public RankApplyResult(long _delta, int _prevTierIndex, int _tierIndex, bool _prevPromoPending = false, bool _promoPending = false)
    {
        Delta = _delta;
        PrevTierIndex = _prevTierIndex;
        TierIndex = _tierIndex;
        PrevPromoPending = _prevPromoPending;
        PromoPending = _promoPending;
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
    // 현재 티어 진입 임계치 = 단계 안 진행률의 0% 기준점(언랭크면 0)
    public readonly long TierRequired;
    // 다음 티어 진입 임계치(최대 티어면 Points와 같다 — 0 나눗셈·음수 잔여 차단)
    public readonly long NextRequired;
    public readonly bool IsMaxTier;

    // 첫 티어 미도달(언랭크). TierIndex는 이때도 0이라 인덱스로는 구분되지 않는다.
    public readonly bool IsUnranked;

    /// <summary>현재 단계를 얼마나 채웠는가(0~1) = 별 줄이 그리는 값. 1승이 정확히 1/4이다.
    /// 최대 티어는 1 — 더 갈 곳이 없어 게이지를 비워 두면 오해가 된다.</summary>
    public float TierProgress
    {
        get
        {
            if (IsMaxTier) return 1f;

            long t_span = NextRequired - TierRequired;
            return t_span > 0 ? Mathf.Clamp01((float)(Points - TierRequired) / t_span) : 0f;
        }
    }

    public RankInfo(int _tierIndex, ERankGrade _grade, int _division, string _displayName, Sprite _badge, long _points, long _tierRequired, long _nextRequired, bool _isMaxTier, bool _isUnranked = false)
    {
        TierIndex = _tierIndex;
        Grade = _grade;
        Division = _division;
        DisplayName = _displayName;
        Badge = _badge;
        Points = _points;
        TierRequired = _tierRequired;
        NextRequired = _nextRequired;
        IsMaxTier = _isMaxTier;
        IsUnranked = _isUnranked;
    }
}
