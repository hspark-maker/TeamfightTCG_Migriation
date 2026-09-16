#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>서버·씬 전환 없이 복귀 커버의 안내 준비 소유권과 대기 수명을 검증한다.</summary>
public static class LobbyReturnPreparationValidation
{
    const BindingFlags STATIC = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static string LastResult { get; private set; } = "Not run";

    [MenuItem("Tools/Tutorial/Validate Lobby Return Preparation")]
    public static void Run() => RunAsync().Forget(Debug.LogException);

    public static async UniTask RunAsync()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop play mode before validation.");
        Require(LastResult != "Running", "Validation is already running.");
        LastResult = "Running";
        var t_restore = new List<Action>();
        var t_objects = new List<UnityEngine.Object>();
        var t_scene = SceneManager.GetActiveScene();
        var t_originalSave = DataSaveManager.Data;
        using var t_lifetime = new CancellationTokenSource();
        try
        {
            var t_data = ScriptableObject.CreateInstance<OutgameTutorialData>();
            t_objects.Add(t_data);
            Replace(t_restore, typeof(DataSaveManager), "<Data>k__BackingField", new UserSaveData());
            Replace(t_restore, typeof(OutgameTutorialRunner), "s_data", t_data);
            Replace(t_restore, typeof(OutgameTutorialRunner), "s_guidedChapter", -1);
            Replace(t_restore, typeof(OnboardingSession), "<Phase>k__BackingField", EOnboardingPhase.Waiting);
            Replace(t_restore, typeof(OutgameTutorialGateUI), "<Instance>k__BackingField", null);

            var t_cover = Inactive<LoadingCoverView>("LobbyReturnValidation.Cover", t_objects);
            var t_bridge = Inactive<OutgameTutorialBridge>("LobbyReturnValidation.Bridge", t_objects);
            Replace(t_restore, typeof(LoadingCoverView), "s_active", t_cover);
            Replace(t_restore, typeof(OutgameTutorialBridge), "s_instance", t_bridge);
            Set(t_cover, "m_targetScene", "LobbyScene");
            ValidateCoverOwnership(t_cover);

            var t_step = new TutorialStepDef();
            t_step.SetStepIdForEditor(990001);
            Set(t_step, "action", EOutgameTutorialAction.Message);
            Set(t_bridge, "m_step", t_step);
            Set(t_bridge, "m_applying", true);
            var t_preparing = OutgameTutorialBridge.PrepareLobbyReturnAsync(t_lifetime.Token).Preserve();
            await UniTask.Yield();
            Require(t_preparing.Status == UniTaskStatus.Pending && Read<bool>(t_bridge, "m_returnPreparing"),
                "The return cover released an unfinished server or surface preparation.");
            Require(LoadingCoverView.OwnsLobbyPreparation, "The pending preparation lost its cover owner.");

            Set(t_bridge, "m_enhancing", true);
            Set(t_bridge, "m_awaitingUnlockFx", true);
            Set(t_bridge, "m_rankPrepared", true);
            Set(t_bridge, "m_rankPreparing", true);
            Set(t_bridge, "m_completing", true);
            Set(t_bridge, "m_applying", false);
            await UniTask.Yield();
            Require(t_preparing.Status == UniTaskStatus.Pending, "Preparation ignored rank confirmation in flight.");
            Set(t_bridge, "m_rankPreparing", false);
            await UniTask.Yield();
            Require(t_preparing.Status == UniTaskStatus.Pending, "Preparation ignored the first-step completion save.");
            Set(t_bridge, "m_completing", false);
            await t_preparing;
            Require(!Read<bool>(t_bridge, "m_returnPreparing") && ReferenceEquals(Read<object>(t_bridge, "m_step"), t_step),
                "Preparation waited for a user action or discarded the pending presentation.");

            Set(t_bridge, "m_applying", true);
            var t_failing = OutgameTutorialBridge.PrepareLobbyReturnAsync(t_lifetime.Token).Preserve();
            var t_failure = new InvalidOperationException("Injected return preparation failure");
            Invoke(t_bridge, "ShowStepFailure", t_failure);
            Require(ReferenceEquals(Read<object>(t_bridge, "m_returnFailure"), t_failure),
                "Bridge failure escaped return-cover ownership.");
            Set(t_bridge, "m_applying", false);
            try
            {
                await t_failing;
                throw new InvalidOperationException("Preparation silently accepted a failed first step.");
            }
            catch (InvalidOperationException t_error) when (ReferenceEquals(t_error, t_failure)) { }
            Require(!Read<bool>(t_bridge, "m_returnPreparing") && LoadingCoverView.OwnsLobbyPreparation,
                "Failure released the cover or leaked the bridge preparation flag.");

            Set(t_bridge, "m_step", t_step);
            Set(t_bridge, "m_applying", true);
            var t_retry = OutgameTutorialBridge.PrepareLobbyReturnAsync(t_lifetime.Token).Preserve();
            Require(Read<object>(t_bridge, "m_returnFailure") == null,
                "Same-scene retry reused the old preparation failure.");
            Set(t_bridge, "m_applying", false);
            await t_retry;
            Require(ReferenceEquals(Get(typeof(OutgameTutorialBridge), "s_instance"), t_bridge)
                && SceneManager.GetActiveScene().handle == t_scene.handle,
                "Preparation retry replaced its bridge or loaded another scene.");

            using (var t_cancel = new CancellationTokenSource())
            {
                Set(t_bridge, "m_applying", true);
                var t_cancelled = OutgameTutorialBridge.PrepareLobbyReturnAsync(t_cancel.Token).Preserve();
                t_cancel.Cancel();
                try
                {
                    await t_cancelled;
                    throw new InvalidOperationException("Cancelled preparation completed successfully.");
                }
                catch (OperationCanceledException) { }
                Require(!Read<bool>(t_bridge, "m_returnPreparing"), "Cancelled preparation retained bridge ownership.");
                Set(t_bridge, "m_applying", false);
            }
            LastResult = "PASS";
            Debug.Log("[LobbyReturnPreparationValidation] PASS: cover ownership, pending preparation, user/presentation independence, failure ownership, same-bridge retry, cancellation. Scene loading and real server calls are not exercised.");
        }
        catch (Exception t_error)
        {
            LastResult = "FAIL: " + t_error.GetBaseException().Message;
            throw;
        }
        finally
        {
            t_lifetime.Cancel();
            for (int t_i = t_objects.Count - 1; t_i >= 0; t_i--) UnityEngine.Object.DestroyImmediate(t_objects[t_i]);
            for (int t_i = t_restore.Count - 1; t_i >= 0; t_i--) t_restore[t_i]();
            Require(ReferenceEquals(DataSaveManager.Data, t_originalSave), "Validation did not restore the original save.");
        }
    }

    static void ValidateCoverOwnership(LoadingCoverView _cover)
    {
        Require(LoadingCoverView.IsCovering && !LoadingCoverView.OwnsLobbyPreparation,
            "An ordinary loading cover claimed battle-return preparation.");
        Set(_cover, "m_waitForBattleResult", true);
        Require(LoadingCoverView.OwnsLobbyPreparation, "Battle-return cover failed to claim preparation.");
        Set(_cover, "m_lobbyPreparationFailed", true);
        Require(LoadingCoverView.OwnsLobbyPreparation, "Preparation failure exposed an unready lobby.");
        Set(_cover, "m_lobbyReady", true);
        Require(LoadingCoverView.IsCovering && !LoadingCoverView.OwnsLobbyPreparation,
            "Ready-to-fade cover still prevented normal lobby initialization.");
        Set(_cover, "m_lobbyReady", false);
        Set(_cover, "m_lobbyPreparationFailed", false);
    }

    static T Inactive<T>(string _name, List<UnityEngine.Object> _objects) where T : Component
    {
        var t_object = new GameObject(_name) { hideFlags = HideFlags.HideAndDontSave };
        t_object.SetActive(false);
        _objects.Add(t_object);
        return t_object.AddComponent<T>();
    }

    static object Get(Type _type, string _field) => _type.GetField(_field, STATIC).GetValue(null);
    static T Read<T>(object _target, string _field) => (T)_target.GetType().GetField(_field, INSTANCE).GetValue(_target);
    static void Set(object _target, string _field, object _value) => _target.GetType().GetField(_field, INSTANCE).SetValue(_target, _value);
    static void Invoke(object _target, string _method, params object[] _args) => _target.GetType().GetMethod(_method, INSTANCE).Invoke(_target, _args);
    static void Replace(List<Action> _restore, Type _type, string _field, object _value)
    {
        var t_field = _type.GetField(_field, STATIC);
        object t_previous = t_field.GetValue(null);
        _restore.Add(() => t_field.SetValue(null, t_previous));
        t_field.SetValue(null, _value);
    }
    static void Require(bool _condition, string _message)
    {
        if (!_condition) throw new InvalidOperationException(_message);
    }
}
#endif
