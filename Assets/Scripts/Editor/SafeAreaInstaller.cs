using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 열려 있는 씬의 Canvas에 SafeArea 래퍼를 끼워 넣는다. 메뉴: Tools/UI/Install SafeArea (Open Scene).
///
/// 하는 일: Canvas 바로 아래에 "SafeArea" RectTransform을 만들고 <b>기존 자식을 순서 그대로</b> 그 밑으로 옮긴다.
/// 손으로 하면 자식 순서(=UI 그리는 순서)가 뒤섞이기 쉬워서 도구로 만든다.
///
/// 전체 화면을 덮어야 하는 캔버스(컷씬 영상, 코인 토스 딤)는 <see cref="SkipCanvases"/>에서 제외한다 —
/// 그런 연출은 노치까지 덮는 게 맞고, 안으로 밀면 가장자리에 빈 띠가 생긴다.
///
/// 멱등: 이미 SafeArea가 있으면 건너뛴다. 여러 번 실행해도 중첩되지 않는다.
/// </summary>
public static class SafeAreaInstaller
{
    const string WrapperName = "SafeArea";

    sealed class PooledLayout
    {
        public readonly string assetPath;
        public readonly string parentPath;
        public readonly string[] children;

        public PooledLayout(string _assetPath, string _parentPath, params string[] _children)
        {
            this.assetPath  = _assetPath;
            this.parentPath = _parentPath;
            this.children   = _children;
        }
    }

    // 딤·전체화면 입력은 원래 부모에 남기고 실제 콘텐츠만 감싼다.
    // 이름/anchor 추론은 조용히 잘못 감쌀 수 있으므로 풀링 프리팹 계약을 명시한다.
    static readonly PooledLayout[] PooledLayouts =
    {
        new("Assets/Assets/Prefabs/UI/PooledUI/ProfileEditPanel.prefab", "Root", "Panel"),
        new("Assets/Assets/Prefabs/UI/PooledUI/SimpleYNPopup.prefab", "Contents", "TitleText", "YesButton", "NoButton"),
        new("Assets/Assets/Prefabs/UI/PooledUI/PooledCardElement.prefab", "", "CardElement"),
        new("Assets/Assets/Prefabs/UI/PooledUI/AdventureNodePopup.prefab", "Contents", "Panel"),
        new("Assets/Assets/Prefabs/UI/PooledUI/RankRewardOverlay.prefab", "Root", "Panel"),
        new("Assets/Assets/Prefabs/UI/PooledUI/KeywordGrowthOverlay.prefab", "Root", "Panel"),
        new("Assets/Assets/Prefabs/UI/PooledUI/SettingUI.prefab", "Contents", "Panel"),
        new("Assets/Assets/Prefabs/UI/PooledUI/PackOddsPopup.prefab", "Contents", "Panel"),
        new("Assets/Assets/Prefabs/UI/LobbyUI/Tabs/Tab_Deck/DeckEditPanel.prefab", "",
            "Title", "BackButton", "DeckArea", "CollectionArea", "ButtonBar", "SaveButton", "PlayButton"),
    };

    // 이름이 여기 포함되면 건너뛴다(전체 화면 연출용 캔버스).
    static readonly string[] SkipCanvases = { "CinematicCanvas", "CoinFlipCanvas" };

    [MenuItem("Tools/UI/Install SafeArea (Open Scene)")]
    public static void InstallInOpenScene()
    {
        var t_scene = EditorSceneManager.GetActiveScene();
        int t_added = 0, t_skipped = 0;

        foreach (GameObject t_root in t_scene.GetRootGameObjects())
        {
            foreach (Canvas t_canvas in t_root.GetComponentsInChildren<Canvas>(true))
            {
                // 중첩 Canvas(자체 정렬용)는 대상이 아니다 — 래퍼는 최상위 캔버스당 하나.
                if (t_canvas.transform.parent != null
                    && t_canvas.transform.parent.GetComponentInParent<Canvas>() != null) continue;

                string t_name = t_canvas.gameObject.name;
                if (System.Array.IndexOf(SkipCanvases, t_name) >= 0)
                {
                    Debug.Log($"[SafeArea] 제외(전체화면 연출): {t_name}");
                    t_skipped++;
                    continue;
                }
                if (t_canvas.transform.Find(WrapperName) != null)
                {
                    Debug.Log($"[SafeArea] 이미 있음: {t_name}/{WrapperName}");
                    t_skipped++;
                    continue;
                }

                Wrap(t_canvas);
                t_added++;
            }
        }

        if (t_added > 0) EditorSceneManager.MarkSceneDirty(t_scene);
        Debug.Log($"[SafeArea] 씬 '{t_scene.name}' — 삽입 {t_added}건, 건너뜀 {t_skipped}건"
                  + (t_added > 0 ? " (씬 저장 필요)" : ""));
    }

    [MenuItem("Tools/UI/Install SafeArea (Pooled UI Prefabs)")]
    public static void InstallInPooledPrefabs()
    {
        int t_added = 0, t_skipped = 0, t_failed = 0;

        foreach (PooledLayout t_layout in PooledLayouts)
        {
            GameObject t_root = PrefabUtility.LoadPrefabContents(t_layout.assetPath);
            try
            {
                Transform t_parent = string.IsNullOrEmpty(t_layout.parentPath)
                    ? t_root.transform
                    : t_root.transform.Find(t_layout.parentPath);
                if (t_parent == null)
                {
                    Debug.LogError($"[SafeArea] 부모 없음: {t_layout.assetPath}/{t_layout.parentPath}");
                    t_failed++;
                    continue;
                }

                Transform t_existing = t_parent.Find(WrapperName);
                if (t_existing != null)
                {
                    if (t_existing.GetComponent<SafeAreaFitter>() == null)
                        Debug.LogError($"[SafeArea] 이름은 있지만 Fitter 없음: {t_layout.assetPath}/{t_layout.parentPath}/{WrapperName}");
                    else
                        Debug.Log($"[SafeArea] 이미 있음: {t_layout.assetPath}/{t_layout.parentPath}/{WrapperName}");
                    t_skipped++;
                    continue;
                }

                var t_children = new List<Transform>(t_layout.children.Length);
                foreach (string t_name in t_layout.children)
                {
                    Transform t_child = t_parent.Find(t_name);
                    if (t_child == null || t_child.parent != t_parent)
                    {
                        Debug.LogError($"[SafeArea] 직속 자식 없음: {t_layout.assetPath}/{t_layout.parentPath}/{t_name}");
                        t_children.Clear();
                        break;
                    }
                    t_children.Add(t_child);
                }

                if (t_children.Count == 0)
                {
                    t_failed++;
                    continue;
                }

                t_children.Sort((a, b) => a.GetSiblingIndex().CompareTo(b.GetSiblingIndex()));
                int t_sibling = t_children[0].GetSiblingIndex();

                var t_go = new GameObject(WrapperName, typeof(RectTransform));
                t_go.layer = t_parent.gameObject.layer;
                var t_rect = (RectTransform)t_go.transform;
                t_rect.SetParent(t_parent, false);
                Stretch(t_rect);
                t_rect.SetSiblingIndex(t_sibling);

                foreach (Transform t_child in t_children)
                {
                    t_child.SetParent(t_rect, false);
                    t_child.SetAsLastSibling();
                }

                t_go.AddComponent<SafeAreaFitter>();
                Stretch(t_rect); // ExecuteAlways가 현재 Device Simulator 값을 굽지 않게 저작 상태는 full stretch로 저장.
                PrefabUtility.SaveAsPrefabAsset(t_root, t_layout.assetPath);
                Debug.Log($"[SafeArea] 풀링 프리팹 적용: {t_layout.assetPath} (콘텐츠 {t_children.Count}개)");
                t_added++;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(t_root);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[SafeArea] 풀링 프리팹 완료: 적용 {t_added}건, 건너뜀 {t_skipped}건, 실패 {t_failed}건");
    }

    static void Wrap(Canvas _canvas)
    {
        Transform t_canvasTr = _canvas.transform;

        // 옮기기 **전에** 현재 자식을 순서대로 스냅샷. 순회 중 부모를 바꾸면 인덱스가 밀려 순서가 깨진다.
        var t_children = new List<Transform>(t_canvasTr.childCount);
        for (int i = 0; i < t_canvasTr.childCount; i++) t_children.Add(t_canvasTr.GetChild(i));

        Undo.RegisterFullObjectHierarchyUndo(_canvas.gameObject, "Install SafeArea");

        var t_go = new GameObject(WrapperName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(t_go, "Install SafeArea");
        t_go.layer = _canvas.gameObject.layer;

        var t_rect = (RectTransform)t_go.transform;
        t_rect.SetParent(t_canvasTr, false);
        // 시작 상태는 전체 화면 stretch. 실행 중 SafeAreaFitter가 안전 영역으로 좁힌다
        // (노치 없는 기기에서는 이 값 그대로라 레이아웃 변화가 없다).
        t_rect.anchorMin = Vector2.zero;
        t_rect.anchorMax = Vector2.one;
        t_rect.offsetMin = Vector2.zero;
        t_rect.offsetMax = Vector2.zero;
        t_rect.localScale = Vector3.one;

        foreach (Transform t_child in t_children)
        {
            Undo.SetTransformParent(t_child, t_rect, "Install SafeArea");
            t_child.SetAsLastSibling();   // 스냅샷 순서 유지 = 그리는 순서 유지
        }

        t_go.AddComponent<SafeAreaFitter>();
        t_rect.SetAsFirstSibling();

        Debug.Log($"[SafeArea] {_canvas.name} → {WrapperName} 삽입, 자식 {t_children.Count}개 이동");
    }

    static void Stretch(RectTransform _rect)
    {
        _rect.anchorMin = Vector2.zero;
        _rect.anchorMax = Vector2.one;
        _rect.offsetMin = Vector2.zero;
        _rect.offsetMax = Vector2.zero;
        _rect.localScale = Vector3.one;
    }
}
