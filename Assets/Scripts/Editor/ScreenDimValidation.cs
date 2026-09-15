#if UNITY_EDITOR
using System;
using System.Reflection;
using DG.Tweening;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>사용자 씬·세이브를 변경하지 않고 공통 딤의 인계와 저작을 검사한다.</summary>
public static class ScreenDimValidation
{
    [MenuItem("Tools/UI/Validate Shared Overlay Dim")]
    public static void Run()
    {
        Require(!EditorApplication.isPlaying && !ScreenDim.IsAvailable,
            "Run outside play mode with no registered Full dim.");
        ValidatePrefabs();
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            root = new GameObject("DimValidation", typeof(RectTransform), typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/UI/Common/ScreenDim.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.SetParent(root.transform, false);
            var screen = instance.GetComponent<ScreenDim>();
            var canvas = instance.AddComponent<Canvas>();
            instance.AddComponent<GraphicRaycaster>();
            var serialized = new SerializedObject(screen);
            serialized.FindProperty("sortingCanvas").objectReferenceValue = canvas;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            InvokeLifecycle(screen, "Awake");
            var group = instance.GetComponent<CanvasGroup>();
            var image = instance.transform.Find("Full").GetComponent<Image>();
            var first = new object();
            var second = new object();

            ScreenDim.Show(first, 0.6f, false);
            Require(Near(group.alpha, 0.6f) && !group.blocksRaycasts && !canvas.overrideSorting,
                "Legacy request changed.");
            ScreenDim.Hide(first);
            Require(group.alpha == 0f && !group.blocksRaycasts, "Legacy hide must remain immediate.");

            var lower = ScreenDim.Acquire(first, Options(0.7f, UiSortingOrder.RewardDim));
            Require(Near(group.alpha, 0.7f) && canvas.sortingOrder == 119 && group.blocksRaycasts,
                "Reward request must render above pack opening and below reward.");
            var upper = ScreenDim.Acquire(second, Options(0.9f, UiSortingOrder.IntroDim));
            Require(Near(group.alpha, 0.9f) && canvas.sortingOrder == 149,
                "Intro must dim lifted card detail without dimming itself.");
            lower.SetColor(Color.red);
            Require(image.color == Color.black, "Background owner's pulse leaked to foreground.");
            upper.Release();
            Require(Near(group.alpha, 0.7f) && image.color == Color.red && canvas.sortingOrder == 119,
                "Underlying color, alpha and sorting must restore together.");

            var replacement = ScreenDim.Acquire(first, Options(0.8f, UiSortingOrder.RewardDim));
            lower.Release();
            lower.SetColor(Color.blue);
            Require(Near(group.alpha, 0.8f) && image.color == Color.black,
                "Stale handle changed replacement request.");
            replacement.Release(0.15f);
            replacement.Release();
            ScreenDim.Hide(new object());
            Require(Near(group.alpha, 0.8f) && group.blocksRaycasts,
                "Release must retain visual/input until fade starts; repeated release must be harmless.");
            var next = ScreenDim.Acquire(second, Options(0.8f, UiSortingOrder.IntroDim, 0.12f));
            InvokeLifecycle(screen, "LateUpdate");
            Require(Near(group.alpha, 0.8f), "Same-frame handoff flashed hidden.");
            DOTween.Complete(group);
            next.Release(0.15f);
            InvokeLifecycle(screen, "LateUpdate");
            DOTween.Goto(group, 0.075f, false);
            float halfway = group.alpha;
            Require(halfway > 0f && halfway < 0.8f && group.blocksRaycasts,
                "Last release must fade while retaining input blocking.");
            var resumed = ScreenDim.Acquire(first, Options(1f, UiSortingOrder.RewardDim, 0.12f));
            Require(Near(group.alpha, halfway), "Reentry must start from current alpha.");
            DOTween.Complete(group);
            Require(Near(group.alpha, 1f) && group.blocksRaycasts, "Stale fade hid new request.");
            resumed.Release(0.15f);
            InvokeLifecycle(screen, "LateUpdate");
            DOTween.Complete(group);
            Require(group.alpha == 0f && !group.blocksRaycasts && !canvas.overrideSorting,
                "Completed exit left a dim, blocker or lifted canvas.");

            var page = new OverlayDim();
            var view = new OverlayDim();
            page.Show(first, UiSortingOrder.IntroDim, 0f);
            view.Show(second, UiSortingOrder.IntroDim, 0f);
            view.Hide(0.15f);
            view.Clear();
            InvokeLifecycle(screen, "LateUpdate");
            Require(Near(group.alpha, 0.72f), "Content page handoff lost its background hold.");
            view.Show(second, UiSortingOrder.IntroDim, 0f);
            page.Clear();
            Require(Near(group.alpha, 0.72f), "Final page lost its own dim when handoff released.");
            view.Clear();
            Require(group.alpha == 0f, "Forced view cancellation left a request.");
            view.Show(second, UiSortingOrder.RewardDim, 0f);
            view.Hide(0.15f);
            view.Clear();
            InvokeLifecycle(screen, "LateUpdate");
            Require(group.alpha == 0f && !group.blocksRaycasts, "Forced exit did not cancel pending fade.");
            view.Show(second, UiSortingOrder.RewardDim, 0f);
            view.Hide(0.15f);
            InvokeLifecycle(screen, "LateUpdate");
            DOTween.Goto(group, 0.05f, false);
            view.Clear();
            Require(group.alpha == 0f && !group.blocksRaycasts, "Forced exit did not cancel active fade.");
            view.Show(second, UiSortingOrder.RewardDim, 0f);
            view.Hide(0.15f);
            page.Show(first, UiSortingOrder.IntroDim, 0f);
            view.Clear();
            Require(Near(group.alpha, 0.72f) && canvas.sortingOrder == 149,
                "Old exit cleanup canceled successor's dim.");
            page.Clear();

            ScreenDim.Show(first, 0.5f, false);
            var modal = ScreenDim.Acquire(second, Options(1f, UiSortingOrder.IntroDim));
            modal.Release();
            Require(Near(group.alpha, 0.5f) && !group.blocksRaycasts && !canvas.overrideSorting,
                "Managed release did not restore legacy behavior.");
            ScreenDim.ShowWithHole(first, new Rect(100f, 100f, 200f, 200f));
            Require(!image.gameObject.activeSelf, "Legacy hole rendering changed.");
            ScreenDim.Hide(first);

            ScreenDim.Acquire(first, Options(1f, UiSortingOrder.IntroDim));
            InvokeLifecycle(screen, "OnDisable");
            Require(group.alpha == 0f && !group.blocksRaycasts, "Disabled dim retained requests.");
            Debug.Log("[ScreenDimValidation] PASS: prefab wiring, layering, color ownership, stale handles, handoff, fade reentry, page hold, legacy/hole and forced cleanup.");
        }
        finally
        {
            if (root != null)
            {
                var screen = root.GetComponentInChildren<ScreenDim>(true);
                if (screen != null) InvokeLifecycle(screen, "OnDestroy");
                UnityEngine.Object.DestroyImmediate(root);
            }
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    internal static void InvokeLifecycle(ScreenDim screen, string method)
        => typeof(ScreenDim).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(screen, null);

    static ScreenDim.Options Options(float alpha, int order, float fade = 0f)
        => new ScreenDim.Options(alpha, Color.black, true, fade, order);

    static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.001f;

    static void ValidatePrefabs()
    {
        var host = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/UI/LobbyUI/LobbyCanvas.prefab");
        Require(host != null, "Lobby canvas prefab missing.");
        ScreenDim full = null;
        foreach (var candidate in host.GetComponentsInChildren<ScreenDim>(true))
        {
            if (new SerializedObject(candidate).FindProperty("layer").enumValueIndex != (int)EDimLayer.Full) continue;
            Require(full == null, "Lobby must author exactly one Full dim.");
            full = candidate;
        }
        Require(full != null, "Lobby Full dim missing.");
        var data = new SerializedObject(full);
        Require(data.FindProperty("layer").enumValueIndex == 0
            && data.FindProperty("sortingCanvas").objectReferenceValue == full.GetComponent<Canvas>()
            && full.GetComponent<Canvas>() != null && full.GetComponent<GraphicRaycaster>() != null,
            "Lobby Full dim sorting/raycast wiring missing.");
        string[] names = { "CardRewardOverlay", "CardSetRewardOverlay", "PackRewardOverlay",
            "ContentUnlockIntroView", "UnlockIntroOverlay" };
        foreach (string name in names)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Assets/Prefabs/UI/PooledUI/{name}.prefab");
            Require(prefab != null, name + " pooled prefab missing.");
            var dim = prefab.transform.Find("Contents/PopupDim").GetComponent<Image>();
            Require(dim.color.a == 0f && dim.enabled && dim.raycastTarget && dim.GetComponent<Button>() == null,
                name + " must keep a transparent non-confirming input blocker.");
            var view = prefab.GetComponent<PooledOverlay>();
            Require(view != null && view.contents != null && view.contents.transform.parent == view.transform,
                name + " must author a direct Contents child for pooled initialization.");
            var settings = new SerializedObject(view).FindProperty("dim");
            Require(settings != null && Near(settings.FindPropertyRelative("alpha").floatValue, 0.72f),
                name + " must preserve authored dim opacity.");
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("[ScreenDimValidation] " + message);
    }
}
#endif
