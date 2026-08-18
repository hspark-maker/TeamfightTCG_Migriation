using System;
using UnityEngine;

/// <summary>Marker base used by DataLibrary to index non-pooled runtime overlays.</summary>
public abstract class SingletonOverlayBase : MonoBehaviour
{
}

/// <summary>
/// Shares the singleton lookup and open-state contract without changing how each overlay
/// creates, parents, shows, or closes its authored prefab.
/// </summary>
public abstract class SingletonOverlay<T> : SingletonOverlayBase
    where T : SingletonOverlay<T>
{
    static T s_instance;

    public static bool IsOpen { get; protected set; }
    public static event Action OnAnyClosed;

    protected static bool TryGetExisting(out T _overlay)
    {
        if (s_instance == null)
            s_instance = FindFirstObjectByType<T>(FindObjectsInactive.Include);

        _overlay = s_instance;
        return _overlay != null;
    }

    protected static bool TryGetOrCreate(Func<GameObject> _loadPrefab, out T _overlay)
    {
        if (TryGetExisting(out _overlay)) return true;

        GameObject t_prefab = _loadPrefab?.Invoke();
        if (t_prefab == null) return false;

        GameObject t_instance = Instantiate(t_prefab);
        s_instance = t_instance.GetComponent<T>();
        if (s_instance == null)
        {
            Debug.LogError(
                $"[SingletonOverlay] {t_prefab.name} 루트에 {typeof(T).Name}이 없습니다.",
                t_prefab);
            Destroy(t_instance);
        }

        _overlay = s_instance;
        return _overlay != null;
    }

    protected static void RaiseClosed() => OnAnyClosed?.Invoke();

    protected virtual void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
        IsOpen = false;
    }
}
