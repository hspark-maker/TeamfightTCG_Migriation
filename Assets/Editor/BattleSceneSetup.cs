using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;

public static class BattleSceneSetup
{
    const float CARD_W       = 1.5f;
    const float CARD_H       = 2.1f;
    const float SLOT_SPACING = 1.9f;
    const float FIELD_Y      = 2.8f;

    const string BATTLE_SCENE_PATH = "Assets/Scenes/BattleScene.unity";

    [MenuItem("Tools/Setup Battle Scene")]
    public static void SetupScene()
    {
        // Always operate on BattleScene, never the currently-open scene
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != BATTLE_SCENE_PATH)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(BATTLE_SCENE_PATH) != null)
                EditorSceneManager.OpenScene(BATTLE_SCENE_PATH);
            else
            {
                var t_newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(t_newScene, BATTLE_SCENE_PATH);
            }
        }

        DestroyIfExists("GameManager");
        DestroyIfExists("PlayerBattleField");
        DestroyIfExists("EnemyBattleField");
        DestroyIfExists("PlayerFieldView");
        DestroyIfExists("EnemyFieldView");
        DestroyIfExists("Canvas");
        DestroyIfExists("EventSystem");

        // ── Camera: orthographic portrait ────────────────────────────────
        Camera t_cam = Camera.main;
        if (t_cam != null)
        {
            t_cam.orthographic = true;
            t_cam.orthographicSize = 5f;
            t_cam.transform.position = new Vector3(0f, 0f, -10f);
            t_cam.backgroundColor = new Color(0.12f, 0.14f, 0.20f, 1f);
            EditorUtility.SetDirty(t_cam);
        }

        // ── Canvas: UI only (TurnLabel, ActionPanel) ─────────────────────
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

        // ── BattleField (data layer, non-visual) ─────────────────────────
        BattleField t_pf = new GameObject("PlayerBattleField").AddComponent<BattleField>();
        BattleField t_ef = new GameObject("EnemyBattleField").AddComponent<BattleField>();

        // ── White sprite shared by all card visuals ───────────────────────
        Sprite t_white = GetOrCreateWhiteSprite();

        TMP_FontAsset t_korFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/MalgunGothic_TMP.asset");

        // ── World-space field views ───────────────────────────────────────
        BattleFieldView t_pfv = CreateFieldView("PlayerFieldView",
            new Vector3(0f, -FIELD_Y, 0f), t_pf, t_white, t_korFont).GetComponent<BattleFieldView>();
        BattleFieldView t_efv = CreateFieldView("EnemyFieldView",
            new Vector3(0f,  FIELD_Y, 0f), t_ef, t_white, t_korFont).GetComponent<BattleFieldView>();

        // ── GameManager ───────────────────────────────────────────────────
        GameObject t_gmGO = new GameObject("GameManager");
        GameInitializer t_init = t_gmGO.AddComponent<GameInitializer>();
        SerializedObject t_so = new SerializedObject(t_init);
        t_so.FindProperty("playerField").objectReferenceValue     = t_pf;
        t_so.FindProperty("enemyField").objectReferenceValue      = t_ef;
        t_so.FindProperty("playerFieldView").objectReferenceValue = t_pfv;
        t_so.FindProperty("enemyFieldView").objectReferenceValue  = t_efv;
        t_so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("[BattleSceneSetup] World-space scene ready. Run 'Setup Battle UI' next.");
    }

    // ─────────────────────────────────────────────────────────────────────
    static GameObject CreateFieldView(string _name, Vector3 _pos, BattleField _field, Sprite _white, TMP_FontAsset _font)
    {
        GameObject t_go = new GameObject(_name);
        t_go.transform.position = _pos;
        BattleFieldView t_fv = t_go.AddComponent<BattleFieldView>();

        float[] t_xs = { -SLOT_SPACING, 0f, SLOT_SPACING };
        CardView[] t_views = new CardView[BattleField.SLOT_COUNT];
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            GameObject t_slot = CreateCardSlot(t_go.transform, i,
                new Vector3(t_xs[i], 0f, 0f), _white, _font);
            t_views[i] = t_slot.GetComponent<CardView>();
        }

        SerializedObject t_so = new SerializedObject(t_fv);
        SerializedProperty t_sp = t_so.FindProperty("slotViews");
        t_sp.arraySize = BattleField.SLOT_COUNT;
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
            t_sp.GetArrayElementAtIndex(i).objectReferenceValue = t_views[i];
        t_so.FindProperty("field").objectReferenceValue = _field;
        t_so.ApplyModifiedProperties();

        return t_go;
    }

    static GameObject CreateCardSlot(Transform _parent, int _index, Vector3 _localPos, Sprite _white, TMP_FontAsset _font)
    {
        GameObject t_root = new GameObject("Slot" + _index);
        t_root.transform.SetParent(_parent, false);
        t_root.transform.localPosition = _localPos;
        BoxCollider2D t_col = t_root.AddComponent<BoxCollider2D>();
        t_col.size = new Vector2(CARD_W, CARD_H);

        // Background
        SpriteRenderer t_bgSR = MakeSpriteChild(t_root.transform, "Background", _white,
            new Color(0.93f, 0.87f, 0.72f, 1f), 0, new Vector3(CARD_W, CARD_H, 1f), Vector3.zero);

        // Illustration placeholder
        SpriteRenderer t_illSR = MakeSpriteChild(t_root.transform, "Illustration", _white,
            new Color(0.72f, 0.72f, 0.72f, 1f), 1,
            new Vector3(CARD_W * 0.8f, CARD_H * 0.32f, 1f), new Vector3(0f, 0.08f, -0.01f));

        // FaceDown overlay
        GameObject t_fdGO = MakeSpriteChild(t_root.transform, "FaceDownOverlay", _white,
            new Color(0.12f, 0.12f, 0.30f, 0.90f), 3,
            new Vector3(CARD_W, CARD_H, 1f), new Vector3(0f, 0f, -0.02f)).gameObject;
        t_fdGO.SetActive(false);

        // Empty overlay
        GameObject t_emGO = MakeSpriteChild(t_root.transform, "EmptyOverlay", _white,
            new Color(0.35f, 0.35f, 0.35f, 0.60f), 3,
            new Vector3(CARD_W, CARD_H, 1f), new Vector3(0f, 0f, -0.02f)).gameObject;

        // Highlight
        GameObject t_hlGO = MakeSpriteChild(t_root.transform, "Highlight", _white,
            new Color(1f, 0.93f, 0.1f, 0.55f), 4,
            new Vector3(CARD_W + 0.08f, CARD_H + 0.08f, 1f), new Vector3(0f, 0f, -0.03f)).gameObject;
        t_hlGO.SetActive(false);

        // Texts (TextMeshPro world-space)
        TMP_Text t_nameTmp = MakeTMPChild(t_root.transform, "NameText",
            new Vector3(0f,  0.78f, -0.04f), new Vector2(CARD_W - 0.1f, 0.30f),
            0.87f, Color.black, FontStyles.Bold, _font);
        TMP_Text t_typeTmp = MakeTMPChild(t_root.transform, "TypeText",
            new Vector3(0f,  0.52f, -0.04f), new Vector2(CARD_W - 0.1f, 0.24f),
            0.67f, new Color(0.25f, 0.25f, 0.60f), FontStyles.Normal, _font);
        TMP_Text t_hpTmp   = MakeTMPChild(t_root.transform, "HPText",
            new Vector3(0f, -0.72f, -0.04f), new Vector2(CARD_W - 0.1f, 0.34f),
            1.00f, new Color(0.80f, 0.10f, 0.10f), FontStyles.Bold, _font);

        // Wire CardView
        CardView t_cv = t_root.AddComponent<CardView>();
        SerializedObject t_so = new SerializedObject(t_cv);
        t_so.FindProperty("hpText").objectReferenceValue           = t_hpTmp;
        t_so.FindProperty("nameText").objectReferenceValue         = t_nameTmp;
        t_so.FindProperty("typeText").objectReferenceValue         = t_typeTmp;
        t_so.FindProperty("illustration").objectReferenceValue     = t_illSR;
        t_so.FindProperty("bgRenderer").objectReferenceValue       = t_bgSR;
        t_so.FindProperty("faceDownOverlay").objectReferenceValue  = t_fdGO;
        t_so.FindProperty("emptyOverlay").objectReferenceValue     = t_emGO;
        t_so.FindProperty("selectedHighlight").objectReferenceValue = t_hlGO;
        t_so.ApplyModifiedProperties();

        return t_root;
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    static SpriteRenderer MakeSpriteChild(Transform _parent, string _name, Sprite _sprite,
        Color _color, int _order, Vector3 _scale, Vector3 _localPos)
    {
        GameObject t_go = new GameObject(_name);
        t_go.transform.SetParent(_parent, false);
        t_go.transform.localPosition = _localPos;
        t_go.transform.localScale = _scale;
        SpriteRenderer t_sr = t_go.AddComponent<SpriteRenderer>();
        t_sr.sprite = _sprite;
        t_sr.color  = _color;
        t_sr.sortingOrder = _order;
        return t_sr;
    }

    static TMP_Text MakeTMPChild(Transform _parent, string _name, Vector3 _localPos,
        Vector2 _size, float _fontSize, Color _color, FontStyles _style, TMP_FontAsset _font = null)
    {
        GameObject t_go = new GameObject(_name);
        t_go.transform.SetParent(_parent, false);
        t_go.transform.localPosition = _localPos;
        TextMeshPro t_tmp = t_go.AddComponent<TextMeshPro>();
        t_tmp.GetComponent<RectTransform>().sizeDelta = _size;
        t_tmp.fontSize    = _fontSize;
        t_tmp.fontStyle   = _style;
        t_tmp.alignment   = TextAlignmentOptions.Center;
        t_tmp.color       = _color;
        t_tmp.sortingOrder = 10;
        t_tmp.enableWordWrapping = false;
        t_tmp.overflowMode = TextOverflowModes.Ellipsis;
        if (_font != null) t_tmp.font = _font;
        return t_tmp;
    }

    static Sprite GetOrCreateWhiteSprite()
    {
        const string FOLDER = "Assets/Sprites";
        const string PATH   = "Assets/Sprites/White.png";

        if (!AssetDatabase.IsValidFolder(FOLDER))
            AssetDatabase.CreateFolder("Assets", "Sprites");

        if (!System.IO.File.Exists(Application.dataPath + "/Sprites/White.png"))
        {
            Texture2D t_tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color[] t_cols = new Color[16];
            for (int i = 0; i < 16; i++) t_cols[i] = Color.white;
            t_tex.SetPixels(t_cols);
            t_tex.Apply();
            System.IO.File.WriteAllBytes(Application.dataPath + "/Sprites/White.png",
                t_tex.EncodeToPNG());
            AssetDatabase.ImportAsset(PATH);
        }

        // PPU=4 → 4px/4 = 1 world unit natural size, so scale (1.5,2.1) = 1.5×2.1 world units
        TextureImporter t_ti = AssetImporter.GetAtPath(PATH) as TextureImporter;
        if (t_ti != null && (t_ti.textureType != TextureImporterType.Sprite || (int)t_ti.spritePixelsPerUnit != 4))
        {
            t_ti.textureType = TextureImporterType.Sprite;
            t_ti.spriteImportMode = SpriteImportMode.Single;
            t_ti.spritePixelsPerUnit = 4f;
            t_ti.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(PATH);
    }

    static void DestroyIfExists(string _name)
    {
        GameObject t_go = GameObject.Find(_name);
        if (t_go != null) Object.DestroyImmediate(t_go);
    }
}
