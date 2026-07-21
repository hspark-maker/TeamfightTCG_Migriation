using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;

public static class BattleUISetup
{
    [MenuItem("Tools/Setup Battle UI")]
    public static void SetupUI()
    {
        // ── TurnManager on GameManager ───────────────────────────────────
        GameObject t_gm = GameObject.Find("GameManager");
        if (t_gm == null) { Debug.LogError("[BattleUISetup] GameManager not found. Run 'Tools/Setup Battle Scene' first."); return; }

        TurnRunner t_tm = t_gm.GetComponent<TurnRunner>();
        if (t_tm == null) t_tm = t_gm.AddComponent<TurnRunner>();

        // ── Canvas root ──────────────────────────────────────────────────
        GameObject t_canvas = GameObject.Find("Canvas");
        if (t_canvas == null) { Debug.LogError("[BattleUISetup] Canvas not found."); return; }
        Transform t_canvasTr = t_canvas.transform;

        // ── Korean font (load first so text never renders with wrong font) ─
        TMP_FontAsset t_korFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/MalgunGothic_TMP.asset");

        // Remove old ActionPanel / TurnLabel if re-running
        DestroyChild(t_canvasTr, "ActionPanel");
        DestroyChild(t_canvasTr, "TurnLabel");

        // ── TurnLabel ────────────────────────────────────────────────────
        GameObject t_labelGO = new GameObject("TurnLabel");
        t_labelGO.transform.SetParent(t_canvasTr, false);
        RectTransform t_labelRT = t_labelGO.AddComponent<RectTransform>();
        t_labelRT.anchoredPosition = new Vector2(0f, 860f);
        t_labelRT.sizeDelta = new Vector2(700f, 90f);
        TMP_Text t_label = t_labelGO.AddComponent<TextMeshProUGUI>();
        if (t_korFont != null) t_label.font = t_korFont;  // set font before text
        t_label.text = "플레이어 턴";
        t_label.fontSize = 42f;
        t_label.fontStyle = FontStyles.Bold;
        t_label.alignment = TextAlignmentOptions.Center;
        t_label.color = Color.white;

        // ── ActionPanel ──────────────────────────────────────────────────
        GameObject t_panel = new GameObject("ActionPanel");
        t_panel.transform.SetParent(t_canvasTr, false);
        RectTransform t_panelRT = t_panel.AddComponent<RectTransform>();
        t_panelRT.anchoredPosition = new Vector2(0f, -820f);
        t_panelRT.sizeDelta = new Vector2(440f, 130f);
        Image t_panelBg = t_panel.AddComponent<Image>();
        t_panelBg.color = new Color(0.1f, 0.1f, 0.1f, 0.75f);
        ActionPanel t_ap = t_panel.AddComponent<ActionPanel>();
        t_panel.SetActive(false);

        // Attack Button
        GameObject t_btnGO = new GameObject("AttackButton");
        t_btnGO.transform.SetParent(t_panel.transform, false);
        RectTransform t_btnRT = t_btnGO.AddComponent<RectTransform>();
        t_btnRT.anchoredPosition = Vector2.zero;
        t_btnRT.sizeDelta = new Vector2(360f, 90f);
        Image t_btnImg = t_btnGO.AddComponent<Image>();
        t_btnImg.color = new Color(0.85f, 0.25f, 0.15f, 1f);
        Button t_btn = t_btnGO.AddComponent<Button>();

        GameObject t_btnLabelGO = new GameObject("Label");
        t_btnLabelGO.transform.SetParent(t_btnGO.transform, false);
        RectTransform t_btnLabelRT = t_btnLabelGO.AddComponent<RectTransform>();
        t_btnLabelRT.anchorMin = Vector2.zero;
        t_btnLabelRT.anchorMax = Vector2.one;
        t_btnLabelRT.offsetMin = t_btnLabelRT.offsetMax = Vector2.zero;
        TMP_Text t_btnTxt = t_btnLabelGO.AddComponent<TextMeshProUGUI>();
        if (t_korFont != null) t_btnTxt.font = t_korFont;
        t_btnTxt.text = "공격";
        t_btnTxt.fontSize = 36f;
        t_btnTxt.fontStyle = FontStyles.Bold;
        t_btnTxt.alignment = TextAlignmentOptions.Center;
        t_btnTxt.color = Color.white;

        // ── Wire TurnRunner SerializedFields ─────────────────────────────
        BattleField t_pf  = GameObject.Find("PlayerBattleField")?.GetComponent<BattleField>();
        BattleField t_ef  = GameObject.Find("EnemyBattleField")?.GetComponent<BattleField>();
        BattleFieldView t_pfv = GameObject.Find("PlayerFieldView")?.GetComponent<BattleFieldView>();
        BattleFieldView t_efv = GameObject.Find("EnemyFieldView")?.GetComponent<BattleFieldView>();

        if (t_pf == null || t_ef == null || t_pfv == null || t_efv == null)
        {
            Debug.LogError("[BattleUISetup] Missing scene objects. Run 'Tools/Setup Battle Scene' first.");
            return;
        }

        // ── DragArrow (world-space LineRenderer) ─────────────────────────
        foreach (var t_da in Object.FindObjectsByType<DragArrow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(t_da.gameObject);
        GameObject t_arrowGO = new GameObject("DragArrow");
        t_arrowGO.AddComponent<LineRenderer>();
        DragArrow t_arrow = t_arrowGO.AddComponent<DragArrow>();

        SerializedObject t_so = new SerializedObject(t_tm);
        t_so.FindProperty("playerField").objectReferenceValue     = t_pf;
        t_so.FindProperty("enemyField").objectReferenceValue      = t_ef;
        t_so.FindProperty("playerFieldView").objectReferenceValue = t_pfv;
        t_so.FindProperty("enemyFieldView").objectReferenceValue  = t_efv;
        t_so.FindProperty("turnLabel").objectReferenceValue       = t_label;
        t_so.FindProperty("dragArrow").objectReferenceValue       = t_arrow;
        t_so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("[BattleUISetup] TurnManager + ActionPanel + TurnLabel 세팅 완료.");
    }

    static void DestroyChild(Transform _parent, string _name)
    {
        Transform t_child = _parent.Find(_name);
        if (t_child != null) Object.DestroyImmediate(t_child.gameObject);
    }
}
