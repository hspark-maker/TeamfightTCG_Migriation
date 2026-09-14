#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using System.Reflection;

/// <summary>실제 인트로 프리팹의 확인·취소 계약을 서버 접속 없이 검사한다.</summary>
public static class GuideOnboardingPresentationValidation
{
    [MenuItem("Tools/Tutorial/Validate Guide Intro Presentation")]
    public static void Run()
    {
        Require(!EditorApplication.isPlaying && !UnlockIntroOverlay.IsOpen && !ScreenDim.IsAvailable, "Close active UI and stop play mode before validation.");
        var t_scene = EditorSceneManager.NewPreviewScene();
        ScreenDim t_dim = null;
        UnlockIntroOverlay t_overlay = null;
        try
        {
            var t_dimPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/UI/Common/ScreenDim.prefab");
            var t_dimRoot = (GameObject)PrefabUtility.InstantiatePrefab(t_dimPrefab, t_scene);
            t_dim = t_dimRoot.GetComponent<ScreenDim>();
            var t_dimSo = new SerializedObject(t_dim);
            t_dimSo.FindProperty("sortingCanvas").objectReferenceValue = t_dimRoot.AddComponent<Canvas>();
            t_dimSo.ApplyModifiedPropertiesWithoutUndo();
            typeof(ScreenDim).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(t_dim, null);
            var t_prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/UI/OverlayUI/UnlockIntroOverlay.prefab");
            var t_root = (GameObject)PrefabUtility.InstantiatePrefab(t_prefab, t_scene);
            t_overlay = t_root.GetComponent<UnlockIntroOverlay>();
            var t_serialized = new SerializedObject(t_overlay);
            var t_button = (Button)t_serialized.FindProperty("confirmButton").objectReferenceValue;
            var t_defer = (Button)t_serialized.FindProperty("deferButton").objectReferenceValue;
            Require(t_button != null && t_serialized.FindProperty("pageRoot").objectReferenceValue != null, "Page controls are not authored.");
            Require(t_defer != null, "An optional introduction needs a defer button.");
            Require(t_serialized.FindProperty("previewCards").arraySize == 3 && t_serialized.FindProperty("previewStates").arraySize == 3, "Three preview slots are required.");
            int t_confirmed = 0, t_cancelled = 0;
            var t_pages = new[] { new UnlockIntroPage("첫 설명", "키워드 개념"), new UnlockIntroPage("두 번째 설명", "실제 능력 확인") };
            System.Action<bool> t_result = _done => { if (_done) t_confirmed++; else t_cancelled++; };
            t_overlay.ShowPages(t_pages, 0, t_result);
            Require(UnlockIntroOverlay.IsOpen && t_confirmed == 0 && t_cancelled == 0, "Showing a page must not complete it.");
            t_button.interactable = true;
            t_button.onClick.Invoke();
            Require(UnlockIntroOverlay.IsOpen && t_confirmed == 0, "An intermediate confirmation completed the introduction.");
            t_button.interactable = true;
            t_button.onClick.Invoke();
            Require(!UnlockIntroOverlay.IsOpen && t_confirmed == 1 && t_cancelled == 0, "Final confirmation must complete exactly once.");
            t_button.onClick.Invoke();
            Require(t_confirmed == 1, "Repeated click completed twice.");
            t_overlay.ShowPages(t_pages, 0, t_result);
            t_defer.onClick.Invoke();
            t_overlay.Cancel();
            Require(t_confirmed == 1 && t_cancelled == 1, "Cancellation must be distinct and idempotent.");
            t_overlay.ShowPages(t_pages, 0, t_result);
            typeof(ContentsUIBehaviour).GetMethod("NotifyContentsVisibility", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(t_overlay, new object[] { true });
            t_root.SetActive(false);
            // EditMode는 MonoBehaviour 수명 콜백을 보내지 않으므로 실제 OnDisable 경로를 직접 호출한다.
            typeof(ContentsUIBehaviour).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(t_overlay, null);
            Require(t_confirmed == 1 && t_cancelled == 2 && !UnlockIntroOverlay.IsOpen, "Parent deactivation must cancel without completion.");
            ValidateMissionRow(t_scene);
            Debug.Log("[GuideOnboardingPresentation] PASS: intermediate/final confirm, repeated click, cancellation, simulated OnDisable, mission row reuse.");
        }
        finally
        {
            if (t_overlay != null) t_overlay.Cancel();
            if (t_dim != null) typeof(ScreenDim).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(t_dim, null);
            EditorSceneManager.ClosePreviewScene(t_scene);
        }
    }

    static void ValidateMissionRow(UnityEngine.SceneManagement.Scene _scene)
    {
        var t_asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/UI/LobbyUI/MissionUI/MissionRow.prefab");
        var t_rowRoot = (GameObject)PrefabUtility.InstantiatePrefab(t_asset, _scene);
        var t_row = t_rowRoot.GetComponent<MissionRowView>();
        var t_type = typeof(MissionRowView).Assembly.GetType("MissionDefinition");
        var t_definition = Newtonsoft.Json.JsonConvert.DeserializeObject(
            "{\"id\":\"guide.06\",\"period\":\"guide\",\"event\":\"Guide.StarterCardsAtStar2\",\"target\":1,\"title\":\"돌보미 카드 3종을 2성으로 성장\"}", t_type);
        var t_bind = typeof(MissionRowView).GetMethod("Bind", BindingFlags.Instance | BindingFlags.NonPublic);
        var t_rect = t_rowRoot.GetComponent<RectTransform>();
        float t_originalHeight = t_rect.sizeDelta.y;
        t_bind.Invoke(t_row, new object[] { t_definition, null, null });
        var t_status = t_rowRoot.transform.Find("CaretakerPreparation").GetComponent<TMPro.TMP_Text>();
        t_status.ForceMeshUpdate(true, true);
        Require(t_status.text.Split('\n').Length == 3 && !t_status.isTextOverflowing, "Three card states must fit in the mission row.");
        float t_expanded = t_rect.sizeDelta.y;
        Require(t_expanded > t_originalHeight, "Preparation row was not expanded.");
        t_bind.Invoke(t_row, new object[] { t_definition, null, null });
        Require(Mathf.Approximately(t_rect.sizeDelta.y, t_expanded), "Rebinding grew the row twice.");
        t_type.GetProperty("Event").SetValue(t_definition, "Guide.EnhanceCompleted");
        t_bind.Invoke(t_row, new object[] { t_definition, null, null });
        Require(Mathf.Approximately(t_rect.sizeDelta.y, t_originalHeight) && !t_status.gameObject.activeSelf, "Reused normal row kept preparation layout.");
    }

    static void Require(bool _condition, string _message)
    {
        if (!_condition) throw new System.InvalidOperationException(_message);
    }
}
#endif
