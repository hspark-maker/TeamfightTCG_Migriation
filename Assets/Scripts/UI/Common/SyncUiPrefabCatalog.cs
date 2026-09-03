using UnityEngine;

public enum ESyncUiPrefab
{
    SceneCurtain,
    LockBadge,
    LoadingCover,
    MatchmakingRoot,
}

[CreateAssetMenu(fileName = "SyncUiPrefabCatalog", menuName = "UI/Sync UI Prefab Catalog")]
public sealed class SyncUiPrefabCatalog : ScriptableObject
{
    [SerializeField] GameObject sceneCurtain;
    [SerializeField] GameObject lockBadge;
    [SerializeField] GameObject loadingCover;
    [SerializeField] GameObject matchmakingRoot;

    public GameObject Get(ESyncUiPrefab _id)
        => _id switch
        {
            ESyncUiPrefab.SceneCurtain => sceneCurtain,
            ESyncUiPrefab.LockBadge => lockBadge,
            ESyncUiPrefab.LoadingCover => loadingCover,
            ESyncUiPrefab.MatchmakingRoot => matchmakingRoot,
            _ => null,
        };
}

public static class SyncUiPrefabs
{
    const string CatalogAddress = "SyncUiPrefabCatalog";
    static SyncUiPrefabCatalog s_catalog;

    public static void SetSource(SyncUiPrefabCatalog _catalog) => s_catalog = _catalog;

    public static GameObject Get(ESyncUiPrefab _id)
    {
        if (s_catalog == null)
            s_catalog = SyncAddressable.Load<SyncUiPrefabCatalog>(CatalogAddress);

        GameObject t_prefab = s_catalog != null ? s_catalog.Get(_id) : null;
        if (t_prefab == null)
            Debug.LogError($"[SyncUiPrefabs] {_id} 프리팹이 동기 UI 카탈로그에 연결되지 않았습니다.");
        return t_prefab;
    }
}
