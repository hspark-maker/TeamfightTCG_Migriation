using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class PooledCardElement : PooledUIBase
{
    PooledCardElementData cardElementData;
    CardData cardData;

    // 이 창은 전체 카드 한 가지만 띄운다 — 작은(Simple) 모드는 폐지했다.
    [SerializeField] GameObject     fullCardContents;
    // 카드 그림 한 장의 정본은 CardVisualView다(도감·덱편집·팩개봉과 같은 컴포넌트) —
    // 정보창만 다른 구현으로 그리면 같은 카드가 화면마다 달라 보인다.
    [SerializeField] CardVisualView fullCardElement;

    [SerializeField] Transform keywordListRoot;
    [SerializeField] GameObject keywordExplainItemPrefab;
    [SerializeField] KeywordIconConfig keywordIconConfig;

    [Header("Synergy Icons")]
    // 카드 정보 창의 시너지 아이콘 줄. 아이콘을 누르면 ExplainPopupUI가 뜬다.
    [SerializeField] Transform synergyIconRoot;
    [SerializeField] GameObject synergyIconPrefab;

    [SerializeField] RectTransform keywordPanel;
    [SerializeField] float keywordOffsetX = 300f;

    [Header("커서 기준 카드 위치 보정(px). 손가락에 카드가 가려지면 y를 올린다.")]
    [SerializeField] Vector2 cursorOffset;

    [Header("롱프레스 누르는 동안 차오르는 배경 어둡기")]
    [Tooltip("전체 화면 덮개. 누르는 진행도(0~1)에 비례해 알파가 오른다")]
    [SerializeField] Image dimBg;
    [Tooltip("완전히 눌렀을 때의 알파. 0이면 어둡기 연출 없음")]
    [SerializeField] float dimMaxAlpha = 0.6f;
    [Tooltip("손을 뗐을 때 어둡기가 사라지는 시간(초). 들어올 때보다 빨라야 손 뗀 느낌이 산다")]
    [SerializeField] float dimFadeOut = 0.12f;

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
            return this.fullCardContents != null ? this.fullCardContents.transform as RectTransform : null;
        }
    }

    /// <summary>커서 자리에서 출발하되, **놓을 자리는 카드+설명판을 합친 실제 화면 범위**로 정한다.
    ///
    /// 예전에는 카드만 화면 안으로 밀고, 설명판 좌우는 "커서가 화면 오른쪽인가"로만 뒤집었다 —
    /// 설명판이 카드보다 넓어서 커서가 가운데 근처면 뒤집어도 그대로 화면 밖으로 잘렸다.
    /// 지금은 좌/우 두 배치를 다 재서 **화면 밖으로 덜 나가는 쪽**을 고르고, 합친 범위를 화면 안으로 민다
    /// (커서 쪽은 동점일 때만 쓰는 취향값이다).
    ///
    /// 크기는 전부 실측이다 — 콘텐츠·설명판이 축소돼 있을 수 있고 프리팹 값이 바뀌어도 따라가야 한다.</summary>
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
            this.rectTransform, Input.mousePosition, t_cam, out Vector2 t_cursor);
        t_cursor += this.cursorOffset;

        Vector2 t_scale = new Vector2(Mathf.Abs(t_content.localScale.x), Mathf.Abs(t_content.localScale.y));
        Vector2 t_cardSize = Vector2.Scale(t_content.rect.size, t_scale);
        Vector2 t_half     = t_canvasRect.rect.size * 0.5f;

        // 카드 범위(콘텐츠 중심 기준). 피벗이 가운데가 아닐 수 있어 한쪽씩 잰다.
        Vector2 t_cardMin = -Vector2.Scale(t_cardSize, t_content.pivot);
        Vector2 t_cardMax = t_cardMin + t_cardSize;

        if (!PanelInUse(t_content))
        {
            t_content.anchoredPosition = ClampBounds(t_cursor, t_cardMin, t_cardMax, t_half);
            return;
        }

        bool  t_preferLeft = Input.mousePosition.x > Screen.width * 0.5f;   // 동점일 때만 쓰는 취향값
        bool  t_bestLeft     = t_preferLeft;
        Vector2 t_bestPos    = t_cursor;
        float t_bestOverflow = float.MaxValue;

        for (int i = 0; i < 2; i++)
        {
            bool  t_left   = i == 0 ? t_preferLeft : !t_preferLeft;
            float t_panelX = t_left ? -this.keywordOffsetX : this.keywordOffsetX;

            PanelBounds(t_content, t_scale, t_panelX, out Vector2 t_panelMin, out Vector2 t_panelMax);
            Vector2 t_min = Vector2.Min(t_cardMin, t_panelMin);
            Vector2 t_max = Vector2.Max(t_cardMax, t_panelMax);

            Vector2 t_pos  = ClampBounds(t_cursor, t_min, t_max, t_half);
            float   t_over = Overflow(t_pos, t_min, t_max, t_half);

            // 먼저 잰 쪽(커서 취향)이 이긴다 — 반대쪽이 확실히 덜 잘릴 때만 뒤집는다.
            if (t_over >= t_bestOverflow - 0.01f) continue;
            t_bestOverflow = t_over;
            t_bestPos      = t_pos;
            t_bestLeft     = t_left;
        }

        Vector2 t_kpos = this.keywordPanel.anchoredPosition;
        t_kpos.x = t_bestLeft ? -this.keywordOffsetX : this.keywordOffsetX;
        this.keywordPanel.anchoredPosition = t_kpos;

        t_content.anchoredPosition = t_bestPos;
    }

    /// <summary>지금 켜져 있는 콘텐츠에 딸린 설명판이고, 실제로 채울 줄이 있는가.
    /// 줄이 없으면(시너지·키워드가 하나도 없는 카드) 빈 판 자리까지 잡느라 카드가 화면 구석으로 밀린다.</summary>
    bool PanelInUse(RectTransform _content)
    {
        if (this.keywordPanel == null || !this.keywordPanel.IsChildOf(_content)) return false;
        if (!this.keywordPanel.gameObject.activeInHierarchy) return false;
        return this.keywordListRoot == null || this.keywordListRoot.childCount > 0;
    }

    /// <summary>설명판 범위를 **콘텐츠 중심 기준 화면 단위**로 낸다.
    ///
    /// 판 자신의 rect로 재면 모자란다 — 레이아웃 그룹이 자식 폭을 건드리지 않는 설정이라 설명 줄이
    /// 판보다 넓게 삐져나온다. 그래서 자식까지 감싼 실측(CalculateRelativeRectTransformBounds)을 쓴다.
    /// 설명판은 콘텐츠의 자식이라 콘텐츠 축척이 한 번 더 곱해진다 — 빠뜨리면 축소된 카드에서 과대평가한다.</summary>
    void PanelBounds(RectTransform _content, Vector2 _contentScale, float _panelX,
                     out Vector2 _min, out Vector2 _max)
    {
        RectTransform t_panel = this.keywordPanel;
        Bounds  t_bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(t_panel, t_panel);
        Vector2 t_scale  = new Vector2(Mathf.Abs(t_panel.localScale.x), Mathf.Abs(t_panel.localScale.y));

        // anchoredPosition = 앵커 기준 피벗 위치. 앵커가 가운데가 아닐 수도 있어 콘텐츠 중심 기준으로 옮긴다.
        Vector2 t_anchor = (t_panel.anchorMin + t_panel.anchorMax) * 0.5f - new Vector2(0.5f, 0.5f);
        Vector2 t_origin = new Vector2(_panelX, t_panel.anchoredPosition.y)
                         + Vector2.Scale(t_anchor, _content.rect.size);

        // bounds는 판의 피벗 원점 기준이므로 그대로 더하면 된다.
        _min = Vector2.Scale(t_origin + Vector2.Scale((Vector2)t_bounds.min, t_scale), _contentScale);
        _max = Vector2.Scale(t_origin + Vector2.Scale((Vector2)t_bounds.max, t_scale), _contentScale);
    }

    /// <summary>범위(_min~_max, 중심 기준)를 통째로 화면 안에 넣는 중심 좌표.
    /// 범위가 화면보다 크면 넣을 방법이 없으므로 그 축은 가운데 정렬한다(양쪽으로 고르게 잘리게).</summary>
    static Vector2 ClampBounds(Vector2 _pos, Vector2 _min, Vector2 _max, Vector2 _half)
        => new Vector2(ClampAxis(_pos.x, _min.x, _max.x, _half.x),
                       ClampAxis(_pos.y, _min.y, _max.y, _half.y));

    static float ClampAxis(float _v, float _min, float _max, float _half)
    {
        float t_lo = -_half - _min;
        float t_hi =  _half - _max;
        return t_lo > t_hi ? -(_min + _max) * 0.5f : Mathf.Clamp(_v, t_lo, t_hi);
    }

    /// <summary>그 자리에 놨을 때 화면 밖으로 삐져나가는 총량(px). 좌우 배치 중 덜 잘리는 쪽을 고르는 데 쓴다.</summary>
    static float Overflow(Vector2 _pos, Vector2 _min, Vector2 _max, Vector2 _half)
        => OverflowAxis(_pos.x, _min.x, _max.x, _half.x) + OverflowAxis(_pos.y, _min.y, _max.y, _half.y);

    static float OverflowAxis(float _v, float _min, float _max, float _half)
        => Mathf.Max(0f, -_half - (_v + _min)) + Mathf.Max(0f, (_v + _max) - _half);

    /// <summary>누르는 진행도(0~1)를 배경 어둡기로 바꾼다. 롱프레스가 차오르는 동안 매 프레임 불린다 —
    /// 여기서 카드 정보를 다시 만들지 않는다(줄 재생성이 매 프레임 돌면 프레임이 튄다).</summary>
    public void SetDim(float _progress)
    {
        if (this.dimBg == null) return;

        this.dimBg.DOKill();   // 사라지던 중에 다시 눌렀으면 그 트윈이 새 값을 덮어쓰지 않게

        float t_alpha = Mathf.Clamp01(_progress) * this.dimMaxAlpha;
        Color t_color = this.dimBg.color;
        t_color.a = t_alpha;
        this.dimBg.color   = t_color;
        this.dimBg.enabled = t_alpha > 0.001f;   // 완전 투명일 때 굳이 그리지 않는다
    }

    public override void Initialization(UIData _data)
    {
        this.cardElementData = _data as PooledCardElementData;
        if (this.cardElementData == null) return;
        this.cardData = this.cardElementData.card;

        SetDim(this.cardElementData.dimProgress);

        // 아직 롱프레스가 차는 중 = 배경만. 카드/설명은 만들지도, 켜지도 않는다.
        if (this.cardElementData.dimOnly) return;

        // 이 창은 전체 카드 한 가지만 띄운다(작은 모드 폐지) — mod 값이 뭐로 오든 Full로 그린다.
        // 전투에서 열렸으면 인스턴스가 값의 진실원이다(현재 체력·그 카드가 실제로 가진 키워드).
        if (this.cardElementData.instance != null)
            this.fullCardElement.Bind(this.cardElementData.instance);
        else
            this.fullCardElement.Bind(this.cardData, _owned: true);
        this.fullCardContents.SetActive(true);


        RefreshKeywordList(this.cardData);
        // 시너지 아이콘 줄: 튜토리얼 미도입 구간에선 아예 만들지 않는다(적용도 안 된 효과라 설명할 게 없다).
        if (TutorialConfig.SynergyVisible)
            SynergyIconStrip.Build(this.cardData, this.synergyIconRoot, this.synergyIconPrefab,
                                   this.cardElementData.synergy);
        else
            SynergyIconStrip.Clear(this.synergyIconRoot);
    }

    /// <summary>설명 목록. 순서는 <b>활성 시너지 → 키워드 → 비활성 시너지</b>다.
    ///
    /// 지금 켜져 있는 것(활성 시너지·보유 키워드)이 위에 모이고, <b>아직 못 켠 시너지는 목록 맨 아래</b>로 내린다 —
    /// 회색으로만 구분하면 켜진 것 사이에 섞여 있어 "지금 이 카드가 무엇을 하는가"를 한눈에 못 읽는다.</summary>
    void RefreshKeywordList(CardData _card)
    {
        if (this.keywordListRoot == null || this.keywordExplainItemPrefab == null) return;

        foreach (Transform t_child in this.keywordListRoot)
            Destroy(t_child.gameObject);

        if (_card == null) return;

        // 시너지를 활성/비활성으로 가른다. 순서·중복 제거는 아이콘 줄과 같은 규칙(CardVisualRules) —
        // 두 곳이 갈리면 같은 카드인데 아이콘 순서와 설명 순서가 달라진다.
        var t_active   = new List<SynergyData>();
        var t_inactive = new List<SynergyData>();

        if (_card.synergies != null && TutorialConfig.SynergyVisible)
        {
            SynergyState t_state = this.cardElementData?.synergy;
            foreach (SynergyData t_syn in CardVisualRules.CollectSynergyBadges(
                         _card.synergies, t_state, _card.synergies.Length))
            {
                if (t_syn == null) continue;
                bool t_on = t_state == null || CardVisualRules.IsSynergyActive(t_state, t_syn);
                (t_on ? t_active : t_inactive).Add(t_syn);
            }
        }

        // 1) 활성 시너지
        foreach (SynergyData t_syn in t_active) AddSynergyRow(t_syn, _active: true);

        // 2) 키워드 — 카드가 실제로 가진 것이라 항상 원래 색이다(활성 개념이 없다).
        if (this.keywordIconConfig != null)
        {
            foreach (CardKeyword t_kw in System.Enum.GetValues(typeof(CardKeyword)))
            {
                if (t_kw == CardKeyword.None) continue;
                if (!_card.HasKeyword(t_kw)) continue;
                if (!this.keywordIconConfig.TryGetEntry(t_kw, out var t_entry)) continue;

                var t_obj = Instantiate(this.keywordExplainItemPrefab, this.keywordListRoot);
                t_obj.GetComponent<KeywordExplainItem>()?.Init(t_entry.icon, t_entry.displayName, t_entry.explain);
            }
        }

        // 3) 아직 못 켠 시너지 — 맨 아래.
        foreach (SynergyData t_syn in t_inactive) AddSynergyRow(t_syn, _active: false);
    }

    void AddSynergyRow(SynergyData _synergy, bool _active)
    {
        Sprite t_icon = _active ? _synergy.activeIcon
                                : (_synergy.inactiveIcon != null ? _synergy.inactiveIcon : _synergy.activeIcon);

        var t_row = Instantiate(this.keywordExplainItemPrefab, this.keywordListRoot);
        t_row.GetComponent<KeywordExplainItem>()?.Init(
            t_icon, SynergyText.Name(_synergy), _synergy.effectDescription,
            SynergyIconStrip.IconPadCompensation,   // 시너지 PNG 투명 여백 보정(키워드 행과 크기 맞춤)
            _active);
    }

    public override void Show()
    {
        bool t_dimOnly = this.cardElementData != null && this.cardElementData.dimOnly;

        this.fullCardContents.SetActive(!t_dimOnly);
        this.isShow = true;

        if (!t_dimOnly) PlaceAtCursor();
    }

    /// <summary>둘 다 끄고 어둡기는 짧게 페이드아웃. 어느 모드로 떴는지 따지지 않는다 —
    /// 눌렀다 만 경우(배경만 떠 있던 상태)에도 같은 경로로 정리돼야 한다.
    ///
    /// 카드는 즉시 사라지고 배경만 남아 옅어진다 — 손을 뗀 순간 정보는 끝난 것이고,
    /// 배경까지 같이 끊으면 화면이 툭 끊긴다.</summary>
    public override void Hide()
    {
        this.fullCardContents.SetActive(false);
        this.isShow = false;

        if (this.dimBg == null) return;

        this.dimBg.DOKill();
        if (this.dimFadeOut <= 0f || this.dimBg.color.a <= 0.001f) { SetDim(0f); return; }

        this.dimBg.DOFade(0f, this.dimFadeOut)
            .SetLink(gameObject)                              // 파괴된 뒤 접근 방지(트윈 수명 규약)
            .OnComplete(() => this.dimBg.enabled = false);    // 다 옅어지면 그리기까지 끈다
    }
}

public class PooledCardElementData : UIData
{
    public CardData card;

    /// <summary>전투 카드면 그 인스턴스. 있으면 키워드·체력의 진실원이 이쪽이다 —
    /// 적 카드 정보창에 내 강화 해금이 얹히는 것을 막는 유일한 구분이다.</summary>
    public CardInstance instance;

    /// <summary>true면 배경 어둡기만 — 롱프레스가 차오르는 중이라 카드는 아직 띄우지 않는다.</summary>
    public bool  dimOnly;
    /// <summary>배경 어둡기 진행도(0~1). 팝업이 실제로 뜰 때는 1(완전히 어두움).</summary>
    public float dimProgress = 1f;

    /// <summary>지금 필드의 확정 시너지 스냅샷. 있으면 활성 시너지가 앞, 비활성이 회색으로 뒤에 온다.
    /// 없으면(도감 등 필드 밖 호출) 전부 활성으로 그린다.</summary>
    public SynergyState synergy;
}