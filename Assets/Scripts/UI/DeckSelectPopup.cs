using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(CanvasScaler))]
[RequireComponent(typeof(GraphicRaycaster))]
public class DeckSelectPopup : MonoBehaviour
{
    [SerializeField] string battleSceneName = "Battle";

    Button[]   deckButtons;
    TMP_Text[] deckLabels;

    void Awake()
    {
        var t_canvas = GetComponent<Canvas>();
        t_canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        t_canvas.sortingOrder = 100;

        var t_scaler = GetComponent<CanvasScaler>();
        t_scaler.uiScaleMode       = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        t_scaler.referenceResolution = new Vector2(1080f, 1920f);

        BuildUI();
        gameObject.SetActive(false);
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void Show()  { gameObject.SetActive(true);  Refresh(); }
    public void Hide()  => gameObject.SetActive(false);

    // ── Build ─────────────────────────────────────────────────────────────

    void BuildUI()
    {
        // dim overlay
        var t_bg = MakeImage(transform, "BG", new Color(0f, 0f, 0f, 0.75f));
        Stretch(t_bg);

        // centered box
        var t_box = MakeImage(transform, "Box", new Color(0.10f, 0.11f, 0.15f, 1f));
        var t_boxRect = t_box.GetComponent<RectTransform>();
        Center(t_boxRect, new Vector2(620f, 780f));

        // title
        var t_titleRect = MakeLabel(t_box.transform, "Title", "덱 선택", 40).GetComponent<RectTransform>();
        t_titleRect.anchorMin = new Vector2(0f, 1f);
        t_titleRect.anchorMax = new Vector2(1f, 1f);
        t_titleRect.pivot     = new Vector2(0.5f, 1f);
        t_titleRect.anchoredPosition = new Vector2(0f, -28f);
        t_titleRect.sizeDelta = new Vector2(0f, 56f);

        // 6 deck buttons  (2 col × 3 row)
        const float t_bW = 264f, t_bH = 112f, t_gX = 16f, t_gY = 16f;
        const float t_startY = -112f;

        this.deckButtons = new Button[DeckSaveManager.SLOT_COUNT];
        this.deckLabels  = new TMP_Text[DeckSaveManager.SLOT_COUNT];

        for (int i = 0; i < DeckSaveManager.SLOT_COUNT; i++)
        {
            int t_col = i % 2;
            int t_row = i / 2;
            float t_x = (t_col - 0.5f) * (t_bW + t_gX);
            float t_y = t_startY - t_row * (t_bH + t_gY);

            var t_btnGo = MakeButton(t_box.transform, $"Slot{i}");
            var t_btnRect = t_btnGo.GetComponent<RectTransform>();
            t_btnRect.anchorMin = t_btnRect.anchorMax = new Vector2(0.5f, 1f);
            t_btnRect.pivot     = new Vector2(0.5f, 1f);
            t_btnRect.anchoredPosition = new Vector2(t_x, t_y);
            t_btnRect.sizeDelta = new Vector2(t_bW, t_bH);

            this.deckButtons[i] = t_btnGo.GetComponent<Button>();
            this.deckLabels[i]  = t_btnGo.GetComponentInChildren<TMP_Text>();
            int t_idx = i;
            this.deckButtons[i].onClick.AddListener(() => SelectDeck(t_idx));
        }

        // close button
        var t_closeGo = MakeButton(t_box.transform, "Close", "닫기", new Color(0.35f, 0.18f, 0.18f, 1f));
        var t_closeRect = t_closeGo.GetComponent<RectTransform>();
        t_closeRect.anchorMin = t_closeRect.anchorMax = new Vector2(0.5f, 0f);
        t_closeRect.pivot     = new Vector2(0.5f, 0f);
        t_closeRect.anchoredPosition = new Vector2(0f, 28f);
        t_closeRect.sizeDelta = new Vector2(200f, 64f);
        t_closeGo.GetComponent<Button>().onClick.AddListener(Hide);
    }

    void Refresh()
    {
        for (int i = 0; i < DeckSaveManager.SLOT_COUNT; i++)
        {
            bool t_valid  = DeckSaveManager.IsSlotValid(i);
            int  t_count  = DeckSaveManager.GetSlot(i)?.Count ?? 0;
            this.deckButtons[i].interactable = t_valid;
            this.deckLabels[i].text = t_valid
                ? $"{DeckSaveManager.GetName(i)}\n<size=70%>{t_count}장</size>"
                : $"{DeckSaveManager.GetName(i)}\n<size=70%><color=#777777>비어있음</color></size>";
        }
    }

    void SelectDeck(int _index)
    {
        if (!DeckSaveManager.IsSlotValid(_index)) return;
        DeckConfig.Set(DeckSaveManager.GetSlot(_index));
        SceneManager.LoadScene(this.battleSceneName);
    }

    // ── UI helpers ────────────────────────────────────────────────────────

    static RectTransform Stretch(GameObject _go)
    {
        var t_rt = _go.GetComponent<RectTransform>();
        t_rt.anchorMin = Vector2.zero;
        t_rt.anchorMax = Vector2.one;
        t_rt.offsetMin = t_rt.offsetMax = Vector2.zero;
        return t_rt;
    }

    static void Center(RectTransform _rt, Vector2 _size)
    {
        _rt.anchorMin = _rt.anchorMax = _rt.pivot = new Vector2(0.5f, 0.5f);
        _rt.anchoredPosition = Vector2.zero;
        _rt.sizeDelta = _size;
    }

    static GameObject MakeImage(Transform _parent, string _name, Color _color)
    {
        var t_go  = new GameObject(_name);
        t_go.transform.SetParent(_parent, false);
        t_go.AddComponent<Image>().color = _color;
        return t_go;
    }

    static GameObject MakeLabel(Transform _parent, string _name, string _text, int _size)
    {
        var t_go  = new GameObject(_name);
        t_go.transform.SetParent(_parent, false);
        t_go.AddComponent<RectTransform>();
        var t_tmp = t_go.AddComponent<TextMeshProUGUI>();
        t_tmp.text      = _text;
        t_tmp.fontSize  = _size;
        t_tmp.color     = Color.white;
        t_tmp.alignment = TextAlignmentOptions.Center;
        return t_go;
    }

    static GameObject MakeButton(Transform _parent, string _name, string _label = "", Color? _color = null)
    {
        var t_go  = new GameObject(_name);
        t_go.transform.SetParent(_parent, false);
        t_go.AddComponent<RectTransform>();
        var t_img = t_go.AddComponent<Image>();
        t_img.color = _color ?? new Color(0.18f, 0.22f, 0.32f, 1f);
        var t_btn  = t_go.AddComponent<Button>();
        var t_cols = t_btn.colors;
        t_cols.highlightedColor = new Color(0.28f, 0.34f, 0.50f, 1f);
        t_cols.disabledColor    = new Color(0.12f, 0.12f, 0.12f, 0.4f);
        t_btn.colors = t_cols;

        var t_labelGo = new GameObject("Label");
        t_labelGo.transform.SetParent(t_go.transform, false);
        Stretch(t_labelGo);
        var t_tmp = t_labelGo.AddComponent<TextMeshProUGUI>();
        t_tmp.text      = _label;
        t_tmp.fontSize  = 28f;
        t_tmp.color     = Color.white;
        t_tmp.alignment = TextAlignmentOptions.Center;

        return t_go;
    }
}
