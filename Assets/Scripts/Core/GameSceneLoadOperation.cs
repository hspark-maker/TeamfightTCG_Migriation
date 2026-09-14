using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

/// <summary>로컬 시작 씬과 원격 게임 씬의 준비·활성화 수명을 한곳에서 관리한다.</summary>
public sealed class GameSceneLoadOperation
{
    readonly bool m_remote;
    AsyncOperation m_local;
    AsyncOperationHandle<SceneInstance> m_handle;
    readonly UniTaskCompletionSource<bool> m_completion = new();
    Exception m_error;
    bool m_committing;
    bool m_finished;

    public string SceneName { get; }
    public bool Succeeded { get; private set; }
    public Exception Error => m_error ?? (m_handle.IsValid() && m_handle.IsDone
        && m_handle.Status == AsyncOperationStatus.Failed ? m_handle.OperationException : null);
    public bool IsReady => m_error != null || (m_remote
        ? m_handle.IsValid() && m_handle.IsDone
        : m_local != null && (m_local.isDone || m_local.progress >= 0.9f));
    public float Progress => IsReady ? 1f : m_remote
        ? m_handle.IsValid() ? m_handle.PercentComplete : 0f
        : m_local != null ? Mathf.Clamp01(m_local.progress / 0.9f) : 0f;

    public static bool IsRemote(string _scene)
        => _scene == "LobbyScene" || _scene == "BattleScene" || _scene == "MultiplayerTestScene";

    public static bool CanLoad(string _scene)
        => !string.IsNullOrEmpty(_scene) && (IsRemote(_scene) || Application.CanStreamedLevelBeLoaded(_scene));

    public GameSceneLoadOperation(string _scene)
    {
        SceneName = _scene;
        m_remote = IsRemote(_scene);
        try
        {
            if (!CanLoad(_scene)) throw new InvalidOperationException($"Scene '{_scene}' is not registered.");
            if (m_remote)
                m_handle = Addressables.LoadSceneAsync(_scene, LoadSceneMode.Single, activateOnLoad: false);
            else
            {
                m_local = SceneManager.LoadSceneAsync(_scene, LoadSceneMode.Single);
                if (m_local == null) throw new InvalidOperationException($"Scene '{_scene}' could not be loaded.");
                m_local.allowSceneActivation = false;
            }
        }
        catch (Exception t_exception) { m_error = t_exception; }
    }

    public IEnumerator Commit(Action _beforeLoad = null)
    {
        BeginCommit(_beforeLoad);
        while (!m_finished) yield return null;
    }

    public UniTask<bool> CommitAsync(Action _beforeLoad = null)
    {
        BeginCommit(_beforeLoad);
        return m_completion.Task;
    }

    /// <summary>씬 로드는 취소할 수 없다. 연출 소유자가 사라져도 활성화 잠금은 반드시 푼다.</summary>
    public void FinishWithoutCover(Action _beforeLoad = null) => BeginCommit(_beforeLoad);

    void BeginCommit(Action _beforeLoad)
    {
        if (m_committing) return;
        m_committing = true;
        CompleteAsync(_beforeLoad).Forget();
    }

    async UniTaskVoid CompleteAsync(Action _beforeLoad)
    {
        try
        {
            while (!IsReady) await UniTask.Yield();
            if (Error != null) throw Error;

            // 정리 훅이 예외를 던져도 Unity의 씬 활성화 큐를 막은 채 남기지 않는다.
            AsyncOperation t_activation = null;
            try { _beforeLoad?.Invoke(); }
            catch (Exception t_exception) { Debug.LogException(t_exception); }
            finally
            {
                if (m_remote) t_activation = m_handle.Result.ActivateAsync();
                else
                {
                    m_local.allowSceneActivation = true;
                    t_activation = m_local;
                }
            }
            while (t_activation != null && !t_activation.isDone) await UniTask.Yield();
            Succeeded = true;
        }
        catch (Exception t_exception)
        {
            m_error = t_exception;
            Debug.LogError($"[GameSceneLoadOperation] Cannot load '{SceneName}': {t_exception}");
        }
        finally
        {
            // 성공한 씬은 Single 교체/sceneUnloaded 때 Addressables가 해제한다.
            // 활성 씬의 핸들을 여기서 Release하면 씬이 사용하는 번들까지 내려간다.
            if (m_handle.IsValid() && m_handle.IsDone && m_handle.Status == AsyncOperationStatus.Failed)
                Addressables.Release(m_handle);
            m_finished = true;
            m_completion.TrySetResult(Succeeded);
        }
    }
}
