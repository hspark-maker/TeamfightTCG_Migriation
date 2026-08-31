using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// BattleScene을 복제해 AttackAnimTester가 배선된 공격 연출 테스트 씬을 만든다.
/// </summary>
public static class AttackTestSceneBuilder
{
    const string BattlePath = "Assets/Scenes/BattleScene.unity";
    const string TestPath = "Assets/Scenes/TEST/AttackTestScene.unity";

    // 기존 테스트 씬에 이관된 순서를 그대로 유지한다.
    static readonly int[] PlayerCardIds = { 12, 18, 0 };
    static readonly int[] EnemyCardIds = { 27, 6, 9 };

    [MenuItem("Tools/Build Attack Test Scene")]
    public static void Build()
    {
        var t_battle = EditorSceneManager.OpenScene(BattlePath, OpenSceneMode.Single);
        if (!t_battle.IsValid())
        {
            Debug.LogError($"[AttackTest] BattleScene을 열 수 없다: {BattlePath}");
            return;
        }

        EditorSceneManager.SaveScene(t_battle, TestPath, true);
        var t_scene = EditorSceneManager.OpenScene(TestPath, OpenSceneMode.Single);

        Deactivate("GameManager");
        Deactivate("NetworkGameController");
        Deactivate("MultiplayerTurnRunner");
        Deactivate("CoinFlipCanvas");

        var t_playerFieldView = FindComponent<BattleFieldView>("PlayerFieldView");
        var t_enemyFieldView = FindComponent<BattleFieldView>("EnemyFieldView");
        if (t_playerFieldView == null || t_enemyFieldView == null)
        {
            Debug.LogError("[AttackTest] BattleFieldView를 찾지 못했다.");
            return;
        }

        var t_old = GameObject.Find("AttackAnimTester");
        if (t_old != null) Object.DestroyImmediate(t_old);

        var t_go = new GameObject("AttackAnimTester");
        var t_tester = t_go.AddComponent<AttackAnimTester>();
        var t_serialized = new SerializedObject(t_tester);
        t_serialized.FindProperty("playerFieldView").objectReferenceValue = t_playerFieldView;
        t_serialized.FindProperty("enemyFieldView").objectReferenceValue = t_enemyFieldView;
        SetCardIdArray(t_serialized, "playerCardIds", PlayerCardIds);
        SetCardIdArray(t_serialized, "enemyCardIds", EnemyCardIds);
        t_serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(t_scene);
        EditorSceneManager.SaveScene(t_scene, TestPath);
        Debug.Log($"[AttackTest] 테스트 씬 생성 완료: {TestPath}");
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

    static void SetCardIdArray(SerializedObject _serialized, string _propertyName, int[] _ids)
    {
        var t_array = _serialized.FindProperty(_propertyName);
        t_array.arraySize = _ids.Length;
        for (int i = 0; i < _ids.Length; i++)
            t_array.GetArrayElementAtIndex(i).intValue = _ids[i];
    }
}
