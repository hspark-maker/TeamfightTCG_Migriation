using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.UI;

// 정점 도전 확인 팝업(TournamentNodePopup) 프리팹을 코드로 세운다.
// 챕터 띠와 같은 관용구다 — 손으로 옮긴 좌표가 아니라 이 파일이 진실원이고, 다시 뽑으면 같은 것이 나온다.
public static class TournamentNodePopupBuilder
{
    const string PREFAB_PATH = "Assets/Assets/Prefabs/UI/PooledUI/TournamentNodePopup.prefab";
    const string NODE_PATH   = "Assets/Assets/Prefabs/UI/LobbyUI/Tournament/TournamentNode.prefab";
    const string UI_LABEL    = "UIPrefab";   // DataLibrary가 이 라벨로 팝업 프리팹을 훑어 타입→프리팹 표를 만든다.

    // 캔버스 기준 해상도(1080x1920, 가로 고정). 팝업은 이 폭 안에 들어와야 한다.
    const float SCREEN_W = 1080f;
    const float SCREEN_H = 1920f;

    const float PANEL_W = 880f;
    const float PANEL_H = 1480f;

    static readonly Color DIM        = new Color(0f, 0f, 0f, 0.72f);
    static readonly Color PANEL_BODY = new Color(0.20f, 0.24f, 0.33f, 1f);
    static readonly Color PANEL_TOP  = new Color(0.14f, 0.17f, 0.25f, 1f);
    static readonly Color FRAME      = new Color(0.31f, 0.37f, 0.50f, 1f);   // 초상 액자 — 판보다 밝아야 그림이 액자 안에 든 것으로 읽힌다.
    static readonly Color TITLE_BAR  = new Color(0.11f, 0.13f, 0.20f, 1f);
    static readonly Color SLOT_BG    = new Color(0.13f, 0.15f, 0.22f, 1f);
    static readonly Color TEXT_MAIN  = Color.white;
    static readonly Color TEXT_SUB   = new Color(0.66f, 0.79f, 0.95f, 1f);   // "가능한 보상" — 금색은 클리어 축이라 쓰지 않는다.
    static readonly Color POWER      = new Color(1f, 0.86f, 0.45f, 1f);

    [MenuItem("Tools/Tournament/Rebuild NodePopup Prefab")]
    public static void Build()
    {
        TMP_FontAsset t_font = BorrowFont();

        Sprite t_panel    = FindSprite("Popup02~09_Topber_White_Bg");
        Sprite t_titleBar = FindSprite("Popup09_Topber_White_Label_Bg");
        Sprite t_frame    = FindSprite("BasicFrame_Square01");
        Sprite t_slotBg   = FindSprite("BasicFrame_Square01_White");
        Sprite t_plate    = FindSprite("Label_Trapezoid_41_White");
        Sprite t_btnGo    = FindSprite("Button01_l_Green");
        Sprite t_btnBack  = FindSprite("Button01_l_Gray");
        Sprite t_coin     = FindSprite("ResourceBar_Single_Icon_Coin");
        Sprite t_gem      = FindSprite("ResourceBar_Single_Icon_Gem");
        Sprite t_shard    = FindSprite("ResourceBar_Single_Icon_Coin");   // 조각·기력 그림은 CurrencyLook이 런타임에 덮는다.
        Sprite t_portrait = BorrowPortrait();

        // 루트는 풀 컨테이너를 가득 채운다(딤이 화면 전체를 덮어야 뒤의 맵이 눌리지 않는다).
        GameObject t_root = NewRect("TournamentNodePopup", null, new Vector2(SCREEN_W, SCREEN_H), Vector2.zero);
        Stretch(t_root);
        var t_view = t_root.AddComponent<TournamentNodePopup>();

        GameObject t_contents = NewRect("Contents", t_root, new Vector2(SCREEN_W, SCREEN_H), Vector2.zero);
        Stretch(t_contents);
        t_contents.AddComponent<CanvasGroup>();

        // 딤은 그림 없는 판이다 — 뒤로 손이 새지 않게 raycast만 받는다(탭으로는 닫지 않는다. 닫는 자리는 [돌아가기] 하나).
        GameObject t_dim = NewRect("Dim", t_contents, new Vector2(SCREEN_W, SCREEN_H), Vector2.zero);
        Stretch(t_dim);
        AddImage(t_dim, null, DIM);

        GameObject t_panelGo = NewRect("Panel", t_contents, new Vector2(PANEL_W, PANEL_H), Vector2.zero);
        AddImage(t_panelGo, t_panel, PANEL_BODY);

        // ── 제목 ────────────────────────────────────────────────
        GameObject t_titleBarGo = NewRect("TitleBar", t_panelGo, new Vector2(720f, 120f), new Vector2(0f, 650f));
        AddImage(t_titleBarGo, t_titleBar, TITLE_BAR).raycastTarget = false;
        TMP_Text t_title = AddText(NewRect("Label", t_titleBarGo, new Vector2(660f, 80f), Vector2.zero),
            t_font, "어둠의 드루이드", 46f, TEXT_MAIN);

        // ── 초상 ────────────────────────────────────────────────
        // 액자와 그림을 나눠 둔다 — avatar 미저작 정점이 프리팹 그림을 그대로 쓰므로 액자까지 갈리면 안 된다.
        GameObject t_frameGo = NewRect("PortraitFrame", t_panelGo, new Vector2(700f, 700f), new Vector2(0f, 210f));
        AddImage(t_frameGo, t_frame, FRAME).raycastTarget = false;
        Image t_portraitImage = AddImage(
            NewRect("Portrait", t_frameGo, new Vector2(640f, 640f), Vector2.zero), t_portrait, Color.white);
        t_portraitImage.preserveAspect = true;
        t_portraitImage.raycastTarget = false;

        // ── 권장 전투력 ─────────────────────────────────────────
        // 밑판을 깐다 — 판 위에 흰 글씨만 놓으면 초상 아래 여백에 떠 있는 자막처럼 읽힌다.
        TMP_Text t_power = AddPlatedText(t_panelGo, "PowerLine", new Vector2(440f, 76f), new Vector2(0f, -208f),
            t_plate, t_font, "권장 전투력 32", 36f, POWER);

        // ── 보상 ────────────────────────────────────────────────
        GameObject t_rewardSection = NewRect("RewardSection", t_panelGo, Vector2.zero, Vector2.zero);
        AddText(NewRect("Header", t_rewardSection, new Vector2(400f, 60f), new Vector2(0f, -301f)),
            t_font, "가능한 보상", 34f, TEXT_SUB).raycastTarget = false;

        GameObject t_rewardRow = NewRect("Rewards", t_rewardSection, new Vector2(PANEL_W, 170f), new Vector2(0f, -444f));
        var t_layout = t_rewardRow.AddComponent<HorizontalLayoutGroup>();
        t_layout.childAlignment = TextAnchor.MiddleCenter;
        t_layout.spacing = 30f;
        t_layout.childForceExpandWidth = false;
        t_layout.childForceExpandHeight = false;
        t_layout.childControlWidth = false;
        t_layout.childControlHeight = false;

        var t_slots = new List<SlotParts>
        {
            BuildSlot("Slot0", t_rewardRow, t_slotBg, t_coin,   t_font),
            BuildSlot("Slot1", t_rewardRow, t_slotBg, t_gem,    t_font),
            BuildSlot("Slot2", t_rewardRow, t_slotBg, t_shard,  t_font),
        };

        // ── 버튼 두 짝 ──────────────────────────────────────────
        // [돌아가기]가 왼쪽, [전투]가 오른쪽 — 되돌아가는 손은 왼쪽에, 나아가는 손은 오른쪽에 둔다.
        Button t_back   = BuildButton("BackBtn",   t_panelGo, new Vector2(-215f, -642f), t_btnBack, "돌아가기", t_font);
        Button t_battle = BuildButton("BattleBtn", t_panelGo, new Vector2( 215f, -642f), t_btnGo,   "전투",     t_font);

        Wire(t_view, t_contents, t_panelGo, t_title, t_portraitImage, t_power, t_rewardSection, t_slots, t_battle, t_back);

        // 저작 상태는 '꺼짐'이다 — 켠 채로 저장하면 인스턴스화된 프레임에 배선 전 목업이 한 번 번쩍인다.
        t_contents.SetActive(false);

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PREFAB_PATH));
        PrefabUtility.SaveAsPrefabAsset(t_root, PREFAB_PATH);

        // LoadPrefabContents로 연 것이 아니므로 UnloadPrefabContents를 부르면 안 된다(오브젝트가 씬에 남는다).
        Object.DestroyImmediate(t_root);

        AssetDatabase.SaveAssets();
        RegisterAddressable();

        Debug.Log($"[TournamentNodePopupBuilder] 저장 완료 — {PREFAB_PATH}");
    }

    // 인스펙터 배선은 SerializedObject로 한다 — private [SerializeField]에 리플렉션 없이 닿는 유일한 길이다.
    static void Wire(TournamentNodePopup _view, GameObject _contents, GameObject _panel, TMP_Text _title,
                     Image _portrait, TMP_Text _power, GameObject _rewardSection, List<SlotParts> _slots,
                     Button _battle, Button _back)
    {
        _view.contents = _contents;   // PooledUIBase의 public 필드(풀이 켜고 끄는 대상)

        var t_so = new SerializedObject(_view);
        t_so.FindProperty("titleText").objectReferenceValue = _title;
        t_so.FindProperty("portraitImage").objectReferenceValue = _portrait;
        t_so.FindProperty("powerText").objectReferenceValue = _power;
        t_so.FindProperty("rewardSection").objectReferenceValue = _rewardSection;
        t_so.FindProperty("battleButton").objectReferenceValue = _battle;
        t_so.FindProperty("backButton").objectReferenceValue = _back;

        // 스케일 팝은 판만 한다 — 딤까지 물리면 화면 전체가 커졌다 작아진다.
        t_so.FindProperty("transition").FindPropertyRelative("panel").objectReferenceValue = _panel.transform;

        SerializedProperty t_array = t_so.FindProperty("rewardSlots");
        t_array.arraySize = _slots.Count;
        for (int t_i = 0; t_i < _slots.Count; t_i++)
        {
            SerializedProperty t_slot = t_array.GetArrayElementAtIndex(t_i);
            t_slot.FindPropertyRelative("root").objectReferenceValue = _slots[t_i].Root;
            t_slot.FindPropertyRelative("icon").objectReferenceValue = _slots[t_i].Icon;
            t_slot.FindPropertyRelative("amountLabel").objectReferenceValue = _slots[t_i].Amount;
        }

        t_so.ApplyModifiedPropertiesWithoutUndo();
    }

    // 라벨을 붙여 두지 않으면 DataLibrary의 표에 안 올라가 팝업이 통째로 안 뜬다(맵은 폴백으로 바로 전투에 든다).
    static void RegisterAddressable()
    {
        AddressableAssetSettings t_settings = AddressableAssetSettingsDefaultObject.Settings;
        if (t_settings == null)
        {
            Debug.LogWarning("[TournamentNodePopupBuilder] Addressables 설정이 없다 — 라벨을 손으로 붙여야 한다.");
            return;
        }

        string t_guid = AssetDatabase.AssetPathToGUID(PREFAB_PATH);
        AddressableAssetGroup t_group = GroupOfExistingUiPrefab(t_settings) ?? t_settings.DefaultGroup;

        AddressableAssetEntry t_entry = t_settings.CreateOrMoveEntry(t_guid, t_group);
        t_entry.SetLabel(UI_LABEL, true, true);

        t_settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, t_entry, true);
        AssetDatabase.SaveAssets();

        Debug.Log($"[TournamentNodePopupBuilder] Addressable 등록 — 그룹 '{t_group.Name}' / 라벨 '{UI_LABEL}'");
    }

    // 이미 UIPrefab 라벨이 붙은 항목이 사는 그룹으로 따라 들어간다 — 팝업이 그룹마다 흩어지지 않게.
    static AddressableAssetGroup GroupOfExistingUiPrefab(AddressableAssetSettings _settings)
    {
        foreach (AddressableAssetGroup t_group in _settings.groups)
        {
            if (t_group == null) continue;

            foreach (AddressableAssetEntry t_entry in t_group.entries)
                if (t_entry != null && t_entry.labels != null && t_entry.labels.Contains(UI_LABEL)) return t_group;
        }

        return null;
    }

    // 보상 칸 하나(밑판 + 아이콘 + 수량). 챕터 띠·수령 팝업과 같은 세 부품이라 배선 규약이 갈리지 않는다.
    static SlotParts BuildSlot(string _name, GameObject _parent, Sprite _bg, Sprite _mockIcon, TMP_FontAsset _font)
    {
        GameObject t_root = NewRect(_name, _parent, new Vector2(170f, 170f), Vector2.zero);
        AddImage(t_root, _bg, SLOT_BG).raycastTarget = false;

        Image t_icon = AddImage(NewRect("Icon", t_root, new Vector2(96f, 96f), new Vector2(0f, 18f)), _mockIcon, Color.white);
        t_icon.preserveAspect = true;
        t_icon.raycastTarget = false;

        TMP_Text t_amount = AddText(NewRect("Amount", t_root, new Vector2(160f, 44f), new Vector2(0f, -56f)),
            _font, "0", 30f, TEXT_MAIN);
        t_amount.raycastTarget = false;

        return new SlotParts(t_root, t_icon, t_amount);
    }

    static Button BuildButton(string _name, GameObject _parent, Vector2 _pos, Sprite _sprite, string _label, TMP_FontAsset _font)
    {
        GameObject t_go = NewRect(_name, _parent, new Vector2(400f, 136f), _pos);
        Image t_bg = AddImage(t_go, _sprite, Color.white);

        var t_button = t_go.AddComponent<Button>();
        t_button.targetGraphic = t_bg;

        AddText(NewRect("Label", t_go, new Vector2(340f, 60f), new Vector2(0f, 4f)), _font, _label, 42f, TEXT_MAIN)
            .raycastTarget = false;

        return t_button;
    }

    // 밑판 위에 얹은 한 줄(정점 이름표·챕터 띠와 같은 관용구).
    static TMP_Text AddPlatedText(GameObject _parent, string _name, Vector2 _size, Vector2 _pos,
                                  Sprite _plate, TMP_FontAsset _font, string _text, float _fontSize, Color _color)
    {
        GameObject t_root = NewRect(_name, _parent, _size, _pos);
        AddImage(t_root, _plate, TITLE_BAR).raycastTarget = false;

        TMP_Text t_text = AddText(NewRect("Label", t_root, new Vector2(_size.x - 30f, _size.y - 10f), Vector2.zero),
            _font, _text, _fontSize, _color);
        t_text.raycastTarget = false;
        return t_text;
    }

    static GameObject NewRect(string _name, GameObject _parent, Vector2 _size, Vector2 _pos)
    {
        var t_go = new GameObject(_name, typeof(RectTransform));
        var t_rect = (RectTransform)t_go.transform;
        if (_parent != null) t_rect.SetParent(_parent.transform, false);

        t_rect.anchorMin = new Vector2(0.5f, 0.5f);
        t_rect.anchorMax = new Vector2(0.5f, 0.5f);
        t_rect.pivot = new Vector2(0.5f, 0.5f);
        t_rect.sizeDelta = _size;
        t_rect.anchoredPosition = _pos;
        return t_go;
    }

    // 화면을 가득 채우는 판(딤·컨테이너). 기준 해상도와 실기기 비가 갈려도 여백이 생기지 않게 늘려 둔다.
    static void Stretch(GameObject _go)
    {
        var t_rect = (RectTransform)_go.transform;
        t_rect.anchorMin = Vector2.zero;
        t_rect.anchorMax = Vector2.one;
        t_rect.pivot = new Vector2(0.5f, 0.5f);
        t_rect.sizeDelta = Vector2.zero;
        t_rect.anchoredPosition = Vector2.zero;
    }

    static Image AddImage(GameObject _go, Sprite _sprite, Color _color)
    {
        var t_image = _go.AddComponent<Image>();
        t_image.sprite = _sprite;
        t_image.color = _color;

        // 9-slice 여백이 저작된 스프라이트만 Sliced로 늘린다(없는 것을 Sliced로 두면 경고가 뜬다).
        if (_sprite != null && _sprite.border != Vector4.zero) t_image.type = Image.Type.Sliced;

        return t_image;
    }

    static TMP_Text AddText(GameObject _go, TMP_FontAsset _font, string _text, float _size, Color _color)
    {
        var t_text = _go.AddComponent<TextMeshProUGUI>();
        if (_font != null) t_text.font = _font;
        t_text.text = _text;
        t_text.fontSize = _size;
        t_text.color = _color;
        t_text.alignment = TextAlignmentOptions.Center;
        t_text.enableWordWrapping = false;
        t_text.overflowMode = TextOverflowModes.Overflow;
        return t_text;
    }

    // 폰트는 정점 프리팹에서 빌려 온다 — 맵과 팝업의 글꼴이 갈리지 않게.
    static TMP_FontAsset BorrowFont()
    {
        var t_node = AssetDatabase.LoadAssetAtPath<GameObject>(NODE_PATH);
        if (t_node == null) return null;

        TMP_Text t_any = t_node.GetComponentInChildren<TMP_Text>(true);
        return t_any != null ? t_any.font : null;
    }

    // 초상 목업도 정점에서 빌려 온다 — avatar가 전 정점 미저작이라 맵과 팝업이 같은 그림을 보여야 한다.
    static Sprite BorrowPortrait()
    {
        var t_node = AssetDatabase.LoadAssetAtPath<GameObject>(NODE_PATH);
        if (t_node == null) return null;

        foreach (Transform t_child in t_node.GetComponentsInChildren<Transform>(true))
            if (t_child.name == "Portrait")
            {
                var t_image = t_child.GetComponent<Image>();
                if (t_image != null) return t_image.sprite;
            }

        return null;
    }

    static Sprite FindSprite(string _name)
    {
        foreach (string t_guid in AssetDatabase.FindAssets($"{_name} t:Sprite"))
        {
            string t_path = AssetDatabase.GUIDToAssetPath(t_guid);
            foreach (Object t_obj in AssetDatabase.LoadAllAssetsAtPath(t_path))
                if (t_obj is Sprite t_sprite && t_sprite.name == _name) return t_sprite;
        }

        Debug.LogWarning($"[TournamentNodePopupBuilder] 스프라이트를 찾지 못했다 — {_name}");
        return null;
    }

    // 보상 칸 하나가 배선에 필요한 세 부품. CurrencyRewardSlotView는 뷰가 필드로 소유하는 값이라 여기서 인스턴스를 만들지 않는다.
    readonly struct SlotParts
    {
        public readonly GameObject Root;
        public readonly Image Icon;
        public readonly TMP_Text Amount;

        public SlotParts(GameObject _root, Image _icon, TMP_Text _amount)
        {
            Root = _root;
            Icon = _icon;
            Amount = _amount;
        }
    }
}
