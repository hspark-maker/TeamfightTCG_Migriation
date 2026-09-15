using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>공용 화면의 풀 배선·재사용·개폐를 실제 프리팹으로 검사한다. 저장/서버 요청 없음.</summary>
public static class PooledOverlayValidation
{
    [MenuItem("Tools/UI/Validate Common Overlay Pool")]
    public static void Run()
    {
        Require(!EditorApplication.isPlaying, "Run outside play mode.");
        var previousPool = UIPoolManager.instance;
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            root = new GameObject("OverlayPoolValidation", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(root, scene);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = UiSortingOrder.Pool;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0;
            var pool = root.AddComponent<UIPoolManager>();
            var serialized = new SerializedObject(pool);
            serialized.FindProperty("canvas").objectReferenceValue = canvas;
            serialized.FindProperty("uiRoot").objectReferenceValue = root.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            UIPoolManager.instance = pool;

            Type[] types = { typeof(CardDetailOverlayView), typeof(PackOpenOverlay), typeof(RewardClaimPopup),
                typeof(CardRewardOverlay), typeof(CardSetRewardOverlay), typeof(PackRewardOverlay),
                typeof(RankPromoteOverlay), typeof(UnlockIntroOverlay), typeof(ContentUnlockIntroView) };
            foreach (Type type in types)
            {
                var create = typeof(UIPoolManager).GetMethod(nameof(UIPoolManager.GetOrCreateUI)).MakeGenericMethod(type);
                var ui = (PooledOverlay)create.Invoke(pool, null);
                Require(ui != null && ui.IsUIInitialized, type.Name + " did not initialize.");
                Require(ui.contents != null && ui.contents.transform.parent == ui.transform,
                    type.Name + " lost its direct Contents reference.");
                Require(!ui.contents.activeSelf && !ui.isShow, type.Name + " opened before receiving data.");
                Require(ReferenceEquals(ui, create.Invoke(pool, null)), type.Name + " created a duplicate.");
                Require(ui.transform.IsChildOf(root.transform), type.Name + " escaped the pool.");
                Require(ui.SourceSceneHandle == SceneManager.GetActiveScene().handle, type.Name + " lost scene ownership.");
                var ownCanvas = ui.GetComponent<Canvas>();
                Require(ownCanvas != null && ownCanvas.overrideSorting && ui.GetComponent<GraphicRaycaster>() != null,
                    type.Name + " lost independent drawing/input order.");
                if (ui is CardDetailOverlayView)
                    Require(ui.transform.parent.GetComponent<SafeAreaFitter>() != null
                        && ownCanvas.sortingOrder == UiSortingOrder.CardDetail, "Card detail lost SafeArea/layer.");
                if (ui is RankPromoteOverlay)
                    Require(ui.GetComponent<PooledCanvasScale>() != null, "Rank intro lost authored scaling.");
            }

            ValidatePackLayout(pool.GetUI<PackOpenOverlay>(), (RectTransform)root.transform);

            int confirmed = 0, cancelled = 0;
            var intro = pool.GetUI<ContentUnlockIntroView>();
            Action show = () => intro.Show("Pool validation", "", Array.Empty<Sprite>(), () => confirmed++, () => cancelled++);
            pool.AddOrUpdateUI<ContentUnlockIntroView>(new UIData { showCustomMethod = show });
            Require(intro.isShow && ContentUnlockIntroView.IsOpen && pool.HasVisibleUIExcept(), "Pool show did not expose the intro.");
            pool.HideUI<ContentUnlockIntroView>();
            pool.HideUI<ContentUnlockIntroView>();
            Require(cancelled == 1 && confirmed == 0 && !intro.isShow && !ContentUnlockIntroView.IsOpen,
                "Pool hide must cancel once without confirming.");
            pool.AddOrUpdateUI<ContentUnlockIntroView>(new UIData { showCustomMethod = show });
            Require(ReferenceEquals(intro, pool.GetUI<ContentUnlockIntroView>()) && intro.isShow, "Reopening did not reuse the intro.");
            pool.HideUI<ContentUnlockIntroView>();
            Require(cancelled == 2 && !pool.HasVisibleUIExcept(), "Reopened intro left visibility or callback state behind.");
            Debug.Log("[PooledOverlayValidation] PASS: 9 prefabs, hidden initialization, type reuse, scene ownership, layers, SafeArea, rank scaling, pack background/input coverage, show/hide/reopen and single cancellation.");
        }
        finally
        {
            if (root != null)
            {
                foreach (var ui in root.GetComponentsInChildren<PooledOverlay>(true))
                    ui.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(ui, null);
                UnityEngine.Object.DestroyImmediate(root);
            }
            UIPoolManager.instance = previousPool;
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static void ValidatePackLayout(PackOpenOverlay pack, RectTransform poolRect)
    {
        // A Transform between the pool and the nested canvas collapses stretch backgrounds to zero.
        pack.contents.SetActive(true);
        try
        {
            Canvas.ForceUpdateCanvases();
            var canvas = pack.transform.Find("Contents/Content/UICanvas").GetComponent<Canvas>();
            var backdrop = canvas.transform.Find("Backdrop").GetComponent<Image>();
            var background = canvas.transform.Find("SafeArea/BG").GetComponent<Image>();
            RequireSameBounds(poolRect, (RectTransform)canvas.transform, "Pack canvas");
            RequireSameBounds(poolRect, backdrop.rectTransform, "Pack backdrop");
            Require(background.rectTransform.rect.width > 0 && background.rectTransform.rect.height > 0
                && background.isActiveAndEnabled && background.color.a > 0, "Pack background collapsed or hidden.");
            Require(backdrop.raycastTarget && canvas.GetComponent<GraphicRaycaster>() != null,
                "Pack backdrop cannot block lobby input.");
            foreach (Vector2 point in new[] { new Vector2(0.05f, 0.1f), new Vector2(0.5f, 0.5f), new Vector2(0.95f, 0.9f) })
            {
                Vector2 screenPoint = new Vector2(Screen.width * point.x, Screen.height * point.y);
                Require(RectTransformUtility.RectangleContainsScreenPoint(backdrop.rectTransform, screenPoint)
                    && backdrop.Raycast(screenPoint, null), "Pack backdrop has a hole in its input coverage.");
            }
            var input = pack.GetComponentInChildren<PackCardStack>(true).GetComponent<RectTransform>();
            Require(input.rect.width > 0 && input.rect.height > 0, "Pack card input collapsed.");
        }
        finally { pack.contents.SetActive(false); }
    }

    static void RequireSameBounds(RectTransform expected, RectTransform actual, string label)
    {
        var expectedCorners = new Vector3[4];
        var actualCorners = new Vector3[4];
        expected.GetWorldCorners(expectedCorners);
        actual.GetWorldCorners(actualCorners);
        Require(actual.rect.width > 0 && actual.rect.height > 0, label + " collapsed.");
        for (int i = 0; i < 4; i++)
            Require(Vector3.Distance(expectedCorners[i], actualCorners[i]) < 0.1f, label + " does not cover the screen.");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
