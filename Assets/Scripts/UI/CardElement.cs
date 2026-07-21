using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;

public enum CardElementMod
{
    Full,
    Simple,
}
public class CardElement : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    CardData Data;
    public CardData CardData => Data;
    bool isInteractable = true;

    [SerializeField] Image cardBg;
    [SerializeField] Image cardPortrait;
    [SerializeField] Button button;

    [SerializeField] TextMeshProUGUI nameText;
    [SerializeField] TextMeshProUGUI explainText;
    [SerializeField] TextMeshProUGUI hpText;

    [SerializeField] Sprite emptySlot;

    [Header("Keyword Icons")]
    [SerializeField] Transform keywordIconContainer;
    [SerializeField] GameObject keywordIconPrefab;
    
    [SerializeField] GameObject disabledOverlay;

    public Action<CardData, PointerEventData> onBeginDrag;
    public Action<PointerEventData> onDrag;
    public Action<PointerEventData> onEndDrag;

    public void Init(CardData _card, CardElementMod _mod = CardElementMod.Full, int _displayHp = -1)
    {
        if (_card == null)
        {
            EmptySlotInit();
            return;
        }

        this.Data = _card;
        this.nameText.text = _card.name;
        if (_mod == CardElementMod.Full)
        {
            this.cardPortrait.sprite = _card.fullImage;
            this.explainText.text = _card.cardExplain;
            this.hpText.text = (_displayHp >= 0 ? _displayHp : _card.maxHp).ToString();
        }
        else
        {
            this.cardPortrait.sprite = _card.portrait;
        }

        RefreshKeywordIcons(_card);
    }

    public void Init(CardInstance _instance, CardElementMod _mod = CardElementMod.Full)
    {
        Init(_instance?.data, _mod, _instance?.hp ?? -1);
    }

    void RefreshKeywordIcons(CardData _card)
    {
        if (this.keywordIconContainer == null || this.keywordIconPrefab == null) return;

        foreach (Transform t_child in this.keywordIconContainer)
            Destroy(t_child.gameObject);

        KeywordIconConfig t_config = DataLibrary.instance?.keywordIconConfig;
        if (t_config == null) return;

        CardKeyword t_allKeywords = _card.keywords | _card.explainKeywords;
        foreach (CardKeyword t_kw in (CardKeyword[])Enum.GetValues(typeof(CardKeyword)))
        {
            if (t_kw == CardKeyword.None) continue;
            if ((t_allKeywords & t_kw) == 0) continue;
            if (!t_config.TryGetEntry(t_kw, out KeywordIconConfig.Entry t_entry)) continue;
            if (t_entry.icon == null) continue;

            GameObject t_obj = Instantiate(this.keywordIconPrefab, this.keywordIconContainer);
            t_obj.GetComponent<Image>().sprite = t_entry.icon;

            LongPressDetector t_lp      = t_obj.GetComponent<LongPressDetector>();
            KeywordIconButton  t_btn     = t_obj.GetComponent<KeywordIconButton>();
            RectTransform      t_iconRt  = t_obj.GetComponent<RectTransform>();
            KeywordIconConfig.Entry t_captured = t_entry;
            if (t_lp  != null) t_lp.OnLongPress = () => ShowKeywordExplain(t_captured, t_iconRt);
            if (t_btn != null) t_btn.onPointerUp = HideKeywordExplain;
        }
    }

    void ShowKeywordExplain(KeywordIconConfig.Entry _entry, RectTransform _iconRect)
    {
        UIPoolManager.instance?.AddOrUpdateUI<KeywordExplainPopupUI>(new KeywordExplainData
        {
            icon        = _entry.icon,
            displayName = _entry.displayName,
            explain     = _entry.explain,
            iconRect    = _iconRect,
        });
    }

    void HideKeywordExplain() => UIPoolManager.instance?.HideUI<KeywordExplainPopupUI>();

    void EmptySlotInit()
    {
        this.nameText.text = "빈 슬롯";
        this.cardPortrait.sprite = this.emptySlot;
    }

    public void SetButtonAction(Action _buttonAction)
    {
        this.button.onClick.AddListener(() => _buttonAction?.Invoke());
    }

    public void SetInteractable(bool _active, bool _activeDisabledOverlay = false)
    {
        this.isInteractable = _active;
        this.button.interactable = _active;
        if (_activeDisabledOverlay)
            this.disabledOverlay.SetActive(!_active);
    }

    public void HighlightBG()
    {
        this.cardBg.color = Color.white;
    }

    public void OnBeginDrag(PointerEventData _eventData)
    {
        if (!this.isInteractable) return;
        this.onBeginDrag?.Invoke(this.Data, _eventData);
    }
    public void OnDrag(PointerEventData _eventData) => this.onDrag?.Invoke(_eventData);
    public void OnEndDrag(PointerEventData _eventData) => this.onEndDrag?.Invoke(_eventData);
}
