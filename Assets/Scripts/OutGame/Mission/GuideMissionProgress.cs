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
