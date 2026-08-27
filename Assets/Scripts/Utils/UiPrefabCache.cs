using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>Addressables "UIPrefab" 라벨의 UI 프리팹 색인. 진행도·완료·실패의 단일 진실원이다.
/// static인 이유는 순서다 — 컴포넌트(DataLibrary)의 Awake에 로드가 붙어 있으면 시작 시점이
/// 실행 순서에 끌려다닌다. 시작은 부트 초기화(InitializationRunner)가 명시적으로 건다(CardArtCache와 같은 모양).</summary>
public static class UiPrefabCache
{
    static readonly Dictionary<Type, GameObject> s_prefabs = new();

    static AsyncOperationHandle<IList<GameObject>> s_handle;
    static bool s_started;
    static bool s_complete;
    static bool s_failed;

    public static bool IsComplete => s_complete;
    public static bool HasFailed => s_failed;

    /// <summary>0~1. 시작 전이면 0, 완료면 1.</summary>
    public static float LoadProgress
    {
        get
        {
            if (s_complete) return 1f;
            return s_handle.IsValid() ? s_handle.PercentComplete : 0f;
        }
    }

    // 도메인 리로드를 끈 세션에서 이전 Play의 색인·핸들이 남지 않게 되돌린다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        if (s_handle.IsValid()) Addressables.Release(s_handle);
        s_handle = default;
        s_prefabs.Clear();
        s_started = false;
        s_complete = false;
        s_failed = false;
    }

    /// <summary>라벨 로드 1회. 두 번째 호출은 아무 일도 하지 않는다(초기화 사본이 둘이라 멱등이어야 한다).</summary>
    public static async UniTask Preload()
    {
        if (s_started) return;
        s_started = true;

        try
        {
            s_handle = Addressables.LoadAssetsAsync<GameObject>("UIPrefab", Register);
            await s_handle.ToUniTask();
            if (s_handle.Status != AsyncOperationStatus.Succeeded)
                throw new InvalidOperationException("UIPrefab Addressables load failed.");

            s_complete = true;
            LogUtil.Log("All Good");
        }
        catch (Exception t_exception)
        {
            s_failed = true;
            Debug.LogException(t_exception);
        }
    }

    public static GameObject Get<T>() where T : PooledUIBase
    {
        if (s_prefabs.TryGetValue(typeof(T), out GameObject t_prefab)) return t_prefab;

        LogUtil.Log($"UI Prefab Not Found: {typeof(T).Name}");
        return null;
    }

    public static bool TryGet(Type _type, out GameObject _prefab)
    {
        _prefab = null;
        return _type != null && s_prefabs.TryGetValue(_type, out _prefab) && _prefab != null;
    }

    // 라벨에 걸린 프리팹 중 풀 UI·상시 오버레이만 타입 키로 색인한다.
    static void Register(GameObject _prefab)
    {
        Component t_ui = _prefab.GetComponent<PooledUIBase>();
        if (t_ui == null) t_ui = _prefab.GetComponent<SingletonOverlayBase>();
        if (t_ui == null) return;

        Type t_type = t_ui.GetType();
        if (s_prefabs.TryGetValue(t_type, out GameObject t_existing) && t_existing != _prefab)
        {
            Debug.LogError($"[UiPrefabCache] UIPrefab 타입 중복: {t_type.Name} ({t_existing.name}, {_prefab.name})");
            return;
        }

        s_prefabs[t_type] = _prefab;
    }
}
