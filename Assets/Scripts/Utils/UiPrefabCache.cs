using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>Addressables "UIPrefab" 라벨의 UI 프리팹 색인. 진행도·완료·실패의 단일 진실원이다.
/// static인 이유는 순서다 — 컴포넌트(DataLibrary)의 Awake에 로드가 붙어 있으면 시작 시점이
/// 실행 순서에 끌려다닌다. 시작은 초기화(InitializationRunner)가 명시적으로 건다(CardArtCache와 같은 모양).</summary>
public static class UiPrefabCache
{
    static readonly Dictionary<Type, GameObject> s_prefabs = new();
    // 반환값을 즉시 쓰지 않는 선택형 화면만 지연 적재한다. 상시 오버레이·입력 차단·전투 UI는 선로드한다.
    static readonly Dictionary<Type, string> s_deferredAddresses = new()
    {
        { typeof(PackOddsPopup), "PackOddsPopup" },
        { typeof(SettingsPanel), "SettingsPanel" },
        { typeof(LobbySettingPanel), "LobbySettingPanel" },
        { typeof(MissionPanel), "MissionPanel" },
        { typeof(PassPanel), "PassPanel" },
        { typeof(RankingBoardPanel), "RankingBoardPanel" },
        { typeof(RoulettePanel), "RouletteOverlay" }
    };
    static readonly Dictionary<Type, IResourceLocation> s_locations = new();
    static readonly Dictionary<Type, AsyncOperationHandle<GameObject>> s_deferredHandles = new();
    static readonly Dictionary<Type, UniTaskCompletionSource<GameObject>> s_requests = new();
    static AsyncOperationHandle<IList<IResourceLocation>> s_catalogHandle;
    static AsyncOperationHandle<SyncUiPrefabCatalog> s_syncCatalogHandle;
    static AsyncOperationHandle<TMPro.TMP_FontAsset> s_fontHandle;

    static AsyncOperationHandle<IList<GameObject>> s_handle;
    static bool s_started;
    static bool s_complete;
    static bool s_failed;

    // 옛 적재의 continuation·Register 콜백을 잘라내는 토큰(CardArtCache와 같은 모양).
    static int s_generation;

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
    static void ResetRuntimeState() => Reset();

    /// <summary>실패한 적재만 처음 상태로 되돌린다(초기화 재시도용).</summary>
    public static void ResetIfFailed()
    {
        if (!s_failed) return;

        Reset();
    }

    static void Reset()
    {
        s_generation++;
        var t_requests = new List<UniTaskCompletionSource<GameObject>>(s_requests.Values);
        s_requests.Clear();
        foreach (var t_handle in s_deferredHandles.Values)
            if (t_handle.IsValid()) Addressables.Release(t_handle);
        s_deferredHandles.Clear();
        s_locations.Clear();
        SyncUiPrefabs.SetSource(null);
        TutorialUIStyle.SetFont(null);
        if (s_syncCatalogHandle.IsValid()) Addressables.Release(s_syncCatalogHandle);
        s_syncCatalogHandle = default;
        if (s_fontHandle.IsValid()) Addressables.Release(s_fontHandle);
        s_fontHandle = default;
        if (s_catalogHandle.IsValid()) Addressables.Release(s_catalogHandle);
        s_catalogHandle = default;
        if (s_handle.IsValid()) Addressables.Release(s_handle);
        s_handle = default;
        s_prefabs.Clear();
        s_started = false;
        s_complete = false;
        s_failed = false;
        // 취소 continuation이 재진입하기 전에 이전 세대의 상태를 모두 비운다.
        foreach (var t_request in t_requests) t_request.TrySetCanceled();
    }

    /// <summary>라벨 로드 1회. 두 번째 호출은 아무 일도 하지 않는다(초기화 사본이 둘이라 멱등이어야 한다).</summary>
    public static async UniTask Preload()
    {
        if (s_started) return;
        s_started = true;

        int t_generation = s_generation;
        try
        {
            // 다운로드 이후에만 들어온다. 동기 소비자도 HTTP를 기다리지 않도록 먼저 적재한다.
            s_syncCatalogHandle = Addressables.LoadAssetAsync<SyncUiPrefabCatalog>("SyncUiPrefabCatalog");
            var t_syncCatalog = await s_syncCatalogHandle.ToUniTask();
            if (t_generation != s_generation) return;
            if (t_syncCatalog == null) throw new InvalidOperationException("Sync UI catalog is missing.");
            SyncUiPrefabs.SetSource(t_syncCatalog);

            s_fontHandle = Addressables.LoadAssetAsync<TMPro.TMP_FontAsset>("MalgunGothic_TMP");
            var t_font = await s_fontHandle.ToUniTask();
            if (t_generation != s_generation) return;
            if (t_font == null) throw new InvalidOperationException("Tutorial font is missing.");
            TutorialUIStyle.SetFont(t_font);

            s_catalogHandle = Addressables.LoadResourceLocationsAsync("UIPrefab", typeof(GameObject));
            var t_locations = await s_catalogHandle.ToUniTask();
            if (t_generation != s_generation) return;
            var t_startup = new List<IResourceLocation>();
            foreach (var t_location in t_locations)
            {
                Type t_deferredType = null;
                foreach (var t_entry in s_deferredAddresses)
                    if (t_entry.Value == t_location.PrimaryKey) { t_deferredType = t_entry.Key; break; }
                if (t_deferredType != null) s_locations[t_deferredType] = t_location;
                else t_startup.Add(t_location);
            }
            foreach (var t_entry in s_deferredAddresses)
                if (!s_locations.ContainsKey(t_entry.Key))
                    throw new InvalidOperationException($"Missing deferred UIPrefab address: {t_entry.Value}");

            AsyncOperationHandle<IList<GameObject>> t_handle =
                Addressables.LoadAssetsAsync<GameObject>(t_startup, _prefab => Register(t_generation, _prefab));
            s_handle = t_handle;

            await t_handle.ToUniTask();
            if (t_generation != s_generation) return;

            if (t_handle.Status != AsyncOperationStatus.Succeeded)
                throw new InvalidOperationException("UIPrefab Addressables load failed.");

            s_complete = true;
            LogUtil.Log("All Good");
        }
        catch (Exception t_exception)
        {
            if (t_generation != s_generation) return;

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

    /// <summary>선택형 화면의 첫 요청만 적재한다. 동시 요청은 한 작업을 공유하고 실패 후 다음 클릭은 재시도한다.</summary>
    public static UniTask<GameObject> LoadAsync<T>() where T : PooledUIBase
    {
        Type t_type = typeof(T);
        if (TryGet(t_type, out var t_prefab)) return UniTask.FromResult(t_prefab);
        if (s_requests.TryGetValue(t_type, out var t_request)) return t_request.Task;
        if (!s_complete || !s_locations.TryGetValue(t_type, out var t_location))
            return UniTask.FromException<GameObject>(new InvalidOperationException($"UIPrefab is not ready: {t_type.Name}"));

        int t_generation = s_generation;
        t_request = new UniTaskCompletionSource<GameObject>();
        s_requests.Add(t_type, t_request);
        var t_handle = Addressables.LoadAssetAsync<GameObject>(t_location);
        s_deferredHandles[t_type] = t_handle;
        t_handle.Completed += t_result =>
        {
            if (t_generation != s_generation) return;
            s_requests.Remove(t_type);
            if (t_result.Status == AsyncOperationStatus.Succeeded && t_result.Result.GetComponent<T>() != null)
            {
                Register(t_result.Result);
                t_request.TrySetResult(t_result.Result);
            }
            else
            {
                s_deferredHandles.Remove(t_type);
                var t_error = t_result.OperationException ?? new InvalidOperationException($"Invalid UIPrefab: {t_type.Name}");
                Addressables.Release(t_result);
                t_request.TrySetException(t_error);
            }
        };
        return t_request.Task;
    }

    static void Register(int _generation, GameObject _prefab)
    {
        if (_generation != s_generation) return;

        Register(_prefab);
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
            Debug.LogError($"[UiPrefabCache] Duplicate UIPrefab type: {t_type.Name} ({t_existing.name}, {_prefab.name})");
            return;
        }

        s_prefabs[t_type] = _prefab;
    }
}
