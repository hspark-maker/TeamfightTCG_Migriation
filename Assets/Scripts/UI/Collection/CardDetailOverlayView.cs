using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

// 로비 컬렉션 탭의 카드 상세 오버레이(CardDetailOverlay.prefab 루트에 부착).
// 카드 타일을 길게 누르면 열리고, 누른 카드의 이름·체력·키워드·시너지를 채운다.
// 닫기 버튼은 두지 않는다 — 오버레이 아무 곳이나 탭하면 닫힌다(조작 바 tapCloseExclude만 제외).
//
// 인게임 카드 정보창(PooledCardElement)과 달리 풀드 UI가 아니라 로비 씬에 직접 배치한다 —
// 로비 전용 풀스크린 한 장이라 Addressables("UIPrefab" 라벨) 등록까지 갈 이유가 없다(PackOpenOverlay와 같은 결).
//
// 표시 규칙은 복제하지 않는다: 카드 그림 한 장은 CardVisualView.Bind, 시너지 이름은 SynergyText,
// 키워드 아이콘·표시명·설명은 KeywordIconConfig가 정본이다.
public class CardDetailOverlayView : MonoBehaviour, IPointerClickHandler
{
    const string LockedName  = "???";
    const string LockedValue = "?";
    const string NoneValue   = "없음";
    const string NoValue     = "-";

    // 강화가 왜 막혔는지. 상세 패널의 상시 문구와 결과판의 "한 번 더" 아래 문구가 같은 문장을 쓴다.
    const string MaxLevelNotice     = "최고 레벨에 도달했다";
    const string NotAffordableNotice = "골드가 부족하다";

    [Header("배선")]
    [SerializeField] CardVisualView cardView;        // CardArea 안의 CardUIView 인스턴스
    [SerializeField] TMP_Text       powerValueText;  // 체력 수치(프리팹 목업의 "파워" 행을 체력으로 쓴다)

    [Header("성장 (선택 — 미배선이면 성장 표시 없이 지금까지와 동일하게 동작)")]
    [SerializeField] TMP_Text levelValueText;      // 강화 레벨 "Lv 3 / 10"

    [Header("강화 조작 (선택 — 미배선이면 조작 없이 표시만 한다)")]
    [SerializeField] Button     enhanceButton;
    [SerializeField] TMP_Text   enhanceCostText;    // 다음 레벨 골드 비용
    [SerializeField] TMP_Text   successRateText;    // 다음 레벨 성공률(%)
    [Tooltip("지금 왜 막혔는지 알려주는 상시 문구(최고 레벨·잔액 부족).")]
    [SerializeField] TMP_Text   growthNoticeText;

    [Header("강화 연출 (선택 — 미배선이면 연출 없이 지금까지처럼 값만 즉시 갱신)")]
    [SerializeField] CardEnhanceRitualView ritual;

    [Tooltip("연출이 끝난 자리에 뜨는 결과판. 미배선이면 연출이 스스로 걷고 곧바로 상세로 돌아온다.")]
    [SerializeField] EnhanceResultPanelView resultPanel;

    [Header("키워드 섹션")]
    [SerializeField] GameObject keywordSection;      // 칩이 0개면 통째로 숨긴다
    [SerializeField] Transform  keywordChipRoot;     // 칩이 깔리는 List 노드
    [SerializeField] TMP_Text   keywordDescText;     // 칩들의 설명을 줄바꿈으로 이어 붙인다

    [Header("시너지 섹션")]
    [SerializeField] GameObject synergySection;
    [SerializeField] Transform  synergyChipRoot;
    [SerializeField] TMP_Text   synergyDescText;

    [Header("공용")]
    // 키워드/시너지 칩 공용 프리팹. 인게임 정보창의 설명 행과 같은 컴포넌트를 쓰되,
    // 칩에는 설명 줄이 없으므로 프리팹의 explainText를 미배선으로 비워둔다(Init이 null 가드).
    [SerializeField] KeywordExplainItem chipPrefab;
    [SerializeField] KeywordIconConfig  keywordIconConfig;
    [SerializeField] PopupTransition    transition = new PopupTransition();

    [Header("탭해서 닫기")]
    [Tooltip("탭해도 닫히지 않을 영역(BottomBar). 미배선이면 바의 빈 곳 탭도 닫기로 샌다.")]
    [SerializeField] RectTransform tapCloseExclude;
    
    [Tooltip("좌우 스와이프 감지. 오버레이 전면을 덮는 raycastTarget Graphic 위에 올려야 한다.")]
    [SerializeField] HorizontalSwipeDetector swipeDetector;

    [Header("전환 연출 (선택 — slideTarget 미배선이면 트윈 없이 즉시 교체)")]
    [Tooltip("좌우로 밀렸다 들어올 노드. 딤·닫기버튼까지 흔들리지 않게 카드 본문 패널을 물릴 것.\n" +
             "· LayoutGroup/ContentSizeFitter에 드리븐되지 않는 노드여야 한다 — 기준 좌표를 1회만 캡처하므로 " +
             "레이아웃이 매 프레임 좌표를 되돌리면 슬라이드가 떨린다.\n" +
             "· 페이드까지 받으려면 이 노드에 CanvasGroup을 미리 저작해 두는 편이 좋다(없으면 런타임에 추가한다).")]
    [SerializeField] RectTransform slideTarget;
    [SerializeField] float slideDistance = 120f;
    [SerializeField] float slideDuration = 0.18f;

    static CardDetailOverlayView s_instance;
    static bool s_missingWarned;

    // 지금 넘겨볼 수 있는 카드들과 그 안에서의 위치. 목록은 호출처가 쥔 것을 참조로 들고 있을 뿐이라
    // 여기서 복사하거나 수정하지 않는다(도감 재빌드가 같은 List를 재사용해도 최신 내용이 그대로 보인다).
    IReadOnlyList<CardData> m_cards;
    int m_index;

    // 전환 트윈의 중간 지점에서 갈아끼울 카드. 트윈이 잘리면 콜백이 오지 않으므로 잘라내는 쪽(CancelSlide)이
    // 이 카드를 버린다(취소 경로는 모두 직후에 다른 카드가 확정된다). 트윈 자체는 핸들이 아니라 id(this)로 찾아 자른다.
    CardData m_pendingCard;

    // slideTarget의 authoring 좌표·페이드 대상. 트윈이 여기서 출발해 여기로 돌아온다.
    CanvasGroup m_slideGroup;
    float       m_slideBaseX;
    bool        m_slideBaseCaptured;

    // 강화 연출 중에는 값 갱신을 미룬다 — TryEnhance가 판정·세이브·통지를 동기로 끝내므로,
    // 그대로 두면 연출이 시작하기도 전에 Lv·HP가 새 값으로 튀어 공개할 것이 남지 않는다.
    // 결과판이 떠 있는 동안까지 켜져 있다(연출 → 결과판 → 복귀 전체가 한 덩이의 "연출 중"이다).
    bool m_ritualPlaying;

    // 결과판의 "한 번 더". 무대가 돌아오기 전에 다음 연출을 시작하면 두 연출이 같은 노드를 두고 싸운다 →
    // 복귀가 끝나는 시점까지 눌린 사실만 들고 있는다.
    bool m_retryQueued;

    /// <summary>_card의 상세를 띄운다. 오버레이가 씬에 없으면 경고 1회 후 무시.
    /// 넘길 이웃이 없는 1장짜리 목록으로 취급한다(화살표·스와이프가 꺼진다).</summary>
    public static void Open(CardData _card)
    {
        if (_card == null) return;

        Open(new[] { _card }, 0);
    }

    /// <summary>_cards[_index]의 상세를 띄우고, 좌우로 같은 목록 안을 순환하며 넘겨볼 수 있게 한다.
    /// _cards는 "화면에 보이는 순서" 그대로여야 한다 — 넘기는 방향과 도감 배열이 어긋나면 길을 잃는다.
    /// null 슬롯(미authoring 카드)은 그대로 넘겨도 된다. 넘기기가 알아서 건너뛴다.</summary>
    public static void Open(IReadOnlyList<CardData> _cards, int _index)
    {
        if (_cards == null || _cards.Count == 0) return;

        CardDetailOverlayView t_view = Resolve();
        if (t_view == null) return;

        t_view.Show(_cards, _index);
    }

    public static void Close()
    {
        // 열린 적이 없으면 닫을 것도 없다 — 여기서 Resolve를 돌려 경고를 띄울 이유가 없다.
        if (s_instance == null) return;
        s_instance.Hide();
    }

    /// <summary>타일에 "상세 열기 + 목록 안에서 좌우로 넘기기"를 배선한다.
    /// _index는 _cards 안에서 이 타일의 자리(= 화면에 보이는 순서상 위치).
    /// 타일 프리팹에 LongPressDetector가 아직 안 붙어 있으면 조용히 넘어간다(배선 전 상태).
    ///
    /// 탭 판정을 이 컴포넌트에 맡기는 이유는 <see cref="LongPressDetector.OnTap"/> 주석 참고 —
    /// 도감/생산 타일은 ScrollRect 안에 있어서 스크롤 드래그가 클릭으로 새면 안 된다.
    ///
    /// 목록을 값이 아니라 **참조**로 잡아둔다 — 컨트롤러가 같은 List 인스턴스를 재사용해 다시 채우면
    /// 이미 배선된 타일들도 재배선 없이 최신 내용을 넘겨보게 된다(대신 인덱스 정합은 컨트롤러 책임이다).</summary>
    public static void BindTile(CardVisualView _tile, IReadOnlyList<CardData> _cards, int _index)
    {
        if (_tile == null || _cards == null) return;

        LongPressDetector t_press = _tile.GetComponent<LongPressDetector>();
        if (t_press == null) return;

        // 대입(+= 아님) — 타일이 재사용·재바인딩돼도 이전 콜백이 겹쳐 남지 않는다(CardElement와 같은 관용구).
        t_press.OnTap = () => Open(_cards, _index);
    }

    // 오버레이는 씬에 **비활성**으로 배치된다. 비활성 오브젝트는 Awake가 돌지 않아
    // PackOpenOverlay식 Awake 싱글턴으로는 자신을 등록할 수 없다 → 첫 호출 때 비활성 포함으로 찾아 캐시한다.
    // 씬이 바뀌면 참조가 죽으므로 아래 null 검사에서 자연히 재탐색된다.
    static CardDetailOverlayView Resolve()
    {
        if (s_instance != null) return s_instance;

        s_instance = FindFirstObjectByType<CardDetailOverlayView>(FindObjectsInactive.Include);

        if (s_instance == null && !s_missingWarned)
        {
            s_missingWarned = true;
            Debug.LogError("[CardDetailOverlayView] 현재 씬에 카드 상세 오버레이가 배치되지 않았습니다 — 카드를 길게 눌러도 열리지 않습니다.");
        }

        return s_instance;
    }

    void Awake()
    {
        s_instance = this;

        // 카드 그림 위 탭은 루트의 OnPointerClick으로 오지 않는다 —
        // LongPressDetector가 pointerPress를 가져가 클릭 대상 비교가 어긋난다.
        if (this.cardView != null)
        {
            LongPressDetector t_tap = this.cardView.GetComponent<LongPressDetector>();
            if (t_tap != null) t_tap.OnTap = TapClose;
        }
    }

    // 화살표·스와이프는 Awake가 아니라 여기서 배선한다 — 오버레이는 열 때마다 꺼졌다 켜지므로
    // Awake 한 번으로는 부족하고, Remove 후 Add라 중복 등록도 남지 않는다.
    void OnEnable()
    {

        if (this.enhanceButton != null)
        {
            this.enhanceButton.onClick.RemoveListener(OnEnhancePressed);
            this.enhanceButton.onClick.AddListener(OnEnhancePressed);
        }

        // 대입 — 구독자는 언제나 이 오버레이 하나뿐이다.
        if (this.swipeDetector != null) this.swipeDetector.OnSwipe = Step;

        // 강화 실패에도 통지가 온다 — 핸들러는 "레벨이 올랐다"고 가정하지 않고 값을 다시 읽는다.
        CardGrowthManager.OnGrowthChanged += OnGrowthChanged;

        // 잔액이 바뀌면 버튼 활성이 따라와야 한다(다른 화면·디버그 지급으로도 바뀐다).
        CurrencyManager.OnCurrencyChanged += HandleCurrencyChanged;

        RefreshArrows();
    }

    void OnDisable()
    {
        if (this.swipeDetector != null) this.swipeDetector.OnSwipe = null;

        if (this.enhanceButton != null) this.enhanceButton.onClick.RemoveListener(OnEnhancePressed);

        CardGrowthManager.OnGrowthChanged -= OnGrowthChanged;
        CurrencyManager.OnCurrencyChanged -= HandleCurrencyChanged;

        // 전환 도중에 닫히면 slideTarget이 옆으로 밀린 채·반투명인 채 굳는다 → 다음 열기에 그대로 보인다.
        // pending 카드는 버린다 — 안 보이는 채로 칩을 재생성할 이유가 없고, 씬 언로드 경로에서 Instantiate/Destroy를 도는 건 위험하다.
        CancelSlide();

        // 연출 중에 닫히면 카드가 확대·회색인 채 굳는다. 잘라내되 콜백은 흘러나오므로 유예도 함께 풀린다.
        // 무대를 먼저 자른다 — 잘리며 흘러나오는 공개 콜백이 결과판을 한 번 더 띄우므로, 결과판 정리가 뒤여야 한다.
        this.ritual?.CancelImmediate();
        this.resultPanel?.HideImmediate();
        this.m_retryQueued = false;

        // 퇴장 트윈이 완료 전에 잘렸으면(부모가 먼저 꺼짐) 여기서 마무리해야 다음 열기에 유령 프레임이 안 뜬다.
        this.transition.HandleDisabled(gameObject);
    }

    void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }

    // 켜는 것이 먼저다 — 비활성으로 시작한 오브젝트는 이 시점에 Awake가 돌아 닫기 버튼 배선이 성립한다.
    // 목록·인덱스는 그보다 먼저 확정한다(SetVisible이 유발하는 OnEnable의 RefreshArrows가 이미 최신을 보게).
    void Show(IReadOnlyList<CardData> _cards, int _index)
    {
        // 유효 인덱스를 **확정한 뒤에** 목록을 갈아끼운다 — 전부 null인 목록에서 중도 return하면
        // m_cards만 새 목록이 되고 m_index는 이전 목록 기준으로 남아 화살표·넘기기가 엉뚱한 자리를 가리킨다.
        //
        // 요청 위치가 비었으면(드리프트로 null 슬롯) 가장 가까운 유효 카드로 물러선다 — 빈 상세를 띄우느니 낫다.
        // 탐색이 순환하므로 한 방향만 봐도 목록 전체를 훑는다(전부 null일 때만 -1).
        int t_index = Mathf.Clamp(_index, 0, _cards.Count - 1);
        if (_cards[t_index] == null)
        {
            t_index = FindValidIn(_cards, t_index, 1);
            if (t_index < 0) return;
        }

        this.m_cards = _cards;
        this.m_index = t_index;

        // 곧바로 Apply가 이어지므로 pending은 버린다(중간 카드에 칩을 한 번 더 짓지 않게).
        CancelSlide();
        this.ritual?.CancelImmediate();       // 순서는 OnDisable 주석 참고 — 무대가 먼저다.
        this.resultPanel?.HideImmediate();
        this.m_retryQueued = false;
        this.transition.SetVisible(gameObject, true);
        Apply(CardAt(this.m_index));
        RefreshArrows();
    }

    void Hide()
    {
        // 퇴장 중 입력부터 죽인다 — 닫히는 도중 화살표·스와이프가 전환을 시작하면 close 시퀀스와 같은 노드를 두고 싸운다.
        // 다시 열 때는 SetVisible(true) → OnEnable → RefreshArrows()가 되살린다.
        if (this.swipeDetector != null) this.swipeDetector.Interactable = false;

        this.transition.SetVisible(gameObject, false);
    }

    /// <summary>딤·상세 패널 어디를 탭해도 닫는다. 조작 바(tapCloseExclude)와 카드 그림(Awake 참고)은 제외.</summary>
    public void OnPointerClick(PointerEventData _e)
    {
        if (_e == null || _e.button != PointerEventData.InputButton.Left) return;

        // 스와이프로 소비된 포인터는 탭이 아니다 — 없으면 카드를 넘긴 뒤 손 떼는 순간 닫힌다.
        if (_e.dragging) return;

        // 누른 노드로 판정 — 이 경로의 pointerPress는 루트 자신이라 영역을 알려주지 못한다.
        GameObject t_hit = _e.pointerPressRaycast.gameObject;
        if (t_hit != null && this.tapCloseExclude != null
         && t_hit.transform.IsChildOf(this.tapCloseExclude)) return;

        TapClose();
    }

    // 강화 연출 중의 탭은 닫기가 아니라 스킵이다 — 연타하는 조작이라 결과를 기다리게만 두면 지겹고,
    // 그렇다고 닫아버리면 방금 쓴 골드의 결과를 못 보고 화면이 사라진다.
    void TapClose()
    {
        // "연출 중"의 진실원은 m_ritualPlaying 하나다 — ritual.IsPlaying은 유예를 세운 뒤 Play 전까지,
        // 그리고 OnKill 콜백 구간에서 어긋난다.
        if (this.m_ritualPlaying)
        {
            this.ritual?.RequestSkip();
            return;
        }

        Hide();
    }

    void OnPrevPressed() => Step(-1);
    void OnNextPressed() => Step(1);

    // 그 방향의 다음 "유효" 카드로 한 칸. 목록 끝에서는 반대편 끝으로 이어진다(순환) —
    // 상점 캐러셀(PackCarouselView)과 같은 규약이라 두 화면의 손맛이 갈리지 않는다.
    void Step(int _dir)
    {
        if (_dir == 0) return;

        // 연출 중에 카드가 바뀌면 무대에 선 카드와 결과가 어긋난다.
        if (this.m_ritualPlaying) return;

        int t_next = FindValid(this.m_index + _dir, _dir);

        // 한 바퀴 돌아 제자리면(유효 카드가 지금 이 한 장뿐) 아무 일도 하지 않는다 —
        // 같은 카드에 슬라이드만 걸면 화면이 이유 없이 흔들린다.
        if (t_next < 0 || t_next == this.m_index) return;

        this.m_index = t_next;
        PlaySlide(CardAt(t_next), _dir);
        RefreshArrows();
    }

    // 카드를 갈아끼운다. slideTarget이 없으면 트윈 없이 즉시 — 배선 전에도 넘기기 자체는 동작해야 한다.
    //
    // 새 카드 반영은 "나가는 트윈이 끝난 뒤"가 아니라 화면에서 가장 안 보이는 중간 지점 한 번이다.
    // 나감 → 콜백 → 들어옴을 각각의 OnComplete로 이으면 어느 한 마디가 잘렸을 때 빈 화면이 남는다.
    void PlaySlide(CardData _card, int _dir)
    {
        if (_card == null) return;

        if (this.slideTarget == null || !isActiveAndEnabled)
        {
            Apply(_card);
            return;
        }

        EnsureSlideBase();
        // 연타 인계 — 자리·투명도만 원복하고 이전 pending은 버린다. 어차피 이 전환이 새 카드를 덮어쓰므로
        // 한 프레임도 안 보일 중간 카드에 칩 전량을 재생성할 이유가 없다.
        CancelSlide();
        this.m_pendingCard = _card;

        float t_out  = -_dir * this.slideDistance;   // 다음(+1)으로 가면 보던 카드는 왼쪽으로 빠진다.
        float t_half = Mathf.Max(0.02f, this.slideDuration) * 0.5f;

        // id는 이 컴포넌트 인스턴스 자체 — CancelSlide가 같은 노드의 남의 트윈을 건드리지 않고 자기 것만 자르기 위한 표식이다.
        Sequence t_seq = DOTween.Sequence().SetLink(gameObject).SetId(this);

        t_seq.Append(this.slideTarget.DOAnchorPosX(this.m_slideBaseX + t_out, t_half).SetEase(Ease.InQuad));
        if (this.m_slideGroup != null) t_seq.Join(this.m_slideGroup.DOFade(0f, t_half));

        t_seq.AppendCallback(() =>
        {
            ApplyPending();
            this.slideTarget.anchoredPosition = new Vector2(this.m_slideBaseX - t_out, this.slideTarget.anchoredPosition.y);
        });

        t_seq.Append(this.slideTarget.DOAnchorPosX(this.m_slideBaseX, t_half).SetEase(Ease.OutQuad));
        if (this.m_slideGroup != null) t_seq.Join(this.m_slideGroup.DOFade(1f, t_half));

        t_seq.Play();   // 재생 책임을 코드에 남긴다(PopupTransition과 같은 결).
    }
    
    void CancelSlide()
    {
        DOTween.Kill(this);

        this.m_pendingCard = null;

        if (this.m_slideBaseCaptured && this.slideTarget != null)
            this.slideTarget.anchoredPosition = new Vector2(this.m_slideBaseX, this.slideTarget.anchoredPosition.y);
        if (this.m_slideGroup != null) this.m_slideGroup.alpha = 1f;
    }

    void ApplyPending()
    {
        if (this.m_pendingCard == null) return;

        CardData t_card    = this.m_pendingCard;
        this.m_pendingCard = null;
        Apply(t_card);
    }

    // authoring 좌표를 1회만 캡처한다. 매번 읽으면 트윈 중간값을 기준으로 잡아 자리가 조금씩 밀린다.
    void EnsureSlideBase()
    {
        if (this.m_slideBaseCaptured || this.slideTarget == null) return;

        this.m_slideBaseCaptured = true;
        this.m_slideBaseX        = this.slideTarget.anchoredPosition.x;

        // 페이드는 slideTarget 전용 CanvasGroup으로만 한다. 루트에 붙이면 PopupTransition의 등장·퇴장 페이드와
        // 같은 알파를 두고 싸운다 — 그래서 slideTarget이 루트면 페이드 없이 이동만 한다.
        if (this.slideTarget.gameObject == gameObject) return;

        this.m_slideGroup = this.slideTarget.GetComponent<CanvasGroup>();
        if (this.m_slideGroup == null) this.m_slideGroup = this.slideTarget.gameObject.AddComponent<CanvasGroup>();
    }

    // 순환이라 끝이 없으므로 양쪽 화살표는 항상 살아 있다 — 넘길 카드가 아예 없을 때(1장짜리)만 통째로 숨긴다.
    // interactable을 매번 다시 세우는 이유는 Hide()가 퇴장 중 입력을 죽여두기 때문이다(다시 열 때 여기서 되살아난다).
    void RefreshArrows()
    {
        bool t_multi = HasMultipleCards() && !this.m_ritualPlaying;

        if (this.swipeDetector != null) this.swipeDetector.Interactable = t_multi;
    }

    // _from부터 _dir 방향으로 처음 만나는 유효(null 아닌) 카드의 인덱스. 없으면 -1.
    // 도감 행에는 authoring이 비어 null인 슬롯이 있을 수 있어 "한 칸"이 곧 "다음 카드"가 아니다.
    int FindValid(int _from, int _dir)
    {
        return FindValidIn(this.m_cards, _from, _dir);
    }

    // 아직 m_cards에 대입하기 전의 후보 목록에도 같은 판정을 쓰기 위해 목록을 인자로 받는다(Show 참고).
    //
    // 끝에 닿으면 반대편으로 감는다(순환). 그래서 종료 조건을 "범위를 벗어남"에 맡길 수 없다 —
    // 전부 null인 목록에서 영원히 돈다. 대신 **자기 자신을 포함해 Count칸만** 보고 끊는다.
    // _from은 음수·Count 이상이어도 되게 미리 접는다(Step이 m_index±1을 그대로 넘긴다).
    static int FindValidIn(IReadOnlyList<CardData> _cards, int _from, int _dir)
    {
        if (_cards == null || _dir == 0) return -1;

        int t_count = _cards.Count;
        if (t_count == 0) return -1;

        int t_i = Wrap(_from, t_count);

        for (int t_n = 0; t_n < t_count; t_n++)
        {
            if (_cards[t_i] != null) return t_i;

            t_i = Wrap(t_i + _dir, t_count);
        }

        return -1;
    }

    // 0.._count-1로 접는다. C#의 %는 음수를 음수로 남기므로 한 번 더 더해야 한다.
    static int Wrap(int _value, int _count)
    {
        return ((_value % _count) + _count) % _count;
    }

    // 유효 카드가 2장 이상인지. 전체를 세지 않고 2장째에서 끊는다.
    bool HasMultipleCards()
    {
        if (this.m_cards == null) return false;

        int t_count = 0;
        for (int t_i = 0; t_i < this.m_cards.Count; t_i++)
        {
            if (this.m_cards[t_i] == null) continue;
            if (++t_count >= 2) return true;
        }

        return false;
    }

    CardData CardAt(int _index)
    {
        return this.m_cards != null && _index >= 0 && _index < this.m_cards.Count ? this.m_cards[_index] : null;
    }

    // 강화/진화 통지. m_index는 전환 중에도 이미 목표 카드를 가리키므로 지금 카드만 다시 그리면 된다.
    // 칩 섹션까지 다시 짓지 않는 이유는 RefreshGrowth 주석 참고.
    void OnGrowthChanged()
    {
        // 연출 중이면 흘려보낸다 — 결과는 공개 순간에 한 번에 반영된다(m_ritualPlaying 주석 참고).
        if (this.m_ritualPlaying) return;

        CardData t_card = CardAt(this.m_index);
        if (t_card != null) RefreshGrowth(t_card, OwnershipManager.IsOwned(t_card));
    }

    // 재화 종류에 따라 버튼 활성만 바뀐다 — 어느 종류든 다시 판정하면 되므로 걸러내지 않는다.
    void HandleCurrencyChanged(ECurrencyType _type, long _balance)
    {
        if (this.m_ritualPlaying) return;

        CardData t_card = CardAt(this.m_index);
        if (t_card != null) RefreshGrowth(t_card, OwnershipManager.IsOwned(t_card));
    }

    // 카드가 바뀔 때의 전량 갱신. 칩 재생성이 여기에만 있다.
    void Apply(CardData _card)
    {
        bool t_owned = OwnershipManager.IsOwned(_card);

        // 그림·이름·체력·키워드 아이콘·잠김 오버레이는 도감 타일과 같은 컴포넌트에 그대로 위임한다.
        if (this.cardView != null) this.cardView.Bind(_card, t_owned);
        

        BuildKeywordSection(_card, t_owned);
        BuildSynergySection(_card, t_owned);

        RefreshGrowth(_card, t_owned);
    }

    // 성장에 따라 움직이는 것만 다시 그린다. 강화는 연타하는 조작이라, 통지마다 Apply를 통째로 돌리면
    // 값이 그대로인 키워드·시너지 칩까지 매번 Destroy + Instantiate 된다.
    //
    // _deferCardHp: 카드 그림의 체력만 손대지 않는다. 결과판이 그 숫자를 굴려 보여줄 참이라
    // 여기서 최종값을 먼저 찍으면, 빛이 걷힌 카드에 새 숫자가 잠깐 비쳤다가 굴리기가 시작되며 옛 값으로 되돌아간다.
    void RefreshGrowth(CardData _card, bool _owned, bool _deferCardHp = false)
    {
        // 카드 그림의 HP도 강화를 따라와야 한다. Bind가 아니라 RefreshHp인 이유는 그쪽 주석 참고
        // (Bind는 키워드 아이콘·시너지 배지까지 전부 다시 짓는다).
        if (this.cardView != null && !_deferCardHp) this.cardView.RefreshHp(_card, _owned);

        // CardData에 파워 필드가 없어 프리팹 목업의 "파워" 행을 체력으로 쓴다(라벨/아이콘은 프리팹 쪽 값).
        // 수치는 강화 반영값 — 환산의 정본은 DeckPower다(마스터 maxHp를 직접 읽지 않는다).
        int t_maxHp = DeckPower.MaxHpOf(_card);
        if (this.powerValueText != null)
            this.powerValueText.text = !_owned           ? LockedValue
                                     : _card.bonusHp > 0 ? $"{t_maxHp} (+{_card.bonusHp})"
                                                         : t_maxHp.ToString();

        ApplyGrowth(_card, _owned);
        RefreshGrowthActions(_card, _owned);
    }

    // 강화 레벨. 미배선 필드는 조용히 건너뛴다(이전/다음 화살표와 같은 옵션 배선 규약).
    // 값이 없어도 행을 끄지 않는 이유는 ApplySection 주석과 같다 — 카드마다 패널 높이가 흔들린다.
    void ApplyGrowth(CardData _card, bool _owned)
    {
        if (this.levelValueText == null) return;

        if (_owned) SetLevelText(CardGrowthManager.GrowthOf(_card).Level);
        else        this.levelValueText.text = LockedValue;
    }

    // 강화 버튼과 비용·성공률·안내 문구. 규칙·비용·성공률은 전부 CardGrowthManager가 정본이고 여기선 표시만 한다.
    void RefreshGrowthActions(CardData _card, bool _owned)
    {
        GrowthStep t_step = default;
        bool t_hasStep = _owned && CardGrowthManager.TryGetNextStep(_card, out t_step);

        // 미소유 카드에는 조작을 숨긴다(버튼만 — 바는 켜둔 채로 높이를 지킨다).
        bool t_canPayEnhance = t_hasStep && CurrencyManager.CanAfford(ECurrencyType.Gold, t_step.Cost);
        if (this.enhanceButton != null)
        {
            this.enhanceButton.gameObject.SetActive(_owned);
            // 연출 중에는 공개 시점의 갱신이 버튼을 되살리지 않게 눌러둔다(복귀에서 다시 판정된다).
            this.enhanceButton.interactable = t_canPayEnhance && !this.m_ritualPlaying;
        }
        if (this.enhanceCostText != null) this.enhanceCostText.text = t_hasStep ? t_step.Cost.ToString("N0") : NoValue;
        if (this.successRateText != null)
            this.successRateText.text = t_hasStep ? $"{Mathf.RoundToInt(t_step.SuccessRate * 100f)}%" : NoValue;

        if (this.growthNoticeText != null)
            this.growthNoticeText.text = _owned ? GrowthNotice(t_hasStep, t_canPayEnhance) : string.Empty;
    }

    // 지금 강화가 왜 막혔는지 한 문장. 상세 패널과 결과판이 같은 문장을 써야 화면마다 이유가 달라 보이지 않는다.
    static string GrowthNotice(bool _hasStep, bool _canPay)
    {
        return !_hasStep ? MaxLevelNotice : !_canPay ? NotAffordableNotice : string.Empty;
    }

    void OnEnhancePressed()
    {
        if (this.m_ritualPlaying) return;

        CardData t_card = CardAt(this.m_index);
        if (t_card == null)
        {
            AbortEnhance(null);
            return;
        }

        // 시도 **전에** 잡아둔다 — 결과에는 오른 폭도 이전 값도 없다.
        int t_fromLevel = CardGrowthManager.GrowthOf(t_card).Level;
        int t_fromHp    = DeckPower.MaxHpOf(t_card);

        // 유예를 먼저 세운다 — TryEnhance가 그 안에서 OnGrowthChanged를 동기로 발화한다.
        this.m_ritualPlaying = true;

        EnhanceResult t_result = CardGrowthManager.TryEnhance(t_card);

        // 저작 실수(부트 누락)는 조용히 넘기지 않는다 — 재화는 소모되지 않았고 원인이 화면 밖에 있다.
        if (t_result.Outcome == EEnhanceOutcome.NotReady)
            Debug.LogError("[CardDetailOverlayView] 성장 데이터 미초기화 — CardGrowthManager.Init()이 부트에서 호출되지 않았다.");

        bool t_played = t_result.Outcome == EEnhanceOutcome.Success || t_result.Outcome == EEnhanceOutcome.Failed;

        // 결제 전에 막힌 경우(잔액 부족·최고 레벨·미초기화)엔 보여줄 결과가 없다. 미배선도 같은 길로 — 배선 실패가 소프트락이 되면 안 된다.
        if (!t_played || this.ritual == null)
        {
            AbortEnhance(t_card);
            return;
        }

        // 누른 순간엔 조작만 잠근다. 여기서 값을 다시 그리면 안 된다 — TryEnhance는 이미 끝난 거래라
        // RefreshGrowth가 곧바로 새 Lv·HP를 찍고, 그것이 상세 패널이 걷히는 0.15초 동안 그대로 비친다.
        LockControls();

        this.ritual.Play(
            t_result.Outcome, _awaitReturn: this.resultPanel != null,
            _onReveal: () =>
            {
                // 카드가 이미 바뀐 뒤 잘려 들어온 콜백이면 옛 값을 찍지 않는다(Show/Step의 CancelImmediate 경로).
                if (CardAt(this.m_index) != t_card) return;

                // 카드가 빛에 완전히 덮인 프레임이다. 걷혀 있는 상세 패널의 값을 여기서 조용히 갈아두면
                // 결과판을 닫고 돌아왔을 때 숫자가 튀지 않는다 — 보여주는 일은 결과판이 맡는다.
                //
                // 카드 위의 체력만 옛 값에 붙들어 둔다. 그 숫자는 결과판의 체력 행과 같은 박자로 굴러 오를 참이다.
                RefreshGrowth(t_card, OwnershipManager.IsOwned(t_card), _deferCardHp: this.resultPanel != null);
            },
            _onSettled: () =>
            {
                // 카드 위 연출이 다 끝난 뒤다. 여기서부터가 읽는 시간 — 결과판이 제 박자로 글자를 쌓는다.
                //
                // 무대에 선 카드가 바뀌었으면 옛 결과를 띄우지 않는다. 다만 무대는 **반드시** 돌려보낸다 —
                // 결과판이 안 뜨면 복귀를 시작할 주체가 없어 오버레이가 통째로 굳는다(도감이 목록을 제자리에서
                // 다시 채우면 연출 도중에도 CardAt이 다른 카드를 가리킬 수 있다).
                if (CardAt(this.m_index) != t_card)
                {
                    this.ritual.PlayReturn();
                    return;
                }

                ShowResultPanel(t_card, t_result, t_fromLevel, t_fromHp);
            },
            _onFinished: () =>
            {
                this.m_ritualPlaying = false;

                // 지금 보이는 카드로 다시 그린다 — 중간에 카드가 바뀌었어도 화면과 값이 어긋나지 않게.
                CardData t_now = CardAt(this.m_index);
                if (t_now != null) RefreshGrowth(t_now, OwnershipManager.IsOwned(t_now));
                RefreshArrows();

                // "한 번 더"는 여기서 이어간다 — 그 경로의 무대는 걷힌 채라(EndAwaitForChain) 다음 연출이 곧장 물려받는다.
                // 재입력 가드(m_ritualPlaying)가 풀렸다 다시 서기까지 한 프레임도 벌어지지 않으므로 그 사이에 손이 낄 자리가 없고,
                // 잔액 부족·만렙은 TryEnhance가 알아서 되돌린다(AbortEnhance가 걷힌 무대를 되돌린다).
                if (!this.m_retryQueued) return;

                this.m_retryQueued = false;
                OnEnhancePressed();
            });
    }

    // 보여줄 것 없이 끝난 강화(잔액 부족·최고 레벨·미초기화·연출 미배선). 잠금을 풀고 조작을 되살린다.
    //
    // 무대까지 되돌리는 이유는 "한 번 더"로 이어온 길 때문이다 — 그 경로에선 패널이 걷힌 채로 넘어오므로
    // 여기서 되돌리지 않으면 상세 패널이 사라진 채 굳는다(첫 시도에서 막힌 경우엔 되돌릴 것이 없어 무해하다).
    void AbortEnhance(CardData _card)
    {
        this.m_ritualPlaying = false;

        this.ritual?.CancelImmediate();

        // 잔액부족은 통지가 없다 → 여기서 한 번(멱등)
        if (_card != null) RefreshGrowth(_card, OwnershipManager.IsOwned(_card));
        RefreshArrows();
    }

    // 강화를 누른 직후의 잠금. 값은 손대지 않는다 — 여기서 RefreshGrowth를 부르면 공개할 것이 사라진다.
    void LockControls()
    {
        if (this.enhanceButton != null) this.enhanceButton.interactable = false;

        RefreshArrows();   // 연출 중에 카드가 넘어가면 무대에 선 카드와 결과가 어긋난다.
    }

    // 결과판을 띄운다. 판정은 이미 끝났고 여기서는 "무엇이 얼마나 바뀌었나"만 모아 넘긴다.
    void ShowResultPanel(CardData _card, EnhanceResult _result, int _fromLevel, int _fromHp)
    {
        if (this.resultPanel == null) return;   // 미배선이면 연출이 스스로 걷는다(Play의 _awaitReturn 참고).

        // "한 번 더"의 가부는 오른 뒤의 다음 단계로 판정한다 — 방금 쓴 비용이 아니라 지금 낼 비용이 기준이다.
        bool t_hasNext  = CardGrowthManager.TryGetNextStep(_card, out GrowthStep t_next);
        bool t_canRetry = t_hasNext && CurrencyManager.CanAfford(ECurrencyType.Gold, t_next.Cost);

        var t_line = new EnhanceResultLine(_result.Outcome,
                                           _fromHp, DeckPower.MaxHpOf(_card),
                                           _fromLevel, _result.Level,
                                           t_canRetry, GrowthNotice(t_hasNext, t_canRetry));

        this.resultPanel.Show(t_line,
                              _onClose: () => this.ritual.PlayReturn(),
                              // 무대는 돌려보내지 않는다 — 상세 패널이 0.35초 돌아왔다 곧바로 다시 걷히면
                              // 연타의 리듬이 그 왕복에서 끊긴다. 걷힌 채로 다음 담금질이 이어진다.
                              _onRetry: () =>
                              {
                                  this.m_retryQueued = true;
                                  this.ritual.EndAwaitForChain();
                              },
                              // 결과판의 "체력 71 → 73"이 굴러 오르는 그 박자에 무대에 선 카드의 숫자도 함께 오른다 —
                              // 따로 놀면 오른 것이 저 카드의 저 값이라는 연결이 끊긴다.
                              _onHpRoll: _dur =>
                              {
                                  if (this.cardView == null) return;
                                  this.cardView.RollHp(_card, OwnershipManager.IsOwned(_card), _fromHp, _dur);
                              });
    }

    void SetLevelText(int _level)
    {
        if (this.levelValueText != null) this.levelValueText.text = $"Lv {_level} / {CardGrowthManager.MaxLevel}";
    }

    void BuildKeywordSection(CardData _card, bool _owned)
    {
        ClearChildren(this.keywordChipRoot);

        var t_lines = new List<string>();

        if (_owned && this.keywordIconConfig != null && this.chipPrefab != null && this.keywordChipRoot != null)
        {
            // 판정 기준은 인게임 카드 정보창(CardElement)과 같다 — 규칙 자체는 CardVisualRules가 소유한다.
            // 해금 전 키워드는 여기서도 뜨지 않는다(공급자 미주입이면 마스터 데이터 그대로).
            CardKeyword t_all = CardVisualRules.InfoKeywords(_card);

            // 순회 순서 = CardKeyword 선언 순. 카드 타일 아이콘 줄(CardVisualRules.CollectKeywordIcons)과 같은 순서다.
            foreach (CardKeyword t_kw in (CardKeyword[])Enum.GetValues(typeof(CardKeyword)))
            {
                if (t_kw == CardKeyword.None) continue;
                if ((t_all & t_kw) == 0) continue;
                if (!this.keywordIconConfig.TryGetEntry(t_kw, out KeywordIconConfig.Entry t_entry)) continue;

                Instantiate(this.chipPrefab, this.keywordChipRoot).Init(t_entry.icon, t_entry.displayName, null);

                if (!string.IsNullOrEmpty(t_entry.explain)) t_lines.Add(t_entry.explain);
            }
        }

        ApplySection(this.keywordSection, this.keywordDescText, t_lines, _owned);
    }

    void BuildSynergySection(CardData _card, bool _owned)
    {
        ClearChildren(this.synergyChipRoot);

        var t_lines = new List<string>();

        if (_owned && _card.synergies != null && this.chipPrefab != null && this.synergyChipRoot != null)
        {
            var t_seen = new HashSet<SynergyData>();
            foreach (SynergyData t_syn in _card.synergies)
            {
                if (t_syn == null || !t_seen.Add(t_syn)) continue;   // 중복 나열 방어

                // 마지막 인자는 시너지 PNG 투명 여백 보정 — 없으면 키워드 칩 옆에서 혼자 작아 보인다.
                Instantiate(this.chipPrefab, this.synergyChipRoot)
                    .Init(t_syn.activeIcon, SynergyText.Name(t_syn), null, SynergyIconStrip.IconPadCompensation);

                if (!string.IsNullOrEmpty(t_syn.effectDescription)) t_lines.Add(t_syn.effectDescription);
            }
        }

        ApplySection(this.synergySection, this.synergyDescText, t_lines, _owned);
    }

    // 비어 있어도 섹션을 끄지 않는다 — 끄면 DetailPanel의 높이가 줄고, 루트 VerticalLayoutGroup에서
    // 남는 높이를 통째로 받는 것이 CardArea(flexibleHeight=1)라 **카드 그림의 크기와 위치가 카드마다 달라진다**.
    // 넘길 때마다 카드가 튀어 보이므로, 빈 섹션은 자리를 지킨 채 "없음"(미소유는 ???)만 적는다.
    // 그래도 설명 줄 수만큼은 흔들리므로 높이 고정의 정본은 프리팹의 DetailFrame LayoutElement.preferredHeight다.
    static void ApplySection(GameObject _section, TMP_Text _desc, List<string> _lines, bool _owned)
    {
        if (_desc != null)
            _desc.text = _lines.Count > 0 ? string.Join("\n", _lines)
                       : _owned           ? NoneValue
                                          : LockedName;

        if (_section != null) _section.SetActive(true);
    }

    static void ClearChildren(Transform _root)
    {
        if (_root == null) return;

        for (int t_i = _root.childCount - 1; t_i >= 0; t_i--)
            Destroy(_root.GetChild(t_i).gameObject);
    }
}
