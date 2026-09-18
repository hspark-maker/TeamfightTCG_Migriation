#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>온보딩 세션 수명·완료 경계·저장 호환성을 외부 쓰기 없이 검사한다.</summary>
public static class OnboardingExecutionValidation
{
    const BindingFlags STATIC_FIELDS = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [MenuItem("Tools/Tutorial/Validate Onboarding Execution")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !OnboardingPlayTest.IsActive &&
            !OnboardingPlayTest.IsPreparing, "Stop play mode before validation.");
        var t_restore = new List<Action>();
        var t_data = ScriptableObject.CreateInstance<OutgameTutorialData>();
        int t_saveNotifications = 0;
        try
        {
            Replace(t_restore, typeof(DataSaveManager), "OnSaved", (Action<ESaveUploadTiming>)(_ => t_saveNotifications++));
            Replace(t_restore, typeof(DataSaveManager), "s_immediateUploadHandler", (Action)(() =>
                throw new InvalidOperationException("Description completion attempted an immediate upload.")));
            Replace(t_restore, typeof(DataSaveManager), "<Data>k__BackingField", new UserSaveData());
            Replace(t_restore, typeof(OutgameTutorialRunner), "s_data", t_data);
            Replace(t_restore, typeof(OutgameTutorialRunner), "s_forcedCount", 0);
            Replace(t_restore, typeof(OutgameTutorialRunner), "s_guidedChapter", 0);
            Replace(t_restore, typeof(OutgameTutorialRunner), "s_guidedStep", 0);
            Replace(t_restore, typeof(OnboardingSession), "s_version", 0);
            Replace(t_restore, typeof(OnboardingSession), "s_stepId", 0);
            Replace(t_restore, typeof(OnboardingSession), "s_completing", false);
            Replace(t_restore, typeof(OnboardingSession), "s_lifetime", null);
            Replace(t_restore, typeof(OnboardingSession), "<Phase>k__BackingField", EOnboardingPhase.Idle);

            var t_first = Message(910001);
            var t_second = Message(910002);
            var t_chapter = new OutgameTutorialChapter { EditorTrigger = EOutgameTutorialTrigger.CollectionTabFirstEnter };
            t_chapter.EditorSteps.Add(t_first);
            t_chapter.EditorSteps.Add(t_second);
            t_data.guide.guideChapters.Add(t_chapter);
            t_data.NormalizeChapterKinds();

            ValidateContracts();
            ValidateStorage();
            ValidateUnlockHistoryAdoption(t_restore);
            ValidateLifetime(t_first, t_second);
            ValidateCompletion(t_first, t_second);
            ValidatePackReplay(t_data);
            ValidateDefeatEnhance(t_data, t_restore);
            Require(t_saveNotifications > 0, "Description completion did not enqueue asynchronous save notification.");
            Debug.Log("[OnboardingExecutionValidation] PASS: stale generation, cancellation, description completion without remote confirmation, reentrant/late duplicate completion, all action contracts, legacy nullable records, execution/command JSON roundtrip, unlock history preserved across server responses, pack presentation replay and chapter/consumption isolation. Server and assets were not modified.");
        }
        finally
        {
            OnboardingSession.Suspend();
            for (int t_i = t_restore.Count - 1; t_i >= 0; t_i--) t_restore[t_i]();
            UnityEngine.Object.DestroyImmediate(t_data);
        }
    }

    static void ValidateDefeatEnhance(OutgameTutorialData _data, List<Action> _restore)
    {
        Replace(_restore, typeof(ContentUnlockManager), "s_initialized", false);
        Replace(_restore, typeof(OutgameTutorialRunner), "OnGuidedChanged", (Action)null);
        Replace(_restore, typeof(OutgameTutorialRunner), "OnBattleEntryRestored", (Action)null);
        Replace(_restore, typeof(OutgameFeatureLock), "OnChanged", (Action)null);
        var t_slot = new TutorialSaveData { StepId = 910003 };
        DataSaveManager.Data.Tutorial = t_slot;
        _data.ftueChapters.Clear();
        _data.guide.guideChapters.Clear();
        _data.guide.guideFlows.Clear();
        var t_beforeBattle = Message(910003);
        var t_forced = new OutgameTutorialChapter();
        t_forced.EditorSteps.Add(t_beforeBattle);
        _data.ftueChapters.Add(t_forced);
        var t_guided = new OutgameTutorialChapter { EditorTrigger = EOutgameTutorialTrigger.CollectionTabFirstEnter };
        t_guided.EditorSteps.Add(Message(910004));
        _data.guide.guideChapters.Add(t_guided);
        var t_flow = new GuideMissionFlow { missionId = "guide.01", tutorial = EOutgameTutorialTrigger.CollectionTabFirstEnter };
        _data.guide.guideFlows.Add(t_flow);
        _data.NormalizeChapterKinds();
        Set(typeof(OutgameTutorialRunner), "s_forcedCount", 1);
        Set(typeof(OutgameTutorialRunner), "s_guidedChapter", -1);
        var t_result = typeof(OutgameTutorialRunner).GetMethod("NotifyDefeatForEnhance", STATIC_FIELDS);
        t_result.Invoke(null, new object[] { false });
        Require(!t_slot.DefeatEnhancePending, "A win/draw scheduled defeat guidance.");
        t_result.Invoke(null, new object[] { true });
        Require(t_slot.DefeatEnhancePending && !OutgameTutorialRunner.TryBeginDefeatEnhance(),
            "Defeat guidance must wait for card grants and deck preparation before the next battle entry.");
        SetStep(t_beforeBattle, "action", EOutgameTutorialAction.BattleEntry);
        t_slot.Execution = new OnboardingExecutionSaveData { BattleEntryStepId = t_beforeBattle.StepId };
        Require(!OutgameTutorialRunner.TryBeginDefeatEnhance(), "An unfinished battle was interrupted by enhancement.");
        t_slot.Execution.BattleEntryStepId = 0;
        Require(OutgameTutorialRunner.TryBeginDefeatEnhance() && !OutgameTutorialRunner.IsRunning
            && OutgameTutorialRunner.IsDefeatEnhanceInterlude && GuideMissionFlows.IsEligible(t_flow)
            && OutgameTutorialRunner.HasPending(t_guided.Trigger, _includeDeferred: true)
            && t_slot.StepId == t_beforeBattle.StepId && !t_slot.OutgameCompleted,
            "The early enhancement did not pause the exact FTUE position without graduating.");
        Require(!GuideMissionFlows.IsEligible(new GuideMissionFlow { missionId = "guide.03",
                tutorial = EOutgameTutorialTrigger.AdventureUnlocked }), "Early enhancement unlocked another mission guide.");
        string t_snapshot = DataSaveManager.CreateSnapshot();
        Require(JObject.Parse(t_snapshot)["tutorial"]?["defeatEnhancePending"]?.Value<bool>() == true,
            "The defeat request was not serialized with its Firestore key.");
        DataSaveManager.Data.Tutorial = JsonConvert.DeserializeObject<UserSaveData>(t_snapshot).Tutorial;
        Require(OutgameTutorialRunner.IsDefeatEnhanceInterlude, "Reload lost the early enhancement request.");
        Set(typeof(OutgameTutorialRunner), "s_guidedChapter", 1);
        Set(typeof(OutgameTutorialRunner), "s_guidedStep", 0);
        OutgameTutorialRunner.FinishGuided();
        Require(OutgameTutorialRunner.IsDefeatEnhanceInterlude && !OutgameTutorialRunner.IsRunning
            && OutgameTutorialProgress.IsTriggerDone(t_guided.Trigger),
            "Finishing the guide resumed FTUE before completion confirmation and surface restoration.");
        // 앱 종료가 완료 저장과 로비 복귀 사이에 끼어도 같은 완료 복구 경로를 사용한다.
        DataSaveManager.Data.Tutorial = JsonConvert.DeserializeObject<UserSaveData>(DataSaveManager.CreateSnapshot()).Tutorial;
        Require(OutgameTutorialRunner.IsDefeatEnhanceInterlude, "Reload lost a completed guide's pending FTUE return.");
        OutgameTutorialRunner.ResumeAfterDefeatEnhance();
        Require(OutgameTutorialRunner.IsRunning && !OutgameTutorialRunner.IsDefeatEnhanceInterlude
            && OutgameTutorialProgress.StepId == t_beforeBattle.StepId && GuideResume.Record == null
            && !OutgameTutorialRunner.TryBeginDefeatEnhance(), "The guide failed to resume the saved FTUE battle once.");
        t_result.Invoke(null, new object[] { true });
        Require(!DataSaveManager.Data.Tutorial.DefeatEnhancePending, "A second defeat repeated completed enhancement guidance.");
        DataSaveManager.Data.Tutorial = new TutorialSaveData { OutgameCompleted = true };
        t_result.Invoke(null, new object[] { true });
        Require(!DataSaveManager.Data.Tutorial.DefeatEnhancePending, "Graduated players received the early FTUE branch.");
        Require(!JsonConvert.DeserializeObject<TutorialSaveData>("{}").DefeatEnhancePending,
            "Legacy saves acquired a fabricated defeat request.");
        Debug.Log("[DefeatEnhanceValidation] PASS: loss-only scheduling, grant/deck checkpoint, battle guard, FTUE suspension, isolated guide eligibility, pending/completed save roundtrips, exact resumption, no repeat and legacy defaults.");
    }

    static void ValidateContracts()
    {
        foreach (EOutgameTutorialAction t_action in Enum.GetValues(typeof(EOutgameTutorialAction)))
        {
            var t_meta = TutorialActionMeta.Of(t_action);
            Require(t_meta.Action == t_action && t_meta.HasExecutionContract, $"Missing contract: {t_action}.");
            Require(!t_meta.RequiresEntryConfirmation || t_meta.RequiresCompletionConfirmation,
                $"Entry confirmation loses its completion boundary: {t_action}.");
        }
        Require(!TutorialActionMeta.Of((EOutgameTutorialAction)int.MaxValue).HasExecutionContract,
            "An unknown action silently acquired an execution contract.");
        Require(!TutorialActionMeta.Of(EOutgameTutorialAction.Message).RequiresCompletionConfirmation,
            "Explanation requires a server checkpoint.");
        Require(TutorialActionMeta.Of(EOutgameTutorialAction.WaitSynergyDeck).RequiresCompletionConfirmation &&
            TutorialActionMeta.Of(EOutgameTutorialAction.WaitEnhance).RequiresCompletionConfirmation,
            "Deck or growth completion lost its save boundary.");
    }

    static void ValidateStorage()
    {
        var t_old = JsonConvert.DeserializeObject<TutorialSaveData>("{\"outgameCompleted\":false,\"stepId\":76}");
        Require(t_old.Execution == null && t_old.OnboardingCommand == null && t_old.StepId == 76,
            "Legacy progress fabricated a pending execution or lost its stable step ID.");
        DataSaveManager.Data.Tutorial = t_old;
        t_old.Execution = new OnboardingExecutionSaveData
            { Version = 1, StepId = 77, ConfirmedStepId = 76, Phase = "Confirming", Guided = true };
        t_old.OnboardingCommand = new OnboardingCommandSaveData
        {
            Version = 1, StepId = 77, Command = "openPack", TxId = "openPack:validation",
            ArgumentsJson = "{\"env\":\"test\",\"packId\":\"fixed\"}",
            ResultJson = "{\"revision\":123,\"cards\":[1,2]}", Completed = true, Consumed = false
        };
        string t_json = DataSaveManager.CreateSnapshot();
        var t_token = JObject.Parse(t_json)["tutorial"];
        Require(t_token["execution"]?["confirmedStepId"]?.Value<int>() == 76 &&
            t_token["onboardingCommand"]?["argumentsJson"]?.Value<string>() == t_old.OnboardingCommand.ArgumentsJson,
            "Execution or command storage keys drifted from Firestore names.");
        var t_copy = JsonConvert.DeserializeObject<UserSaveData>(t_json).Tutorial;
        Require(t_copy.Execution.StepId == 77 && t_copy.Execution.ConfirmedStepId == 76 &&
            t_copy.Execution.Guided && t_copy.Execution.Phase == "Confirming" &&
            t_copy.OnboardingCommand.TxId == t_old.OnboardingCommand.TxId &&
            t_copy.OnboardingCommand.Command == "openPack" && t_copy.OnboardingCommand.Completed &&
            !t_copy.OnboardingCommand.Consumed && t_copy.OnboardingCommand.ResultJson == t_old.OnboardingCommand.ResultJson,
            "Save roundtrip lost a command identity, result or confirmation boundary.");
        DataSaveManager.Data.Tutorial = new TutorialSaveData();
    }

    static void ValidateUnlockHistoryAdoption(List<Action> _restore)
    {
        Replace(_restore, typeof(DataSaveManager), "OnServerSlotsAdopted", (Action<ESaveSlot>)null);
        var t_history = new ContentUnlockSaveData
        {
            Version = 1,
            Unlocked = new List<string> { ContentUnlockManager.MISSION, ContentUnlockManager.CARD_ENHANCE },
            Pending = new List<string> { ContentUnlockManager.CARD_ENHANCE },
        };
        DataSaveManager.Data.Profile.ContentUnlocks = t_history;
        var t_patch = new ServerSlotPatch
        {
            Profile = new ProfileSaveData
            {
                AccountExp = 123,
                AccountRewardLevel = 4,
                ContentUnlocks = new ContentUnlockSaveData
                {
                    Version = 1,
                    Unlocked = new List<string> { ContentUnlockManager.MISSION },
                    Pending = new List<string> { ContentUnlockManager.MISSION },
                },
            },
        };
        typeof(DataSaveManager).GetMethod("AdoptServerSlots", STATIC_FIELDS).Invoke(null, new object[] { t_patch });
        Require(ReferenceEquals(DataSaveManager.Data.Profile.ContentUnlocks, t_history)
            && !t_history.Pending.Contains(ContentUnlockManager.MISSION)
            && t_history.Pending.Contains(ContentUnlockManager.CARD_ENHANCE),
            "A server response restored a completed introduction or erased an unlocked pending introduction.");
        Require(DataSaveManager.Data.Profile.AccountExp == 123
            && DataSaveManager.Data.Profile.AccountRewardLevel == 4,
            "Preserving introduction history prevented authoritative profile rewards from being adopted.");
        var t_remote = new UserSaveData();
        typeof(DataSaveManager).GetMethod("AdoptRemote", STATIC_FIELDS).Invoke(null, new object[] { t_remote });
        Require(ReferenceEquals(DataSaveManager.Data, t_remote)
            && !ReferenceEquals(DataSaveManager.Data.Profile.ContentUnlocks, t_history),
            "A fresh remote session inherited the previous session's introduction history.");
    }

    static void ValidateLifetime(TutorialStepDef _first, TutorialStepDef _second)
    {
        CancellationToken t_firstToken = OnboardingSession.Begin(_first, CancellationToken.None);
        int t_firstVersion = OnboardingSession.Version;
        Require(OnboardingSession.IsCurrent(t_firstVersion, _first.StepId), "New step did not own its lifetime.");
        CancellationToken t_secondToken = OnboardingSession.Begin(_second, CancellationToken.None);
        Require(t_firstToken.IsCancellationRequested && !OnboardingSession.IsCurrent(t_firstVersion, _first.StepId),
            "Late callback from replaced step remained current.");
        int t_secondVersion = OnboardingSession.Version;
        Require(!OnboardingSession.IsCurrent(t_secondVersion, _first.StepId), "Different step ID accepted a callback.");
        OnboardingSession.Suspend();
        Require(t_secondToken.IsCancellationRequested && !OnboardingSession.IsCurrent(t_secondVersion, _second.StepId),
            "Suspended step retained a live callback.");
        using var t_owner = new CancellationTokenSource();
        OnboardingSession.Begin(_first, t_owner.Token);
        t_owner.Cancel();
        Require(!OnboardingSession.IsCurrent(OnboardingSession.Version, _first.StepId),
            "Destroyed owner did not invalidate its step.");
    }

    static void ValidateCompletion(TutorialStepDef _first, TutorialStepDef _second)
    {
        Set(typeof(OutgameTutorialRunner), "s_guidedStep", 0);
        OnboardingSession.Begin(_first, CancellationToken.None);
        OnboardingSession.SetPhase(EOnboardingPhase.Waiting);
        int t_advances = 0;
        bool t_reentrant = true;
        var t_completion = OnboardingSession.CompleteAsync(_first, () =>
        {
            t_advances++;
            t_reentrant = Result(OnboardingSession.CompleteAsync(_first, () => t_advances++, CancellationToken.None));
            Set(typeof(OutgameTutorialRunner), "s_guidedStep", 1);
        }, CancellationToken.None);
        Require(Result(t_completion) && !t_reentrant && t_advances == 1,
            "Description completion stalled or accepted a reentrant duplicate.");
        Require(!Result(OnboardingSession.CompleteAsync(_first, () => t_advances++, CancellationToken.None)) && t_advances == 1,
            "Late completion advanced the next step.");
        Require(DataSaveManager.Data.Tutorial.Execution.StepId == _second.StepId,
            "Completed description did not save the next stable cursor.");

        Set(typeof(OutgameTutorialRunner), "s_guidedStep", 0);
        OnboardingSession.Begin(_first, CancellationToken.None);
        OnboardingSession.SetPhase(EOnboardingPhase.Waiting);
        using var t_cancel = new CancellationTokenSource();
        t_cancel.Cancel();
        bool t_cancelled = false;
        try { OnboardingSession.CompleteAsync(_first, () => t_advances++, t_cancel.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { t_cancelled = true; }
        Require(t_cancelled && t_advances == 1, "Cancelled completion changed progress.");
    }

    static bool Result(UniTask<bool> _task)
    {
        Require(_task.Status == UniTaskStatus.Succeeded, "Description unexpectedly awaited a remote operation.");
        return _task.GetAwaiter().GetResult();
    }

    static void ValidatePackReplay(OutgameTutorialData _data)
    {
        var t_purchase = Message(920001);
        var t_open = Message(920002);
        var t_acquire = Message(920003);
        SetStep(t_purchase, "action", EOutgameTutorialAction.WaitPurchase);
        SetStep(t_open, "action", EOutgameTutorialAction.WaitPackOpen);
        SetStep(t_acquire, "action", EOutgameTutorialAction.WaitClick);
        SetStep(t_acquire, "anchor", EOutgameTutorialAnchor.PackAcquireButton);
        var t_chapter = new OutgameTutorialChapter { EditorTrigger = EOutgameTutorialTrigger.CollectionTabFirstEnter };
        t_chapter.EditorSteps.Add(t_purchase);
        t_chapter.EditorSteps.Add(t_open);
        t_chapter.EditorSteps.Add(t_acquire);
        _data.guide.guideChapters.Clear();
        _data.guide.guideChapters.Add(t_chapter);
        _data.NormalizeChapterKinds();
        Set(typeof(OutgameTutorialRunner), "s_guidedStep", 1);
        var t_journal = new OnboardingCommandSaveData
        {
            StepId = t_purchase.StepId, Command = "openPack", TxId = "openPack:replay-validation", Completed = true,
            ArgumentsJson = JsonConvert.SerializeObject(new { env = ContentProfileConfig.Active.CloudEnvId, packId = "validation-pack" }),
            ResultJson = "{\"cards\":[{\"cardId\":910101,\"isNew\":true,\"snack\":0}]}"
        };
        DataSaveManager.Data.Tutorial.OnboardingCommand = t_journal;
        var t_method = typeof(DataSaveManager).Assembly.GetType("OnboardingCommands")
            .GetMethod("TryRestorePackPresentation", STATIC_FIELDS);
        bool Replay()
        {
            object[] t_args = { default(OpenedPack), null };
            bool t_restored = (bool)t_method.Invoke(null, t_args);
            if (t_restored)
                Require(((OpenedPack)t_args[0]).Cards.Count == 1 && (string)t_args[1] == "validation-pack",
                    "Pack replay changed the immutable result.");
            return t_restored;
        }
        string t_before = DataSaveManager.CreateSnapshot();
        Require(Replay(), "Saved opening step could not restore its purchased pack.");
        Set(typeof(OutgameTutorialRunner), "s_guidedStep", 2);
        Require(Replay(), "Saved acquire step could not restore its purchased pack.");
        Require(t_before == DataSaveManager.CreateSnapshot(), "Presentation replay changed save data.");
        t_journal.Consumed = true;
        Require(!Replay(), "Consumed pack operation was replayed.");
        t_journal.Consumed = false;
        SetStep(t_open, "action", EOutgameTutorialAction.Message);
        Require(!Replay(), "A presentation replay crossed an unrelated step.");
        SetStep(t_open, "action", EOutgameTutorialAction.WaitPackOpen);
        var t_other = new OutgameTutorialChapter { EditorTrigger = EOutgameTutorialTrigger.CollectionTabFirstEnter };
        var t_otherOpen = Message(930001);
        SetStep(t_otherOpen, "action", EOutgameTutorialAction.WaitPackOpen);
        t_other.EditorSteps.Add(t_otherOpen);
        _data.guide.guideChapters.Add(t_other);
        _data.NormalizeChapterKinds();
        Set(typeof(OutgameTutorialRunner), "s_guidedChapter", 1);
        Set(typeof(OutgameTutorialRunner), "s_guidedStep", 0);
        Require(!Replay(), "An old pack operation leaked into another chapter.");
        DataSaveManager.Data.Tutorial.OnboardingCommand = null;
        Require(!Replay(), "Legacy missing journal fabricated pack results.");
    }

    static void SetStep(TutorialStepDef _step, string _field, object _value)
        => typeof(TutorialStepDef).GetField(_field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_step, _value);

    static TutorialStepDef Message(int _id)
    {
        var t_step = new TutorialStepDef();
        t_step.SetStepIdForEditor(_id);
        typeof(TutorialStepDef).GetField("action", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(t_step, EOutgameTutorialAction.Message);
        return t_step;
    }

    static void Set(Type _type, string _field, object _value)
        => _type.GetField(_field, STATIC_FIELDS).SetValue(null, _value);

    static void Replace(List<Action> _restore, Type _type, string _field, object _value)
    {
        object t_previous = _type.GetField(_field, STATIC_FIELDS).GetValue(null);
        _restore.Add(() => Set(_type, _field, t_previous));
        Set(_type, _field, _value);
    }

    static void Require(bool _condition, string _message)
    {
        if (!_condition) throw new InvalidOperationException(_message);
    }
}
#endif
