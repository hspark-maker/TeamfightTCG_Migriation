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
}
