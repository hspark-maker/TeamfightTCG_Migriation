using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>주소 하나를 동기로 읽는 Addressables 창구. 인스펙터로 못 꽂는 자리 —
/// 코드가 세우는 UI·전역 카탈로그 — 가 Resources 대신 여기를 쓴다.
/// 핸들을 주소별로 붙잡아 두므로 두 번째 호출부터는 즉시 돌려준다.</summary>
public static class SyncAddressable
{
    static readonly Dictionary<string, AsyncOperationHandle> s_handles =
        new Dictionary<string, AsyncOperationHandle>();

    /// <summary>_address 에셋을 동기로 읽는다. 못 읽으면 null — 폴백은 호출부가 책임진다.</summary>
    public static T Load<T>(string _address) where T : UnityEngine.Object
    {
        if (s_handles.TryGetValue(_address, out AsyncOperationHandle t_cached) && t_cached.IsValid())
            return t_cached.Result as T;

        AsyncOperationHandle<T> t_handle = Addressables.LoadAssetAsync<T>(_address);
        T t_asset = t_handle.WaitForCompletion();

        if (t_handle.Status != AsyncOperationStatus.Succeeded || t_asset == null)
        {
            Debug.LogError($"[SyncAddressable] '{_address}' 에셋을 읽지 못했습니다.");
            if (t_handle.IsValid()) Addressables.Release(t_handle);
            return null;
        }

        s_handles[_address] = t_handle;
        return t_asset;
    }

    // 도메인 리로드를 끄고 Play를 눌러도 죽은 핸들을 물고 있지 않게(ContentProfileConfig.ResetActive와 같은 규약).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetHandles() => s_handles.Clear();
}
