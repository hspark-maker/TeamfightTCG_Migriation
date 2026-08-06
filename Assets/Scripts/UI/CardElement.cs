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
public class CardElement : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
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

    // 프레임 키워드 장식. 인게임 CardView.KeywordFrame과 같은 구조·같은 판정(CardVisualRules.TraitKeywords) —
    // 규칙을 복제하지 않고 같은 함수를 부른다. 안 쓸 장식은 배열에서 빼면 그냥 안 켜진다.
    // 이름 매칭이 아니라 참조 배선인 이유: 오브젝트 이름을 바꿔도 조용히 꺼지지 않게.
    [System.Serializable]
    public struct KeywordFrame
    {
        public CardKeyword keyword;
        public GameObject  overlay;
    }

    [Header("Frame Decorations")]
    [SerializeField] KeywordFrame[] keywordFrames;

    [Header("Keyword Icons")]
    [SerializeField] Transform keywordIconContainer;
    [SerializeField] GameObject keywordIconPrefab;
    // true면 키워드 아이콘 줄만 그리고 시너지 아이콘은 표시하지 않는다(CardView.keywordIconsUseSynergySlot과 같은 규칙).
    // 인게임 CardView는 좌하단 세로열 한 자리를 둘이 공유하므로, "그 자리의 주인"을 이 스위치 하나가 정한다.
    // false면 종전대로 키워드 줄 + 시너지 줄 둘 다.
    [SerializeField] bool keywordIconsUseSynergySlot;

    [Header("Synergy Icons")]
    // 미배선이면 keywordIconContainer에 이어붙인다(전용 자리를 만들기 전에도 바로 보이게).
    [SerializeField] Transform synergyIconContainer;
    [SerializeField] GameObject synergyIconPrefab;   // 미배선이면 keywordIconPrefab 재사용

    [SerializeField] GameObject disabledOverlay;

    public Action<CardData, PointerEventData> onBeginDrag;
    public Action<PointerEventData> onDrag;
    public Action<PointerEventData> onEndDrag;

    /// <summary>누르는 동안만 보여줄 것(카드 상세 등)의 켜기/끄기. 손을 떼거나 카드 밖으로 벗어나면 끈다.
    /// 클릭(onClick)이 아니라 누름 단위인 이유: "누르면 뜨고 떼면 사라진다"가 목록에서 훑어보기 좋다.</summary>
    public Action<CardData> onPressStart;
    public Action           onPressEnd;

    bool pressing;   // 떼기/벗어남에서 중복으로 끄지 않게(콜백이 두 번 불리면 다른 카드의 창까지 닫는다)

    // 전투 인스턴스로 바인딩됐다면 그쪽이 키워드의 진실원이다 — 적 카드에 내 강화 해금을 얹지 않기 위함.
    // CardData로 바인딩되면 null이 되어 아웃게임(내 카드) 해금 규칙을 탄다.
    CardInstance boundInstance;

    public void Init(CardData _card, CardElementMod _mod = CardElementMod.Full, int _displayHp = -1)
    {
        this.boundInstance = null;   // CardData 바인딩 = 아웃게임(내 카드) 기준으로 되돌린다

        if (_card == null)
        {
            EmptySlotInit();
            return;
        }

        this.Data = _card;
        this.nameText.text = _card.name;
        if (_mod == CardElementMod.Full)
        {
            // 아트 소스는 CardVisualRules 단독(battleImage → fullImage → portrait). fullImage 직접 참조 금지 —
            // 프레임이 인게임 CardView와 같은 세로형이라 비율이 다른 fullImage를 쓰면 카드마다 잘림이 갈라진다.
            this.cardPortrait.sprite = CardVisualRules.PickCardArt(_card);
            //this.explainText.text = _card.cardExplain;
            // 호출부가 값을 정했으면 그대로(전투는 인스턴스 현재 체력을 넘긴다).
            // 안 정했으면 = CardData만 아는 아웃게임 경로라 강화 반영 최대 체력이 맞다.
            // 적 카드는 이 길로 오지 않는다 — 전투 팝업은 인스턴스를 넘기므로 _displayHp가 항상 채워진다.
            this.hpText.text = (_displayHp >= 0 ? _displayHp : DeckPower.MaxHpOf(_card)).ToString();
        }
        else
        {
            this.cardPortrait.sprite = _card.portrait;
        }

        RefreshKeywordIcons(_card);
        RefreshKeywordFrames(_card);
        RefreshSynergyIcons(_card);
    }

    public void Init(CardInstance _instance, CardElementMod _mod = CardElementMod.Full)
    {
        Init(_instance?.data, _mod, _instance?.hp ?? -1);
        // Init(CardData)가 null로 지운 뒤에 다시 세운다 — 순서가 뒤집히면 인스턴스 기준이 날아간다.
        this.boundInstance = _instance;
        RefreshKeywordIcons(_instance?.data);
    }

    void RefreshKeywordIcons(CardData _card)
    {
        if (this.keywordIconContainer == null || this.keywordIconPrefab == null) return;

        foreach (Transform t_child in this.keywordIconContainer)
            Destroy(t_child.gameObject);

        KeywordIconConfig t_config = DataLibrary.instance?.keywordIconConfig;
        if (t_config == null) return;

        // 정보창 키워드 규칙은 CardVisualRules 단독. 인스턴스가 있으면(전투 카드) 그 인스턴스의 값이 정답이다 —
        // CardData 경로는 내 강화 해금을 태우므로 적 카드에 쓰면 안 된다.
        CardKeyword t_allKeywords = this.boundInstance != null
            ? CardVisualRules.InfoKeywords(this.boundInstance)
            : CardVisualRules.InfoKeywords(_card);
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

    // 프레임 키워드 장식 on/off. 판정 기준은 CardView.RefreshKeywordFrames와 동일한 TraitKeywords —
    // 즉 표식(Mark)은 아이콘 줄엔 없어도 프레임엔 뜬다(그 차이의 단일 선언 지점은 CardVisualRules.IconRowExcluded).
    void RefreshKeywordFrames(CardData _card)
    {
        if (this.keywordFrames == null) return;

        CardKeyword t_keywords = CardVisualRules.TraitKeywords(_card);

        foreach (KeywordFrame t_frame in this.keywordFrames)
        {
            if (t_frame.overlay == null) continue;
            // None 배선은 항상 꺼짐 — HasFlag(None)은 늘 true라 그대로 두면 모든 카드에서 켜진다.
            bool t_on = t_frame.keyword != CardKeyword.None && (t_keywords & t_frame.keyword) != 0;
            t_frame.overlay.SetActive(t_on);
        }
    }

    /// <summary>시너지 아이콘 줄. 전용 컨테이너가 없으면 키워드 아이콘 뒤에 이어붙인다
    /// (그 경우 클리어는 RefreshKeywordIcons가 이미 했으므로 여기서 또 비우면 안 된다).</summary>
    void RefreshSynergyIcons(CardData _card)
    {
        // 그 자리를 키워드 아이콘이 쓰는 모드면 시너지 아이콘을 아예 만들지 않는다(겹침 방지). CardView와 동일.
        if (this.keywordIconsUseSynergySlot) { ClearIcons(this.synergyIconContainer); return; }

        bool       t_dedicated = this.synergyIconContainer != null;
        Transform  t_parent    = t_dedicated ? this.synergyIconContainer : this.keywordIconContainer;
        GameObject t_prefab    = this.synergyIconPrefab != null ? this.synergyIconPrefab : this.keywordIconPrefab;
        SynergyIconStrip.Build(_card, t_parent, t_prefab, _clearFirst: t_dedicated);
    }

    void ShowKeywordExplain(KeywordIconConfig.Entry _entry, RectTransform _iconRect)
    {
        UIPoolManager.Instance?.AddOrUpdateUI<KeywordExplainPopupUI>(new KeywordExplainData
        {
            icon        = _entry.icon,
            displayName = _entry.displayName,
            explain     = _entry.explain,
            iconRect    = _iconRect,
        });
    }

    void HideKeywordExplain() => UIPoolManager.Instance?.HideUI<KeywordExplainPopupUI>();

    void EmptySlotInit()
    {
        this.Data = null;
        this.nameText.text = "빈 슬롯";
        this.cardPortrait.sprite = this.emptySlot;
        // 슬롯을 비울 때 이전 카드의 아이콘이 남지 않게 정리(덱 편성에서 슬롯 교체 시 발생).
        ClearIcons(this.keywordIconContainer);
        ClearIcons(this.synergyIconContainer);
        RefreshKeywordFrames(null);   // 빈 슬롯: 프레임 장식도 전부 끈다(아이콘 줄과 동일한 정보 은닉).
    }

    static void ClearIcons(Transform _container)
    {
        if (_container == null) return;
        foreach (Transform t_child in _container)
            Destroy(t_child.gameObject);
    }

    /// <summary>_replace=true면 기존 리스너를 지우고 건다. 목록 항목처럼 <b>풀에서 재사용되는</b> 요소는
    /// 이걸 써야 한다 — 계속 더하기만 하면 한 번 눌러 예전 카드의 동작까지 같이 터진다.</summary>
    public void SetButtonAction(Action _buttonAction, bool _replace = false)
    {
        if (_replace) this.button.onClick.RemoveAllListeners();
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

    // ── 누름(보는 동안만 표시) ──────────────────────────────────────────
    // 배선이 없으면(onPressStart null) 아무 일도 하지 않는다 — 덱 편성처럼 드래그로 쓰는 화면은 그대로다.

    public void OnPointerDown(PointerEventData _eventData)
    {
        if (!this.isInteractable || this.onPressStart == null) return;
        this.pressing = true;
        this.onPressStart.Invoke(this.Data);
    }

    public void OnPointerUp(PointerEventData _eventData)   => EndPress();
    public void OnPointerExit(PointerEventData _eventData) => EndPress();

    void OnDisable() => EndPress();   // 목록이 닫히거나 항목이 꺼져도 창이 남지 않게

    void EndPress()
    {
        if (!this.pressing) return;
        this.pressing = false;
        this.onPressEnd?.Invoke();
    }
}
