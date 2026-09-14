using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

// Cards 라벨의 번들을 디스크 캐시에 먼저 받는다. Sprite 적재는 CardArtCache가 이어받는다.
public static class RemoteCardArtDownload
{
    public static bool IsDownloading { get; private set; }
    public static bool IsComplete { get; private set; }
    public static bool HasFailed { get; private set; }
    public static long TotalBytes { get; private set; }
    public static long DownloadedBytes { get; private set; }
    public static float Progress => IsComplete ? 1f : TotalBytes > 0
        ? Mathf.Clamp01((float)DownloadedBytes / TotalBytes) : 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        IsDownloading = IsComplete = HasFailed = false;
        TotalBytes = DownloadedBytes = 0;
    }

    public static async UniTask PrepareAsync(CancellationToken _cancellationToken)
    {
        if (IsComplete) return;
        HasFailed = false;
        IsDownloading = true;
        TotalBytes = DownloadedBytes = 0;
        AsyncOperationHandle<List<string>> t_catalogCheck = default;
        AsyncOperationHandle t_catalogUpdate = default;
        AsyncOperationHandle<long> t_size = default;
        AsyncOperationHandle t_download = default;
        try
        {
            // 시작 UI의 동기 로딩이 네트워크 카탈로그를 기다리지 않도록 원격 확인은 이 비동기 단계에서 한다.
            t_catalogCheck = Addressables.CheckForCatalogUpdates(false);
            while (!t_catalogCheck.IsDone) await UniTask.Yield(PlayerLoopTiming.Update, _cancellationToken);
            if (t_catalogCheck.Status != AsyncOperationStatus.Succeeded)
                throw t_catalogCheck.OperationException ?? new InvalidOperationException("Remote catalog check failed.");
            if (t_catalogCheck.Result != null && t_catalogCheck.Result.Count > 0)
            {
                t_catalogUpdate = Addressables.UpdateCatalogs(t_catalogCheck.Result, false);
                while (!t_catalogUpdate.IsDone) await UniTask.Yield(PlayerLoopTiming.Update, _cancellationToken);
                if (t_catalogUpdate.Status != AsyncOperationStatus.Succeeded)
                    throw t_catalogUpdate.OperationException ?? new InvalidOperationException("Remote catalog update failed.");
            }

            t_size = Addressables.GetDownloadSizeAsync("Cards");
            while (!t_size.IsDone) await UniTask.Yield(PlayerLoopTiming.Update, _cancellationToken);
            if (t_size.Status != AsyncOperationStatus.Succeeded)
                throw t_size.OperationException ?? new InvalidOperationException("Card art download size lookup failed.");
            TotalBytes = t_size.Result;
            if (TotalBytes > 0)
            {
                t_download = Addressables.DownloadDependenciesAsync("Cards", false);
                while (!t_download.IsDone)
                {
                    DownloadStatus t_status = t_download.GetDownloadStatus();
                    DownloadedBytes = t_status.DownloadedBytes;
                    await UniTask.Yield(PlayerLoopTiming.Update, _cancellationToken);
                }
                if (t_download.Status != AsyncOperationStatus.Succeeded)
                    throw t_download.OperationException ?? new InvalidOperationException("Card art download failed.");
            }
            DownloadedBytes = TotalBytes;
            IsComplete = true;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            HasFailed = true;
            throw;
        }
        finally
        {
            if (t_catalogCheck.IsValid()) Addressables.Release(t_catalogCheck);
            if (t_catalogUpdate.IsValid()) Addressables.Release(t_catalogUpdate);
            if (t_size.IsValid()) Addressables.Release(t_size);
            if (t_download.IsValid()) Addressables.Release(t_download);
            IsDownloading = false;
        }
    }
}
