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
        MissionDefinition t_predecessor = null;
        foreach (MissionDefinition t_definition in _definitions)
        {
            if (!GuideMissionTrack.IsGuide(t_definition)) continue;
            if (t_definition.Id == _missionId)
            {
                t_target = t_definition;
                break;
            }
            t_predecessor = t_definition;
        }
        if (t_target == null) return false;
        if (_isClaimed(_missionId)) return true;
        // 앞에 새 미션이 삽입돼도 기존 수령으로 도달한 콘텐츠 이용 자격은 유지한다.
        if (t_predecessor != null && _isClaimed(t_predecessor.Id)) return true;
        foreach (MissionDefinition t_definition in _definitions)
            if (GuideMissionTrack.IsGuide(t_definition) && t_definition.SortOrder > t_target.SortOrder
                && _isClaimed(t_definition.Id)) return true;
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
        ValidateFlowActivation();
        ValidateInsertedMissionAccess();
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

    static void ValidateFlowActivation()
    {
        var t_enhance = new GuideMissionFlow
        {
            missionId = "guide.01", tutorial = EOutgameTutorialTrigger.CollectionTabFirstEnter,
        };
        var t_adventure = new GuideMissionFlow
        {
            missionId = "guide.03", tutorial = EOutgameTutorialTrigger.AdventureUnlocked,
        };
        var t_graduation = new GuideMissionFlow();
        var t_flows = new List<GuideMissionFlow> { null, t_graduation, t_enhance, t_adventure };
        Require(GuideMissionFlows.TryGet(t_flows, t_enhance.tutorial, out var t_found)
            && ReferenceEquals(t_found, t_enhance), "Enhance chapter must resolve its mission flow.");
        Require(!GuideMissionFlows.TryGet(t_flows, EOutgameTutorialTrigger.KeywordGrowthFirstOpen, out _)
            && !GuideMissionFlows.TryGet(t_flows, EOutgameTutorialTrigger.None, out _)
            && !GuideMissionFlows.TryGet(null, t_enhance.tutorial, out _),
            "Unlinked chapters and unavailable flow data must not start tutorials.");
        t_graduation.tutorial = EOutgameTutorialTrigger.KeywordGrowthFirstOpen;
        Require(!GuideMissionFlows.TryGet(t_flows, t_graduation.tutorial, out _),
            "Graduation without a mission ID must not activate a chapter.");
        Require(!GuideMissionFlows.IsEligible(t_enhance, false, "guide.01")
            && !GuideMissionFlows.IsEligible(t_graduation, false, null), "FTUE must finish first.");
        Require(GuideMissionFlows.IsEligible(t_graduation, true, null)
            && !GuideMissionFlows.IsEligible(t_enhance, true, null),
            "Graduation introductions may run before mission data, mission tutorials may not.");
        Require(GuideMissionFlows.IsEligible(t_enhance, true, "guide.01")
            && !GuideMissionFlows.IsEligible(t_adventure, true, "guide.01"),
            "Only the active mission may start onboarding.");
        Require(!GuideMissionFlows.IsEligible(t_enhance, true, "guide.03")
            && GuideMissionFlows.IsEligible(t_adventure, true, "guide.03"),
            "Claim progression must activate the next flow without replaying past missions.");
        Require(!GuideMissionFlows.IsEligible(t_adventure, true, null)
            && !GuideMissionFlows.IsEligible(null, true, "guide.03"),
            "Completed missions and missing flows must not activate onboarding.");
    }

    static void ValidateInsertedMissionAccess()
    {
        var t_definitions = new List<MissionDefinition>
        {
            new MissionDefinition { Id = "daily.test", Period = "daily", SortOrder = 0 },
            new MissionDefinition { Id = "guide.06", Period = "guide", SortOrder = 6 },
            new MissionDefinition { Id = "guide.16", Period = "guide", SortOrder = 7 },
            new MissionDefinition { Id = "guide.08", Period = "guide", SortOrder = 8 },
            new MissionDefinition { Id = "guide.09", Period = "guide", SortOrder = 9 },
            new MissionDefinition { Id = "guide.11", Period = "guide", SortOrder = 10 },
            new MissionDefinition { Id = "guide.10", Period = "guide", SortOrder = 11 },
        };
        var t_claimed = new HashSet<string>(StringComparer.Ordinal) { "guide.06" };
        Require(GuideMissionProgress.CurrentOf(t_definitions, t_claimed.Contains)?.Id == "guide.16"
            && !GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "guide.08"),
            "A new account must complete the inserted mission before reaching the next act.");
        t_claimed.Add("guide.08");
        Require(GuideMissionProgress.CurrentOf(t_definitions, t_claimed.Contains)?.Id == "guide.16"
            && GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "guide.08")
            && GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "guide.09")
            && !GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "guide.11"),
            "An inserted mission must preserve claimed access and the next previously reached guide.");
        t_claimed.Add("guide.11");
        Require(GuideMissionProgress.CurrentOf(t_definitions, t_claimed.Contains)?.Id == "guide.16"
            && GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "guide.09")
            && GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "guide.10"),
            "Later claims must preserve earlier access and advance by sort order rather than mission ID.");
        t_claimed.Add("missing");
        t_claimed.Add("daily.test");
        Require(!GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "missing")
            && !GuideMissionProgress.HasReached(t_definitions, t_claimed.Contains, "daily.test"),
            "Historical claims must not unlock unknown or non-guide content.");
    }

    static void Require(bool _condition, string _message)
    {
        if (!_condition) throw new InvalidOperationException(_message);
    }
}
#endif
