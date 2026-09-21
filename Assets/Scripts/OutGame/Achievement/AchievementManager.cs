using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>업적 UI가 읽는 서버 상태. 클라이언트에서는 카운터나 수령 낙인을 쓰지 않는다.</summary>
internal static class AchievementManager
{
    static AchievementSnapshot s_snapshot;
    static readonly List<AchievementDefinition> s_definitions = new List<AchievementDefinition>();
    static bool s_hasDefinitions;

    internal static event Action OnChanged;
    internal static bool IsReady => s_snapshot != null && s_hasDefinitions;
    internal static long StateVersion { get; private set; }
    internal static IReadOnlyList<AchievementDefinition> Definitions => s_definitions;

    internal static void Adopt(AchievementSnapshot _snapshot)
    {
        if (!Accept(_snapshot)) return;
        NotifyChanged();
    }

    internal static void Adopt(AchievementSnapshot _snapshot, List<AchievementDefinition> _definitions)
    {
        if (!Accept(_snapshot)) return;
        s_definitions.Clear();
        if (_definitions != null)
            foreach (var t_definition in _definitions)
                if (t_definition != null && !string.IsNullOrEmpty(t_definition.Id) &&
                    !string.IsNullOrEmpty(t_definition.GroupId) && !string.IsNullOrEmpty(t_definition.Event) &&
                    t_definition.Stage > 0 && t_definition.Target > 0)
                    s_definitions.Add(t_definition);
        s_definitions.Sort((a, b) =>
        {
            int t_order = a.SortOrder.CompareTo(b.SortOrder);
            if (t_order != 0) return t_order;
            t_order = string.CompareOrdinal(a.GroupId, b.GroupId);
            return t_order != 0 ? t_order : a.Stage.CompareTo(b.Stage);
        });
        s_hasDefinitions = true;
        NotifyChanged();
    }

    static bool Accept(AchievementSnapshot _snapshot)
    {
        if (_snapshot == null || (s_snapshot != null && _snapshot.Revision < s_snapshot.Revision)) return false;
        _snapshot.Progress ??= new Dictionary<string, long>();
        _snapshot.Claimed ??= new Dictionary<string, bool>();
        s_snapshot = _snapshot;
        unchecked { StateVersion++; }
        return true;
    }

    internal static long ProgressOf(AchievementDefinition _definition)
    {
        if (_definition == null) return 0;
        if (PlayerStatisticsManager.IsReady && (s_snapshot == null ||
            PlayerStatisticsManager.Snapshot.AchievementRevision >= s_snapshot.Revision))
            return PlayerStatisticsManager.AchievementProgress(_definition.Event, _definition.SynergyId);
        // 혼합 배포·롤백 중 더 새 구 서버 응답은 다음 통계 조회까지 호환 사본으로 표시한다.
        if (s_snapshot == null) return 0;
        string t_key = _definition.Event == "PlaySynergy"
            ? "PlaySynergy:" + _definition.SynergyId : _definition.Event;
        return s_snapshot.Progress.TryGetValue(t_key, out long t_value) ? Math.Max(0, t_value) : 0;
    }

    internal static bool IsClaimed(string _id)
        => !string.IsNullOrEmpty(_id) && s_snapshot != null &&
           s_snapshot.Claimed.TryGetValue(_id, out bool t_claimed) && t_claimed;

    internal static bool CanClaim(AchievementDefinition _definition)
    {
        if (_definition == null || !IsReady || IsClaimed(_definition.Id) ||
            AchievementCommands.IsInFlight(_definition.Id) || ProgressOf(_definition) < _definition.Target) return false;
        foreach (var t_previous in s_definitions)
            if (t_previous.GroupId == _definition.GroupId && t_previous.Stage < _definition.Stage &&
                !IsClaimed(t_previous.Id)) return false;
        return true;
    }

    internal static bool HasAnyClaimable
    {
        get
        {
            foreach (var t_definition in s_definitions)
                if (CanClaim(t_definition)) return true;
            return false;
        }
    }

    internal static List<RewardLine> RewardLines(AchievementDefinition _definition)
    {
        var t_lines = new List<RewardLine>();
        foreach (var t_gain in ToGains(_definition?.Reward?.Currencies)) t_lines.Add(new RewardLine(t_gain));
        return t_lines;
    }

    internal static List<CurrencyGain> ToGains(IReadOnlyList<ClaimRewardGain> _currencies)
    {
        var t_gains = new List<CurrencyGain>();
        if (_currencies != null)
            foreach (var t_line in _currencies)
                if (t_line != null && t_line.Amount > 0 && CurrencyCode.TryParse(t_line.Currency, out var t_type))
                    t_gains.Add(new CurrencyGain(t_type, t_line.Amount));
        return t_gains;
    }

    internal static void ResetSession()
    {
        s_snapshot = null;
        s_definitions.Clear();
        s_hasDefinitions = false;
        unchecked { StateVersion++; }
        NotifyChanged();
    }

    internal static void NotifyCommandStateChanged() => NotifyChanged();

    internal static void NotifyStatisticsChanged()
    {
        unchecked { StateVersion++; }
        NotifyChanged();
    }

    static void NotifyChanged()
    {
        try { OnChanged?.Invoke(); }
        catch (Exception t_error) { Debug.LogException(t_error); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        OnChanged = null;
        ResetSession();
    }
}
