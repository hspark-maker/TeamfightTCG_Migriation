using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

// 게임 세션 동안 설정과 그 의존성을 유지한다. 실패한 요청만 해제해서 다시 받는다.
public static class RuntimeContentCache
{
    public const string Address = "RuntimeContentCatalog";
    static AsyncOperationHandle<RuntimeContentCatalog> s_handle;
    static int s_generation;
    public static RuntimeContentCatalog Config { get; private set; }
    public static bool HasFailed { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        s_generation++;
        Config = null;
        HasFailed = false;
        if (s_handle.IsValid()) Addressables.Release(s_handle);
        s_handle = default;
    }

    public static async UniTask PreloadAsync(CancellationToken _cancellationToken)
    {
        if (Config != null) return;
        if (HasFailed) Reset();
        int t_generation = s_generation;
        try
        {
            if (!s_handle.IsValid()) s_handle = Addressables.LoadAssetAsync<RuntimeContentCatalog>(Address);
            var t_handle = s_handle;
            while (!t_handle.IsDone)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, _cancellationToken);
                if (t_generation != s_generation) throw new OperationCanceledException();
            }
            _cancellationToken.ThrowIfCancellationRequested();
            if (t_generation != s_generation) throw new OperationCanceledException();
            if (t_handle.Status != AsyncOperationStatus.Succeeded || t_handle.Result == null)
                throw t_handle.OperationException ?? new InvalidOperationException("Runtime content load failed.");
            t_handle.Result.Validate();
            Config = t_handle.Result;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            if (t_generation == s_generation) HasFailed = true;
            throw;
        }
    }
}
