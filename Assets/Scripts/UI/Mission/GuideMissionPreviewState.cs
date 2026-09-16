/// <summary>프리뷰 클릭의 현재 행동과 실행 가능 여부.</summary>
internal readonly struct GuideMissionPreviewState
{
    internal enum EAction { Unavailable, Claiming, Claim, Resume, Start, Move }

    internal EAction Action { get; }
    internal bool CanExecute => Action != EAction.Unavailable && Action != EAction.Claiming;

    GuideMissionPreviewState(EAction _action)
    {
        Action = _action;
    }

    internal static GuideMissionPreviewState Of(MissionDefinition _mission)
    {
        if (!MissionManager.IsReady || _mission == null) return new GuideMissionPreviewState(EAction.Unavailable);
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission)) return new GuideMissionPreviewState(EAction.Unavailable);
        if (MissionCommands.IsInFlight(_mission.Id)) return new GuideMissionPreviewState(EAction.Claiming);
        if (MissionManager.CanClaim(_mission)) return new GuideMissionPreviewState(EAction.Claim);
        if (MissionManager.IsComplete(_mission)) return new GuideMissionPreviewState(EAction.Unavailable);
        switch (GuidanceCoordinator.GetMissionGuideAction(_mission.Id))
        {
            case GuidanceCoordinator.EMissionGuideAction.Resume: return new GuideMissionPreviewState(EAction.Resume);
            case GuidanceCoordinator.EMissionGuideAction.Start: return new GuideMissionPreviewState(EAction.Start);
        }
        string t_reason = GuideMissionNavigator.UnavailableReason(_mission);
        return new GuideMissionPreviewState(t_reason == null ? EAction.Move : EAction.Unavailable);
    }

}
