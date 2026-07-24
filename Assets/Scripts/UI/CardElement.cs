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

    [Header("Synergy Icons")]
    // 미배선이면 keywordIconContainer에 이어붙인다(전용 자리를 만들기 전에도 바로 보이게).
    [SerializeField] Transform synergyIconContainer;
    [SerializeField] GameObject synergyIconPrefab;   // 미배선이면 keywordIconPrefab 재사용

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
        RefreshSynergyIcons(_card);
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
            // prefab = 배경(루트 Image) + 아이콘(자식 Image). 배경 유지, 자식에만 키워드 스프라이트 주입.
            Image t_iconImg = t_obj.transform.childCount > 0
                ? t_obj.transform.GetChild(0).GetComponent<Image>()
                : t_obj.GetComponent<Image>();
            if (t_iconImg != null) t_iconImg.sprite = t_entry.icon;

            LongPressDetector t_lp      = t_obj.GetComponent<LongPressDetector>();
            KeywordIconButton  t_btn     = t_obj.GetComponent<KeywordIconButton>();
            RectTransform      t_iconRt  = t_obj.GetComponent<RectTransform>();
            KeywordIconConfig.Entry t_captured = t_entry;
            if (t_lp  != null) t_lp.OnLongPress = () => ShowKeywordExplain(t_captured, t_iconRt);
            if (t_btn != null) t_btn.onPointerUp = HideKeywordExplain;
        }
    }

    /// <summary>시너지 아이콘 줄. 전용 컨테이너가 없으면 키워드 아이콘 뒤에 이어붙인다
    /// (그 경우 클리어는 RefreshKeywordIcons가 이미 했으므로 여기서 또 비우면 안 된다).</summary>
    void RefreshSynergyIcons(CardData _card)
    {
        bool       t_dedicated = this.synergyIconContainer != null;
        Transform  t_parent    = t_dedicated ? this.synergyIconContainer : this.keywordIconContainer;
        GameObject t_prefab    = this.synergyIconPrefab != null ? this.synergyIconPrefab : this.keywordIconPrefab;
        SynergyIconStrip.Build(_card, t_parent, t_prefab, _clearFirst: t_dedicated);
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
        this.Data = null;
        this.nameText.text = "빈 슬롯";
        this.cardPortrait.sprite = this.emptySlot;
        // 슬롯을 비울 때 이전 카드의 아이콘이 남지 않게 정리(덱 편성에서 슬롯 교체 시 발생).
        ClearIcons(this.keywordIconContainer);
        ClearIcons(this.synergyIconContainer);
    }

    static void ClearIcons(Transform _container)
    {
        if (_container == null) return;
        foreach (Transform t_child in _container)
            Destroy(t_child.gameObject);
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
