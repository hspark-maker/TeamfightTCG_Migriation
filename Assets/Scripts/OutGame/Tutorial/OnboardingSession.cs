using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;

public enum EOnboardingPhase { Idle, Preparing, Waiting, Executing, Confirming, Suspended, Failed }

[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public sealed class OnboardingExecutionSaveData
{
    [FirestoreProperty("version")] public int Version { get; set; } = 1;
    [FirestoreProperty("stepId")] public int StepId { get; set; }
    [FirestoreProperty("confirmedStepId")] public int ConfirmedStepId { get; set; }
    [FirestoreProperty("phase")] public string Phase { get; set; }
    [FirestoreProperty("guided")] public bool Guided { get; set; }
    [FirestoreProperty("battleEntryStepId")] public int BattleEntryStepId { get; set; }
}

/// <summary>온보딩 실행과 완료의 수명을 한 번만 확정한다.</summary>
public static class OnboardingSession
{
    static int s_version;
    static int s_stepId;
    static bool s_completing;
    static CancellationTokenSource s_lifetime;

    public static EOnboardingPhase Phase { get; private set; }
    public static bool IsActive => OutgameTutorialRunner.IsRunning || OutgameTutorialRunner.IsGuidedRunning;
    public static int CurrentStepId => OutgameTutorialGuide.TryGetCurrentStep(out var t_step) ? t_step.StepId : 0;
    public static bool IsBusy => Phase == EOnboardingPhase.Executing || Phase == EOnboardingPhase.Confirming
        || ServerSaveCommands.IsInFlight;
    public static bool CanAcceptCompletion => Phase == EOnboardingPhase.Waiting && !s_completing;
    public static int Version => s_version;

    public static CancellationToken Begin(TutorialStepDef _step, CancellationToken _destroy)
    {
        Suspend();
        s_stepId = _step?.StepId ?? 0;
        s_lifetime = CancellationTokenSource.CreateLinkedTokenSource(_destroy);
        Phase = EOnboardingPhase.Preparing;
        return s_lifetime.Token;
    }

    public static bool IsCurrent(int _version, int _stepId)
        => _version == s_version && s_stepId == _stepId && s_lifetime != null
            && !s_lifetime.IsCancellationRequested;

    public static void SetPhase(EOnboardingPhase _phase) => Phase = _phase;

    public static bool RequiresCompletionConfirmation(TutorialStepDef _step)
    {
        if (TutorialActionMeta.Of(_step.Action).RequiresCompletionConfirmation) return true;
        var t_data = OutgameTutorialRunner.Data;
        if (t_data == null) return false;
        foreach (var t_chapter in t_data.Chapters)
            if (t_chapter.StepCount > 0 && t_chapter.TryGetStep(t_chapter.StepCount - 1, out var t_last)
                && ReferenceEquals(t_last, _step)) return true;
        return false;
    }

    public static void Suspend()
    {
        s_version++;
        s_lifetime?.Cancel();
        s_lifetime?.Dispose();
        s_lifetime = null;
        s_completing = false;
        Phase = EOnboardingPhase.Suspended;
    }

    public static async UniTask<EOutgameTutorialStepResult> ExecuteAsync(TutorialStepDef _step,
        Func<CancellationToken, UniTask<EOutgameTutorialStepResult>> _execute, CancellationToken _ct)
    {
        Phase = EOnboardingPhase.Preparing;
        await OnboardingCommands.RecoverPendingAsync(_ct);
        _ct.ThrowIfCancellationRequested();
        Phase = TutorialActionMeta.Of(_step.Action).RequiresEntryConfirmation
            ? EOnboardingPhase.Executing : EOnboardingPhase.Preparing;
        var t_result = await _execute(_ct);
        _ct.ThrowIfCancellationRequested();
        Phase = t_result == EOutgameTutorialStepResult.Failed ? EOnboardingPhase.Failed : EOnboardingPhase.Waiting;
        return t_result;
    }

    public static async UniTask<bool> CompleteAsync(TutorialStepDef _step, Action _advance, CancellationToken _ct)
    {
        if (!CanAcceptCompletion || CurrentStepId != _step.StepId) return false;
        s_completing = true;
        int t_version = s_version;
        try
        {
            bool t_confirm = RequiresCompletionConfirmation(_step);
            if (t_confirm)
            {
                Phase = EOnboardingPhase.Confirming;
                if (!await GuideResume.SaveConfirmedAsync(_ct)) throw new InvalidOperationException("진행 결과를 저장하지 못했습니다.");
            }
            _ct.ThrowIfCancellationRequested();
            if (!IsCurrent(t_version, _step.StepId)) return false;
            _advance();
            if (!IsCurrent(t_version, _step.StepId)) return true;
            if (DataSaveManager.Data.Tutorial != null)
            {
                var t_record = DataSaveManager.Data.Tutorial.Execution ??= new OnboardingExecutionSaveData();
                t_record.StepId = CurrentStepId;
                t_record.Guided = OutgameTutorialRunner.IsGuidedRunning;
                t_record.Phase = t_confirm ? "Confirming" : "Waiting";
                OutgameTutorialProgress.Save();
            }
            if (t_confirm && !await GuideResume.SaveConfirmedAsync(_ct))
                throw new InvalidOperationException("진행 위치를 저장하지 못했습니다. 재시도하면 저장부터 이어갑니다.");
            _ct.ThrowIfCancellationRequested();
            if (!IsCurrent(t_version, _step.StepId)) return true;
            if (DataSaveManager.Data.Tutorial?.Execution is { } t_saved && t_confirm)
            {
                t_saved.ConfirmedStepId = t_saved.StepId;
                t_saved.Phase = "Waiting";
                OutgameTutorialProgress.Save();
            }
            Phase = EOnboardingPhase.Waiting;
            return true;
        }
        catch
        {
            if (t_version == s_version) Phase = EOnboardingPhase.Failed;
            throw;
        }
        finally { if (t_version == s_version) s_completing = false; }
    }
}
