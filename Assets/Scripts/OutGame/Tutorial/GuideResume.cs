using System;
using System.Threading;
using Cysharp.Threading.Tasks;

/// <summary>안내 진행과 대상을 서버 세이브에 보존한다.</summary>
public static class GuideResume
{
    public static GuideResumeSaveData Record => DataSaveManager.Data.Tutorial?.GuideResume;
    public static EOutgameTutorialTrigger Trigger => Record != null
        && Enum.TryParse(Record.Trigger, out EOutgameTutorialTrigger t_trigger) ? t_trigger : EOutgameTutorialTrigger.None;
    public static bool HasPending => Trigger != EOutgameTutorialTrigger.None
        && !OutgameTutorialProgress.IsTriggerDone(Trigger);

    public static bool IsFor(EOutgameTutorialTrigger _trigger) => HasPending && Trigger == _trigger;

    public static void Begin(GuideMissionFlow _flow, int _targetCard = 0, int _targetLevel = 0)
    {
        if (_flow == null || _flow.tutorial == EOutgameTutorialTrigger.None) return;
        if (DataSaveManager.Data.Tutorial == null) DataSaveManager.Data.Tutorial = new TutorialSaveData();
        if (!IsFor(_flow.tutorial))
            DataSaveManager.Data.Tutorial.GuideResume = new GuideResumeSaveData
            {
                Trigger = _flow.tutorial.ToString(), MissionId = _flow.missionId,
            };
        if (_targetCard > 0) Record.CardId = _targetCard;
        if (_targetLevel > 0) Record.TargetLevel = _targetLevel;
        OutgameTutorialProgress.Save();
    }

    public static void SetStep(int _stepId)
    {
        if (Record == null || _stepId <= 0 || Record.StepId == _stepId) return;
        Record.StepId = _stepId;
        OutgameTutorialProgress.Save();
    }

    public static void SetTarget(int _cardId, int _targetLevel)
    {
        if (Record == null) return;
        Record.CardId = _cardId;
        Record.TargetLevel = _targetLevel;
        OutgameTutorialProgress.Save();
    }

    public static void MarkGoalReached()
    {
        if (Record == null || Record.GoalReached) return;
        Record.GoalReached = true;
        OutgameTutorialProgress.Save();
    }

    public static void MarkIntroductionSeen()
    {
        if (Record == null || Record.IntroductionSeen) return;
        Record.IntroductionSeen = true;
        OutgameTutorialProgress.Save();
    }

    public static void Clear()
    {
        if (Record == null) return;
        DataSaveManager.Data.Tutorial.GuideResume = null;
        OutgameTutorialProgress.Save();
    }

    public static async UniTask<bool> SaveConfirmedAsync(CancellationToken _ct)
    {
#if UNITY_EDITOR
        if (OnboardingPlayTest.IsActive || OnboardingPlayTest.IsPreparing) return true;
#endif
        return await PlayerSaveCloud.FlushConfirmedAsync(_ct);
    }
}
