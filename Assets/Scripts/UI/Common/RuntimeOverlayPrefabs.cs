using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Resolves authored, independently-instantiated overlays by component type.
/// Normal initialization uses DataLibrary's UIPrefab label index; standalone scene Play falls back
/// to the same Addressables entry synchronously so editor workflows keep working.
/// </summary>
public static class RuntimeOverlayPrefabs
{
    static readonly Dictionary<Type, AsyncOperationHandle<GameObject>> s_fallbackHandles =
        new Dictionary<Type, AsyncOperationHandle<GameObject>>();

    public static GameObject Get<T>() where T : SingletonOverlayBase
    {
        Type t_type = typeof(T);
        if (DataLibrary.instance != null &&
            DataLibrary.instance.TryGetUiPrefab(t_type, out GameObject t_indexed))
            return t_indexed;

        if (s_fallbackHandles.TryGetValue(t_type, out var t_cached) && t_cached.IsValid())
            return t_cached.Result;

#if !UNITY_EDITOR
        Debug.LogError(
            $"[RuntimeOverlayPrefabs] {t_type.Name} prefab is unavailable. " +
            "Player builds must initialize Initialize/DataLibrary before requesting overlays.");
        return null;
#else
        AsyncOperationHandle<GameObject> t_handle =
            Addressables.LoadAssetAsync<GameObject>(t_type.Name);
        GameObject t_prefab = t_handle.WaitForCompletion();
        if (t_handle.Status != AsyncOperationStatus.Succeeded ||
            t_prefab == null || t_prefab.GetComponent<T>() == null)
        {
            Debug.LogError(
                $"[RuntimeOverlayPrefabs] Addressables에서 {t_type.Name} 프리팹을 찾지 못했습니다.");
            if (t_handle.IsValid()) Addressables.Release(t_handle);
            return null;
        }

        s_fallbackHandles[t_type] = t_handle;
        return t_prefab;
#endif
    }
}
