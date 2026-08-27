using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>Cards 라벨의 Addressables 카탈로그를 조회하고 카드 아트를 앱 수명 동안 캐시한다.</summary>
public static class CardArtCache
{
    const string CardsLabel = "Cards";
    const string CardAssetPrefix = "Data_Card_";

    static readonly HashSet<string> s_addresses = new HashSet<string>(StringComparer.Ordinal);
    static readonly Dictionary<string, Sprite> s_loaded = new Dictionary<string, Sprite>(StringComparer.Ordinal);
    static readonly Dictionary<string, AsyncOperationHandle<Sprite>> s_handles =
        new Dictionary<string, AsyncOperationHandle<Sprite>>(StringComparer.Ordinal);
    static readonly HashSet<string> s_pending = new HashSet<string>(StringComparer.Ordinal);
    static readonly HashSet<string> s_reportedMisses = new HashSet<string>(StringComparer.Ordinal);

    static AsyncOperationHandle<IList<IResourceLocation>> s_catalogHandle;
    static bool s_catalogRequested;
    static bool s_catalogReady;
    static bool s_catalogFailed;
    static bool s_preloadStarted;
    static bool s_preloadComplete;
    static bool s_loadFailed;
    static bool s_reportedCatalogNotReady;
    static int s_wantedCount;
    static int s_finishedCount;
    static int s_generation;

    public static event Action OnArtLoaded;

    public static bool IsCatalogReady => s_catalogReady;
    public static bool IsComplete => s_preloadComplete;
    public static bool HasFailed => s_catalogFailed || s_loadFailed;
    public static bool IsReady => s_preloadComplete && !HasFailed;
    public static bool IsBusy => (s_catalogRequested && !s_catalogReady && !s_catalogFailed) || s_pending.Count > 0;
    public static int LoadedCount => s_loaded.Count;

    public static float LoadProgress
    {
        get
        {
            if (s_preloadComplete) return 1f;
            if (!s_catalogReady) return s_catalogRequested && s_catalogHandle.IsValid()
                ? Mathf.Clamp01(s_catalogHandle.PercentComplete) * 0.1f
                : 0f;
            if (!s_preloadStarted || s_wantedCount == 0) return 0.1f;
            return 0.1f + 0.9f * Mathf.Clamp01((float)s_finishedCount / s_wantedCount);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => ReleaseAll();

    /// <summary>런타임 stage 0이 주소 Stage1에 대응한다.</summary>
    public static string AddressOf(CardSpec _spec, int _stage)
    {
        if (_spec == null) throw new ArgumentNullException(nameof(_spec));
        string t_name = _spec.AssetName;
        if (t_name.StartsWith(CardAssetPrefix, StringComparison.Ordinal))
            t_name = t_name.Substring(CardAssetPrefix.Length);
        return $"Image_Card_{t_name}_Stage{_stage + 1}";
    }

    /// <summary>Cards 라벨 위치를 한 번 조회해 PrimaryKey 집합을 만든다. 실제 Sprite는 로드하지 않는다.</summary>
    public static IEnumerator EnsureCatalog()
    {
        if (s_catalogReady || s_catalogFailed) yield break;

        int t_generation = s_generation;

        if (!s_catalogRequested)
        {
            s_catalogRequested = true;
            s_catalogHandle = Addressables.LoadResourceLocationsAsync(CardsLabel, typeof(Sprite));
        }

        AsyncOperationHandle<IList<IResourceLocation>> t_handle = s_catalogHandle;

        while (t_generation == s_generation && t_handle.IsValid() && !t_handle.IsDone) yield return null;
        if (t_generation != s_generation) yield break;
        if (s_catalogReady || s_catalogFailed) yield break;

        if (!t_handle.IsValid() || t_handle.Status != AsyncOperationStatus.Succeeded)
        {
            s_catalogFailed = true;
            Debug.LogError("[CardArtCache] Cards 라벨 Addressables 카탈로그 조회 실패.");
        }
        else
        {
            foreach (IResourceLocation t_location in t_handle.Result)
                if (t_location != null && !string.IsNullOrEmpty(t_location.PrimaryKey))
                    s_addresses.Add(t_location.PrimaryKey);
            s_catalogReady = true;
        }

        if (t_handle.IsValid()) Addressables.Release(t_handle);
        s_catalogHandle = default;
    }

    /// <summary>카탈로그에 주소가 실제로 등록되어 있는지 판정한다. 프리로드 여부와 무관하다.</summary>
    public static bool Exists(string _address)
    {
        if (!s_catalogReady)
        {
            if (!s_reportedCatalogNotReady)
            {
                s_reportedCatalogNotReady = true;
                Debug.LogWarning("[CardArtCache] 카탈로그가 준비되지 않아 카드 아트를 표시하지 않습니다.");
            }
            return false;
        }
        return !string.IsNullOrEmpty(_address) && s_addresses.Contains(_address);
    }

    public static Sprite Get(string _address)
    {
        if (string.IsNullOrEmpty(_address)) return null;
        if (s_loaded.TryGetValue(_address, out Sprite t_sprite)) return t_sprite;
        if (s_reportedMisses.Add(_address))
            Debug.LogError($"[CardArtCache] 프리로드되지 않았거나 로드에 실패한 카드 아트: {_address}");
        return null;
    }

    public static IEnumerator Preload(IEnumerable<CardSpec> _specs)
    {
        if (s_preloadComplete) yield break;

        int t_generation = s_generation;
        if (s_preloadStarted)
        {
            while (t_generation == s_generation && !s_preloadComplete) yield return null;
            yield break;
        }

        s_preloadStarted = true;
        yield return EnsureCatalog();
        if (t_generation != s_generation) yield break;
        if (!s_catalogReady)
        {
            s_preloadComplete = true;
            yield break;
        }

        var t_wanted = new HashSet<string>(StringComparer.Ordinal);
        if (_specs != null)
        {
            foreach (CardSpec t_spec in _specs)
            {
                if (t_spec == null) continue;
                string t_baseAddress = AddressOf(t_spec, 0);
                if (!s_addresses.Contains(t_baseAddress))
                {
                    s_loadFailed = true;
                    Debug.LogError($"[CardArtCache] 기본 카드 아트 주소 없음: {t_baseAddress}");
                }

                for (int t_stage = 0; t_stage <= CardSpec.MaxEvolutionStage; t_stage++)
                {
                    string t_address = AddressOf(t_spec, t_stage);
                    if (s_addresses.Contains(t_address)) t_wanted.Add(t_address);
                }
            }
        }

        s_wantedCount = t_wanted.Count;
        s_finishedCount = 0;

        foreach (string t_address in t_wanted)
        {
            if (s_loaded.ContainsKey(t_address))
            {
                s_finishedCount++;
                continue;
            }
            if (!s_pending.Add(t_address)) continue;
            LoadOne(t_address, t_generation);
        }

        while (s_pending.Count > 0 && t_generation == s_generation) yield return null;
        if (t_generation != s_generation) yield break;

        s_preloadComplete = true;
        OnArtLoaded?.Invoke();
    }

    static void LoadOne(string _address, int _generation)
    {
        AsyncOperationHandle<Sprite> t_handle = Addressables.LoadAssetAsync<Sprite>(_address);
        s_handles[_address] = t_handle;
        t_handle.Completed += _operation =>
        {
            if (_generation != s_generation) return;
            s_pending.Remove(_address);
            s_finishedCount++;
            if (_operation.Status == AsyncOperationStatus.Succeeded && _operation.Result != null)
                s_loaded[_address] = _operation.Result;
            else
            {
                s_loadFailed = true;
                Debug.LogError($"[CardArtCache] 카드 아트 로드 실패: {_address}");
            }
        };
    }

    /// <summary>실패한 적재만 처음 상태로 되돌린다(부트 재시도용).</summary>
    public static void ResetIfFailed()
    {
        if (!HasFailed) return;

        ReleaseAll();
    }

    public static void ReleaseAll()
    {
        s_generation++;
        if (s_catalogHandle.IsValid()) Addressables.Release(s_catalogHandle);
        foreach (KeyValuePair<string, AsyncOperationHandle<Sprite>> t_pair in s_handles)
            if (t_pair.Value.IsValid()) Addressables.Release(t_pair.Value);

        s_catalogHandle = default;
        s_addresses.Clear();
        s_loaded.Clear();
        s_handles.Clear();
        s_pending.Clear();
        s_reportedMisses.Clear();
        s_catalogRequested = false;
        s_catalogReady = false;
        s_catalogFailed = false;
        s_preloadStarted = false;
        s_preloadComplete = false;
        s_loadFailed = false;
        s_reportedCatalogNotReady = false;
        s_wantedCount = 0;
        s_finishedCount = 0;
        OnArtLoaded = null;
    }
}
