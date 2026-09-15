#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>안내 재개의 저장 호환성과 미션 변경·화면 이탈 복구를 메모리에서 검증한다.</summary>
public static class GuideResumeValidation
{
    const BindingFlags STATIC_FIELDS = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const EOutgameTutorialTrigger TRIGGER = EOutgameTutorialTrigger.CollectionTabFirstEnter;

    /// <summary>실제 세이브·서버·에셋을 변경하지 않고 재개 계약을 검사한다.</summary>
    [MenuItem("Tools/Tutorial/Validate Guide Resume")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode && !OnboardingPlayTest.IsActive
            && !OnboardingPlayTest.IsPreparing, "Stop play mode before validation.");
        RunTimeoutLifetime();
        var t_restore = new List<Action>();
        var t_data = ScriptableObject.CreateInstance<OutgameTutorialData>();
        try
        {
            Replace(t_restore, typeof(DataSaveManager), "OnSaved", null);
            Replace(t_restore, typeof(DataSaveManager), "s_immediateUploadHandler", null);
            Replace(t_restore, typeof(DataSaveManager), "<Data>k__BackingField", new UserSaveData());
            Replace(t_restore, typeof(OutgameTutorialRunner), "s_data", t_data);
            Replace(t_restore, typeof(OutgameTutorialRunner), "s_forcedCount", 0);
            Replace(t_restore, typeof(OutgameTutorialRunner), "s_guidedChapter", -1);
            Replace(t_restore, typeof(OutgameTutorialRunner), "s_guidedStep", 0);
            Replace(t_restore, typeof(OutgameTutorialRunner), "OnGuidedActivated", null);
            Replace(t_restore, typeof(OutgameTutorialRunner), "OnGuidedChanged", null);
            Replace(t_restore, typeof(OutgameTutorialRunner), "OnStepChanged", null);
            Replace(t_restore, typeof(OutgameTutorialGuide), "s_enhanceCard", 0);
            Replace(t_restore, typeof(OutgameTutorialGuide), "s_enhanceFree", false);
            Replace(t_restore, typeof(OutgameTutorialGuide), "s_growthAlreadyReached", false);
            ClearDeferred(t_restore);

            var t_flow = new GuideMissionFlow { missionId = "validation.past-mission", tutorial = TRIGGER };
            var t_chapter = new OutgameTutorialChapter { EditorTrigger = TRIGGER };
            var t_first = new TutorialStepDef();
            var t_second = new TutorialStepDef();
            t_first.SetStepIdForEditor(900001);
            t_second.SetStepIdForEditor(900002);
            t_chapter.EditorSteps.Add(t_first);
            t_chapter.EditorSteps.Add(t_second);
            t_data.guide.guideFlows.Add(t_flow);
            t_data.guide.guideChapters.Add(t_chapter);
            t_data.NormalizeChapterKinds();

            ValidateStorage(t_flow, t_second.StepId);
            Require(OutgameTutorialRunner.HasPending(TRIGGER),
                "A saved guide was lost because its mission is no longer current.");
            OutgameTutorialRunner.Fire(TRIGGER);
            Require(OutgameTutorialRunner.TryGetGuidedStep(out var t_step) && t_step.StepId == t_second.StepId,
                "Restart did not restore the stable step ID.");
            OutgameTutorialRunner.AbortGuided(TRIGGER);
            Require(GuideResume.HasPending && GuideResume.Record.StepId == t_second.StepId
                && GuideResume.Record.CardId == 900101 && GuideResume.Record.GoalReached,
                "Leaving the guide discarded progress, target or reached goal.");
            OutgameTutorialRunner.ResumeDeferred(TRIGGER);
            OutgameTutorialRunner.Fire(TRIGGER);
            Require(OutgameTutorialRunner.TryGetGuidedStep(out t_step) && t_step.StepId == t_second.StepId,
                "Explicit retry restarted a different step.");
            OutgameTutorialRunner.AbortGuided();
            GuideResume.SetStep(999999);
            OutgameTutorialRunner.Fire(TRIGGER);
            Require(OutgameTutorialRunner.TryGetGuidedStep(out t_step) && t_step.StepId == t_first.StepId,
                "A removed step ID did not fall back to a valid chapter step.");
            ValidateInputAndReturn(t_first);
            ValidateAuthoredGrowthSequence();
            OutgameTutorialRunner.FinishGuided(TRIGGER);
            Require(OutgameTutorialProgress.IsTriggerDone(TRIGGER) && !GuideResume.HasPending
                && GuideResume.Record == null, "Completing the guide left a stale resume record.");
            Require(!OutgameTutorialRunner.HasPending(TRIGGER), "A completed guide became pending again.");
            Debug.Log("[GuideResumeValidation] PASS: old save compatibility, target and goal roundtrip, mission drift, step restore, leave/retry, missing step fallback, completion cleanup, transition input/return lock, internal navigation scope, selected tab detection, authored #30/#61/#67 sequence. UI play and remote upload are not covered.");
        }
        finally
        {
            for (int t_i = t_restore.Count - 1; t_i >= 0; t_i--) t_restore[t_i]();
            UnityEngine.Object.DestroyImmediate(t_data);
        }
    }

    static void ValidateStorage(GuideMissionFlow _flow, int _stepId)
    {
        var t_old = JsonConvert.DeserializeObject<TutorialSaveData>("{\"outgameCompleted\":true}");
        Require(t_old != null && t_old.GuideResume == null, "An old save fabricated a pending guide.");
        DataSaveManager.Data.Tutorial = t_old;
        Require(!GuideResume.HasPending, "An empty resume record became pending.");
        GuideResume.Begin(_flow, 900101, 3);
        GuideResume.SetStep(_stepId);
        GuideResume.MarkGoalReached();
        GuideResume.MarkIntroductionSeen();
        GuideResume.Begin(_flow);
        string t_snapshot = DataSaveManager.CreateSnapshot();
        Set(typeof(DataSaveManager), "<Data>k__BackingField", JsonConvert.DeserializeObject<UserSaveData>(t_snapshot));
        Require(GuideResume.IsFor(TRIGGER) && GuideResume.Record.MissionId == _flow.missionId
            && GuideResume.Record.StepId == _stepId && GuideResume.Record.CardId == 900101
            && GuideResume.Record.TargetLevel == 3 && GuideResume.Record.GoalReached
            && GuideResume.Record.IntroductionSeen, "Save roundtrip or repeated Begin discarded resume facts.");
    }

    static void ClearDeferred(List<Action> _restore)
    {
        var t_set = (HashSet<EOutgameTutorialTrigger>)Get(typeof(OutgameTutorialRunner), "s_deferred");
        var t_previous = new List<EOutgameTutorialTrigger>(t_set);
        _restore.Add(() =>
        {
            t_set.Clear();
            foreach (var t_trigger in t_previous) t_set.Add(t_trigger);
        });
        t_set.Clear();
    }

    static void ValidateInputAndReturn(TutorialStepDef _step)
    {
        var t_owner = new GameObject("GuideResumeValidation.Owner") { hideFlags = HideFlags.HideAndDontSave };
        t_owner.SetActive(false);
        var t_blocker = new GameObject("GuideResumeValidation.Blocker", typeof(RectTransform), typeof(Image))
            { hideFlags = HideFlags.HideAndDontSave };
        var t_restore = new List<Action>();
        object t_action = InstanceField(_step, "action").GetValue(_step);
        object t_anchor = InstanceField(_step, "anchor").GetValue(_step);
        try
        {
            var t_coordinator = t_owner.AddComponent<GuidanceCoordinator>();
            var t_gate = t_owner.AddComponent<OutgameTutorialGateUI>();
            var t_tabs = t_owner.AddComponent<LobbyTabController>();
            Replace(t_restore, typeof(GuidanceCoordinator), "s_instance", t_coordinator);
            Replace(t_restore, typeof(OutgameTutorialGateUI), "<Instance>k__BackingField", t_gate);
            SetInstance(t_coordinator, "m_flowLocked", true);
            SetInstance(t_coordinator, "m_shell", t_tabs);
            SetInstance(t_tabs, "tabs", new List<LobbyTabController.Tab>
                { new LobbyTabController.Tab { tutorialAnchor = EOutgameTutorialAnchor.LobbyCollectionTab } });
            SetInstance(t_tabs, "m_currentIndex", 0);
            SetInstance(t_gate, "m_gateRoot", t_blocker);
            SetInstance(t_gate, "blocker", t_blocker.GetComponent<Image>());
            SetInstance(_step, "action", EOutgameTutorialAction.WaitClick);
            SetInstance(_step, "anchor", EOutgameTutorialAnchor.LobbyCollectionTab);
            Require(GuidanceCoordinator.IsCurrentTabAnchor(_step.Anchor)
                && !GuidanceCoordinator.IsCurrentTabAnchor(EOutgameTutorialAnchor.LobbyDeckTab),
                "Selected tab detection points at a hidden or different tab.");
            t_gate.ShowTransitionGate(t_coordinator);
            Require(t_gate.IsTransitionOnly && t_blocker.GetComponent<Image>().raycastTarget
                && t_blocker.GetComponent<Image>().color.a == 0f, "Transition is not a transparent input blocker.");
            Require(!GuidanceCoordinator.AllowsUserAction(_step.Anchor)
                && !GuidanceCoordinator.AllowsUserAction(EOutgameTutorialAnchor.None),
                "Checkpoint transition allowed target or unrelated input.");
            SetInstance(_step, "action", EOutgameTutorialAction.WaitLobbyReturn);
            Require(!GuidanceCoordinator.CanCloseCardDetail && !GuidanceCoordinator.CanCloseAlbum,
                "Checkpoint transition allowed an explicit return action before save confirmation.");
            using (GuidanceCoordinator.InternalNavigation())
                Require(GuidanceCoordinator.CanCloseCardDetail && GuidanceCoordinator.CanCloseAlbum,
                    "Owned internal screen restoration was blocked.");
            Require(!GuidanceCoordinator.IsInternalNavigation, "Internal navigation escaped its scope.");
            t_gate.Clear(t_coordinator);
            Require(GuidanceCoordinator.CanCloseCardDetail && GuidanceCoordinator.CanCloseAlbum,
                "Confirmed lobby return step cannot close its surfaces.");
            SetInstance(_step, "action", EOutgameTutorialAction.WaitCardDetailReturn);
            Require(GuidanceCoordinator.CanCloseCardDetail && !GuidanceCoordinator.CanCloseAlbum,
                "Card-detail return opened the album exit as well.");
            SetInstance(_step, "action", EOutgameTutorialAction.WaitClick);
            Require(GuidanceCoordinator.AllowsUserAction(_step.Anchor)
                && !GuidanceCoordinator.AllowsUserAction(EOutgameTutorialAnchor.LobbyDeckTab),
                "Confirmed click step does not restrict navigation to its target.");
        }
        finally
        {
            SetInstance(_step, "action", t_action);
            SetInstance(_step, "anchor", t_anchor);
            UnityEngine.Object.DestroyImmediate(t_owner);
            UnityEngine.Object.DestroyImmediate(t_blocker);
            for (int t_i = t_restore.Count - 1; t_i >= 0; t_i--) t_restore[t_i]();
        }
    }

    static void ValidateAuthoredGrowthSequence()
    {
        var t_asset = AssetDatabase.LoadAssetAtPath<OutgameTutorialData>(
            "Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset");
        Require(t_asset != null, "Authored tutorial asset is missing.");
        bool t_introPair = false;
        bool t_synergy = false;
        foreach (var t_chapter in t_asset.Chapters)
            for (int t_i = 0; t_i < t_chapter.StepCount; t_i++)
            {
                if (!t_chapter.TryGetStep(t_i, out var t_step)) continue;
                if (t_step.StepId == 30)
                    t_introPair = t_step.Completion == EOutgameTutorialCompletion.Enhance && t_step.WaitUnlockIntro
                        && t_chapter.TryGetStep(t_i + 1, out var t_next) && t_next.StepId == 61
                        && t_next.Completion == EOutgameTutorialCompletion.Confirm
                        && t_next.Anchor == EOutgameTutorialAnchor.CardDetailKeywordDescription;
                if (t_step.StepId == 67)
                    t_synergy = t_step.Completion == EOutgameTutorialCompletion.Enhance
                        && t_step.WaitUnlockIntro && t_step.FreeOfCharge;
            }
        Require(t_introPair, "#61 must follow the unlock-waiting #30 as a confirmation message.");
        Require(t_synergy, "#67 must be a free growth step that waits for unlock presentation.");
    }

    /// <summary>정상 종료 뒤 지연 콜백은 멈추고, 실제 타임아웃은 취소되는지 검사한다.</summary>
    public static void RunTimeoutLifetime()
    {
        IPlayerLoopItem t_finishedTimer;
        using (var t_source = new CancellationTokenSource())
        {
            using var t_timer = t_source.CancelAfterSlim(TimeSpan.Zero, DelayType.Realtime);
            t_finishedTimer = (IPlayerLoopItem)t_timer;
        }
        Require(!t_finishedTimer.MoveNext(), "Completed request left its timeout timer running.");
        using (var t_source = new CancellationTokenSource())
        {
            using var t_timer = t_source.CancelAfterSlim(TimeSpan.Zero, DelayType.Realtime);
            ((IPlayerLoopItem)t_timer).MoveNext();
            Require(t_source.IsCancellationRequested, "Timeout did not cancel its token source.");
        }
        Debug.Log("[GuideResumeValidation] PASS: disposed source is never cancelled after completion; timeout cancellation preserved.");
    }

    static FieldInfo InstanceField(object _target, string _field)
        => _target.GetType().GetField(_field, BindingFlags.Instance | BindingFlags.NonPublic);

    static void SetInstance(object _target, string _field, object _value)
        => InstanceField(_target, _field).SetValue(_target, _value);

    static object Get(Type _type, string _field) => _type.GetField(_field, STATIC_FIELDS).GetValue(null);
    static void Set(Type _type, string _field, object _value) => _type.GetField(_field, STATIC_FIELDS).SetValue(null, _value);

    static void Replace(List<Action> _restore, Type _type, string _field, object _value)
    {
        object t_previous = Get(_type, _field);
        _restore.Add(() => Set(_type, _field, t_previous));
        Set(_type, _field, _value);
    }

    static void Require(bool _condition, string _message)
    {
        if (!_condition) throw new InvalidOperationException(_message);
    }
}
#endif
