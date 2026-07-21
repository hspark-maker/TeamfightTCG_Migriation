#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class DeckSelectPopupCreator
{
    const string PREFAB_PATH = "Assets/Prefabs/UI/DeckSelectPopup.prefab";

    [MenuItem("Tools/BurgerMonster/Create DeckSelectPopup Prefab")]
    static void Create()
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PREFAB_PATH));

        var t_go = new GameObject("DeckSelectPopup");
        t_go.AddComponent<Canvas>();
        t_go.AddComponent<CanvasScaler>();
        t_go.AddComponent<GraphicRaycaster>();
        t_go.AddComponent<DeckSelectPopup>();

        PrefabUtility.SaveAsPrefabAsset(t_go, PREFAB_PATH);
        Object.DestroyImmediate(t_go);

        AssetDatabase.Refresh();
        Debug.Log($"DeckSelectPopup prefab created → {PREFAB_PATH}");
    }
}
#endif
