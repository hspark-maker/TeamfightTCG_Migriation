using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class PooledCardElement : PooledUIBase
{
    PooledCardElementData cardElementData;
    CardData cardData;

    [SerializeField] GameObject fullCardContents;
    [SerializeField] GameObject simpleCardContents;

    [SerializeField] CardElement fullCardElement;
    [SerializeField] CardElement simpleCardElement;

    [SerializeField] Transform keywordListRoot;
    [SerializeField] GameObject keywordExplainItemPrefab;
    [SerializeField] KeywordIconConfig keywordIconConfig;

    [SerializeField] RectTransform keywordPanel;
    [SerializeField] float keywordOffsetX = 300f;

    Canvas cachedCanvas;
    RectTransform rectTransform;

    protected override void Awake()
    {
        base.Awake();
        this.rectTransform = (RectTransform)transform;
        this.cachedCanvas = GetComponentInParent<Canvas>();
    }

    void PlaceAtCursor()
    {
        if (this.cachedCanvas == null) return;

        RectTransform t_canvasRect = (RectTransform)this.cachedCanvas.transform;
        Camera t_cam = this.cachedCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : this.cachedCanvas.worldCamera;

        LayoutRebuilder.ForceRebuildLayoutImmediate(this.rectTransform);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            t_canvasRect, Input.mousePosition, t_cam, out Vector2 t_pos);

        Vector2 t_size = this.rectTransform.rect.size;
        Vector2 t_half = t_canvasRect.rect.size * 0.5f;
        t_pos.x = Mathf.Clamp(t_pos.x,
            -t_half.x + t_size.x * this.rectTransform.pivot.x,
             t_half.x - t_size.x * (1f - this.rectTransform.pivot.x));
        t_pos.y = Mathf.Clamp(t_pos.y,
            -t_half.y + t_size.y * this.rectTransform.pivot.y,
             t_half.y - t_size.y * (1f - this.rectTransform.pivot.y));

        this.rectTransform.localPosition = t_pos;

        if (this.keywordPanel != null)
        {
            bool t_goLeft = Input.mousePosition.x > Screen.width * 0.5f;
            Vector2 t_kpos = this.keywordPanel.anchoredPosition;
            t_kpos.x = t_goLeft ? -this.keywordOffsetX : this.keywordOffsetX;
            this.keywordPanel.anchoredPosition = t_kpos;
        }
    }

    public override void Initialization(UIData _data)
    {
        this.cardElementData = _data as PooledCardElementData;
        if (this.cardElementData == null) return;
        this.cardData = this.cardElementData.card;

        if (this.cardElementData.mod == CardElementMod.Full)
        {
            this.fullCardElement.Init(this.cardData, this.cardElementData.mod);
            this.fullCardContents.SetActive(true);
            this.simpleCardContents.SetActive(false);
        }
        else
        {
            this.simpleCardElement.Init(this.cardData, this.cardElementData.mod);
            this.fullCardContents.SetActive(false);
            this.simpleCardContents.SetActive(true);
        }


        RefreshKeywordList(this.cardData);
    }

    void RefreshKeywordList(CardData _card)
    {
        if (this.keywordListRoot == null || this.keywordExplainItemPrefab == null || this.keywordIconConfig == null) return;

        foreach (Transform t_child in this.keywordListRoot)
            Destroy(t_child.gameObject);

        if (_card == null) return;

        foreach (CardKeyword t_kw in System.Enum.GetValues(typeof(CardKeyword)))
        {
            if (t_kw == CardKeyword.None) continue;
            if (!_card.HasKeyword(t_kw)) continue;
            if (!this.keywordIconConfig.TryGetEntry(t_kw, out var t_entry)) continue;

            var t_obj = Instantiate(this.keywordExplainItemPrefab, this.keywordListRoot);
            t_obj.GetComponent<KeywordExplainItem>()?.Init(t_entry.icon, t_entry.displayName, t_entry.explain);
        }
    }

    public override void Show()
    {
        if (this.cardElementData.mod == CardElementMod.Full)
            this.fullCardContents.SetActive(true);
        else
            this.simpleCardContents.SetActive(true);
        this.isShow = true;
        PlaceAtCursor();
    }

    public override void Hide()
    {
        if (this.cardElementData.mod == CardElementMod.Full)
            this.fullCardContents.SetActive(false);
        else
            this.simpleCardContents.SetActive(false);
        this.isShow = false;
    }
}

public class PooledCardElementData : UIData
{
    public CardData card;
    public CardElementMod mod = CardElementMod.Full;
}