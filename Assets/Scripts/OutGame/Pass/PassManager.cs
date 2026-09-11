using System;
using System.Collections.Generic;

/// <summary>배틀패스 상태의 메모리 진실원. 시즌 정의·곡선·보상은 전부 서버가 준 값이고
/// 이 클래스는 사본을 만들지 않는다 — 레벨 판정도 서버가 준 currentLevel 을 그대로 쓴다.
///
/// <para>시즌이 끝나면 exp·수령 낙인이 리셋되는데 그 판정도 서버 몫이다. 클라가 기간을 재면
/// 기기 시계가 앞선 유저에게 다음 시즌 화면이 먼저 뜬다.</para>
/// </summary>
internal static class PassManager
{
    static PassGetResponse s_snapshot;

    internal static event Action OnChanged;

    /// <summary>서버 응답을 한 번이라도 채택했는가. false면 화면은 조회 중 상태로 그린다.</summary>
    internal static bool IsReady => s_snapshot != null;

    /// <summary>활성 시즌이 있는가. 시즌 공백기에는 false다(응답은 왔지만 season 이 null).</summary>
    internal static bool HasSeason => s_snapshot?.Season != null;

    internal static bool HasAnyClaimable
    {
        get
        {
            foreach (var t_level in Levels)
                if (CanClaim(t_level) || CanClaimPremium(t_level)) return true;
            return CanClaimRepeat;
        }
    }

    internal static PassSeasonDefinition Season => s_snapshot?.Season;
    internal static IReadOnlyList<string> PackChoices => (IReadOnlyList<string>)s_snapshot?.PackChoices ?? Array.Empty<string>();

    internal static IReadOnlyList<PassLevelDefinition> Levels
        => (IReadOnlyList<PassLevelDefinition>)s_snapshot?.Levels ?? Array.Empty<PassLevelDefinition>();

    internal static long Exp => s_snapshot?.Progress?.Exp ?? 0L;
    internal static bool PremiumUnlocked => s_snapshot?.Progress?.PremiumUnlocked ?? false;

    internal static PassRepeatDefinition Repeat => s_snapshot?.Repeat;
    internal static long RepeatClaimedCount => s_snapshot?.Progress?.RepeatClaimed ?? 0L;
    internal static long MaxRequiredExp => Levels.Count > 0 ? Levels[Levels.Count - 1].RequiredExp : 0L;
    internal static bool HasRepeatReward => Repeat?.RequiredExp > 0 &&
        (Repeat.Reward?.Exists(t_gain => t_gain != null && t_gain.Amount > 0) ?? false);
    internal static long RepeatEarnedCount => HasRepeatReward && Levels.Count > 0
        ? Math.Max(0L, Exp - MaxRequiredExp) / Repeat.RequiredExp : 0L;
    internal static long RepeatAvailableClaims => Math.Max(0L, RepeatEarnedCount - RepeatClaimedCount);
    internal static long RepeatProgressExp => HasRepeatReward
        ? Math.Max(0L, Exp - MaxRequiredExp) % Repeat.RequiredExp : 0L;
    internal static bool IsSeasonOpen => HasSeason && Season.StartAtMs <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < Season.EndAtMs;
    internal static bool CanClaimRepeat => IsSeasonOpen && HasRepeatReward && RepeatAvailableClaims > 0
        && !PassCommands.IsRepeatInFlight;

    // 구매 UX의 예상치. 실제 결제·경험치 지급 경로는 아직 연결하지 않는다.
    internal static long PremiumBaseExp => Levels.Count > 1
        ? Math.Max(0L, MaxRequiredExp - Levels[Levels.Count - 2].RequiredExp) : MaxRequiredExp;

    internal static long PremiumExpAt(long _nowMs)
    {
        if (!HasSeason || _nowMs < Season.StartAtMs || _nowMs >= Season.EndAtMs) return 0L;
        double t_days = Math.Ceiling((Season.EndAtMs - Season.StartAtMs) / 86400000d);
        double t_remainingDays = Math.Ceiling((Season.EndAtMs - _nowMs) / 86400000d);
        double t_elapsed = t_days <= 1 ? 1 : Math.Max(0d, Math.Min(1d, (t_days - t_remainingDays) / (t_days - 1d)));
        return PremiumBaseExp + (long)Math.Floor(PremiumBaseExp * 3d * t_elapsed);
    }

    internal static void AdoptRepeat(ClaimPassRepeatRewardResult _result)
    {
        if (_result?.Progress == null || !HasSeason || _result.Progress.SeasonId != Season.SeasonId) return;
        if (_result.Repeat != null) s_snapshot.Repeat = _result.Repeat;
        Adopt(_result.Progress);
    }

    internal static int CurrentLevel => s_snapshot?.CurrentLevel ?? 0;

    /// <summary>다음 레벨의 누적 문턱. 최대 레벨이면 null이다.</summary>
    internal static long? NextRequiredExp => s_snapshot?.NextRequiredExp;

    internal static void Adopt(PassGetResponse _response)
    {
        if (_response == null) return;

        s_snapshot = _response;
        NotifyChanged();
    }

    /// <summary>수령 응답이 돌려준 진행 상태만 갈아끼운다. 정의·곡선은 마지막 getPass 결과를 유지한다.</summary>
    internal static void Adopt(PassProgress _progress)
    {
        if (_progress == null || s_snapshot == null) return;

        s_snapshot.Progress = _progress;
        s_snapshot.CurrentLevel = LevelOf(_progress.Exp);
        s_snapshot.NextRequiredExp = NextThresholdOf(s_snapshot.CurrentLevel);
        NotifyChanged();
    }

    internal static bool IsClaimed(int _level)
    {
        Dictionary<string, bool> t_claimed = s_snapshot?.Progress?.Claimed;
        return t_claimed != null && t_claimed.TryGetValue(_level.ToString(), out bool t_value) && t_value;
    }

    internal static bool IsPremiumClaimed(int _level)
    {
        Dictionary<string, bool> t_claimed = s_snapshot?.Progress?.PremiumClaimed;
        return t_claimed != null && t_claimed.TryGetValue(_level.ToString(), out bool t_value) && t_value;
    }

    internal static bool CanClaimPremium(PassLevelDefinition _level)
        => IsSeasonOpen && PremiumUnlocked && _level != null &&
           ((_level.PremiumReward?.Exists(t_gain => t_gain != null && t_gain.Amount > 0) ?? false) ||
            (_level.PremiumItems?.Exists(t_item => t_item != null && t_item.Amount > 0) ?? false)) &&
           !PassCommands.IsInFlight(_level.Level) && Exp >= _level.RequiredExp && !IsPremiumClaimed(_level.Level);

    /// <summary>수령 가능한가. 서버가 다시 판정하므로 이 값은 버튼 표시용 낙관 검사다.</summary>
    internal static bool CanClaim(PassLevelDefinition _level)
        => HasSeason && _level != null && HasReward(_level) && !PassCommands.IsInFlight(_level.Level) &&
           Exp >= _level.RequiredExp && !IsClaimed(_level.Level);

    static bool HasReward(PassLevelDefinition _level)
        => (_level.Reward?.Exists(t_gain => t_gain != null && t_gain.Amount > 0) ?? false)
           || (_level.Items?.Exists(t_item => t_item != null && t_item.Amount > 0) ?? false);

    internal static void NotifyCommandStateChanged() => NotifyChanged();

    internal static void Clear()
    {
        s_snapshot = null;
        NotifyChanged();
    }

    // 수령 응답에는 레벨이 실려 오지 않는다 — 곡선은 이미 있으므로 경험치로 되짚는다.
    static int LevelOf(long _exp)
    {
        int t_reached = 0;
        foreach (PassLevelDefinition t_level in Levels)
        {
            if (t_level == null || _exp < t_level.RequiredExp) break;
            t_reached = t_level.Level;
        }
        return t_reached;
    }

    static long? NextThresholdOf(int _currentLevel)
    {
        foreach (PassLevelDefinition t_level in Levels)
            if (t_level != null && t_level.Level > _currentLevel) return t_level.RequiredExp;

        return null;
    }

    static void NotifyChanged() => OnChanged?.Invoke();
}
