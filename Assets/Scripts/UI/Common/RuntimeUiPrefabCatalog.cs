using UnityEngine;

public enum ERuntimeUiPrefab
{
    CardRewardOverlay,
    CardSetRewardOverlay,
    UnlockIntroOverlay,
    SceneCurtain,
    LockBadge,
    LoadingCover,
}

[CreateAssetMenu(fileName = "RuntimeUiPrefabCatalog", menuName = "UI/Runtime UI Prefab Catalog")]
public sealed class RuntimeUiPrefabCatalog : ScriptableObject
{
    [SerializeField] GameObject cardRewardOverlay;
    [SerializeField] GameObject cardSetRewardOverlay;
    [SerializeField] GameObject unlockIntroOverlay;
    [SerializeField] GameObject sceneCurtain;
    [SerializeField] GameObject lockBadge;
    [SerializeField] GameObject loadingCover;

    public GameObject Get(ERuntimeUiPrefab _id)
        => _id switch
        {
            ERuntimeUiPrefab.CardRewardOverlay => cardRewardOverlay,
            ERuntimeUiPrefab.CardSetRewardOverlay => cardSetRewardOverlay,
            ERuntimeUiPrefab.UnlockIntroOverlay => unlockIntroOverlay,
            ERuntimeUiPrefab.SceneCurtain => sceneCurtain,
            ERuntimeUiPrefab.LockBadge => lockBadge,
            ERuntimeUiPrefab.LoadingCover => loadingCover,
            _ => null,
        };
}

public static class RuntimeUiPrefabs
{
    static RuntimeUiPrefabCatalog s_catalog;

    public static void SetSource(RuntimeUiPrefabCatalog _catalog) => s_catalog = _catalog;

    public static GameObject Get(ERuntimeUiPrefab _id)
    {
        GameObject t_prefab = s_catalog != null ? s_catalog.Get(_id) : null;
        if (t_prefab == null)
            Debug.LogError($"[RuntimeUiPrefabs] {_id} 프리팹이 Boot 카탈로그에 연결되지 않았습니다.");
        return t_prefab;
    }
}
