using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 챕터 마무리 띠 프리팹(ChapterBand)을 코드로 세운다.
// 손으로 옮긴 좌표가 아니라 이 파일이 진실원이다 — 다시 뽑으면 같은 것이 나온다.
public static class TournamentChapterBandBuilder
{
    const string PREFAB_PATH = "Assets/Assets/Prefabs/UI/LobbyUI/Tournament/ChapterBand.prefab";
    const string NODE_PATH   = "Assets/Assets/Prefabs/UI/LobbyUI/Tournament/TournamentNode.prefab";

    static readonly Color GOLD       = new Color(1f, 0.82f, 0.35f, 1f);
    static readonly Color TEXT_MAIN  = Color.white;
    static readonly Color PLATE_DARK = new Color(0.12f, 0.16f, 0.26f, 0.85f);

    const float BAND_W  = 900f;
    const float BAND_H  = 320f;

    // 리본 원본 높이가 143이다 — 세로로 늘리면 끝단 깃이 늘어지므로 높이는 이 값에 붙들어 둔다(가로만 9-slice).
    const float PLATE_W = 780f;
    const float PLATE_H = 143f;

    // 리본 몸통 중심. 끝단 깃이 아래로 더 내려와 rect 중심과 몸통 중심이 어긋난다.
    const float PLATE_Y = 40f;

    [MenuItem("Tools/Tournament/Rebuild ChapterBand Prefab")]
    public static void Build()
    {
        TMP_FontAsset t_font = BorrowFont();
        Sprite t_plate  = FindSprite("Title_Ribbon01_Gray");
        Sprite t_plateSmall = FindSprite("Label_Trapezoid_41_White");   // 정점 이름표와 같은 밑판
        Sprite t_glow   = FindSprite("Glow01_225");
        Sprite t_check  = FindSprite("Icon_Check03_l");
        Sprite t_button = FindSprite("Button01_s_Green");
        Sprite t_coin   = FindSprite("ResourceBar_Single_Icon_Coin");
        Sprite t_gem    = FindSprite("ResourceBar_Single_Icon_Gem");
        Sprite t_lock   = FindSprite("Image_UI_GUIPackCartoon_Icon_Lock_Silver");

        // ⚠ 루트의 anchoredPosition은 맵이 '이음매로부터의 오프셋'으로 읽는다(TournamentMapOverlayView.bandPrefab 참고).
        //   여기서 (0,0)으로 뽑으므로 이 빌더를 다시 돌리면 손으로 맞춘 오프셋이 0으로 되돌아간다.
        GameObject t_root = NewRect("ChapterBand", null, new Vector2(BAND_W, BAND_H), Vector2.zero);
        var t_group = t_root.AddComponent<CanvasGroup>();
        var t_view  = t_root.AddComponent<TournamentChapterBandView>();

        // 완주 + 미수령 — 받을 것이 남아 있다. 빛 · 보상 미리보기 · [받기]가 한 묶음으로 나온다.
        // 빛이 띠 '뒤'에서 피어야 하므로 이 묶음만 첫 자식이다(보상칸·버튼은 띠 아래라 순서와 무관).
        // 빛은 띠 전체가 아니라 [보상 받기] 뒤에서만 핀다 — 띠 폭으로 깔면 밝은 배경 그림에 묻혀 아무것도 안 보이고
        // 배경만 뿌예진다(실측). 누를 자리 하나를 가리키는 것이 이 빛의 몫이다.
        GameObject t_claimableMark = NewRect("ClaimableMark", t_root, Vector2.zero, Vector2.zero);
        AddImage(NewRect("Glow", t_claimableMark, new Vector2(400f, 280f), new Vector2(212f, -66f)), t_glow,
            new Color(GOLD.r, GOLD.g, GOLD.b, 0.75f)).raycastTarget = false;

        // 상태와 무관한 공통 띠 — 제목은 어느 상태에서도 같은 자리에 선다(상태 묶음마다 제목을 복제하면 진실원이 갈린다).
        // 리본을 골드로 물들이지 않는 이유: 제목이 금색 위 흰 글씨가 돼 읽히지 않는다. 상태는 체크·빛·버튼이 말한다.
        AddImage(NewRect("Plate", t_root, new Vector2(PLATE_W, PLATE_H), new Vector2(0f, PLATE_Y)), t_plate, Color.white);

        TMP_Text t_title = AddText(NewRect("Title", t_root, new Vector2(660f, 50f), new Vector2(0f, PLATE_Y + 6f)),
            t_font, "제1장 · 안개 숲", 38f, TEXT_MAIN);

        // 진행 중 — 눈금만 편다. 아직 챕터가 끝나지 않았다는 사실 외에 말할 것이 없다.
        // 이 줄은 이음매 구름 위에도, 배경 그림 위에도 놓인다 — 흰 글씨만으로는 구름에 묻히므로 정점 이름표와 같은 밑판을 깐다.
        GameObject t_progressMark = NewRect("ProgressMark", t_root, Vector2.zero, Vector2.zero);
        TMP_Text t_progress = AddPlatedText(t_progressMark, "Progress", new Vector2(170f, 46f), new Vector2(0f, -58f),
            t_plateSmall, t_font, "0 / 6", 28f);

        // 완주(수령까지 끝남) — 금색 체크. 받을 것이 남아 있지 않다는 마무리 표식이다.
        GameObject t_clearedMark = NewRect("ClearedMark", t_root, Vector2.zero, Vector2.zero);
        AddImage(NewRect("Check", t_clearedMark, new Vector2(58f, 56f), new Vector2(300f, PLATE_Y + 4f)), t_check, GOLD);

        GameObject t_rewards = NewRect("Rewards", t_claimableMark, Vector2.zero, new Vector2(-160f, -66f));
        var t_slots = new List<SlotParts>
        {
            BuildSlot("Slot0", t_rewards, new Vector2(-52f, 0f), t_coin, t_font),
            BuildSlot("Slot1", t_rewards, new Vector2( 52f, 0f), t_gem,  t_font),
        };

        // 밑판 9-slice의 세로 여백이 원본 높이(114)를 다 쓴다 — 그보다 낮추면 위아래 두께가 서로 파고든다.
        GameObject t_claimBtn = NewRect("ClaimBtn", t_claimableMark, new Vector2(248f, 114f), new Vector2(212f, -66f));
        Image t_btnBg = AddImage(t_claimBtn, t_button, Color.white);
        var t_buttonComp = t_claimBtn.AddComponent<Button>();
        t_buttonComp.targetGraphic = t_btnBg;
        AddText(NewRect("Label", t_claimBtn, new Vector2(210f, 44f), new Vector2(0f, 4f)), t_font, "보상 받기", 32f, TEXT_MAIN)
            .raycastTarget = false;

        // 맵의 끝 — 마지막 챕터를 완주했을 때만 켜진다. 진행이 막힌 것이 아니라 여정이 여기까지라는 안내다.
        GameObject t_endMark = NewRect("EndMark", t_root, Vector2.zero, Vector2.zero);
        AddPlatedText(t_endMark, "EndText", new Vector2(360f, 48f), new Vector2(0f, 142f),
            t_plateSmall, t_font, "다음 여정 준비 중", 28f);

        // 랭크 미달 — 이 장에 아직 들어갈 수 없다. 눈금·보상 미리보기를 대신해 요구 등급 하나만 말한다.
        // 자물쇠(잠겼다) · 배지(어느 등급) · 문구(무엇을 하면 열리나) 셋이 한 줄에 선다.
        GameObject t_rankLockMark = NewRect("RankLockMark", t_root, Vector2.zero, Vector2.zero);
        AddImage(NewRect("Lock", t_rankLockMark, new Vector2(44f, 44f), new Vector2(-108f, -58f)), t_lock, Color.white)
            .raycastTarget = false;

        // 배지는 등급이 정해질 때 코드가 켠다 — 스프라이트 없는 Image를 켜 두면 흰 사각형이 남는다.
        GameObject t_rankBadge = NewRect("Badge", t_rankLockMark, new Vector2(52f, 52f), new Vector2(-172f, -58f));
        Image t_rankBadgeImage = AddImage(t_rankBadge, null, Color.white);
        t_rankBadgeImage.raycastTarget = false;
        t_rankBadge.SetActive(false);

        TMP_Text t_rankLockText = AddPlatedText(t_rankLockMark, "RankLockText", new Vector2(300f, 46f), new Vector2(76f, -58f),
            t_plateSmall, t_font, "실버 도달 시 해금", 28f);

        Wire(t_view, t_title, t_progress, t_buttonComp, t_slots, t_progressMark, t_claimableMark, t_clearedMark, t_endMark,
             t_rankLockMark, t_rankLockText, t_rankBadgeImage, t_group);

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PREFAB_PATH));
        PrefabUtility.SaveAsPrefabAsset(t_root, PREFAB_PATH);

        // LoadPrefabContents로 연 것이 아니므로 UnloadPrefabContents를 부르면 안 된다(오브젝트가 씬에 남는다).
        Object.DestroyImmediate(t_root);

        AssetDatabase.SaveAssets();
        Debug.Log($"[TournamentChapterBandBuilder] 저장 완료 — {PREFAB_PATH}");
    }

    // 인스펙터 배선은 SerializedObject로 한다 — private [SerializeField]에 리플렉션 없이 닿는 유일한 길이다.
    static void Wire(TournamentChapterBandView _view, TMP_Text _title, TMP_Text _progress, Button _claim,
                     List<SlotParts> _slots, GameObject _progressMark, GameObject _claimableMark,
                     GameObject _clearedMark, GameObject _endMark, GameObject _rankLockMark,
                     TMP_Text _rankLockText, Image _rankLockBadge, CanvasGroup _group)
    {
        var t_so = new SerializedObject(_view);
        t_so.FindProperty("titleText").objectReferenceValue = _title;
        t_so.FindProperty("progressText").objectReferenceValue = _progress;
        t_so.FindProperty("claimButton").objectReferenceValue = _claim;
        t_so.FindProperty("progressMark").objectReferenceValue = _progressMark;
        t_so.FindProperty("claimableMark").objectReferenceValue = _claimableMark;
        t_so.FindProperty("clearedMark").objectReferenceValue = _clearedMark;
        t_so.FindProperty("endMark").objectReferenceValue = _endMark;
        t_so.FindProperty("rankLockMark").objectReferenceValue = _rankLockMark;
        t_so.FindProperty("rankLockText").objectReferenceValue = _rankLockText;
        t_so.FindProperty("rankLockBadge").objectReferenceValue = _rankLockBadge;
        t_so.FindProperty("canvasGroup").objectReferenceValue = _group;

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

    // 정점 보상 칸과 같은 저작(아이콘 + 수량) — 두 화면의 눈금이 갈리지 않게 치수를 맞춘다.
    // CurrencyRewardSlotView는 뷰가 필드로 소유하는 값이라 여기서 인스턴스를 만들지 않는다 —
    // 세 부품만 돌려주고 배선은 뷰의 SerializedObject 배열 칸에서 한다.
    static SlotParts BuildSlot(string _name, GameObject _parent, Vector2 _pos, Sprite _icon, TMP_FontAsset _font)
    {
        GameObject t_root = NewRect(_name, _parent, new Vector2(96f, 36f), _pos);
        Image t_iconImage = AddImage(NewRect("Icon", t_root, new Vector2(32f, 32f), new Vector2(-32f, 0f)), _icon, Color.white);
        TMP_Text t_amount = AddText(NewRect("Amount", t_root, new Vector2(60f, 34f), new Vector2(14f, 0f)), _font, "0", 24f, TEXT_MAIN);

        return new SlotParts(t_root, t_iconImage, t_amount);
    }

    // 밑판 위에 얹은 한 줄(정점 이름표와 같은 관용구). 구름·풀밭·암반 어디에 놓여도 글자가 읽히게 하는 몫이다.
    static TMP_Text AddPlatedText(GameObject _parent, string _name, Vector2 _size, Vector2 _pos,
                                  Sprite _plate, TMP_FontAsset _font, string _text, float _fontSize)
    {
        GameObject t_root = NewRect(_name, _parent, _size, _pos);
        AddImage(t_root, _plate, PLATE_DARK).raycastTarget = false;

        return AddText(NewRect("Label", t_root, new Vector2(_size.x - 24f, _size.y - 8f), Vector2.zero),
            _font, _text, _fontSize, TEXT_MAIN);
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

    // 폰트는 새로 고르지 않고 정점 프리팹에서 빌려 온다 — 두 화면의 글꼴이 갈리지 않게.
    static TMP_FontAsset BorrowFont()
    {
        var t_node = AssetDatabase.LoadAssetAtPath<GameObject>(NODE_PATH);
        if (t_node == null) return null;

        TMP_Text t_any = t_node.GetComponentInChildren<TMP_Text>(true);
        return t_any != null ? t_any.font : null;
    }

    static Sprite FindSprite(string _name)
    {
        foreach (string t_guid in AssetDatabase.FindAssets($"{_name} t:Sprite"))
        {
            string t_path = AssetDatabase.GUIDToAssetPath(t_guid);
            foreach (Object t_obj in AssetDatabase.LoadAllAssetsAtPath(t_path))
                if (t_obj is Sprite t_sprite && t_sprite.name == _name) return t_sprite;
        }

        Debug.LogWarning($"[TournamentChapterBandBuilder] 스프라이트를 찾지 못했다 — {_name}");
        return null;
    }

    // 보상 칸 하나가 배선에 필요한 세 부품. 병렬 리스트로 흩지 않으려고 묶는다.
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
