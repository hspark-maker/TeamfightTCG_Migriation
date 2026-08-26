using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

/// <summary>AttackEffect 라벨의 Addressables를 부팅 중 선로드하고 전투에는 동기 조회를 제공한다.</summary>
public static class AttackEffectCache
{
    public const string Label = "AttackEffect";
    const string AssetPrefix = "Data_AttackEffect_";
    const string AddressPrefix = "Effect_Attack_";

    static readonly HashSet<string> s_addresses = new HashSet<string>(StringComparer.Ordinal);
    static readonly Dictionary<string, AttackEffect> s_loaded = new Dictionary<string, AttackEffect>(StringComparer.Ordinal);
    static readonly Dictionary<string, AsyncOperationHandle<AttackEffect>> s_handles = new Dictionary<string, AsyncOperationHandle<AttackEffect>>(StringComparer.Ordinal);
    static AsyncOperationHandle<IList<IResourceLocation>> s_catalogHandle;
    static bool s_started;
    static int s_generation;

    public static bool IsComplete { get; private set; }
    public static bool HasFailed { get; private set; }
    public static float LoadProgress { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => ReleaseAll();

    public static string AddressOf(AttackEffect _effect)
    {
        if (_effect == null) return string.Empty;
        string t_name = _effect.name;
        if (t_name.StartsWith(AssetPrefix, StringComparison.Ordinal))
            t_name = t_name.Substring(AssetPrefix.Length);
        return AddressPrefix + t_name;
    }

    public static AttackEffect Get(string _address)
    {
        if (string.IsNullOrWhiteSpace(_address)) return null;
        if (s_loaded.TryGetValue(_address, out AttackEffect t_effect)) return t_effect;
        Debug.LogError($"[AttackEffectCache] 선로드되지 않았거나 로드에 실패한 공격 이펙트: {_address}");
        return null;
    }

    public static IEnumerator Preload(IEnumerable<string> _addresses)
    {
        if (IsComplete) yield break;
        if (s_started)
        {
            while (!IsComplete) yield return null;
            yield break;
        }

        s_started = true;
        int t_generation = s_generation;
        s_catalogHandle = Addressables.LoadResourceLocationsAsync(Label, typeof(AttackEffect));
        yield return s_catalogHandle;
        if (t_generation != s_generation) yield break;

        if (s_catalogHandle.Status != AsyncOperationStatus.Succeeded)
        {
            HasFailed = true;
            IsComplete = true;
            Debug.LogError("[AttackEffectCache] AttackEffect 라벨 카탈로그 조회 실패.");
            yield break;
        }

        foreach (IResourceLocation t_location in s_catalogHandle.Result)
            if (t_location != null && !string.IsNullOrWhiteSpace(t_location.PrimaryKey))
                s_addresses.Add(t_location.PrimaryKey);
        Addressables.Release(s_catalogHandle);
        s_catalogHandle = default;

        var t_wanted = new HashSet<string>(StringComparer.Ordinal);
        if (_addresses != null)
            foreach (string t_address in _addresses)
                if (!string.IsNullOrWhiteSpace(t_address)) t_wanted.Add(t_address);

        int t_finished = 0;
        foreach (string t_address in t_wanted)
        {
            if (!s_addresses.Contains(t_address))
            {
                HasFailed = true;
                t_finished++;
                Debug.LogError($"[AttackEffectCache] 카탈로그에 공격 이펙트 주소가 없음: {t_address}");
                continue;
            }

            AsyncOperationHandle<AttackEffect> t_handle = Addressables.LoadAssetAsync<AttackEffect>(t_address);
            s_handles.Add(t_address, t_handle);
            yield return t_handle;
            t_finished++;
            LoadProgress = t_wanted.Count == 0 ? 1f : (float)t_finished / t_wanted.Count;
            if (t_handle.Status == AsyncOperationStatus.Succeeded && t_handle.Result != null)
                s_loaded.Add(t_address, t_handle.Result);
            else
            {
                HasFailed = true;
                Debug.LogError($"[AttackEffectCache] 공격 이펙트 로드 실패: {t_address}");
            }
        }

        LoadProgress = 1f;
        IsComplete = true;
    }

    public static void ReleaseAll()
    {
        s_generation++;
        if (s_catalogHandle.IsValid()) Addressables.Release(s_catalogHandle);
        foreach (AsyncOperationHandle<AttackEffect> t_handle in s_handles.Values)
            if (t_handle.IsValid()) Addressables.Release(t_handle);
        s_catalogHandle = default;
        s_addresses.Clear();
        s_loaded.Clear();
        s_handles.Clear();
        s_started = false;
        IsComplete = false;
        HasFailed = false;
        LoadProgress = 0f;
    }
}
