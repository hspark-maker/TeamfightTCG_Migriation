using System;
using UnityEngine;

/// <summary>
/// Resolves authored, independently-instantiated overlays by component type.
/// Normal initialization uses DataLibrary's UIPrefab label index; standalone scene Play falls back
/// to the authored asset through AssetDatabase so remote bundles never block the editor thread.
/// </summary>
public static class RuntimeOverlayPrefabs
{
    public static GameObject Get<T>() where T : SingletonOverlayBase
    {
        Type t_type = typeof(T);
        if (DataLibrary.instance != null &&
            DataLibrary.instance.TryGetUiPrefab(t_type, out GameObject t_indexed))
            return t_indexed;

#if !UNITY_EDITOR
        Debug.LogError(
            $"[RuntimeOverlayPrefabs] {t_type.Name} prefab is unavailable. " +
            "Player builds must initialize Initialize/DataLibrary before requesting overlays.");
        return null;
#else
        GameObject t_prefab = SyncAddressable.Load<GameObject>(t_type.Name);
        if (t_prefab == null || t_prefab.GetComponent<T>() == null)
        {
            Debug.LogError(
                $"[RuntimeOverlayPrefabs] Could not find the {t_type.Name} prefab in Addressables.");
            return null;
        }

        return t_prefab;
#endif
    }
}
