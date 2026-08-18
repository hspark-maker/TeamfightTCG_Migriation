using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 씬에서 <c>localScale</c>로 크기를 맞춘 UI를 찾아 보고한다.
///
/// 왜 문제인가: 이 프로젝트 규약은 "UI 크기는 rect·폰트 값으로, localScale 금지"다.
/// 스케일로 줄이면 자식 폰트·테두리까지 함께 뭉개지고, 프리팹이 정의한 크기를 씬이 배율로 덮어
/// **크기의 진실원이 두 곳으로 갈린다**(프리팹을 고쳐도 씬 배율이 다시 덮는다).
///
/// 보고만 한다 — rect로 옮기는 건 겉모습이 바뀌는 일이라 사람이 값을 보고 정해야 한다.
/// </summary>
static class UiScaleOverrideReport
{
    [MenuItem("Tools/Lobby/Report Scaled UI (Scene)")]
    static void Report()
    {
        Scene t_scene = SceneManager.GetActiveScene();
        if (!t_scene.isLoaded)
        {
            Debug.LogError("[UiScaleOverrideReport] 열린 씬이 없다.");
            return;
        }

        var t_report = new StringBuilder();
        int t_count = 0;

        foreach (GameObject t_root in t_scene.GetRootGameObjects())
        {
            foreach (RectTransform t_rect in t_root.GetComponentsInChildren<RectTransform>(true))
            {
                Vector3 t_scale = t_rect.localScale;
                if (Approximately(t_scale, Vector3.one)) continue;

                t_count++;

                // 프리팹 원본이 갖고 있던 값 — 씬이 덮은 것인지 프리팹부터 그런지 구분한다.
                var t_source = PrefabUtility.GetCorrespondingObjectFromSource(t_rect) as RectTransform;
                string t_origin = t_source != null
                    ? $"프리팹 {t_source.localScale.x:0.######} → 씬 {t_scale.x:0.######}"
                    : $"씬 전용 {t_scale.x:0.######}";

                Vector2 t_size = t_rect.rect.size;
                t_report.AppendLine(
                    $"  {Path(t_rect)}\n"
                  + $"      {t_origin}\n"
                  + $"      rect = {t_size.x:0.#} x {t_size.y:0.#}"
                  + $"  →  실제 표시 = {t_size.x * t_scale.x:0.#} x {t_size.y * t_scale.y:0.#}");
            }
        }

        Debug.Log($"[UiScaleOverrideReport] {t_scene.name} — localScale != 1 인 UI {t_count}개\n{t_report}");
    }

    static bool Approximately(Vector3 _a, Vector3 _b)
        => Mathf.Approximately(_a.x, _b.x) && Mathf.Approximately(_a.y, _b.y) && Mathf.Approximately(_a.z, _b.z);

    static string Path(Transform _transform)
    {
        string t_path = _transform.name;
        for (Transform t = _transform.parent; t != null; t = t.parent) t_path = $"{t.name}/{t_path}";
        return t_path;
    }
}
