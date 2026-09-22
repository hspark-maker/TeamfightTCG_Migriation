using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.UI;

/// <summary>출석 창의 공용 프레임을 재사용해 우편함 프리팹과 로비 진입 버튼을 저작한다.</summary>
public static class MailboxUiBuilder
{
    const string AttendancePath = "Assets/Assets/Prefabs/UI/PooledUI/Progress/AttendanceOverlay.prefab";
    const string MailboxPath = "Assets/Assets/Prefabs/UI/PooledUI/Progress/MailboxPanel.prefab";
    const string LobbyPath = "Assets/Assets/Prefabs/UI/LobbyUI/LobbyCanvas.prefab";
    const string MailArt = "Assets/PurchasedAssets/Layer Lab/GUI Pro-SimpleCasual/ResourcesData/Sprites/Components/Icon_ItemIcons/256/Itemicon_Mail.Png";
    const string OpenMailArt = "Assets/PurchasedAssets/Layer Lab/GUI Pro-SimpleCasual/ResourcesData/Sprites/Components/Icon_ItemIcons/256/Itemicon_Mail_Open.Png";
    static readonly Color Ink = new Color(0.31f, 0.20f, 0.12f);
    static readonly Color Muted = new Color(0.53f, 0.43f, 0.32f);
    static TMP_FontAsset font;

    [MenuItem("Tools/UI/Build Mailbox UI")]
    public static void Build()
    {
        var root = PrefabUtility.LoadPrefabContents(AttendancePath);
        try
        {
            root.SetActive(false);
            root.name = "MailboxPanel";
            Object.DestroyImmediate(root.GetComponent<AttendancePanel>());
            var contents = root.transform.Find("Contents");
            var panel = contents.Find("SafeArea/FitFrame/Design/Panel");
            var ribbon = panel.Find("TitleRibbon");
            var title = ribbon.GetComponentInChildren<TMP_Text>(true);
            font = title.font;
            title.text = "우편함";
            var close = panel.Find("CloseButton").GetComponent<Button>();
            var claimAll = panel.Find("ClaimButton").GetComponent<Button>();
            foreach (Transform child in panel.Cast<Transform>().ToArray())
                if (child != ribbon && child != close.transform && child != claimAll.transform)
                    Object.DestroyImmediate(child.gameObject);

            var view = root.AddComponent<MailboxPanel>();
            var so = new SerializedObject(view);
            Ref(so, "contents", contents.gameObject);
            so.FindProperty("transition").FindPropertyRelative("panel").objectReferenceValue = panel;
            Ref(so, "closeButton", close);
            Ref(so, "dimButton", contents.Find("Dim").GetComponent<Button>());
            Place(close.GetComponent<RectTransform>(), 92, 80, 385, 654);
            close.GetComponentInChildren<TMP_Text>(true).text = "닫기";
            close.GetComponentInChildren<TMP_Text>(true).fontSize = 25;
            FitLabel(close);
            var status = Text(panel, "StatusText", "우편을 확인하고 있어요.", 26, Muted, 840, 70, 0, -486);
            Ref(so, "statusText", status);

            var list = Rect(panel, "ListPage", 940, 1500);
            Ref(so, "listPage", list.gameObject);
            claimAll.transform.SetParent(list, false);
            Place(claimAll.GetComponent<RectTransform>(), 420, 116, 0, -645);
            claimAll.name = "ClaimAllButton";
            claimAll.GetComponentInChildren<TMP_Text>(true).text = "모두 받기";
            FitLabel(claimAll);
            Ref(so, "claimAllButton", claimAll);
            Ref(so, "claimAllLabel", claimAll.GetComponentInChildren<TMP_Text>(true));
            var refresh = SmallButton(list, "RefreshButton", "새로고침", 170, 64, 326, 549, close);
            Ref(so, "refreshButton", refresh);
            Text(list, "ListHeading", "도착한 우편", 32, Ink, 570, 64, -117, 549).alignment = TextAlignmentOptions.MidlineLeft;
            var scroll = Scroll(list, "ListScroll", 840, 920, 0, 42);
            Ref(so, "listScroll", scroll);
            Ref(so, "listContent", scroll.content);
            var row = CreateRow(scroll.content);
            row.gameObject.SetActive(false);
            Ref(so, "rowTemplate", row);
            var empty = Rect(list, "Empty", 800, 400, 0, 40);
            var emptyIcon = Picture(empty, "MailIcon", AssetDatabase.LoadAssetAtPath<Sprite>(OpenMailArt), 180, 180, 0, 66);
            emptyIcon.color = new Color(1, 1, 1, 0.6f);
            Text(empty, "Label", "도착한 우편이 없어요", 32, Muted, 760, 80, 0, -96);
            Ref(so, "emptyRoot", empty.gameObject);
            var next = SmallButton(list, "NextButton", "더 보기", 220, 68, 0, -552, close);
            Ref(so, "nextButton", next);
            Ref(so, "nextLabel", next.GetComponentInChildren<TMP_Text>(true));

            var detail = Rect(panel, "DetailPage", 940, 1500);
            Ref(so, "detailPage", detail.gameObject);
            var back = SmallButton(detail, "BackButton", "목록으로", 196, 70, -319, 549, close);
            Ref(so, "backButton", back);
            var detailScroll = Scroll(detail, "DetailScroll", 840, 920, 0, 42);
            detailScroll.content.GetComponent<VerticalLayoutGroup>().spacing = 24;
            Ref(so, "detailScroll", detailScroll);
            var detailTitle = FlowText(detailScroll.content, "Title", "우편 제목", 38, Ink, 96);
            Ref(so, "detailTitle", detailTitle);
            var time = FlowText(detailScroll.content, "Time", "", 24, Muted, 40);
            Ref(so, "detailTime", time);
            var body = FlowText(detailScroll.content, "Body", "우편 내용", 30, Ink, 160);
            body.lineSpacing = 16;
            Ref(so, "detailBody", body);
            var rewards = Rect(detailScroll.content, "Rewards", 816, 0);
            var grid = rewards.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(148, 182);
            grid.spacing = new Vector2(16, 18);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.childAlignment = TextAnchor.UpperCenter;
            var slots = so.FindProperty("rewardSlots");
            slots.arraySize = 10;
            for (int i = 0; i < 10; i++)
            {
                var slot = Rect(rewards, "Reward" + (i + 1), 148, 182);
                var bg = slot.gameObject.AddComponent<Image>();
                bg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Assets/Images/UI/Frame_02.psd");
                bg.type = Image.Type.Sliced;
                bg.raycastTarget = false;
                var icon = Picture(slot, "Icon", AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Assets/Images/UI/Item_Coin.png"), 106, 110, 0, 21);
                var amount = Text(slot, "Amount", "1,000", 25, Ink, 138, 54, 0, -55);
                amount.enableAutoSizing = true;
                amount.fontSizeMin = 18;
                amount.fontSizeMax = 25;
                var s = slots.GetArrayElementAtIndex(i);
                s.FindPropertyRelative("root").objectReferenceValue = slot.gameObject;
                s.FindPropertyRelative("icon").objectReferenceValue = icon;
                s.FindPropertyRelative("amountLabel").objectReferenceValue = amount;
                slot.gameObject.SetActive(false);
            }
            var claim = Object.Instantiate(claimAll, detail, false);
            claim.name = "ClaimButton";
            claim.GetComponentInChildren<TMP_Text>(true).text = "보상 받기";
            Ref(so, "claimButton", claim);
            Ref(so, "claimLabel", claim.GetComponentInChildren<TMP_Text>(true));
            detail.gameObject.SetActive(false);
            so.ApplyModifiedPropertiesWithoutUndo();
            contents.gameObject.SetActive(false);
            root.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(root, MailboxPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        AddLobbyEntry();
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var source = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(AttendancePath));
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(MailboxPath), source.parentGroup);
        entry.address = "MailboxPanel";
        foreach (var label in source.labels) entry.SetLabel(label, true);
        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(source.parentGroup);
        AssetDatabase.SaveAssetIfDirty(source.parentGroup);
        AssetDatabase.SaveAssetIfDirty(settings);
        Debug.Log("[MailboxUiBuilder] MailboxPanel, 로비 진입, Addressables 등록 완료 (빌드/배포 없음).");
    }

    static MailboxRowView CreateRow(Transform parent)
    {
        var row = Rect(parent, "MailRowTemplate", 816, 176);
        var bg = row.gameObject.AddComponent<Image>();
        bg.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Assets/Images/UI/Frame_02.psd");
        bg.type = Image.Type.Sliced;
        var button = row.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        var layout = row.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = 176;
        var icon = Picture(row, "MailIcon", AssetDatabase.LoadAssetAtPath<Sprite>(MailArt), 108, 108, -333, 0);
        var title = Text(row, "Title", "우편 제목", 31, Ink, 488, 54, -12, 45);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.overflowMode = TextOverflowModes.Ellipsis;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        var time = Text(row, "Time", "7일 남음", 22, Muted, 488, 34, -12, 0);
        time.alignment = TextAlignmentOptions.MidlineLeft;
        var reward = Text(row, "Reward", "첨부 보상", 23, Muted, 488, 40, -12, -42);
        reward.alignment = TextAlignmentOptions.MidlineLeft;
        var state = Text(row, "State", "받기", 24, new Color(0.26f, 0.48f, 0.16f), 112, 66, 330, 0);
        var dot = Picture(row, "ClaimDot", AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Assets/Images/UI/Item_Gift.png"), 40, 40, -286, 54);
        var view = row.gameObject.AddComponent<MailboxRowView>();
        var so = new SerializedObject(view);
        Ref(so, "openButton", button); Ref(so, "titleText", title); Ref(so, "timeText", time);
        Ref(so, "stateText", state); Ref(so, "rewardText", reward); Ref(so, "mailIcon", icon);
        Ref(so, "closedMail", icon.sprite); Ref(so, "openedMail", AssetDatabase.LoadAssetAtPath<Sprite>(OpenMailArt));
        Ref(so, "claimDot", dot.gameObject);
        so.ApplyModifiedPropertiesWithoutUndo();
        return view;
    }

    static void AddLobbyEntry()
    {
        var root = PrefabUtility.LoadPrefabContents(LobbyPath);
        try
        {
            var source = root.GetComponentsInChildren<AttendanceEntryButton>(true).Single();
            if (source.transform.parent.Find("MailboxBtn") != null) return;
            var entry = Object.Instantiate(source.gameObject, source.transform.parent, false);
            entry.SetActive(false);
            entry.name = "MailboxBtn";
            var dot = new SerializedObject(entry.GetComponent<AttendanceEntryButton>()).FindProperty("dot").objectReferenceValue;
            foreach (var component in entry.GetComponents<MonoBehaviour>())
                if (!(component is Graphic) && !(component is Selectable) && !(component is UIClickSound))
                    Object.DestroyImmediate(component);
            ((RectTransform)entry.transform).anchoredPosition = new Vector2(124, -528.5f);
            foreach (var text in entry.GetComponentsInChildren<TMP_Text>(true)) { text.text = "우편함"; text.name = "MailboxText"; }
            var icon = entry.transform.Find("AttendanceIcon");
            icon.name = "MailboxIcon";
            icon.GetComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(MailArt);
            var view = entry.AddComponent<MailboxEntryButton>();
            var so = new SerializedObject(view);
            Ref(so, "dot", dot);
            so.ApplyModifiedPropertiesWithoutUndo();
            entry.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(root, LobbyPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static ScrollRect Scroll(Transform parent, string name, float w, float h, float x, float y)
    {
        var root = Rect(parent, name, w, h, x, y);
        var image = root.gameObject.AddComponent<Image>();
        image.color = new Color(1, 1, 1, 0.001f);
        root.gameObject.AddComponent<RectMask2D>();
        var content = Rect(root, "Content", w, 0);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 18);
        layout.spacing = 14; layout.childControlWidth = true; layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = root; scroll.content = content; scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 40;
        return scroll;
    }

    static TMP_Text FlowText(Transform p, string n, string value, float size, Color color, float minHeight)
    {
        var text = Text(p, n, value, size, color, 816, minHeight);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        text.gameObject.AddComponent<LayoutElement>().minHeight = minHeight;
        return text;
    }

    static Button SmallButton(Transform p, string n, string label, float w, float h, float x, float y, Button template)
    {
        var b = Object.Instantiate(template, p, false);
        b.name = n; Place((RectTransform)b.transform, w, h, x, y);
        var text = b.GetComponentInChildren<TMP_Text>(true);
        text.text = label; text.fontSize = 26;
        FitLabel(b);
        return b;
    }

    static void FitLabel(Button button)
    {
        var rt = button.GetComponentInChildren<TMP_Text>(true).rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(-12, -8);
    }

    static TMP_Text Text(Transform p, string n, string value, float size, Color color, float w, float h, float x = 0, float y = 0)
    {
        var text = Rect(p, n, w, h, x, y).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = size; text.color = color; text.text = value;
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        text.richText = false;
        return text;
    }

    static Image Picture(Transform p, string n, Sprite sprite, float w, float h, float x, float y)
    {
        var image = Rect(p, n, w, h, x, y).gameObject.AddComponent<Image>();
        image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false;
        return image;
    }

    static RectTransform Rect(Transform p, string n, float w, float h, float x = 0, float y = 0)
    {
        var go = new GameObject(n, typeof(RectTransform)); go.layer = 5;
        var rect = (RectTransform)go.transform;
        rect.SetParent(p, false); Place(rect, w, h, x, y); return rect;
    }

    static void Place(RectTransform r, float w, float h, float x, float y)
    {
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = new Vector2(w, h); r.anchoredPosition = new Vector2(x, y); r.localScale = Vector3.one;
    }

    static void Ref(SerializedObject so, string name, Object value)
    {
        var property = so.FindProperty(name);
        if (property == null) throw new System.InvalidOperationException("Missing mailbox field: " + name);
        property.objectReferenceValue = value;
    }

    // 서버 요청 없이 편집기 전용 임시 씬에서 렌더한다. 본 씬과 프리팹은 저장하지 않는다.
    public static string Preview(string mode = "list", int width = 1080, int height = 1920)
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        Texture2D image = null;
        try
        {
            var canvasGo = new GameObject("MailboxPreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.layer = 5;
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasGo, scene);
            var cameraGo = new GameObject("MailboxPreviewCamera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo, scene);
            var camera = cameraGo.GetComponent<Camera>();
            camera.scene = scene;
            camera.overrideSceneCullingMask = UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(scene);
            camera.transform.position = new Vector3(0, 0, -100);
            camera.orthographic = true;
            camera.orthographicSize = 1080f * height / width * 0.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.18f, 0.16f, 0.19f);
            camera.cullingMask = 1 << 5;
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            ((RectTransform)canvasGo.transform).sizeDelta = new Vector2(1080, 1080f * height / width);
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0;
            scaler.enabled = false;
            target = new RenderTexture(width, height, 24);
            camera.targetTexture = target;
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(MailboxPath), scene);
            root.transform.SetParent(canvasGo.transform, false);
            var so = new SerializedObject(root.GetComponent<MailboxPanel>());
            ((GameObject)so.FindProperty("contents").objectReferenceValue).SetActive(true);
            foreach (var group in root.GetComponentsInChildren<CanvasGroup>(true)) group.alpha = 1;
            var safe = root.transform.Find("Contents/SafeArea");
            foreach (var component in safe.GetComponents<MonoBehaviour>()) component.enabled = false;
            ((RectTransform)safe).anchorMin = Vector2.zero;
            ((RectTransform)safe).anchorMax = Vector2.one;
            ((RectTransform)safe).offsetMin = ((RectTransform)safe).offsetMax = Vector2.zero;
            bool detail = mode.StartsWith("detail");
            ((GameObject)so.FindProperty("listPage").objectReferenceValue).SetActive(!detail);
            ((GameObject)so.FindProperty("detailPage").objectReferenceValue).SetActive(detail);
            ((GameObject)so.FindProperty("emptyRoot").objectReferenceValue).SetActive(mode == "empty");
            ((TMP_Text)so.FindProperty("statusText").objectReferenceValue).text = detail ? "기한 안에 보상을 받아주세요." : mode == "empty" ? "새로운 소식이 도착하면 알려드릴게요." : "받을 수 있는 우편 3개";
            ((TMP_Text)so.FindProperty("detailTitle").objectReferenceValue).text = string.Concat(Enumerable.Repeat("가을 축제에 오신 것을 환영해요! ", 8)).Substring(0, 100);
            ((TMP_Text)so.FindProperty("detailTime").objectReferenceValue).text = "운영팀 · 7일 남음";
            ((TMP_Text)so.FindProperty("detailBody").objectReferenceValue).text = string.Concat(Enumerable.Repeat("모험가 여러분, 가을 축제가 시작되었어요.\n함께해 주셔서 감사드리며 작은 선물을 준비했어요. 새로운 카드와 함께 즐거운 모험을 이어가세요!\n\n", 60)).Substring(0, 4000);
            if (detail)
                foreach (var slot in ((GameObject)so.FindProperty("detailPage").objectReferenceValue).GetComponentsInChildren<Transform>(true))
                    if (slot.parent != null && slot.parent.name == "Rewards") slot.gameObject.SetActive(true);
            if (mode == "list")
            {
                var template = (MailboxRowView)so.FindProperty("rowTemplate").objectReferenceValue;
                var labels = new[] { "가을 축제 기념 선물", "랭크 시즌 보상", "업데이트 감사 선물", "정기 점검 보상", "모험가님, 환영합니다!", "시즌 종료 안내" };
                for (int i = 0; i < labels.Length; i++)
                {
                    var row = Object.Instantiate(template, template.transform.parent, false);
                    row.gameObject.SetActive(true);
                    var rs = new SerializedObject(row);
                    ((TMP_Text)rs.FindProperty("titleText").objectReferenceValue).text = labels[i];
                    ((TMP_Text)rs.FindProperty("timeText").objectReferenceValue).text = i == 1 ? "오늘 만료" : "7일 남음";
                    ((TMP_Text)rs.FindProperty("rewardText").objectReferenceValue).text = i < 3 ? "골드 1,000 · 보석 50" : "수령 완료";
                    ((TMP_Text)rs.FindProperty("stateText").objectReferenceValue).text = i < 3 ? "받기" : "완료";
                    ((GameObject)rs.FindProperty("claimDot").objectReferenceValue).SetActive(i < 3);
                    if (i >= 3) ((Image)rs.FindProperty("mailIcon").objectReferenceValue).sprite = AssetDatabase.LoadAssetAtPath<Sprite>(OpenMailArt);
                }
            }
            for (int pass = 0; pass < 3; pass++)
            {
                Canvas.ForceUpdateCanvases();
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(false)) text.ForceMeshUpdate();
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvasGo.transform);
            }
            if (mode == "detail-bottom")
            {
                ((ScrollRect)so.FindProperty("detailScroll").objectReferenceValue).verticalNormalizedPosition = 0;
                Canvas.ForceUpdateCanvases();
            }
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            System.IO.Directory.CreateDirectory("Temp");
            var path = $"Temp/mailbox-{mode}-{width}x{height}.png";
            System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
            var detailScroll = (ScrollRect)so.FindProperty("detailScroll").objectReferenceValue;
            Debug.Log($"[MailboxPreview] {path}; detail content={detailScroll.content.rect.height}, viewport={detailScroll.viewport.rect.height}");
            return path + "; detail content=" + detailScroll.content.rect.height + "; viewport=" + detailScroll.viewport.rect.height;
        }
        finally
        {
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            if (target != null) Object.DestroyImmediate(target);
            if (image != null) Object.DestroyImmediate(image);
        }
    }
}
