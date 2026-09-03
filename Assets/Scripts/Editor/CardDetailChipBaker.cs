using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>카드 상세창(CardDetailOverlay)의 키워드·시너지 칩을 프리팹에 <b>박아 넣는</b> 일회성 도구.
///
/// 예전엔 런타임에 Instantiate/Destroy로 칩을 지었다 — 그래서 칩 위치·간격을 씬에서 눈으로 못 잡고,
/// 줄(List) 노드에 레이아웃이 빠져 있어도 에디터에선 티가 안 났다(실제로 키워드 줄에 빠져 있었다).
/// 칩을 미리 깔아 두면 배치는 저작으로 확정되고, 런타임은 켜고 끄기만 한다.
///
/// 칩 수를 늘리거나 프리팹을 다시 깔아야 하면 메뉴에서 한 번 더 돌리면 된다(멱등).</summary>
static class CardDetailChipBaker
{
    const string OverlayPath = "Assets/Assets/Prefabs/UI/LobbyUI/CardDetailUI/CardDetailOverlay.prefab";

    // 키워드는 CardKeyword 선언 수(None 제외)가 곧 한 카드가 가질 수 있는 최대치다.
    // 시너지는 지금 카드당 1개지만 늘어날 여지를 둔다 — 모자라면 런타임이 앞에서부터만 채우고 경고한다.
    const int KeywordChipCount = 9;
    const int SynergyChipCount = 3;

    [MenuItem("Tools/UI/도감 상세창 칩 박기")]
    static void Bake()
    {
        GameObject t_root = PrefabUtility.LoadPrefabContents(OverlayPath);
        if (t_root == null) { Debug.LogError($"[ChipBaker] 프리팹을 못 열었다: {OverlayPath}"); return; }

        try
        {
            var t_view = t_root.GetComponentInChildren<CardDetailOverlayView>(true);
            if (t_view == null) { Debug.LogError("[ChipBaker] CardDetailOverlayView가 없다"); return; }

            var t_so = new SerializedObject(t_view);
            Transform t_keywordRoot = t_so.FindProperty("keywordChipRoot").objectReferenceValue as Transform;
            Transform t_synergyRoot = t_so.FindProperty("synergyChipRoot").objectReferenceValue as Transform;
            var       t_chip        = t_so.FindProperty("chipPrefab").objectReferenceValue as KeywordExplainItem;

            if (t_keywordRoot == null || t_synergyRoot == null || t_chip == null)
            {
                Debug.LogError("[ChipBaker] keywordChipRoot / synergyChipRoot / chipPrefab 중 미배선이 있다");
                return;
            }

            GameObject t_chipAsset = t_chip.gameObject;

            BakeRow(t_keywordRoot, t_chipAsset, KeywordChipCount, "KeywordChip");
            BakeRow(t_synergyRoot, t_chipAsset, SynergyChipCount, "SynergyChip");

            PrefabUtility.SaveAsPrefabAsset(t_root, OverlayPath);
            Debug.Log($"[ChipBaker] 완료 — 키워드 {KeywordChipCount}개 / 시너지 {SynergyChipCount}개");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(t_root);
        }
    }

    static void BakeRow(Transform _root, GameObject _chipAsset, int _count, string _namePrefix)
    {
        for (int t_i = _root.childCount - 1; t_i >= 0; t_i--)
            Object.DestroyImmediate(_root.GetChild(t_i).gameObject);

        EnsureLayout(_root);

        for (int t_i = 0; t_i < _count; t_i++)
        {
            var t_go = (GameObject)PrefabUtility.InstantiatePrefab(_chipAsset, _root);
            t_go.name = $"{_namePrefix}{t_i:00}";
            t_go.SetActive(false);   // 켜는 주인은 런타임(CardDetailOverlayView)이다
        }
    }

    /// <summary>줄 노드에 가로 배치를 보장한다. 없으면 칩이 전부 같은 자리에 겹쳐 쌓인다(키워드 줄의 증상).
    /// ChildControl을 끄는 이유 — 칩에는 LayoutElement가 없어서 켜 두면 preferred 폭이 0으로 잡혀
    /// 칩이 납작해진다. 칩 프리팹에 저작된 크기(194x100)를 그대로 쓰게 둔다.</summary>
    static void EnsureLayout(Transform _root)
    {
        var t_layout = _root.GetComponent<HorizontalLayoutGroup>();
        if (t_layout == null) t_layout = _root.gameObject.AddComponent<HorizontalLayoutGroup>();

        t_layout.childAlignment       = TextAnchor.MiddleLeft;
        t_layout.spacing              = 12f;
        t_layout.childControlWidth    = false;
        t_layout.childControlHeight   = false;
        t_layout.childForceExpandWidth  = false;
        t_layout.childForceExpandHeight = false;
    }
}
