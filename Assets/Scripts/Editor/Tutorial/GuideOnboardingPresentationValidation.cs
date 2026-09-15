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
        Require(!EditorApplication.isPlaying && !UnlockIntroOverlay.IsOpen && !ScreenDim.IsAvailable
            && OutgameTutorialGateUI.Instance == null && UIPoolManager.instance == null,
            "Close active UI and stop play mode before validation.");
        var t_scene = EditorSceneManager.NewPreviewScene();
        ScreenDim t_dim = null;
        UnlockIntroOverlay t_overlay = null;
        OutgameTutorialGateUI t_gate = null;
        UIPoolManager t_pool = null;
        try
        {
            var t_poolRoot = new GameObject("GuideValidationPool", typeof(RectTransform), typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(t_poolRoot, t_scene);
            t_pool = t_poolRoot.AddComponent<UIPoolManager>();
            // PreviewScene은 Awake를 호출하지 않아 영속 씬으로 이동하지 않고 등록만 재현한다.
            UIPoolManager.instance = t_pool;
            var t_gatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/UI/Tutorial/OutgameTutorialGate.prefab");
            var t_gateRoot = (GameObject)PrefabUtility.InstantiatePrefab(t_gatePrefab, t_scene);
            t_gate = t_gateRoot.GetComponent<OutgameTutorialGateUI>();
            typeof(OutgameTutorialGateUI).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(t_gate, null);
            var t_dimPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/UI/Common/ScreenDim.prefab");
            var t_dimRoot = (GameObject)PrefabUtility.InstantiatePrefab(t_dimPrefab, t_scene);
            t_dim = t_dimRoot.GetComponent<ScreenDim>();
            var t_dimSo = new SerializedObject(t_dim);
            t_dimSo.FindProperty("sortingCanvas").objectReferenceValue = t_dimRoot.AddComponent<Canvas>();
            t_dimSo.ApplyModifiedPropertiesWithoutUndo();
            typeof(ScreenDim).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(t_dim, null);
            var t_prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Assets/Prefabs/UI/PooledUI/UnlockIntroOverlay.prefab");
            var t_root = (GameObject)PrefabUtility.InstantiatePrefab(t_prefab, t_scene);
            t_root.transform.SetParent(t_poolRoot.transform, false);
            t_overlay = t_root.GetComponent<UnlockIntroOverlay>();
            t_overlay.InitializeUI();
            UiSortingOrder.LiftNested(t_root, UiSortingOrder.Intro);
            t_pool.RegisterUI(t_overlay);
            var t_serialized = new SerializedObject(t_overlay);
            var t_button = (Button)t_serialized.FindProperty("confirmButton").objectReferenceValue;
            Require(t_button != null && t_serialized.FindProperty("rowRoot").objectReferenceValue != null, "Intro controls are not authored.");
            Require(t_serialized.FindProperty("pageRoot") == null, "Retired page controls remain.");
            int t_confirmed = 0, t_cancelled = 0;
            var t_intros = new[] { default(UnlockIntro) };
            System.Action<bool> t_result = _done => { if (_done) t_confirmed++; else t_cancelled++; };
            t_overlay.Show(t_intros, 0, t_result, "시너지 안내 검증");
            Require(UnlockIntroOverlay.IsOpen && UnlockIntroOverlay.IsGuidanceShowing && OutgameTutorialGateUI.IsShowing
                && t_confirmed == 0 && t_cancelled == 0,
                $"Intro and guidance must appear together: open={UnlockIntroOverlay.IsOpen}, guidance={UnlockIntroOverlay.IsGuidanceShowing}, gate={OutgameTutorialGateUI.IsShowing}, confirmed={t_confirmed}, cancelled={t_cancelled}.");
            t_button.interactable = true;
            t_button.onClick.Invoke();
            Require(!UnlockIntroOverlay.IsOpen && t_confirmed == 1 && t_cancelled == 0, "Final confirmation must complete exactly once.");
            t_button.onClick.Invoke();
            Require(t_confirmed == 1, "Repeated click completed twice.");
            Require(!OutgameTutorialGateUI.IsShowing, "Confirmed intro left guidance visible.");
            t_overlay.Show(t_intros, 0, t_result, "취소 안내 검증");
            t_overlay.Cancel();
            t_overlay.Cancel();
            Require(t_confirmed == 1 && t_cancelled == 1, "Cancellation must be distinct and idempotent.");
            Require(!OutgameTutorialGateUI.IsShowing, "Cancelled intro left guidance visible.");
            t_overlay.Show(t_intros, 0, t_result, "비활성 안내 검증");
            typeof(ContentsPooledUI).GetMethod("NotifyContentsVisibility", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(t_overlay, new object[] { true });
            t_root.SetActive(false);
            // EditMode는 MonoBehaviour 수명 콜백을 보내지 않으므로 실제 OnDisable 경로를 직접 호출한다.
            typeof(ContentsPooledUI).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(t_overlay, null);
            Require(t_confirmed == 1 && t_cancelled == 2 && !UnlockIntroOverlay.IsOpen, "Parent deactivation must cancel without completion.");
            ValidateMissionRow(t_scene);
            Require(!OutgameTutorialGateUI.IsShowing, "Disabled intro left guidance visible.");
            int t_multiConfirmed = 0;
            t_root.SetActive(true);
            t_overlay.Show(new[] { default(UnlockIntro), default(UnlockIntro) }, 0,
                _done => { if (_done) t_multiConfirmed++; });
            Require(!UnlockIntroOverlay.IsGuidanceShowing && !OutgameTutorialGateUI.IsShowing,
                "Replay without an introduction message must not show guidance.");
            t_button.interactable = true;
            t_button.onClick.Invoke();
            Require(UnlockIntroOverlay.IsOpen && t_multiConfirmed == 0, "Another unlocked ability was skipped.");
            t_button.interactable = true;
            t_button.onClick.Invoke();
            Require(!UnlockIntroOverlay.IsOpen && t_multiConfirmed == 1, "All abilities must complete once.");
            Debug.Log("[GuideOnboardingPresentation] PASS: concurrent guidance, confirm, repeated click, cancellation, simulated OnDisable, mission row reuse.");
        }
        finally
        {
            if (t_overlay != null) t_overlay.Cancel();
            if (t_pool != null && t_overlay != null) t_pool.UnregisterUI(t_overlay);
            if (t_gate != null) typeof(OutgameTutorialGateUI).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(t_gate, null);
            if (t_dim != null) typeof(ScreenDim).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(t_dim, null);
            EditorSceneManager.ClosePreviewScene(t_scene);
            if (UIPoolManager.instance == t_pool) UIPoolManager.instance = null;
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
