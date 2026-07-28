using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 공격 연출 테스트 씬 생성기. 메뉴 Tools/Build Attack Test Scene.
/// BattleScene을 복제해 비주얼(카메라/BG/필드/3v3 슬롯) 그대로 쓰고, 전투 부트스트랩(GameManager/네트워크/코인)만 끈 뒤
/// AttackAnimTester를 붙여 두 BattleFieldView + 카드 6장을 배선한다. 원본 BattleScene은 건드리지 않음.
/// 조작: 카드 탭(무장)→적 탭(공격) / [P] 플레이어 [E] 적 / 인스펙터에서 연출·타이밍 조정.
/// </summary>
public static class AttackTestSceneBuilder
{
    const string BattlePath = "Assets/Scenes/BattleScene.unity";
    const string TestPath   = "Assets/Scenes/AttackTestScene.unity";

    static readonly string[] PlayerCardPaths =
    {
        "Assets/SO/Cards/깜밤이.asset",
        "Assets/SO/Cards/눈덩곰.asset",
        "Assets/SO/Cards/단풍꼬리.asset",
    };
    static readonly string[] EnemyCardPaths =
    {
        "Assets/SO/Cards/모닥콩.asset",
        "Assets/SO/Cards/바위콩.asset",
        "Assets/SO/Cards/버섯냥.asset",
    };

    [MenuItem("Tools/Build Attack Test Scene")]
    public static void Build()
    {
        // 1) BattleScene 열고 → 사본으로 저장(원본 미변경) → 사본 열기.
        var t_battle = EditorSceneManager.OpenScene(BattlePath, OpenSceneMode.Single);
        if (!t_battle.IsValid()) { Debug.LogError($"[AttackTest] BattleScene 못 엶: {BattlePath}"); return; }
        EditorSceneManager.SaveScene(t_battle, TestPath, saveAsCopy: true);
        var t_scene = EditorSceneManager.OpenScene(TestPath, OpenSceneMode.Single);

        // 2) 전투 부트스트랩/네트워크/코인 비활성 → 실제 전투가 렌더를 덮어쓰지 않게.
        Deactivate("GameManager");
        Deactivate("NetworkGameController");
        Deactivate("MultiplayerTurnRunner");
        Deactivate("CoinFlipCanvas");

        // 3) 필드뷰 찾기.
        var t_pfv = FindComponent<BattleFieldView>("PlayerFieldView");
        var t_efv = FindComponent<BattleFieldView>("EnemyFieldView");
        if (t_pfv == null || t_efv == null) { Debug.LogError("[AttackTest] FieldView 못 찾음"); return; }

        // 4) 기존 테스터 제거 후 새로 배선.
        var t_old = GameObject.Find("AttackAnimTester");
        if (t_old != null) Object.DestroyImmediate(t_old);

        var t_go     = new GameObject("AttackAnimTester");
        var t_tester = t_go.AddComponent<AttackAnimTester>();
        var t_so = new SerializedObject(t_tester);
        t_so.FindProperty("playerFieldView").objectReferenceValue = t_pfv;
        t_so.FindProperty("enemyFieldView").objectReferenceValue  = t_efv;
        SetCardArray(t_so, "playerCards", PlayerCardPaths);
        SetCardArray(t_so, "enemyCards",  EnemyCardPaths);
        t_so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(t_scene);
        EditorSceneManager.SaveScene(t_scene, TestPath);
        Debug.Log($"[AttackTest] 씬 생성 완료: {TestPath} (3v3, BattleScene 복제 · 부트스트랩 off)");
    }

    static void Deactivate(string _name)
    {
        var t_go = GameObject.Find(_name);
        if (t_go != null) t_go.SetActive(false);
        else Debug.LogWarning($"[AttackTest] '{_name}' 없음(스킵)");
    }

    static T FindComponent<T>(string _name) where T : Component
    {
        var t_go = GameObject.Find(_name);
        return t_go != null ? t_go.GetComponent<T>() : null;
    }

    static void SetCardArray(SerializedObject _so, string _prop, string[] _paths)
    {
        var t_arr = _so.FindProperty(_prop);
        t_arr.arraySize = _paths.Length;
        for (int i = 0; i < _paths.Length; i++)
        {
            var t_card = AssetDatabase.LoadAssetAtPath<CardData>(_paths[i]);
            if (t_card == null) Debug.LogWarning($"[AttackTest] 카드 없음: {_paths[i]}");
            t_arr.GetArrayElementAtIndex(i).objectReferenceValue = t_card;
        }
    }
}
