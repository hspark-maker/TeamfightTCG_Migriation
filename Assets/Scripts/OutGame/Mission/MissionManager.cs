using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>서버 미션 봉투의 메모리 진실원. 완료 공식과 수령 낙인 해석은 이곳만 소유한다.</summary>
internal static class MissionManager
{
    const string DAILY_COMPLETION_EVENT = "CompleteDailyMissions";
    const string WEEKLY_COMPLETION_EVENT = "CompleteWeeklyMissions";

    static MissionSnapshot s_snapshot;
    static readonly List<MissionDefinition> s_definitions = new List<MissionDefinition>();

    internal static event Action OnChanged;

    internal static bool IsReady => s_snapshot != null;
    internal static long StateVersion { get; private set; }
    internal static IReadOnlyList<MissionDefinition> Definitions => s_definitions;
    internal static string DailyKey => s_snapshot?.DailyKey ?? string.Empty;
    internal static string WeeklyKey => s_snapshot?.WeeklyKey ?? string.Empty;
    internal static long PassExp => s_snapshot?.PassExp ?? 0L;
    internal static long DailyResetAtMs => s_snapshot?.DailyResetAtMs ?? 0L;
    internal static long WeeklyResetAtMs => s_snapshot?.WeeklyResetAtMs ?? 0L;

    /// <summary>변경 명령이 돌려준 상태만 채택한다. 정의 목록은 마지막 getMissions 결과를 유지한다.</summary>
    internal static void Adopt(MissionSnapshot _snapshot)
    {
        if (_snapshot == null) return;
        Normalize(_snapshot);
        s_snapshot = _snapshot;
        unchecked { StateVersion++; }
        NotifyChanged();
    }

    /// <summary>조회 응답의 상태와 정의를 함께 채택한다. 정의 순서도 서버 sortOrder를 따른다.</summary>
    internal static void Adopt(MissionSnapshot _snapshot, List<MissionDefinition> _definitions)
    {
        if (_snapshot == null) return;
        Normalize(_snapshot);
        s_snapshot = _snapshot;
        unchecked { StateVersion++; }

        s_definitions.Clear();
        if (_definitions != null)
        {
            for (int i = 0; i < _definitions.Count; i++)
            {
                MissionDefinition t_definition = _definitions[i];
                if (t_definition == null || string.IsNullOrEmpty(t_definition.Id) ||
                    string.IsNullOrEmpty(t_definition.Period) || string.IsNullOrEmpty(t_definition.Event))
                    continue;

                if (t_definition.Reward == null) t_definition.Reward = new MissionReward();
                if (t_definition.Reward.Currencies == null)
                    t_definition.Reward.Currencies = new List<ClaimRewardGain>();
                s_definitions.Add(t_definition);
            }
        }
        s_definitions.Sort(CompareDefinitions);
        NotifyChanged();
    }

    internal static long ProgressOf(MissionDefinition _definition)
    {
        if (_definition == null || s_snapshot?.Progress == null) return 0L;
        if ((_definition.Period == "daily" && _definition.Event == DAILY_COMPLETION_EVENT) ||
            (_definition.Period == "weekly" && _definition.Event == WEEKLY_COMPLETION_EVENT))
        {
            // 서버 completedMissions와 동일: 활성 정의의 일반 미션만 집계하고 수령 여부는 보지 않는다.
            // 변경 응답의 파생 키가 없거나 오래되어도 최신 일반 카운터로 계산한다.
            long t_completed = 0L;
            foreach (var t_mission in s_definitions)
            {
                if (t_mission.Period != _definition.Period ||
                    t_mission.Event == DAILY_COMPLETION_EVENT || t_mission.Event == WEEKLY_COMPLETION_EVENT)
                    continue;
                if (ProgressOf(t_mission) >= t_mission.Target) t_completed++;
            }
            return t_completed;
        }

        string t_key = ProgressKey(_definition.Period, _definition.Event);
        return s_snapshot.Progress.TryGetValue(t_key, out long t_progress) ? Math.Max(0L, t_progress) : 0L;
    }

    /// <summary>완료 공식의 유일한 구현: 일반 카운터 또는 파생 달성 수가 목표 이상인지 판정한다.</summary>
    internal static bool IsComplete(MissionDefinition _definition)
        => _definition != null && ProgressOf(_definition) >= _definition.Target;

    internal static bool IsClaimed(string _missionId)
        => !string.IsNullOrEmpty(_missionId) && s_snapshot?.Claimed != null &&
           s_snapshot.Claimed.TryGetValue(_missionId, out bool t_claimed) && t_claimed;

    internal static bool CanClaim(MissionDefinition _definition)
        => IsComplete(_definition) && !IsClaimed(_definition.Id) && !MissionCommands.IsInFlight(_definition.Id)
           && IsGuideUnlocked(_definition);

    internal static bool HasAnyClaimable(string _period)
    {
        foreach (var t_mission in s_definitions)
            if (t_mission.Period == _period && CanClaim(t_mission)) return true;
        return false;
    }

    internal static bool HasAnyRegularClaimable => HasAnyClaimable("daily") || HasAnyClaimable("weekly");

    internal static bool IsGuideUnlocked(MissionDefinition _definition)
    {
        if (_definition == null || _definition.Period != "guide") return true;
        foreach (var t_mission in s_definitions)
            if (t_mission.Period == "guide" && t_mission.SortOrder < _definition.SortOrder && !IsClaimed(t_mission.Id))
                return false;
        return true;
    }

    internal static MissionDefinition Find(string _missionId)
    {
        if (string.IsNullOrEmpty(_missionId)) return null;
        for (int i = 0; i < s_definitions.Count; i++)
            if (string.Equals(s_definitions[i].Id, _missionId, StringComparison.Ordinal)) return s_definitions[i];
        return null;
    }

    /// <summary>수령 왕복 시작·종료처럼 봉투 외 UI 상태만 바뀐 경우 다시 그리게 한다.</summary>
    internal static void NotifyCommandStateChanged() => NotifyChanged();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_snapshot = null;
        unchecked { StateVersion++; }
        s_definitions.Clear();
        OnChanged = null;
    }

    static string ProgressKey(string _period, string _event) => (_period ?? string.Empty) + "." + (_event ?? string.Empty);

    static int CompareDefinitions(MissionDefinition _left, MissionDefinition _right)
    {
        int t_period = string.CompareOrdinal(_left.Period, _right.Period);
        if (t_period != 0) return t_period;
        int t_order = _left.SortOrder.CompareTo(_right.SortOrder);
        return t_order != 0 ? t_order : string.CompareOrdinal(_left.Id, _right.Id);
    }

    static void Normalize(MissionSnapshot _snapshot)
    {
        if (_snapshot.Progress == null) _snapshot.Progress = new Dictionary<string, long>();
        if (_snapshot.Claimed == null) _snapshot.Claimed = new Dictionary<string, bool>();
    }

    // 미션 채택은 ServerSaveCommands의 공통 응답 배관 안에서 돈다. 화면 구독자 예외가
    // 세이브 revision·업로드 재개를 막아서는 안 되므로 이벤트 예외를 여기서 격리한다.
    static void NotifyChanged()
    {
        try { OnChanged?.Invoke(); }
        catch (Exception t_exception) { Debug.LogException(t_exception); }
    }
}
