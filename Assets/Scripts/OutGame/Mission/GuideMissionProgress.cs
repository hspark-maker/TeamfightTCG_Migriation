using System;
using System.Collections.Generic;

/// <summary>서버 가이드 미션의 현재 위치와 도달 여부를 판정한다.</summary>
internal static class GuideMissionProgress
{
    internal static bool IsReady
    {
        get
        {
            if (!MissionManager.IsReady) return false;
            foreach (MissionDefinition t_definition in MissionManager.Definitions)
                if (GuideMissionTrack.IsGuide(t_definition)) return true;
            return false;
        }
    }

    internal static MissionDefinition Current => IsReady
        ? CurrentOf(MissionManager.Definitions, MissionManager.IsClaimed) : null;

    internal static bool IsCurrent(string _missionId)
        => !string.IsNullOrEmpty(_missionId) && string.Equals(Current?.Id, _missionId, StringComparison.Ordinal);

    internal static bool HasReached(string _missionId)
        => IsReady && HasReached(MissionManager.Definitions, MissionManager.IsClaimed, _missionId);

    internal static MissionDefinition CurrentOf(IReadOnlyList<MissionDefinition> _definitions,
        Func<string, bool> _isClaimed)
    {
        if (_definitions == null || _isClaimed == null) return null;
        foreach (MissionDefinition t_definition in _definitions)
            if (GuideMissionTrack.IsGuide(t_definition) && !_isClaimed(t_definition.Id))
                return t_definition;
        return null;
    }

    internal static bool HasReached(IReadOnlyList<MissionDefinition> _definitions,
        Func<string, bool> _isClaimed, string _missionId)
    {
        if (_definitions == null || _isClaimed == null || string.IsNullOrEmpty(_missionId)) return false;
        MissionDefinition t_target = null;
        foreach (MissionDefinition t_definition in _definitions)
            if (GuideMissionTrack.IsGuide(t_definition) && t_definition.Id == _missionId)
            {
                t_target = t_definition;
                break;
            }
        if (t_target == null) return false;
        if (_isClaimed(_missionId)) return true;
        MissionDefinition t_current = CurrentOf(_definitions, _isClaimed);
        if (t_current == null) return false;
        return t_target.SortOrder < t_current.SortOrder || t_target.Id == t_current.Id;
    }
}

#if UNITY_EDITOR
/// <summary>서버 상태를 건드리지 않고 합성 가이드 진행을 검증한다.</summary>
public static class GuideMissionProgressValidation
{
    /// <summary>현재 미션과 영구 해금에 쓰는 도달 판정의 회귀를 검사한다.</summary>
    public static void Run()
    {
        var t_definitions = new List<MissionDefinition>
        {
            new MissionDefinition { Id = "daily.test", Period = "daily", SortOrder = 0 },
            new MissionDefinition { Id = "guide.01", Period = "guide", SortOrder = 1 },
            new MissionDefinition { Id = "guide.02", Period = "guide", SortOrder = 2 },
            new MissionDefinition { Id = "guide.03", Period = "guide", SortOrder = 3 },
        };
        var t_claimed = new HashSet<string>(StringComparer.Ordinal);
        Require(GuideMissionProgress.CurrentOf(null, t_claimed.Contains) == null
            && !GuideMissionProgress.HasReached(null, t_claimed.Contains, "guide.01"),
            "Unavailable mission definitions must not activate a guide.");
        Require(GuideMissionProgress.CurrentOf(t_definitions, null) == null
            && !GuideMissionProgress.HasReached(t_definitions, null, "guide.01"),
            "Unavailable claim state must not activate a guide.");
        Require(GuideMissionProgress.CurrentOf(t_definitions, t_claimed.Contains)?.Id == "guide.01"
            && GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "guide.01")
            && !GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "guide.03"),
            "Only the first guide must activate before claims.");
        t_claimed.Add("guide.01");
        Require(GuideMissionProgress.CurrentOf(t_definitions, t_claimed.Contains)?.Id == "guide.02"
            && GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "guide.01")
            && !GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "guide.03"),
            "Claim must advance the guide and preserve previously reached access.");
        t_claimed.Add("guide.02");
        Require(GuideMissionProgress.CurrentOf(t_definitions, t_claimed.Contains)?.Id == "guide.03"
            && GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "guide.03"),
            "Claiming predecessors must reach the next guide.");
        t_claimed.Add("guide.03");
        Require(GuideMissionProgress.CurrentOf(t_definitions, t_claimed.Contains) == null
            && GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "guide.01")
            && GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "guide.03"),
            "Completing all guides must preserve content access.");
        t_claimed.Add("missing");
        Require(!GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "missing")
            && !GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "daily.test"),
            "Unknown or non-guide missions cannot unlock guide content.");
    }

    static void Require(bool _condition, string _message)
    {
        if (!_condition) throw new InvalidOperationException(_message);
    }
}
#endif
