using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;

public static class MainMenuSetup
{
    const string MENU_SCENE_PATH   = "Assets/Scenes/MainMenu.unity";
    const string BATTLE_SCENE_PATH = "Assets/Scenes/BattleScene.unity";

    [MenuItem("Tools/Setup Main Menu")]
    public static void SetupMainMenu()
    {
        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        var t_scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── Camera ───────────────────────────────────────────────────────────
        GameObject t_camGO = new GameObject("Main Camera");
        Camera t_cam = t_camGO.AddComponent<Camera>();
        t_cam.tag = "MainCamera";
        t_cam.orthographic = true;
        t_cam.orthographicSize = 5f;
        t_cam.backgroundColor = new Color(0.08f, 0.08f, 0.12f, 1f);
        t_cam.clearFlags = CameraClearFlags.SolidColor;
        t_camGO.AddComponent<AudioListener>();

        // ── Canvas ───────────────────────────────────────────────────────────
        GameObject t_canvasGO = new GameObject("Canvas");
        Canvas t_canvas = t_canvasGO.AddComponent<Canvas>();
        t_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler t_scaler = t_canvasGO.AddComponent<CanvasScaler>();
        t_scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        t_scaler.referenceResolution = new Vector2(1080, 1920);
        t_scaler.matchWidthOrHeight = 1f;
        t_canvasGO.AddComponent<GraphicRaycaster>();

        GameObject t_esGO = new GameObject("EventSystem");
        t_esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
        t_esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        TMP_FontAsset t_font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/MalgunGothic_TMP.asset");
        Transform t_cv = t_canvasGO.transform;

        // ── UIManager (DontDestroyOnLoad) ────────────────────────────────────
        GameObject t_uiMgrGO = new GameObject("UIManager");
        UIPoolManager t_uiMgr = t_uiMgrGO.AddComponent<UIPoolManager>();
        SerializedObject t_uiMgrSO = new SerializedObject(t_uiMgr);
        t_uiMgrSO.FindProperty("canvas").objectReferenceValue  = t_canvas;
        t_uiMgrSO.FindProperty("uiRoot").objectReferenceValue  = t_canvasGO.transform;
        t_uiMgrSO.ApplyModifiedProperties();

        // ── Manager ──────────────────────────────────────────────────────────
        GameObject t_mgrGO = new GameObject("MainMenuManager");
        MainMenuManager t_mgr = t_mgrGO.AddComponent<MainMenuManager>();

        // ════════════════════════════════════════════════════════════════════
        // MainPanel
        // ════════════════════════════════════════════════════════════════════
        GameObject t_mainPanel = MakePanel(t_cv, "MainPanel");

        // Title
        MakeLabel(t_mainPanel.transform, "Title", "버거몬스터",
            new Vector2(0f, 400f), new Vector2(900f, 200f),
            110f, FontStyles.Bold, new Color(1f, 0.85f, 0.2f, 1f), t_font);

        // Subtitle
        MakeLabel(t_mainPanel.transform, "Subtitle", "카드 배틀 게임",
            new Vector2(0f, 260f), new Vector2(800f, 80f),
            48f, FontStyles.Normal, new Color(0.8f, 0.8f, 0.8f, 1f), t_font);

        // Start button
        Button t_startBtn = MakeButton(t_mainPanel.transform, "StartButton", "게임 시작",
            new Vector2(0f, 0f), new Vector2(480f, 120f),
            new Color(0.85f, 0.25f, 0.15f, 1f), 56f, t_font);
        UnityEventTools.AddVoidPersistentListener(t_startBtn.onClick, t_mgr.OnStartPressed);

        // Deck button
        Button t_deckBtn = MakeButton(t_mainPanel.transform, "DeckButton", "덱 구성",
            new Vector2(0f, -160f), new Vector2(480f, 120f),
            new Color(0.20f, 0.40f, 0.70f, 1f), 52f, t_font);

        // ════════════════════════════════════════════════════════════════════
        // DeckPanel
        // ════════════════════════════════════════════════════════════════════
        GameObject t_deckPanel = MakePanel(t_cv, "DeckPanel");
        t_deckPanel.SetActive(false);
        DeckBuilderUI t_deckUI     = t_deckPanel.AddComponent<DeckBuilderUI>();

        // Header: back button + title
        MakeLabel(t_deckPanel.transform, "DeckTitle", "덱 구성",
            new Vector2(-100f, 860f), new Vector2(600f, 80f),
            52f, FontStyles.Bold, Color.white, t_font);

        Button t_backBtn = MakeButton(t_deckPanel.transform, "BackButton", "뒤로",
            new Vector2(440f, 860f), new Vector2(160f, 70f),
            new Color(0.35f, 0.35f, 0.35f, 1f), 34f, t_font);
        UnityEventTools.AddVoidPersistentListener(t_backBtn.onClick, t_mgr.OnBackPressed);

        // Collection section
        MakeLabel(t_deckPanel.transform, "CollectionTitle", "수집품",
            new Vector2(0f, 740f), new Vector2(900f, 60f),
            38f, FontStyles.Bold, new Color(0.9f, 0.85f, 0.5f, 1f), t_font);

        // Collection grid
        GameObject t_gridGO = new GameObject("CollectionGrid");
        t_gridGO.transform.SetParent(t_deckPanel.transform, false);
        RectTransform t_gridRT = t_gridGO.AddComponent<RectTransform>();
        t_gridRT.anchoredPosition = new Vector2(0f, 530f);
        t_gridRT.sizeDelta = new Vector2(960f, 380f);
        GridLayoutGroup t_grid = t_gridGO.AddComponent<GridLayoutGroup>();
        t_grid.cellSize        = new Vector2(460f, 88f);
        t_grid.spacing         = new Vector2(20f, 16f);
        t_grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        t_grid.constraintCount = 2;
        t_grid.childAlignment  = TextAnchor.UpperCenter;
        t_grid.padding         = new RectOffset(10, 10, 10, 10);

        // Divider
        GameObject t_divGO = new GameObject("Divider");
        t_divGO.transform.SetParent(t_deckPanel.transform, false);
        RectTransform t_divRT = t_divGO.AddComponent<RectTransform>();
        t_divRT.anchoredPosition = new Vector2(0f, 280f);
        t_divRT.sizeDelta = new Vector2(900f, 3f);
        Image t_divImg = t_divGO.AddComponent<Image>();
        t_divImg.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        // Deck slots section
        MakeLabel(t_deckPanel.transform, "SlotsTitle", "내 덱 (6장)",
            new Vector2(0f, 240f), new Vector2(900f, 60f),
            38f, FontStyles.Bold, new Color(0.9f, 0.85f, 0.5f, 1f), t_font);

        var t_slotBgs     = new Image[6];
        var t_slotLabels  = new TMP_Text[6];
        var t_slotButtons = new Button[6];

        for (int i = 0; i < 6; i++)
        {
            float t_y = 160f - i * 96f;
            (t_slotBgs[i], t_slotLabels[i], t_slotButtons[i]) =
                MakeSlot(t_deckPanel.transform, i, new Vector2(0f, t_y), t_font);
        }

        // ── Wire DeckBuilderUI ────────────────────────────────────────────────
        var t_cardAssets = LoadAllCardData();

        SerializedObject t_deckSO = new SerializedObject(t_deckUI);

        SerializedProperty t_allCards = t_deckSO.FindProperty("allCards");
        t_allCards.arraySize = t_cardAssets.Count;
        for (int i = 0; i < t_cardAssets.Count; i++)
            t_allCards.GetArrayElementAtIndex(i).objectReferenceValue = t_cardAssets[i];

        t_deckSO.FindProperty("collectionGrid").objectReferenceValue = t_gridGO.transform;

        SetObjectArray(t_deckSO, "slotBgs",     t_slotBgs);
        SetObjectArray(t_deckSO, "slotLabels",  t_slotLabels);
        SetObjectArray(t_deckSO, "slotButtons", t_slotButtons);
        t_deckSO.ApplyModifiedProperties();


        SerializedObject t_mgrSO = new SerializedObject(t_mgr);
        t_mgrSO.ApplyModifiedProperties();

        // Wire deck button after manager panels are set
        UnityEventTools.AddVoidPersistentListener(t_deckBtn.onClick, t_mgr.OnDeckPressed);

        // ── Build Settings ────────────────────────────────────────────────────
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(t_scene, MENU_SCENE_PATH);

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(MENU_SCENE_PATH,   true),
            new EditorBuildSettingsScene(BATTLE_SCENE_PATH, true),
        };

        Debug.Log("[MainMenuSetup] MainMenu + DeckPanel 생성 완료.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static GameObject MakePanel(Transform _parent, string _name)
    {
        GameObject t_go = new GameObject(_name);
        t_go.transform.SetParent(_parent, false);
        RectTransform t_rt = t_go.AddComponent<RectTransform>();
        t_rt.anchorMin = Vector2.zero;
        t_rt.anchorMax = Vector2.one;
        t_rt.offsetMin = t_rt.offsetMax = Vector2.zero;
        return t_go;
    }

    static TMP_Text MakeLabel(Transform _parent, string _name, string _text,
        Vector2 _pos, Vector2 _size, float _fontSize, FontStyles _style, Color _color,
        TMP_FontAsset _font)
    {
        GameObject t_go = new GameObject(_name);
        t_go.transform.SetParent(_parent, false);
        RectTransform t_rt = t_go.AddComponent<RectTransform>();
        t_rt.anchoredPosition = _pos;
        t_rt.sizeDelta = _size;
        TMP_Text t_txt = t_go.AddComponent<TextMeshProUGUI>();
        if (_font != null) t_txt.font = _font;
        t_txt.text      = _text;
        t_txt.fontSize  = _fontSize;
        t_txt.fontStyle = _style;
        t_txt.alignment = TextAlignmentOptions.Center;
        t_txt.color     = _color;
        return t_txt;
    }

    static Button MakeButton(Transform _parent, string _name, string _label,
        Vector2 _pos, Vector2 _size, Color _bgColor, float _fontSize, TMP_FontAsset _font)
    {
        GameObject t_go = new GameObject(_name);
        t_go.transform.SetParent(_parent, false);
        RectTransform t_rt = t_go.AddComponent<RectTransform>();
        t_rt.anchoredPosition = _pos;
        t_rt.sizeDelta = _size;
        Image t_img = t_go.AddComponent<Image>();
        t_img.color = _bgColor;
        Button t_btn = t_go.AddComponent<Button>();

        GameObject t_labelGO = new GameObject("Label");
        t_labelGO.transform.SetParent(t_go.transform, false);
        RectTransform t_lrt = t_labelGO.AddComponent<RectTransform>();
        t_lrt.anchorMin = Vector2.zero;
        t_lrt.anchorMax = Vector2.one;
        t_lrt.offsetMin = t_lrt.offsetMax = Vector2.zero;
        TMP_Text t_txt = t_labelGO.AddComponent<TextMeshProUGUI>();
        if (_font != null) t_txt.font = _font;
        t_txt.text      = _label;
        t_txt.fontSize  = _fontSize;
        t_txt.fontStyle = FontStyles.Bold;
        t_txt.alignment = TextAlignmentOptions.Center;
        t_txt.color     = Color.white;
        return t_btn;
    }

    static (Image bg, TMP_Text label, Button btn) MakeSlot(
        Transform _parent, int _index, Vector2 _pos, TMP_FontAsset _font)
    {
        GameObject t_go = new GameObject("Slot_" + _index);
        t_go.transform.SetParent(_parent, false);
        RectTransform t_rt = t_go.AddComponent<RectTransform>();
        t_rt.anchoredPosition = _pos;
        t_rt.sizeDelta = new Vector2(880f, 80f);
        Image t_bg = t_go.AddComponent<Image>();
        t_bg.color = new Color(0.18f, 0.18f, 0.18f, 0.9f);
        Button t_btn = t_go.AddComponent<Button>();

        // Index label (left)
        GameObject t_idxGO = new GameObject("Index");
        t_idxGO.transform.SetParent(t_go.transform, false);
        RectTransform t_idxRT = t_idxGO.AddComponent<RectTransform>();
        t_idxRT.anchorMin = new Vector2(0f, 0f);
        t_idxRT.anchorMax = new Vector2(0f, 1f);
        t_idxRT.offsetMin = new Vector2(12f, 0f);
        t_idxRT.offsetMax = new Vector2(60f, 0f);
        TMP_Text t_idxTxt = t_idxGO.AddComponent<TextMeshProUGUI>();
        if (_font != null) t_idxTxt.font = _font;
        t_idxTxt.text      = (_index + 1).ToString();
        t_idxTxt.fontSize  = 28f;
        t_idxTxt.color     = new Color(0.6f, 0.6f, 0.6f, 1f);
        t_idxTxt.alignment = TextAlignmentOptions.Midline;

        // Card name label (center)
        GameObject t_nameGO = new GameObject("Name");
        t_nameGO.transform.SetParent(t_go.transform, false);
        RectTransform t_nameRT = t_nameGO.AddComponent<RectTransform>();
        t_nameRT.anchorMin = new Vector2(0f, 0f);
        t_nameRT.anchorMax = new Vector2(1f, 1f);
        t_nameRT.offsetMin = new Vector2(70f, 0f);
        t_nameRT.offsetMax = new Vector2(-16f, 0f);
        TMP_Text t_nameTxt = t_nameGO.AddComponent<TextMeshProUGUI>();
        if (_font != null) t_nameTxt.font = _font;
        t_nameTxt.text      = "빈 슬롯";
        t_nameTxt.fontSize  = 30f;
        t_nameTxt.color     = new Color(0.5f, 0.5f, 0.5f, 1f);
        t_nameTxt.alignment = TextAlignmentOptions.MidlineLeft;
        t_nameTxt.enableWordWrapping = false;

        return (t_bg, t_nameTxt, t_btn);
    }

    static List<CardData> LoadAllCardData()
    {
        var t_list = new List<CardData>();
        string[] t_guids = AssetDatabase.FindAssets("t:CardData", new[] { "Assets/Cards" });
        foreach (var t_guid in t_guids)
        {
            var t_card = AssetDatabase.LoadAssetAtPath<CardData>(AssetDatabase.GUIDToAssetPath(t_guid));
            if (t_card != null) t_list.Add(t_card);
        }
        return t_list;
    }

    static void SetObjectArray<T>(SerializedObject _so, string _propName, T[] _items)
        where T : Object
    {
        SerializedProperty t_prop = _so.FindProperty(_propName);
        t_prop.arraySize = _items.Length;
        for (int i = 0; i < _items.Length; i++)
            t_prop.GetArrayElementAtIndex(i).objectReferenceValue = _items[i];
    }
}
