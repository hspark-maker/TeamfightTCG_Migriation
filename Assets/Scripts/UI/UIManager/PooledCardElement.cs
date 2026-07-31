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

    [Header("Synergy Icons")]
    // 카드 정보 창의 시너지 아이콘 줄. 아이콘을 누르면 SynergyExplainPopupUI가 뜬다.
    [SerializeField] Transform synergyIconRoot;
    [SerializeField] GameObject synergyIconPrefab;

    [SerializeField] RectTransform keywordPanel;
    [SerializeField] float keywordOffsetX = 300f;

    [Header("커서 기준 카드 위치 보정(px). 손가락에 카드가 가려지면 y를 올린다.")]
    [SerializeField] Vector2 cursorOffset;

    Canvas cachedCanvas;
    RectTransform rectTransform;

    protected override void Awake()
    {
        base.Awake();
        this.rectTransform = (RectTransform)transform;
        this.cachedCanvas = GetComponentInParent<Canvas>();
    }

    /// <summary>지금 켜져 있는 카드 콘텐츠 rect. 루트가 아니라 **이쪽**을 옮겨야 한다 —
    /// 루트는 화면 전체 stretch(0,0~1,1)라 rect.size가 곧 캔버스 크기이고, 그 크기로 clamp하면
    /// 허용 범위가 [0,0]으로 붕괴해 무슨 좌표를 넣든 정중앙에 붙는다(원래 증상).</summary>
    RectTransform ContentRect
    {
        get
        {
            GameObject t_go = this.cardElementData != null && this.cardElementData.mod != CardElementMod.Full
                ? this.simpleCardContents : this.fullCardContents;
            return t_go != null ? t_go.transform as RectTransform : null;
        }
    }

    void PlaceAtCursor()
    {
        if (this.cachedCanvas == null) return;

        RectTransform t_content = ContentRect;
        if (t_content == null) return;

        RectTransform t_canvasRect = (RectTransform)this.cachedCanvas.transform;
        Camera t_cam = this.cachedCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : this.cachedCanvas.worldCamera;

        LayoutRebuilder.ForceRebuildLayoutImmediate(t_content);

        // 루트가 캔버스 전체 stretch라 루트 로컬 좌표계 = 캔버스 중심 기준. 콘텐츠 앵커도 중앙(0.5,0.5)이므로
        // 여기서 나온 값을 anchoredPosition에 그대로 넣으면 된다.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            this.rectTransform, Input.mousePosition, t_cam, out Vector2 t_pos);
        t_pos += this.cursorOffset;

        // 화면 밖으로 안 나가게 clamp. 콘텐츠가 축소돼 있을 수 있으므로(CardAutoScale 등) 실제 표시 크기로 잰다.
        Vector2 t_scale = new Vector2(Mathf.Abs(t_content.localScale.x), Mathf.Abs(t_content.localScale.y));
        Vector2 t_size  = Vector2.Scale(t_content.rect.size, t_scale);
        Vector2 t_half  = t_canvasRect.rect.size * 0.5f;
        t_pos.x = ClampAxis(t_pos.x, t_half.x, t_size.x, t_content.pivot.x);
        t_pos.y = ClampAxis(t_pos.y, t_half.y, t_size.y, t_content.pivot.y);

        t_content.anchoredPosition = t_pos;

        if (this.keywordPanel != null)
        {
            bool t_goLeft = Input.mousePosition.x > Screen.width * 0.5f;
            Vector2 t_kpos = this.keywordPanel.anchoredPosition;
            t_kpos.x = t_goLeft ? -this.keywordOffsetX : this.keywordOffsetX;
            this.keywordPanel.anchoredPosition = t_kpos;
        }
    }

    // 콘텐츠가 캔버스보다 크면 min > max가 되어 Clamp가 뒤집힌 결과를 낸다 → 그 축은 중앙 고정.
    static float ClampAxis(float _v, float _half, float _size, float _pivot)
    {
        float t_min = -_half + _size * _pivot;
        float t_max =  _half - _size * (1f - _pivot);
        return t_min > t_max ? 0f : Mathf.Clamp(_v, t_min, t_max);
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
        SynergyIconStrip.Build(this.cardData, this.synergyIconRoot, this.synergyIconPrefab);
    }

    /// <summary>설명 목록. **시너지를 먼저, 키워드를 나중에** 깐다 — 시너지가 카드의 정체성에
    /// 더 가까워서 위에 오는 게 읽기 좋다. 같은 행 프리팹(KeywordExplainItem)을 공용으로 쓴다.</summary>
    void RefreshKeywordList(CardData _card)
    {
        if (this.keywordListRoot == null || this.keywordExplainItemPrefab == null) return;

        foreach (Transform t_child in this.keywordListRoot)
            Destroy(t_child.gameObject);

        if (_card == null) return;

        // 1) 시너지 (키워드보다 먼저)
        if (_card.synergies != null)
        {
            var t_seen = new HashSet<SynergyData>();
            foreach (SynergyData t_syn in _card.synergies)
            {
                if (t_syn == null || !t_seen.Add(t_syn)) continue;   // 중복 나열 방어
                var t_row = Instantiate(this.keywordExplainItemPrefab, this.keywordListRoot);
                t_row.GetComponent<KeywordExplainItem>()?.Init(
                    t_syn.activeIcon, SynergyText.Name(t_syn), t_syn.effectDescription,
                    SynergyIconStrip.IconPadCompensation);   // 시너지 PNG 투명 여백 보정(키워드 행과 크기 맞춤)
            }
        }

        // 2) 키워드
        if (this.keywordIconConfig == null) return;
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