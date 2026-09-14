using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>가이드 해금 설명의 페이지와 세 카드 미리보기를 저작한다.</summary>
public static class GuideOnboardingAuthoring
{
    const string OVERLAY_PATH = "Assets/Assets/Prefabs/UI/OverlayUI/UnlockIntroOverlay.prefab";
    const string CARD_PATH = "Assets/Assets/Prefabs/UI/Battle/CardView/CardUIView.prefab";
    const string STAGE_PATH = "Contents/SafeArea/Stage";

    /// <summary>기존 연출 행을 보존하고 안내 페이지를 추가·배선한다.</summary>
    [MenuItem("Tools/Tutorial/Author Guide Intro Pages")]
    public static void Author()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(OVERLAY_PATH);
        var contract = new SerializedObject(asset.GetComponent<UnlockIntroOverlay>());
        foreach (var field in new[] { "pageRoot", "pageTitle", "pageBody", "pageCounter", "previewRoot", "previewCards", "previewStates", "deferButton" })
            if (contract.FindProperty(field) == null)
                throw new System.InvalidOperationException("Compile intro page runtime fields before authoring: " + field);
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            var stage = instance.transform.Find(STAGE_PATH);
            var row = stage.Find("Rows/Row0");
            var titleStyle = row.Find("Contents/TItle/AbilityName").GetComponent<TMP_Text>();
            var bodyStyle = row.Find("ExplainText/AbilityExplain").GetComponent<TMP_Text>();
            if (stage.Find("IntroPage") == null)
            {
                var page = Rect("IntroPage", stage, new Vector2(900, 1400), new Vector2(0, 40));
                page.SetSiblingIndex(stage.Find("ConfirmButton").GetSiblingIndex());
                var background = Rect("Background", page, Vector2.zero, Vector2.zero);
                background.anchorMin = Vector2.zero;
                background.anchorMax = Vector2.one;
                var image = background.gameObject.AddComponent<UnityEngine.UI.Image>();
                EditorUtility.CopySerialized(row.Find("Contents/BG").GetComponent<UnityEngine.UI.Image>(), image);
                image.raycastTarget = false;
                Label("Title", page, titleStyle, new Vector2(800, 200), new Vector2(0, 480), 66, "새로운 능력이 열렸어요");
                Label("Body", page, bodyStyle, new Vector2(790, 310), new Vector2(0, 190), 44, "카드가 성장하면 새로운 키워드가 열려요.\n키워드는 전투에서 사용하는 특별한 능력이에요.");
                var preview = Rect("Preview", page, new Vector2(820, 500), new Vector2(0, -250));
                var cardAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CARD_PATH);
                for (int i = 0; i < 3; i++)
                {
                    var slot = Rect("Slot" + i, preview, new Vector2(250, 450), new Vector2((i - 1) * 275, 0));
                    var card = (GameObject)PrefabUtility.InstantiatePrefab(cardAsset, slot);
                    var rect = card.GetComponent<RectTransform>();
                    rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                    rect.anchoredPosition = new Vector2(0, 35);
                    rect.sizeDelta = new Vector2(420, 558);
                    rect.localScale = Vector3.one * .58f;
                    foreach (var graphic in card.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
                    Label("State", slot, bodyStyle, new Vector2(260, 100), new Vector2(0, -195), 32, "성장 필요");
                }
                preview.gameObject.SetActive(false);
                page.gameObject.SetActive(false);
                PrefabUtility.ApplyAddedGameObject(page.gameObject, OVERLAY_PATH, InteractionMode.AutomatedAction);
            }
            if (stage.Find("PageCounter") == null)
            {
                var counter = Label("PageCounter", stage, bodyStyle, new Vector2(840, 60), Vector2.zero, 30, "1 / 3");
                counter.rectTransform.anchorMin = counter.rectTransform.anchorMax = new Vector2(.5f, 0);
                counter.rectTransform.anchoredPosition = new Vector2(0, 335);
                counter.gameObject.SetActive(false);
                PrefabUtility.ApplyAddedGameObject(counter.gameObject, OVERLAY_PATH, InteractionMode.AutomatedAction);
            }
            if (stage.Find("DeferButton") == null)
            {
                var defer = Label("DeferButton", stage, bodyStyle, new Vector2(240, 60), Vector2.zero, 30, "나중에");
                defer.rectTransform.anchorMin = defer.rectTransform.anchorMax = new Vector2(.5f, 0);
                defer.rectTransform.anchoredPosition = new Vector2(0, 70);
                defer.color = Color.white;
                defer.raycastTarget = true;
                var button = defer.gameObject.AddComponent<UnityEngine.UI.Button>();
                button.targetGraphic = defer;
                button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
                defer.gameObject.SetActive(false);
                PrefabUtility.ApplyAddedGameObject(defer.gameObject, OVERLAY_PATH, InteractionMode.AutomatedAction);
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }

        asset = AssetDatabase.LoadAssetAtPath<GameObject>(OVERLAY_PATH);
        var root = asset.transform.Find(STAGE_PATH);
        if (root.Find("IntroPage").GetSiblingIndex() > root.Find("ConfirmButton").GetSiblingIndex())
        {
            var stageOrder = new SerializedObject(root);
            stageOrder.FindProperty("m_Children").MoveArrayElement(root.Find("IntroPage").GetSiblingIndex(), root.Find("ConfirmButton").GetSiblingIndex());
            stageOrder.ApplyModifiedPropertiesWithoutUndo();
        }
        var serialized = new SerializedObject(asset.GetComponent<UnlockIntroOverlay>());
        Reference(serialized, "pageRoot", root.Find("IntroPage").gameObject);
        Reference(serialized, "pageTitle", root.Find("IntroPage/Title").GetComponent<TMP_Text>());
        Reference(serialized, "pageBody", root.Find("IntroPage/Body").GetComponent<TMP_Text>());
        Reference(serialized, "pageCounter", root.Find("PageCounter").GetComponent<TMP_Text>());
        Reference(serialized, "deferButton", root.Find("DeferButton").GetComponent<UnityEngine.UI.Button>());
        Reference(serialized, "previewRoot", root.Find("IntroPage/Preview"));
        var cards = serialized.FindProperty("previewCards");
        var states = serialized.FindProperty("previewStates");
        if (cards == null || states == null) throw new System.InvalidOperationException("Compile intro page runtime fields before authoring.");
        cards.arraySize = states.arraySize = 3;
        for (int i = 0; i < 3; i++)
        {
            var slot = root.Find("IntroPage/Preview/Slot" + i);
            cards.GetArrayElementAtIndex(i).objectReferenceValue = slot.GetComponentInChildren<CardVisualView>(true);
            states.GetArrayElementAtIndex(i).objectReferenceValue = slot.Find("State").GetComponent<TMP_Text>();
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssetIfDirty(asset);
    }

    static void Reference(SerializedObject target, string field, Object value)
    {
        var property = target.FindProperty(field);
        if (property == null) throw new System.InvalidOperationException("Missing intro field: " + field);
        property.objectReferenceValue = value;
    }

    static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    static TMP_Text Label(string name, Transform parent, TMP_Text style, Vector2 size, Vector2 position, float fontSize, string text)
    {
        var rect = Rect(name, parent, size, position);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        EditorUtility.CopySerialized(style, label);
        label.text = text;
        label.fontSize = fontSize;
        label.enableAutoSizing = true;
        label.fontSizeMin = fontSize - 6;
        label.fontSizeMax = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }
}
