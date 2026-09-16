/// <summary>프리뷰의 실행 문구와 클릭이 공유하는 현재 행동.</summary>
internal readonly struct GuideMissionPreviewState
{
    internal enum EAction { Unavailable, Claiming, Claim, Resume, Start, Move }

    internal EAction Action { get; }
    internal string Label { get; }
    internal bool CanExecute => Action != EAction.Unavailable && Action != EAction.Claiming;

    GuideMissionPreviewState(EAction _action, string _label)
    {
        Action = _action;
        Label = _label;
    }

    internal static GuideMissionPreviewState Of(MissionDefinition _mission)
    {
        if (!MissionManager.IsReady || _mission == null) return Unavailable("불러오는 중…");
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission)) return Unavailable("해금 대기");
        if (MissionCommands.IsInFlight(_mission.Id)) return new GuideMissionPreviewState(EAction.Claiming, "수령 중…");
        if (MissionManager.CanClaim(_mission)) return new GuideMissionPreviewState(EAction.Claim, "보상 받기");
        if (MissionManager.IsComplete(_mission)) return Unavailable("수령 확인 중…");
        switch (GuidanceCoordinator.GetMissionGuideAction(_mission.Id))
        {
            case GuidanceCoordinator.EMissionGuideAction.Resume: return new GuideMissionPreviewState(EAction.Resume, "안내 계속 ›");
            case GuidanceCoordinator.EMissionGuideAction.Start: return new GuideMissionPreviewState(EAction.Start, "안내 시작 ›");
        }
        string t_reason = GuideMissionNavigator.UnavailableReason(_mission);
        return t_reason == null ? new GuideMissionPreviewState(EAction.Move, "이동 ›") : Unavailable(t_reason);
    }

    static GuideMissionPreviewState Unavailable(string _reason) => new GuideMissionPreviewState(EAction.Unavailable, _reason);
}
