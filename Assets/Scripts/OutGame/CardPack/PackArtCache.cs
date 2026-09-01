using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

public static class PackArtCache
{
    const string Label = "Packs";

    static readonly Dictionary<string, Sprite> s_loaded = new Dictionary<string, Sprite>(StringComparer.Ordinal);
    static readonly List<AsyncOperationHandle<Sprite>> s_handles = new List<AsyncOperationHandle<Sprite>>();
    static bool s_complete;
    static bool s_failed;
    static float s_progress;

    public static bool IsComplete => s_complete;
    public static bool HasFailed => s_failed;
    public static bool IsReady => s_complete && !s_failed;
    public static float LoadProgress => s_complete ? 1f : s_progress;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => ReleaseAll();

    public static Sprite Get(string _artKey)
    {
        if (string.IsNullOrEmpty(_artKey)) return null;
        s_loaded.TryGetValue(_artKey, out Sprite t_sprite);
        return t_sprite;
    }

    public static IEnumerator Preload()
    {
        if (s_complete) yield break;

        AsyncOperationHandle<IList<IResourceLocation>> t_catalog =
            Addressables.LoadResourceLocationsAsync(Label, typeof(Sprite));
        yield return t_catalog;

        if (t_catalog.Status != AsyncOperationStatus.Succeeded)
        {
            s_failed = true;
            s_complete = true;
            Debug.LogError("[PackArtCache] Packs 라벨 Addressables 카탈로그 조회 실패.");
            if (t_catalog.IsValid()) Addressables.Release(t_catalog);
            yield break;
        }

        IList<IResourceLocation> t_locations = t_catalog.Result;
        int t_count = t_locations != null ? t_locations.Count : 0;
        for (int t_i = 0; t_i < t_count; t_i++)
        {
            IResourceLocation t_location = t_locations[t_i];
            if (t_location == null || string.IsNullOrEmpty(t_location.PrimaryKey)) continue;

            AsyncOperationHandle<Sprite> t_handle = Addressables.LoadAssetAsync<Sprite>(t_location);
            s_handles.Add(t_handle);
            yield return t_handle;
            if (t_handle.Status == AsyncOperationStatus.Succeeded && t_handle.Result != null)
                s_loaded[t_location.PrimaryKey] = t_handle.Result;
            else
            {
                s_failed = true;
                Debug.LogError($"[PackArtCache] 팩 아트 로드 실패: {t_location.PrimaryKey}");
            }
            s_progress = t_count > 0 ? (float)(t_i + 1) / t_count : 1f;
        }

        IReadOnlyList<string> t_packIds = PackSpec.AllPackIds;
        for (int t_i = 0; t_i < t_packIds.Count; t_i++)
        {
            string t_packId = t_packIds[t_i];
            if (!PackSpec.TryGetPack(t_packId, out CardPack t_pack) || string.IsNullOrEmpty(t_pack.artKey)) continue;
            if (s_loaded.ContainsKey(t_pack.artKey)) continue;
            s_failed = true;
            Debug.LogError($"[PackArtCache] CardPack '{t_packId}'의 artKey '{t_pack.artKey}'를 찾지 못했습니다.");
        }

        if (t_catalog.IsValid()) Addressables.Release(t_catalog);
        s_complete = true;
    }

    public static void ResetIfFailed()
    {
        if (s_failed) ReleaseAll();
    }

    public static void ReleaseAll()
    {
        foreach (AsyncOperationHandle<Sprite> t_handle in s_handles)
            if (t_handle.IsValid()) Addressables.Release(t_handle);
        s_handles.Clear();
        s_loaded.Clear();
        s_complete = false;
        s_failed = false;
        s_progress = 0f;
    }
}
