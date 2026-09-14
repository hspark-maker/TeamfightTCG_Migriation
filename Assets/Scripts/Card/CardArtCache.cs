using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>Cards 주소를 색인하고, 화면에서 쓰는 아트와 최근 사용한 24장만 유지한다.</summary>
public static class CardArtCache
{
    const string CardsLabel = "Cards";
    const string CardAssetPrefix = "Data_Card_";

    static readonly HashSet<string> s_addresses = new HashSet<string>(StringComparer.Ordinal);
    public const int IdleCapacity = 24;
    static readonly Dictionary<string, Entry> s_entries = new(StringComparer.Ordinal);
    static long s_access;

    internal sealed class Entry
    {
        public string Address;
        public AsyncOperationHandle<Sprite> Handle;
        public Sprite Sprite;
        public int References;
        public long LastUse;
        public bool Complete;
        public event Action<Sprite> Changed;
        public void Subscribe(Action<Sprite> callback) => Changed += callback;
        public void Unsubscribe(Action<Sprite> callback) => Changed -= callback;
        public void Notify()
        {
            if (Changed == null) return;
            foreach (Action<Sprite> callback in Changed.GetInvocationList())
                try { callback(Sprite); }
                catch (Exception exception) { Debug.LogException(exception); }
        }
    }

    /// <summary>표시 중 참조를 유지하고 숨김/재바인딩 시 반환한다.</summary>
    public sealed class Lease : IDisposable
    {
        Entry m_entry;
        readonly Action<Sprite> m_changed;
        readonly int m_generation;
        internal Lease(Entry entry, Action<Sprite> changed)
        {
            m_entry = entry;
            m_changed = changed;
            m_generation = s_generation;
            entry.References++;
            entry.Subscribe(OnChanged);
        }
        public Sprite Sprite => m_entry?.Sprite;
        void OnChanged(Sprite sprite)
        {
            if (m_entry != null && m_generation == s_generation) m_changed?.Invoke(sprite);
        }
        public void Dispose()
        {
            if (m_entry == null) return;
            m_entry.Unsubscribe(OnChanged);
            m_entry.References--;
            m_entry.LastUse = ++s_access;
            m_entry = null;
            if (m_generation == s_generation) TrimIdle();
        }
    }

    static AsyncOperationHandle<IList<IResourceLocation>> s_catalogHandle;
    static bool s_catalogRequested;
    static bool s_catalogReady;
    static bool s_catalogFailed;
    static bool s_preloadStarted;
    static bool s_preloadComplete;
    static bool s_loadFailed;
    static bool s_reportedCatalogNotReady;
    static int s_generation;

    public static event Action OnArtLoaded;

    public static bool IsCatalogReady => s_catalogReady;
    public static bool IsComplete => s_preloadComplete;
    public static bool HasFailed => s_catalogFailed || s_loadFailed;
    public static bool IsReady => s_preloadComplete && !HasFailed;
    public static bool IsBusy => (s_catalogRequested && !s_catalogReady && !s_catalogFailed) || PendingCount > 0;
    public static int LoadedCount
    {
        get { int count = 0; foreach (var entry in s_entries.Values) if (entry.Sprite != null) count++; return count; }
    }
    public static int PendingCount
    {
        get { int count = 0; foreach (var entry in s_entries.Values) if (!entry.Complete) count++; return count; }
    }

    public static float LoadProgress
    {
        get
        {
            if (s_preloadComplete) return 1f;
            if (!s_catalogReady) return s_catalogRequested && s_catalogHandle.IsValid()
                ? Mathf.Clamp01(s_catalogHandle.PercentComplete) * 0.1f
                : 0f;
            return 0.9f;
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

    /// <summary>미보유 카드는 기본 단계의 전용 실루엣을 표시한다.</summary>
    public static string SilhouetteAddressOf(CardSpec _spec) => AddressOf(_spec, 0) + "_Silhouette";

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
            Debug.LogError("[CardArtCache] Failed to query the Addressables catalog for the Cards label.");
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
                Debug.LogWarning("[CardArtCache] The catalog is not ready, so card art is not displayed.");
            }
            return false;
        }
        return !string.IsNullOrEmpty(_address) && s_addresses.Contains(_address);
    }

    public static Lease Acquire(string _address, Action<Sprite> _changed)
    {
        if (!Exists(_address)) return null;
        bool t_new = !s_entries.TryGetValue(_address, out Entry t_entry);
        if (t_new)
        {
            t_entry = new Entry { Address = _address };
            s_entries.Add(_address, t_entry);
        }
        t_entry.LastUse = ++s_access;
        var t_lease = new Lease(t_entry, _changed);
        if (t_new)
        {
            int t_generation = s_generation;
            t_entry.Handle = Addressables.LoadAssetAsync<Sprite>(_address);
            t_entry.Handle.Completed += operation =>
            {
                if (t_generation != s_generation) return;
                t_entry.Complete = true;
                if (operation.Status == AsyncOperationStatus.Succeeded)
                    t_entry.Sprite = operation.Result;
                else
                    Debug.LogError($"[CardArtCache] Card art load failed: {_address}");
                t_entry.Notify();
                TrimIdle();
            };
        }
        return t_lease;
    }

    static void TrimIdle()
    {
        while (true)
        {
            int t_idle = 0;
            Entry t_oldest = null;
            foreach (Entry t_entry in s_entries.Values)
            {
                if (t_entry.References != 0 || !t_entry.Complete) continue;
                t_idle++;
                if (t_oldest == null || (t_entry.Sprite == null && t_oldest.Sprite != null) ||
                    ((t_entry.Sprite == null) == (t_oldest.Sprite == null) && t_entry.LastUse < t_oldest.LastUse))
                    t_oldest = t_entry;
            }
            if (t_oldest == null || (t_idle <= IdleCapacity && t_oldest.Sprite != null)) return;
            s_entries.Remove(t_oldest.Address);
            if (t_oldest.Handle.IsValid()) Addressables.Release(t_oldest.Handle);
        }
    }

    // 초기화는 주소 배선만 검증한다. Sprite/Texture는 화면에서 Acquire할 때 읽는다.
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

        if (_specs != null)
            foreach (CardSpec t_spec in _specs)
            {
                if (t_spec == null) continue;
                string t_address = AddressOf(t_spec, 0);
                if (s_addresses.Contains(t_address)) continue;
                s_loadFailed = true;
                Debug.LogError($"[CardArtCache] Missing default card art address: {t_address}");
            }

        s_preloadComplete = true;
        OnArtLoaded?.Invoke();
    }

    /// <summary>실패한 적재만 처음 상태로 되돌린다(초기화 재시도용).</summary>
    public static void ResetIfFailed()
    {
        if (!HasFailed) return;

        ReleaseAll();
    }

    public static void ReleaseAll()
    {
        s_generation++;
        if (s_catalogHandle.IsValid()) Addressables.Release(s_catalogHandle);
        foreach (Entry t_entry in s_entries.Values)
            if (t_entry.Handle.IsValid()) Addressables.Release(t_entry.Handle);

        s_catalogHandle = default;
        s_addresses.Clear();
        s_entries.Clear();
        s_access = 0;
        s_catalogRequested = false;
        s_catalogReady = false;
        s_catalogFailed = false;
        s_preloadStarted = false;
        s_preloadComplete = false;
        s_loadFailed = false;
        s_reportedCatalogNotReady = false;
        OnArtLoaded = null;
    }
}
