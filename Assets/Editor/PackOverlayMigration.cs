using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 카드팩 개봉 화면을 별도 씬 → 로비 오버레이로 옮기는 1회성 마이그레이션.
// 손으로 드래그하면 [SerializeField] 배선 40여 개가 끊길 위험이 있어 에디터에서 재부모화로 처리한다
// (컴포넌트를 다른 오브젝트로 옮기면 참조가 끊기지만, 오브젝트를 옮기는 것은 참조를 보존한다).
// 메뉴를 1→4 순서대로 한 번씩 실행하면 끝난다. 실행 후 이 파일은 지워도 된다.
internal static class PackOverlayMigration
{
    const string PackScenePath    = "Assets/Scenes/CardPack.unity";
    const string LobbyScenePath   = "Assets/Scenes/LobbyScene.unity";
    const string OverlayPrefabDir = "Assets/Assets/Prefabs/UI/LobbyUI/PackUI";
    const string OverlayPrefab    = OverlayPrefabDir + "/PackOpenOverlay.prefab";

    const string DirectorName = "PackOpenDirector";
    const string CanvasName   = "UICanvas";
    const string RootName     = "PackOpenOverlay";
    const string ContentName  = "Content";

    // 로비 0 / 게이트 350 / 팝업 400 사이. 개봉은 로비 위, 안내·팝업 아래여야 한다.
    const int OverlaySortingOrder = 100;

    [MenuItem("Tools/카드팩 오버레이 이관/1. 오버레이 프리팹 추출", false, 1)]
    static void ExtractPrefab()
    {
        if (File.Exists(OverlayPrefab))
        {
            Debug.LogWarning($"[PackOverlayMigration] 이미 존재: {OverlayPrefab} — 중단(지우고 다시 실행할 것).");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var t_scene = EditorSceneManager.OpenScene(PackScenePath, OpenSceneMode.Single);

        var t_director = FindRoot(t_scene, DirectorName);
        var t_canvasGo = FindRoot(t_scene, CanvasName);
        if (t_director == null || t_canvasGo == null)
        {
            Debug.LogError($"[PackOverlayMigration] '{DirectorName}' 또는 '{CanvasName}' 루트를 찾지 못했다 — 중단.");
            return;
        }

        // 로비에 이미 있는 것들은 프리팹에 들어가면 안 된다(AudioListener 중복 · 씬당 1개 규약).
        StripComponent<PackStandaloneBoot>(t_director);
        StripComponent<OutgameTutorialBridge>(t_director);

        var t_canvas = t_canvasGo.GetComponent<Canvas>();
        if (t_canvas != null)
        {
            t_canvas.overrideSorting = false;
            t_canvas.sortingOrder    = OverlaySortingOrder;
        }

        // 루트는 항상 활성이어야 Awake가 돌아 Instance를 선점한다 → 켜고 끄는 대상은 Content 하나뿐.
        var t_root    = new GameObject(RootName);
        var t_content = new GameObject(ContentName);
        t_content.transform.SetParent(t_root.transform, false);

        // worldPositionStays:false — 로컬 값을 그대로 보존한다(캔버스 좌표가 밀리지 않게).
        t_director.transform.SetParent(t_content.transform, false);
        t_canvasGo.transform.SetParent(t_content.transform, false);

        var t_overlay = t_root.AddComponent<PackOpenOverlay>();
        Wire(t_overlay, t_content, t_director.GetComponent<PackAcquireController>(), t_director.GetComponent<PackRevealView>());

        Directory.CreateDirectory(OverlayPrefabDir);
        var t_saved = PrefabUtility.SaveAsPrefabAsset(t_root, OverlayPrefab, out bool t_ok);
        Object.DestroyImmediate(t_root);

        // 이 시점의 씬은 원본이 파괴된 껍데기다. 사용자가 실수로 저장하면 되돌릴 수 없으므로
        // 결과와 무관하게 디스크에서 즉시 되읽어 메모리 변경을 버린다(3단계가 온전한 씬을 전제한다).
        EditorSceneManager.OpenScene(PackScenePath, OpenSceneMode.Single);

        if (!t_ok || t_saved == null)
        {
            Debug.LogError("[PackOverlayMigration] 프리팹 저장 실패 — 씬은 원본으로 되돌렸다.");
            return;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[PackOverlayMigration] 1단계 완료: {OverlayPrefab} 생성 · CardPack 씬은 원본 그대로.");
    }

    [MenuItem("Tools/카드팩 오버레이 이관/2. 로비 씬에 오버레이 배치", false, 2)]
    static void PlaceInLobby()
    {
        var t_prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OverlayPrefab);
        if (t_prefab == null)
        {
            Debug.LogError("[PackOverlayMigration] 프리팹이 없다 — 1단계 먼저.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var t_scene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Single);
        if (FindRoot(t_scene, RootName) != null)
        {
            Debug.LogWarning("[PackOverlayMigration] 로비에 이미 배치돼 있다 — 건너뜀.");
            return;
        }

        var t_instance = (GameObject)PrefabUtility.InstantiatePrefab(t_prefab, t_scene);
        t_instance.name = RootName;

        EditorSceneManager.MarkSceneDirty(t_scene);
        EditorSceneManager.SaveScene(t_scene);
        Debug.Log("[PackOverlayMigration] 2단계 완료: LobbyScene 루트에 오버레이 배치 · 저장.");
    }

    [MenuItem("Tools/카드팩 오버레이 이관/3. CardPack을 테스트 씬으로 강등", false, 3)]
    static void DemoteToTestScene()
    {
        var t_prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OverlayPrefab);
        if (t_prefab == null)
        {
            Debug.LogError("[PackOverlayMigration] 프리팹이 없다 — 1단계 먼저.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var t_scene    = EditorSceneManager.OpenScene(PackScenePath, OpenSceneMode.Single);
        var t_director = FindRoot(t_scene, DirectorName);
        var t_canvasGo = FindRoot(t_scene, CanvasName);

        // 더미 주입 설정(dummyPack·환급값 등)은 저작 자산이다 — 지우기 전에 새 오브젝트로 옮겨 담는다.
        var t_oldBoot = t_director != null ? t_director.GetComponent<PackStandaloneBoot>() : null;
        if (t_oldBoot != null)
        {
            var t_bootGo = new GameObject("StandaloneBoot");
            EditorUtility.CopySerialized(t_oldBoot, t_bootGo.AddComponent<PackStandaloneBoot>());
        }
        else Debug.LogWarning("[PackOverlayMigration] PackStandaloneBoot을 찾지 못해 더미 설정을 옮기지 못했다 — 수동 배선 필요.");

        // 연출 본체는 이제 프리팹이 진실원이다 — 씬 사본을 남기면 이중 진실원이 된다.
        if (t_director != null) Object.DestroyImmediate(t_director);
        if (t_canvasGo != null) Object.DestroyImmediate(t_canvasGo);

        if (FindRoot(t_scene, RootName) == null)
            ((GameObject)PrefabUtility.InstantiatePrefab(t_prefab, t_scene)).name = RootName;

        EditorSceneManager.MarkSceneDirty(t_scene);
        EditorSceneManager.SaveScene(t_scene);
        Debug.Log("[PackOverlayMigration] 3단계 완료: CardPack = Main Camera + EventSystem + StandaloneBoot + 오버레이 프리팹.");
    }

    [MenuItem("Tools/카드팩 오버레이 이관/4. 빌드세팅에서 CardPack 제거", false, 4)]
    static void RemoveFromBuildSettings()
    {
        var t_kept = EditorBuildSettings.scenes.Where(_s => _s.path != PackScenePath).ToArray();
        if (t_kept.Length == EditorBuildSettings.scenes.Length)
        {
            Debug.LogWarning("[PackOverlayMigration] 빌드세팅에 CardPack이 없다 — 건너뜀.");
            return;
        }

        EditorBuildSettings.scenes = t_kept;
        Debug.Log($"[PackOverlayMigration] 4단계 완료: 남은 씬 {t_kept.Length}개 — {string.Join(", ", t_kept.Select(_s => Path.GetFileNameWithoutExtension(_s.path)))}");
    }

    static GameObject FindRoot(Scene _scene, string _name)
        => _scene.GetRootGameObjects().FirstOrDefault(_go => _go.name == _name);

    static void StripComponent<T>(GameObject _go) where T : Component
    {
        var t_comp = _go.GetComponent<T>();
        if (t_comp == null) return;

        Object.DestroyImmediate(t_comp);
        Debug.Log($"[PackOverlayMigration] {typeof(T).Name} 제거(로비에 이미 있음).");
    }

    // private [SerializeField]라 직렬화 경로로 배선한다.
    static void Wire(PackOpenOverlay _overlay, GameObject _content, PackAcquireController _controller, PackRevealView _view)
    {
        var t_so = new SerializedObject(_overlay);
        t_so.FindProperty("content").objectReferenceValue    = _content;
        t_so.FindProperty("controller").objectReferenceValue = _controller;
        t_so.FindProperty("view").objectReferenceValue       = _view;
        t_so.ApplyModifiedPropertiesWithoutUndo();

        if (_controller == null || _view == null)
            Debug.LogWarning("[PackOverlayMigration] controller/view를 찾지 못했다 — 프리팹에서 수동 배선 필요.");
    }
}
