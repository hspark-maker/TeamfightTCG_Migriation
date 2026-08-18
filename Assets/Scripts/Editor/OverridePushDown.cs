using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 씬 인스턴스의 오버라이드를 **그 값이 속한 프리팹까지 내려보낸다**(push-down).
///
/// 오버라이드는 지우는 게 아니라 옮기는 것이다. 값이 씬에 있으면 프리팹을 열었을 때 안 보이고,
/// 최상위 프리팹에 몰아넣으면 그 프리팹만 마커투성이가 된다. 값은 **자기가 속한 프리팹**이 갖는 게 맞다.
///
/// 위험한 지점은 하나다 — 여러 화면이 공유하는 프리팹(예: CardUIView)까지 내려보내면
/// 그 프리팹을 쓰는 **다른 화면이 전부 바뀐다**(전투 카드까지). 그래서 목적지는
/// "이 계층 안에서만 쓰이는 가장 깊은 프리팹"으로 고르고, 공유 프리팹은 건너뛴다.
/// </summary>
static class OverridePushDown
{
    [MenuItem("Tools/Lobby/Push Down Overrides - Dry Run")]
    static void DryRun() => Run(false);

    [MenuItem("Tools/Lobby/Push Down Overrides - Apply")]
    static void Apply() => Run(true);

    static void Run(bool _apply)
    {
        Scene t_scene = SceneManager.GetActiveScene();
        if (!t_scene.isLoaded) { Debug.LogError("[PushDown] 열린 씬이 없다."); return; }

        if (t_scene.isDirty)
        {
            Debug.LogError("[PushDown] 씬에 저장 안 된 변경이 있다 — 저장하거나 다시 연 뒤 실행할 것.");
            return;
        }

        Dictionary<string, int> t_users = BuildDirectUserCounts();
        var t_report = new StringBuilder();
        int t_moved = 0, t_blocked = 0, t_stay = 0;

        foreach (GameObject t_sceneRoot in t_scene.GetRootGameObjects())
        {
            foreach (Transform t_tr in t_sceneRoot.GetComponentsInChildren<Transform>(true))
            {
                GameObject t_go = t_tr.gameObject;
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(t_go)) continue;
                if (PrefabUtility.GetOutermostPrefabInstanceRoot(t_go) != t_go) continue;

                foreach (ObjectOverride t_override in PrefabUtility.GetObjectOverrides(t_go, false))
                {
                    GameObject t_target = AsGameObject(t_override.instanceObject);
                    if (t_target == null) continue;

                    List<string> t_chain = PrefabChain(t_target);
                    if (t_chain.Count == 0) { t_stay++; continue; }

                    // 이 계층 안에서만 쓰이는 가장 깊은 프리팹. 공유되면 한 단계 위로 올린다.
                    string t_destination = t_chain.FirstOrDefault(p => Users(t_users, p) <= 1);
                    string t_deepest = t_chain[0];

                    if (t_destination == null)
                    {
                        t_blocked++;
                        t_report.AppendLine(
                            $"  [보류] {Path(t_target.transform)}\n"
                          + $"        {Name(t_deepest)} 이 {Users(t_users, t_deepest)}곳에서 공유됨 — 내려보내면 다른 화면이 바뀐다");
                        continue;
                    }

                    t_report.AppendLine(
                        $"  [이관] {Path(t_target.transform)}\n"
                        + $"        {Name(t_deepest)} → {Name(t_destination)}"
                        + (t_destination == t_deepest ? "" : $"  (공유라 {Users(t_users, t_deepest)}곳, 한 단계 위로)"));

                    if (_apply) t_override.Apply(t_destination);
                    t_moved++;
                }
            }
        }

        if (_apply && t_moved > 0)
        {
            EditorSceneManager.MarkSceneDirty(t_scene);
            EditorSceneManager.SaveScene(t_scene);
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"[PushDown] {(_apply ? "APPLY" : "DRY RUN")} {t_scene.name}\n"
                + $"이관 {t_moved}, 보류(공유) {t_blocked}, 대상아님 {t_stay}\n{t_report}");
    }

    /// <summary>대상 오브젝트가 속한 프리팹 자산 경로를, 안쪽(깊은 것)부터 바깥쪽 순으로.</summary>
    static List<string> PrefabChain(GameObject _target)
    {
        var t_chain = new List<string>();

        for (GameObject t_node = PrefabUtility.GetNearestPrefabInstanceRoot(_target);
             t_node != null;
             t_node = t_node.transform.parent != null
                 ? PrefabUtility.GetNearestPrefabInstanceRoot(t_node.transform.parent.gameObject)
                 : null)
        {
            string t_path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t_node);
            if (!string.IsNullOrEmpty(t_path) && !t_chain.Contains(t_path)) t_chain.Add(t_path);

            if (t_node.transform.parent == null) break;
        }

        return t_chain;
    }

    /// <summary>
    /// 프리팹/씬이 이 자산을 직접 참조하는 개수. 1 이하 = 이 계층 전용으로 본다.
    ///
    /// **빌드에 안 들어가는 씬은 사용처로 세지 않는다.** 테스트 씬(Scenes/TEST)과 백업(_Recovery)이
    /// 섞이면 사실상 단독인 프리팹이 "공유"로 잡혀, 값이 엉뚱하게 한 단계 위에 얹힌다.
    /// 기준은 EditorBuildSettings의 활성 씬 목록 — 실제로 출하되는 것만 사용처다.
    /// </summary>
    static Dictionary<string, int> BuildDirectUserCounts()
    {
        var t_counts = new Dictionary<string, int>();

        var t_shippedScenes = new HashSet<string>(
            EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path));

        string[] t_all = AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Distinct()
            .ToArray();

        foreach (string t_user in t_all)
        {
            if (t_user.EndsWith(".unity") && !t_shippedScenes.Contains(t_user)) continue;

            foreach (string t_dep in AssetDatabase.GetDependencies(t_user, false))
            {
                if (t_dep == t_user || !t_dep.EndsWith(".prefab")) continue;
                t_counts.TryGetValue(t_dep, out int t_n);
                t_counts[t_dep] = t_n + 1;
            }
        }

        return t_counts;
    }

    static int Users(Dictionary<string, int> _counts, string _path)
        => _counts.TryGetValue(_path, out int t_n) ? t_n : 0;

    static GameObject AsGameObject(Object _object)
        => _object as GameObject ?? (_object as Component)?.gameObject;

    static string Name(string _assetPath) => System.IO.Path.GetFileNameWithoutExtension(_assetPath);

    static string Path(Transform _transform)
    {
        string t_path = _transform.name;
        for (Transform t = _transform.parent; t != null; t = t.parent) t_path = $"{t.name}/{t_path}";
        return t_path;
    }
}
