using System;
using System.Collections.Generic;
using Coffee.UIEffects;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

// 로비 컬렉션 탭의 카드 상세 오버레이(CardDetailOverlay.prefab 루트에 부착).
// 카드 타일을 길게 누르면 열리고, 누른 카드의 이름·체력·키워드·시너지를 채운다.

/// <summary>상세를 어떤 모습으로 띄울지. 기본값(default)이 곧 도감에서 여는 평상시다.</summary>
public readonly struct CardDetailOpenOptions
{
    /// <summary>강화·진화 조작을 통째로 걷고 표시만 한다.</summary>
    public readonly bool ReadOnly;

    /// <summary>개봉·보상 화면 위 층으로 올라탄다.</summary>
    public readonly bool LiftAboveAll;

    /// <summary>로비 상단 재화 바를 비켜 앉은 크기를 부모 가득 편다.</summary>
    public readonly bool CoverFullScreen;

    public CardDetailOpenOptions(bool _readOnly = false, bool _liftAboveAll = false, bool _coverFullScreen = false)
    {
        ReadOnly        = _readOnly;
        LiftAboveAll    = _liftAboveAll;
        CoverFullScreen = _coverFullScreen;
    }
}

public class CardDetailOverlayView : MonoBehaviour, IPointerClickHandler
{
    const string LockedName  = "???";
    const string LockedValue = "?";
    const string NoneValue   = "없음";
    const string NoValue     = "-";
    const string FreeCost    = "무료";   // 숫자 0을 띄우면 값을 치르는 칸처럼 읽힌다

    // 상단바 뒤를 메우는 딤의 짙기. 실제로 보이는 건 바의 둥근 모서리 틈뿐이라 배경판(0.98)까지 올릴 필요가 없다.
    const float CONTENT_DIM_ALPHA = 0.8f;

    // 강화가 왜 막혔는지 — 결과판의 "한 번 더" 아래 문구가 쓴다.
    const string MaxLevelNotice      = "최고 성급에 도달했습니다!";
    const string NotAffordableFormat = "{0}{1} 부족합니다";

    [Header("배선")]
    [SerializeField] CardVisualView cardView;
    [SerializeField] TMP_Text       powerValueText;  // 프리팹 목업의 "파워" 행을 체력으로 쓴다
    [Tooltip("상세 목록 스크롤. 카드를 넘길 때 맨 위로 되감는다. 미배선이면 되감기만 없다.")]
    [SerializeField] ScrollRect     detailScroll;

    [Header("성장 (선택 — 미배선이면 성장 표시 없이 지금까지와 동일하게 동작)")]
    [SerializeField] TMP_Text levelValueText;

    [Header("강화 조작 (선택 — 미배선이면 조작 없이 표시만 한다)")]
    [SerializeField] Button     enhanceButton;
    [SerializeField] TMP_Text   enhanceCostText;    // 다음 레벨 비용(재화는 레벨마다 다르다)
    [Tooltip("비용 옆 재화 아이콘(옵션). 표(CurrencyLook)에 그림이 없으면 프리팹 그림 그대로다.")]
    [SerializeField] Image      enhanceCostIcon;
    [SerializeField] TMP_Text   successRateText;

    [Header("진화 얼굴 (선택 — 미배선이면 진화 관문에도 강화 얼굴이 그대로 선다)")]
    [Tooltip("진화 관문 레벨에서 강화 버튼이 갈아입는 글자와 그림. 누르는 결과는 언제나 같은 레벨업 1회다.")]
    [SerializeField] TMP_Text   evolveLabelText;
    [SerializeField] GameObject evolveIcon;
    [Tooltip("강화 구간의 그림. 진화 얼굴이 설 때 물러난다(미배선이면 두 그림이 겹쳐 보인다).")]
    [SerializeField] GameObject enhanceIcon;
    [SerializeField] string     evolveLabel = "진화";

    [Header("한계돌파 조작 (선택 — 미배선이면 만렙 카드에 강화 버튼이 그대로 선다)")]
    [Tooltip("3성 만렙 뒤에 강화 버튼 대신 서는 버튼. 무는 것은 재화가 아니라 그 카드 전용 간식이다.")]
    [SerializeField] Button   limitBreakButton;
    [SerializeField] TMP_Text limitBreakLabelText;
    [SerializeField] TMP_Text limitBreakCostText;
    [SerializeField] string   limitBreakLabel = "간식 먹이기";

    [Header("일러스트만 보기 (선택 — 미배선이면 기능만 빠진다)")]
    [Tooltip("누를 때마다 카드 위 정보(이름·이름판·체력·레벨·키워드 아이콘·프레임 장식·시너지)를 가렸다 되돌린다.")]
    [SerializeField] Button artOnlyButton;
    [Tooltip("켜짐/꺼짐을 색으로 알리는 아이콘(선택 — 미배선이면 색 피드백만 빠진다).")]
    [SerializeField] Image  artOnlyIcon;
    [SerializeField] Color  artOnlyOffColor = Color.white;
    [SerializeField] Color  artOnlyOnColor  = new Color(1f, 0.82f, 0.25f, 1f);

    [Header("강화 연출 (선택 — 미배선이면 연출 없이 지금까지처럼 값만 즉시 갱신)")]
    [SerializeField] CardEnhanceRitualView ritual;

    [Tooltip("진화 관문에서 담금질 대신 서는 연출. 미배선이면 진화도 담금질로 보여준다.")]
    [SerializeField] CardEvolveRitualView evolveRitual;

    [Tooltip("진화 결과판의 제목(진화는 실패가 없어 문구가 하나뿐이다).")]
    [SerializeField] string evolveResultTitle = "진화 성공!";

    [Tooltip("연출이 끝난 자리에 뜨는 결과판. 미배선이면 연출이 스스로 걷고 곧바로 상세로 돌아온다.")]
    [SerializeField] EnhanceResultPanelView resultPanel;

    [Header("키워드 섹션")]
    [SerializeField] GameObject keywordSection;
    [SerializeField] Transform  keywordChipRoot;
    [SerializeField] TMP_Text   keywordDescText;

    [Header("시너지 섹션")]
    [SerializeField] GameObject synergySection;
    [SerializeField] Transform  synergyChipRoot;
    [SerializeField] TMP_Text   synergyDescText;

    [Header("설명 섹션")]
    [Tooltip("카드 설명(CardSpec.CardExplain) 한 문단. 미배선이면 설명 없이 동작한다.")]
    [SerializeField] TMP_Text descriptionText;

    [Header("공용")]
    // 연출 동안 걷었다가 결과를 다 읽은 뒤 돌아오는 하단 바 — 복귀 시점이 연출의 끝이 아니라 결과판이 다 읽힌 시점이다.
    [SerializeField] CanvasGroup bottomBarGroup;
    [Tooltip("하단 바가 돌아오는 시간. 결과를 읽는 눈을 방해하지 않게 짧게.")]
    [SerializeField] float bottomBarFadeDuration = 0.18f;

    [Tooltip("강화 버튼의 글자. 미배선이면 글자는 그대로 두고 동작만 바뀐다.")]
    [SerializeField] TMP_Text enhanceLabelText;
    [SerializeField] string   enhanceLabel = "강화";
    [SerializeField] string   retryLabel   = "한 번 더";

    // 섹션(칩 줄 + 설명)을 통째로 덮는 잠김 판 — 칩 안의 자물쇠는 칩 rect를 못 벗어나 설명까지 가리지 못한다.
    [SerializeField] GameObject keywordSectionLock;
    [SerializeField] GameObject synergySectionLock;

    // 판이 걷힌 뒤 그 아래 내용이 들어오는 연출(옵션). 미배선이면 걷히자마자 완성된 글자가 그대로 있다.
    [SerializeField] SectionRevealFx keywordSectionReveal;
    [SerializeField] SectionRevealFx synergySectionReveal;

    [Tooltip("해금된 줄로 스크롤이 따라가는 시간. 0이면 즉시 옮긴다.")]
    [SerializeField] float unlockScrollDuration = 0.3f;

    // 런타임은 이 프리팹을 만들지 않는다 — 칩을 깔아 주는 에디터 도구(CardDetailChipBaker)가 여기서 읽는다.
    [SerializeField] KeywordExplainItem chipPrefab;
    [SerializeField] KeywordIconConfig  keywordIconConfig;
    [SerializeField] PopupTransition    transition = new PopupTransition();

    [Tooltip("좌우 스와이프 감지. 오버레이 전면을 덮는 raycastTarget Graphic 위에 올려야 한다.")]
    [SerializeField] HorizontalSwipeDetector swipeDetector;

    [Header("전환 연출 (선택 — slideTarget 미배선이면 트윈 없이 즉시 교체)")]
    [Tooltip("좌우로 밀렸다 들어올 카드 본문 패널. LayoutGroup/ContentSizeFitter에 드리븐되지 않는 노드여야 한다(기준 좌표를 1회만 캡처한다).")]
    [SerializeField] RectTransform slideTarget;
    [SerializeField] float slideDistance = 120f;
    [SerializeField] float slideDuration = 0.18f;

    /// <summary>강화가 무대를 쥐었다(연출 시작). 바깥의 안내는 여기서 자기 표시를 접어야 한다.</summary>
    public static event Action OnAnyEnhanceStarted;

    /// <summary>강화 한 방이 연출·결과판까지 끝나 상세로 돌아온 순간(성공·실패 모두).</summary>
    public static event Action<EnhanceResult> OnAnyEnhanceSettled;

    /// <summary>이 창이 닫혔다. 유저가 스스로 화면을 정리하기를 기다리는 쪽(온보딩 안내)이 듣는다.</summary>
    public static event Action OnAnyClosed;

    /// <summary>강화 결과판에 읽을 것이 다 떠오른 순간(성공·실패 모두).</summary>
    public static event Action<EnhanceResult> OnAnyEnhanceResultReady;

    /// <summary>해금 연출의 마지막 축까지 끝났다 — 중간에 잘린 경로(탭 스킵·카드 전환·창 닫힘)도 같이 쏜다.</summary>
    public static event Action OnAnyUnlockFxFinished;

    /// <summary>떠 있는 강화 결과판을 밖에서 걷는다(튜토리얼 자동 복귀). 떠 있지 않으면 아무 일도 없다.</summary>
    public static void CloseEnhanceResult()
    {
        if (s_instance == null || s_instance.resultPanel == null) return;

        s_instance.resultPanel.RequestClose();
    }

    /// <summary>지금 이 창이 화면을 덮고 있는가.</summary>
    public static bool IsOpen => s_instance != null && s_instance.gameObject.activeInHierarchy;

    /// <summary>지금 해금 연출이 도는 중인가.</summary>
    public static bool IsUnlockFxPlaying => s_instance != null && s_instance.m_unlockFxPlaying;

    static CardDetailOverlayView s_instance;
    static bool s_missingWarned;

    // 목록은 호출처가 쥔 것을 참조로 들고 있을 뿐이라 여기서 복사하거나 수정하지 않는다.
    IReadOnlyList<int> m_cards;
    int m_index;


    // 전환 트윈의 중간 지점에서 갈아끼울 카드. 트윈은 핸들이 아니라 id(this)로 찾아 자른다.
    int m_pendingCard;

    CanvasGroup m_slideGroup;
    float       m_slideBaseX;
    bool        m_slideBaseCaptured;

    // 연출 중에는 값 갱신을 미룬다 — 서버 왕복이 끝나는 순간 통지가 와서 공개 전에 Lv·HP가 튄다.
    bool m_ritualPlaying;

    // 진화 연출에 넘길 문양 재사용 버퍼(연타하는 조작이라 매번 새 List를 만들지 않는다).
    readonly List<Graphic> m_emblemBuffer = new List<Graphic>();

    // 누른 순간에 골라 고정한다 — 레벨이 그 직후 올라가므로 나중에 고르면 다른 연출을 붙든다.
    CardGrowthRitualView m_activeRitual;

    bool m_artOnly;

    // 튜토리얼 안내 타깃으로 등록해 둔 성장 버튼 — 자기가 올린 것만 내린다.
    Button m_anchoredGrowthButton;

    bool m_readOnly;

    // 창이 열려 있는 동안만 순서를 덮어쓰고 닫히면 되돌린다 — 상시 최상단이면 로비 레이어와의 순서까지 뒤집힌다.
    Canvas m_sortingCanvas;

    // 로비에 배치된 authoring 크기. 최초 1회만 잡는다 — 매번 읽으면 이미 편 값을 기준으로 잡는다.
    Vector2 m_baseOffsetMin;
    Vector2 m_baseOffsetMax;
    bool    m_baseRectCaptured;

    // 무대가 돌아오기 전에 다음 연출을 시작하면 두 연출이 같은 노드를 두고 싸운다 → 눌린 사실만 들고 있는다.
    bool m_retryQueued;

    // 카드 위(아이콘·프레임 장식)는 TraitKeywords, 칩 줄은 InfoKeywords 기준이라 마스크를 따로 든다.
    int         m_keywordCard;
    CardKeyword m_shownTrait;
    CardKeyword m_shownInfo;

    // 시너지 줄은 칩마다가 아니라 관문 하나(1차 진화)로 통째로 잠긴다 → 기준값도 불리언 하나면 된다.
    bool m_shownSynergyOpen;

    // 각 버튼 밑판의 흑백 효과. 자식은 UIEffectReplica로 따라오므로 코드가 쥐는 것은 버튼당 이 하나뿐이다.
    UIEffect m_enhanceTone;
    UIEffect m_limitBreakTone;

    // 잠김 판정은 "열린 것이 하나도 없는가"라 마스크가 그대로여도 상태가 바뀔 수 있다.
    bool m_shownKeywordLocked;

    // 방금 해금됐지만 아직 연출로 걷지 못한 판. 키워드는 "무엇이 열렸나"에 답해야 해서 열린 마스크 자체를 든다.
    CardKeyword m_pendingUnlockedKeywords;
    bool        m_pendingSynergyUnlockFx;

    // 이 구간의 탭은 닫기가 아니라 스킵이다 — 손이 스쳐 창이 사라지면 방금 열린 것을 다시 볼 자리가 없다.
    bool m_unlockFxPlaying;

    /// <summary>_card의 상세를 띄운다(넘길 이웃이 없는 1장짜리 목록). 오버레이가 씬에 없으면 경고 1회 후 무시.</summary>
    public static void Open(int _card)
    {
        if (_card <= 0) return;

        Open(new[] { _card }, 0);
    }

    /// <summary>_cards[_index]의 상세를 띄우고, 좌우로 같은 목록 안을 순환하며 넘겨볼 수 있게 한다.</summary>
    public static void Open(IReadOnlyList<int> _cards, int _index, CardDetailOpenOptions _options = default)
    {
        if (_cards == null || _cards.Count == 0) return;

        CardDetailOverlayView t_view = Resolve();
        if (t_view == null) return;

        // 세 축 모두 창을 닫을 때 내려가므로(OnDisable) 여기서 매번 다시 세우면 그만이다.
        t_view.m_readOnly = _options.ReadOnly;
        t_view.LiftAbove(_options.LiftAboveAll);
        t_view.SetFullScreen(_options.CoverFullScreen);
        t_view.Show(_cards, _index);
    }

    public static void Close()
    {
        // 열린 적이 없으면 닫을 것도 없다 — 여기서 Resolve를 돌려 경고를 띄울 이유가 없다.
        if (s_instance == null) return;
        s_instance.Hide();
    }

    /// <summary>타일에 "상세 열기 + 목록 안에서 좌우로 넘기기"를 배선한다(_index는 _cards 안에서 이 타일의 자리).</summary>
    public static void BindTile(CardVisualView _tile, IReadOnlyList<int> _cards, int _index,
                                CardDetailOpenOptions _options = default)
    {
        if (_tile == null || _cards == null) return;

        LongPressDetector t_press = _tile.GetComponent<LongPressDetector>();
        if (t_press == null) return;

        // 대입(+= 아님) — 타일이 재사용·재바인딩돼도 이전 콜백이 겹쳐 남지 않는다.
        t_press.OnTap = () => Open(_cards, _index, _options);
    }

    // 오버레이는 씬에 비활성으로 배치돼 Awake 싱글턴으로는 자신을 등록할 수 없다 → 첫 호출 때 비활성 포함으로 찾는다.
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

    // 층 값은 UiSortingOrder 표가 쥔다 — 떠 있는 캔버스를 재서 올라타면 상시 캔버스(UIPoolManager 400) 위로 뛴다.
    /// <summary>이 창을 다른 화면 위 층으로 올리거나(_on) 로비 캔버스 안의 제자리로 되돌린다.</summary>
    void LiftAbove(bool _on)
    {
        if (!_on)
        {
            UiSortingOrder.DropNested(this.m_sortingCanvas);
            return;
        }

        this.m_sortingCanvas = UiSortingOrder.LiftNested(gameObject, UiSortingOrder.CardDetailLifted);
    }

    /// <summary>이 오버레이를 부모(SafeArea) 가득 펴거나 authoring 크기로 되돌린다.</summary>
    void SetFullScreen(bool _on)
    {
        RectTransform t_rect = (RectTransform)transform;

        if (!this.m_baseRectCaptured)
        {
            this.m_baseRectCaptured = true;
            this.m_baseOffsetMin    = t_rect.offsetMin;
            this.m_baseOffsetMax    = t_rect.offsetMax;
        }

        t_rect.offsetMin = _on ? Vector2.zero : this.m_baseOffsetMin;
        t_rect.offsetMax = _on ? Vector2.zero : this.m_baseOffsetMax;
    }

    void Awake()
    {
        s_instance = this;

        if (this.enhanceButton != null) this.m_enhanceTone = this.enhanceButton.GetComponent<UIEffect>();
        if (this.limitBreakButton != null) this.m_limitBreakTone = this.limitBreakButton.GetComponent<UIEffect>();

        // 룩만 얹는 부착이다 — 차단은 RefreshGrowthActions의 계산식이 진다.
        if (this.enhanceButton != null) FeatureLockView.Attach(this.enhanceButton.gameObject, EOutgameFeature.CardEnhance);
        if (this.limitBreakButton != null) FeatureLockView.Attach(this.limitBreakButton.gameObject, EOutgameFeature.CardEnhance);

        // 카드 그림 위 탭은 루트의 OnPointerClick으로 오지 않는다(LongPressDetector가 pointerPress를 가져간다).
        if (this.cardView != null)
        {
            LongPressDetector t_tap = this.cardView.GetComponent<LongPressDetector>();
            if (t_tap != null) t_tap.OnTap = () => SkipPlayingFx();
        }
    }

    // 화살표·스와이프는 열 때마다 꺼졌다 켜지므로 Awake가 아니라 여기서 배선한다.
    void OnEnable()
    {
        // 상단 재화 바는 강화 비용을 보는 자리라 하단 탭바만 걷는다.
        LobbyShellBars.Hide(this, transform, EShellBars.Bottom);

        // 배경판이 상단바 아래에서 시작해 바의 둥근 모서리 틈으로 로비가 비친다 — 그 뒤를 Content 딤이 메운다.
        ScreenDim.Show(this, CONTENT_DIM_ALPHA, true, 0f, EDimLayer.Content);

        if (this.enhanceButton != null)
        {
            this.enhanceButton.onClick.RemoveListener(OnEnhancePressed);
            this.enhanceButton.onClick.AddListener(OnEnhancePressed);
        }

        // 한계돌파는 핸들러가 따로다 — 연출도 결과판도 무는 것도 강화와 다르다.
        if (this.limitBreakButton != null)
        {
            this.limitBreakButton.onClick.RemoveListener(OnLimitBreakPressed);
            this.limitBreakButton.onClick.AddListener(OnLimitBreakPressed);
        }

        if (this.artOnlyButton != null)
        {
            this.artOnlyButton.onClick.RemoveListener(ToggleArtOnly);
            this.artOnlyButton.onClick.AddListener(ToggleArtOnly);
        }

        // 대입 — 구독자는 언제나 이 오버레이 하나뿐이다.
        if (this.swipeDetector != null) this.swipeDetector.OnSwipe = Step;

        // 강화 실패에도 통지가 온다 — 핸들러는 "레벨이 올랐다"고 가정하지 않고 값을 다시 읽는다.
        CardGrowthManager.OnGrowthChanged += OnGrowthChanged;

        CurrencyManager.OnCurrencyChanged += HandleCurrencyChanged;

        // 강화 해금은 이 창이 열린 뒤에 온다 — 안 들으면 버튼이 잠긴 채로 굳어 안내가 멈춘다.
        OutgameFeatureLock.OnChanged += OnFeatureLockChanged;

        RefreshArrows();
    }

    void OnDisable()
    {
        LobbyShellBars.Show(this);

        ScreenDim.Hide(this, EDimLayer.Content);

        if (this.swipeDetector != null) this.swipeDetector.OnSwipe = null;

        if (this.enhanceButton != null) this.enhanceButton.onClick.RemoveListener(OnEnhancePressed);
        if (this.limitBreakButton != null) this.limitBreakButton.onClick.RemoveListener(OnLimitBreakPressed);

        if (this.artOnlyButton != null) this.artOnlyButton.onClick.RemoveListener(ToggleArtOnly);

        // 열람 모드는 창을 닫으면 푼다 — cardView는 이 오버레이 전용 인스턴스라 남겨두면 다음 열기에 따라온다.
        this.m_artOnly = false;
        this.cardView?.SetArtOnly(false);
        ApplyArtOnlyChrome();

        // 창이 닫히면 안내 타깃도 놓는다 — 안 보이는 버튼을 가리키는 등록이 남으면 다음 안내가 허공에 뜬다.
        ApplyGrowthAnchor(null);

        CardGrowthManager.OnGrowthChanged -= OnGrowthChanged;
        CurrencyManager.OnCurrencyChanged -= HandleCurrencyChanged;
        OutgameFeatureLock.OnChanged      -= OnFeatureLockChanged;

        // 전환 도중에 닫히면 slideTarget이 밀린 채·반투명인 채 굳어 다음 열기에 그대로 보인다.
        CancelSlide();

        // 무대를 먼저 자른다 — 잘리며 흘러나오는 공개 콜백이 결과판을 한 번 더 띄운다.
        CancelRituals();
        this.resultPanel?.HideImmediate();
        this.m_retryQueued = false;

        // 퇴장 트윈이 완료 전에 잘렸으면(부모가 먼저 꺼짐) 여기서 마무리해야 다음 열기에 유령 프레임이 안 뜬다.
        this.transition.HandleDisabled(gameObject);

        // 빌린 순서와 크기를 돌려준다 — 남겨두면 다음 창이 상단 바를 덮은 채 조작도 없는 화면이 된다.
        LiftAbove(false);
        SetFullScreen(false);
        this.m_readOnly = false;

        // 정리가 다 끝난 뒤에 알린다 — 구독자가 이 창의 상태를 다시 물어볼 수 있어야 한다.
        OnAnyClosed?.Invoke();

        // 이 파기가 곧 "연출이 끝났다" 신호라 정리의 맨 끝에 둔다.
        DropPendingUnlockFx();
    }

    void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }

    // 목록·인덱스는 SetVisible보다 먼저 확정한다 — 그것이 유발하는 OnEnable의 RefreshArrows가 최신을 보게.
    void Show(IReadOnlyList<int> _cards, int _index)
    {
        // 유효 인덱스를 확정한 뒤에 목록을 갈아끼운다 — 중도 return하면 목록과 인덱스가 서로 다른 기준으로 남는다.
        int t_index = Mathf.Clamp(_index, 0, _cards.Count - 1);
        if (_cards[t_index] <= 0)
        {
            t_index = FindValidIn(_cards, t_index, 1);
            if (t_index < 0) return;
        }

        this.m_cards = _cards;
        this.m_index = t_index;

        // 곧바로 Apply가 이어지므로 pending은 버린다(중간 카드에 칩을 한 번 더 짓지 않게).
        CancelSlide();
        CancelRituals();                      // 무대가 먼저다
        this.resultPanel?.HideImmediate();
        this.m_retryQueued = false;
        ShowBottomBar();   // 연출 도중에 닫았다 다시 연 경우 걷힌 상태가 남아 있을 수 있다
        this.transition.SetVisible(gameObject, true);
        Apply(CardAt(this.m_index));
        RefreshArrows();
    }

    void Hide()
    {
        // 퇴장 중 입력부터 죽인다 — 닫히는 도중 전환이 시작되면 close 시퀀스와 같은 노드를 두고 싸운다.
        if (this.swipeDetector != null) this.swipeDetector.Interactable = false;

        // 퇴장은 트윈이라 OnDisable이 곧바로 오지 않는다 — 예약이 살아 있으면 사라지는 창 위에서 다음 담금질이 시작된다.
        this.m_retryQueued = false;

        this.transition.SetVisible(gameObject, false);
    }

    /// <summary>닫기는 배경(딤) 탭만이다 — 카드·상세 패널·조작 바 위의 탭은 닫지 않는다.</summary>
    public void OnPointerClick(PointerEventData _e)
    {
        if (_e == null || _e.button != PointerEventData.InputButton.Left) return;

        // 스와이프로 소비된 포인터는 탭이 아니다 — 없으면 카드를 넘긴 뒤 손 떼는 순간 닫힌다.
        if (_e.dragging) return;

        // 연출 중의 탭은 어디를 눌렀든 스킵이다 — 해금 구간을 탭으로 지우면 그 사건은 이 카드에 두 번 오지 않는다.
        if (SkipPlayingFx()) return;

        if (_e.pointerPressRaycast.gameObject != gameObject) return;

        Hide();
    }

    /// <summary>강화 연출 스킵. "연출 중"의 진실원은 m_ritualPlaying 하나다(ritual.IsPlaying은 어긋난다).</summary>
    void SkipRitual()
    {
        if (this.m_ritualPlaying) this.m_activeRitual?.RequestSkip();
    }

    /// <summary>지금 도는 연출을 한 박 당긴다. 당길 것이 있었으면 true — 부른 쪽은 자기 일(닫기)을 하지 않는다.</summary>
    bool SkipPlayingFx()
    {
        // 당길 무대가 없으면(한계돌파처럼 유예만 선 왕복) 탭을 삼키지 않는다 — 나가는 문이 왕복 내내 막힌다.
        if (this.m_ritualPlaying && this.m_activeRitual != null) { SkipRitual(); return true; }
        if (this.m_unlockFxPlaying) { SkipUnlockFx(); return true; }

        return false;
    }

    /// <summary>해금 연출의 지금 박을 최종 상태로 끌어당긴다 — 탭 한 번이 한 박씩 넘긴다.</summary>
    void SkipUnlockFx()
    {
        // 자물쇠 판이 아직 도는 중. 두 줄이 함께 열렸으면 둘 다 당긴다(한 쪽만 남으면 박자가 갈린다).
        bool t_lock  = SkipSectionUnlock(this.keywordSectionLock);
             t_lock |= SkipSectionUnlock(this.synergySectionLock);
        if (t_lock) return;

        // 내용이 들어오는 박. 따라가던 스크롤도 함께 도착시킨다 — 글자만 앉고 화면이 계속 미끄러지면 따로 논다.
        if (this.detailScroll != null && this.detailScroll.content != null)
            this.detailScroll.content.DOComplete();

        bool t_reveal  = this.keywordSectionReveal != null && this.keywordSectionReveal.RequestSkip();
             t_reveal |= this.synergySectionReveal != null && this.synergySectionReveal.RequestSkip();

        // 당길 것이 없었다 = 흐름은 끝났는데 플래그만 남은 자리다 — 여기서 내려야 다음 탭에 창이 닫힌다.
        if (!t_reveal) SetUnlockFxPlaying(false);
    }

    // 걷히는 중인 판을 지금 끝낸다. 돌고 있지 않으면 false — 부른 쪽은 "이 박은 이미 지났다"로 읽는다.
    static bool SkipSectionUnlock(GameObject _lock)
    {
        if (_lock == null || !_lock.activeSelf) return false;

        var t_fx = _lock.GetComponent<SectionUnlockFx>();
        return t_fx != null && t_fx.RequestSkip();
    }

    /// <summary>어느 연출이 서 있었든 무대를 잘라낸다 — 중간에 갈린 경우를 대비해 둘 다 자른다.</summary>
    void CancelRituals()
    {
        this.ritual?.CancelImmediate();
        this.evolveRitual?.CancelImmediate();
        this.m_activeRitual = null;
    }

    /// <summary>이 카드의 다음 한 방을 맡을 연출. 진화 관문은 담금질과 다른 얼굴을 쓴다.</summary>
    CardGrowthRitualView RitualFor(int _card)
    {
        if (this.evolveRitual != null
         && CardGrowthManager.TryGetNextStep(_card, out GrowthStep t_step)
         && CardGrowthManager.IsEvolutionLevel(t_step.Level)) return this.evolveRitual;

        return this.ritual;
    }

    void OnPrevPressed() => Step(-1);
    void OnNextPressed() => Step(1);

    // 그 방향의 다음 "유효" 카드로 한 칸. 목록 끝에서는 반대편 끝으로 이어진다(순환).
    void Step(int _dir)
    {
        if (_dir == 0) return;

        // 연출 중에 카드가 바뀌면 무대에 선 카드와 결과가 어긋난다.
        if (this.m_ritualPlaying) return;

        int t_next = FindValid(this.m_index + _dir, _dir);

        // 한 바퀴 돌아 제자리면(유효 카드가 이 한 장뿐) 슬라이드만 걸려 화면이 이유 없이 흔들린다.
        if (t_next < 0 || t_next == this.m_index) return;

        this.m_index = t_next;
        PlaySlide(CardAt(t_next), _dir);
        RefreshArrows();
    }

    // 새 카드 반영은 나가는 트윈의 끝이 아니라 가장 안 보이는 중간 지점 한 번이다 — 마디가 잘려도 빈 화면이 안 남는다.
    void PlaySlide(int _card, int _dir)
    {
        if (_card <= 0) return;

        if (this.slideTarget == null || !isActiveAndEnabled)
        {
            Apply(_card);
            return;
        }

        EnsureSlideBase();
        // 연타 인계 — 한 프레임도 안 보일 중간 카드에 칩 전량을 재생성할 이유가 없다.
        CancelSlide();
        this.m_pendingCard = _card;

        float t_out  = -_dir * this.slideDistance;   // 다음(+1)으로 가면 보던 카드는 왼쪽으로 빠진다.
        float t_half = Mathf.Max(0.02f, this.slideDuration) * 0.5f;

        // id는 이 인스턴스 자체 — CancelSlide가 같은 노드의 남의 트윈을 건드리지 않게 하는 표식이다.
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

        t_seq.Play();
    }

    void CancelSlide()
    {
        DOTween.Kill(this);

        this.m_pendingCard = 0;

        if (this.m_slideBaseCaptured && this.slideTarget != null)
            this.slideTarget.anchoredPosition = new Vector2(this.m_slideBaseX, this.slideTarget.anchoredPosition.y);
        if (this.m_slideGroup != null) this.m_slideGroup.alpha = 1f;
    }

    void ApplyPending()
    {
        if (this.m_pendingCard <= 0) return;

        int t_card         = this.m_pendingCard;
        this.m_pendingCard = 0;
        Apply(t_card);
    }

    // authoring 좌표를 1회만 캡처한다. 매번 읽으면 트윈 중간값을 기준으로 잡아 자리가 조금씩 밀린다.
    void EnsureSlideBase()
    {
        if (this.m_slideBaseCaptured || this.slideTarget == null) return;

        this.m_slideBaseCaptured = true;
        this.m_slideBaseX        = this.slideTarget.anchoredPosition.x;

        // 페이드는 slideTarget 전용 CanvasGroup으로만 한다 — 루트에 붙이면 PopupTransition의 페이드와 알파를 두고 싸운다.
        if (this.slideTarget.gameObject == gameObject) return;

        this.m_slideGroup = this.slideTarget.GetComponent<CanvasGroup>();
        if (this.m_slideGroup == null) this.m_slideGroup = this.slideTarget.gameObject.AddComponent<CanvasGroup>();
    }

    // 순환이라 넘길 카드가 아예 없을 때(1장짜리)만 통째로 숨긴다. Hide()가 죽여둔 입력이 여기서 되살아난다.
    void RefreshArrows()
    {
        bool t_multi = HasMultipleCards() && !this.m_ritualPlaying;

        if (this.swipeDetector != null) this.swipeDetector.Interactable = t_multi;
    }

    // _from부터 _dir 방향으로 처음 만나는 유효 카드의 인덱스. 없으면 -1(도감 행에는 비어 있는 슬롯이 있다).
    int FindValid(int _from, int _dir)
    {
        return FindValidIn(this.m_cards, _from, _dir);
    }

    // 끝에 닿으면 반대편으로 감으므로(순환) 종료 조건을 범위 밖에 맡길 수 없다 — 자기 포함 Count칸만 보고 끊는다.
    static int FindValidIn(IReadOnlyList<int> _cards, int _from, int _dir)
    {
        if (_cards == null || _dir == 0) return -1;

        int t_count = _cards.Count;
        if (t_count == 0) return -1;

        int t_i = Wrap(_from, t_count);

        for (int t_n = 0; t_n < t_count; t_n++)
        {
            if (_cards[t_i] > 0) return t_i;

            t_i = Wrap(t_i + _dir, t_count);
        }

        return -1;
    }

    // 0.._count-1로 접는다. C#의 %는 음수를 음수로 남기므로 한 번 더 더해야 한다.
    static int Wrap(int _value, int _count)
    {
        return ((_value % _count) + _count) % _count;
    }

    bool HasMultipleCards()
    {
        if (this.m_cards == null) return false;

        int t_count = 0;
        for (int t_i = 0; t_i < this.m_cards.Count; t_i++)
        {
            if (this.m_cards[t_i] <= 0) continue;
            if (++t_count >= 2) return true;
        }

        return false;
    }

    int CardAt(int _index)
    {
        return this.m_cards != null && _index >= 0 && _index < this.m_cards.Count ? this.m_cards[_index] : 0;
    }

    // 강화/진화 통지. m_index는 전환 중에도 이미 목표 카드를 가리키므로 지금 카드만 다시 그리면 된다.
    void OnGrowthChanged()
    {
        // 연출 중이면 흘려보낸다 — 결과는 공개 순간에 한 번에 반영된다.
        if (this.m_ritualPlaying) return;

        int t_card = CardAt(this.m_index);
        if (t_card > 0) RefreshGrowth(t_card, OwnershipManager.IsOwned(t_card));
    }

    // 안내가 강화를 열었다(또는 닫았다) — 잔액 변화와 같은 자리에서 다시 판정하면 된다.
    void OnFeatureLockChanged()
    {
        if (this.m_ritualPlaying) return;

        int t_card = CardAt(this.m_index);
        if (t_card > 0) RefreshGrowth(t_card, OwnershipManager.IsOwned(t_card));
    }

    // 재화 종류에 따라 버튼 활성만 바뀐다 — 어느 종류든 다시 판정하면 되므로 걸러내지 않는다.
    void HandleCurrencyChanged(ECurrencyType _type, long _balance)
    {
        if (this.m_ritualPlaying) return;

        int t_card = CardAt(this.m_index);
        if (t_card > 0) RefreshGrowth(t_card, OwnershipManager.IsOwned(t_card));
    }

    // 다시 그리는 것은 cardView.Bind 하나 — Apply를 통째로 돌리면 값이 그대로인 칩까지 Destroy + Instantiate 된다.
    void ToggleArtOnly()
    {
        if (this.m_ritualPlaying) return;   // 연출이 화면을 덮은 동안 카드를 다시 그리면 담금질 자세가 풀린다

        this.m_artOnly = !this.m_artOnly;
        ApplyArtOnlyChrome();

        // 모드는 Bind보다 먼저 세운다 — 키워드 아이콘은 Bind가 지었다 부수므로 나중이면 이번 판만 옛 모습이 남는다.
        this.cardView?.SetArtOnly(this.m_artOnly);

        int t_card = CardAt(this.m_index);
        if (t_card > 0 && this.cardView != null)
            this.cardView.Bind(t_card, OwnershipManager.IsOwned(t_card));
    }

    void ApplyArtOnlyChrome()
    {
        if (this.artOnlyIcon != null)
            this.artOnlyIcon.color = this.m_artOnly ? this.artOnlyOnColor : this.artOnlyOffColor;
    }

    // 카드가 바뀔 때의 전량 갱신. 조건 없는 칩 재생성은 여기뿐이다.
    void Apply(int _card)
    {
        bool t_owned = OwnershipManager.IsOwned(_card);

        // 다른 카드를 그리는 참이다 — 앞 카드의 해금 대기를 안 버리면 이 카드의 판이 이유 없이 터진다.
        DropPendingUnlockFx();

        // 해금 연출이 카드 전환으로 잘리면 끝 콜백이 오지 않는다 — 돌아왔어야 할 하단 바를 여기서 못 박는다(멱등).
        ShowBottomBar();

        if (this.cardView != null) this.cardView.Bind(_card, t_owned);


        BuildKeywordSection(_card, t_owned);
        BuildSynergySection(_card, t_owned);
        ApplyDescription(_card, t_owned);
        RewindScroll();

        RefreshGrowth(_card, t_owned);
    }

    // 성장에 따라 움직이는 것만 다시 그린다 — 통지마다 Apply를 돌리면 값이 그대로인 칩까지 매번 다시 짓는다.
    void RefreshGrowth(int _card, bool _owned)
    {
        // 진화 관문을 넘은 공개 프레임에 그림도 함께 바뀐다.
        if (this.cardView != null) this.cardView.RefreshArt(_card);

        // 진짜 바뀐 때만 다시 짓는다 — 아이콘·칩은 Destroy + Instantiate라 통지마다 지으면 매번 새로 짓는다.
        RefreshUnlockVisuals(_card, _owned);

        // Bind가 아니라 RefreshHp인 이유는 Bind가 키워드 아이콘·시너지 배지까지 전부 다시 짓기 때문이다.
        if (this.cardView != null) this.cardView.RefreshHp(_card, _owned);

        // CardData에 파워 필드가 없어 프리팹 목업의 "파워" 행을 체력으로 쓴다(환산의 정본은 DeckPower).
        int t_maxHp = DeckPower.MaxHpOf(_card);
        if (this.powerValueText != null)
            this.powerValueText.text = !_owned           ? LockedValue
                                     : t_maxHp.ToString();

        ApplyGrowth(_card, _owned);
        RefreshGrowthActions(_card, _owned);
    }

    // 기준값이 그대로면 아무것도 하지 않는다 — 각 줄의 기준값 갱신은 Build*Section이 직접 한다.
    void RefreshUnlockVisuals(int _card, bool _owned)
    {
        CardKeyword t_trait = _owned ? CardVisualRules.TraitKeywords(_card) : CardKeyword.None;
        CardKeyword t_info  = _owned ? CardVisualRules.InfoKeywordsWithLocked(_card) : CardKeyword.None;
        bool        t_syn   = _owned && SynergyUnlocked(_card);

        bool t_sameCard = _card == this.m_keywordCard;

        // 같은 카드가 선 채로 잠김이 풀렸다 = 방금 해금됐다(판을 걷는 일은 PlayPendingUnlockFx가 맡는다).
        if (t_sameCard)
        {
            if (t_syn && !this.m_shownSynergyOpen) this.m_pendingSynergyUnlockFx = true;

            // 섹션째 잠겨 있다가 풀렸다 = 지금 열려 있는 것 전부가 방금 열린 것이다.
            if (this.m_shownKeywordLocked && !KeywordSectionLocked(_card, _owned))
                this.m_pendingUnlockedKeywords = CardVisualRules.InfoKeywords(_card);
        }

        // 시너지 관문(1차 진화)은 키워드 마스크를 안 건드리고 넘어갈 수 있다 — 따로 보지 않으면 잠긴 칩이 그대로 남는다.
        if (t_sameCard && t_syn != this.m_shownSynergyOpen) BuildSynergySection(_card, _owned);

        if (t_sameCard && t_trait == this.m_shownTrait && t_info == this.m_shownInfo) return;

        if (this.cardView != null) this.cardView.RefreshKeywords(_card, _owned);
        BuildKeywordSection(_card, _owned);
    }

    // _pendingFx면 판을 걷지 않는다 — 방금 해금된 줄이라 걷는 일은 연출(SectionUnlockFx)이 맡는다.
    static void SetSectionLock(GameObject _lock, bool _locked, bool _pendingFx = false)
    {
        if (_lock == null) return;
        if (!_locked && _pendingFx) return;

        _lock.SetActive(_locked);
    }

    /// <summary>방금 해금된 줄의 잠김 판을 연출로 걷는다(판 걷힘 → 내용 등장 → 전면 안내).</summary>
    void PlayPendingUnlockFx()
    {
        CardKeyword t_keywords = this.m_pendingUnlockedKeywords;
        bool        t_synergy  = this.m_pendingSynergyUnlockFx;

        this.m_pendingUnlockedKeywords = CardKeyword.None;
        this.m_pendingSynergyUnlockFx  = false;

        Tween t_fx = null;
        if (t_keywords != CardKeyword.None) t_fx = PlayUnlockFx(this.keywordSectionLock) ?? t_fx;
        if (t_synergy)                      t_fx = PlayUnlockFx(this.synergySectionLock) ?? t_fx;

        // 걷을 판도, 돌 연출도 없었다 — 바는 호출부가 이미 되돌렸다.
        if (t_fx == null && t_keywords == CardKeyword.None && !t_synergy) { ShowBottomBar(); return; }

        // 여기부터 마지막 축이 끝날 때까지 탭은 닫기가 아니다(OnPointerClick).
        SetUnlockFxPlaying(true);

        HideBottomBar();

        // 두 줄이 함께 열렸으면 위쪽(키워드)을 기준으로 삼는다 — 두 번 미끄러지면 어느 쪽을 읽을지 흐려진다.
        GameObject t_focus = t_keywords != CardKeyword.None ? this.keywordSection : this.synergySection;

        // 도중에 잘리는 경로(카드 전환·창 닫힘)에는 이 콜백이 오지 않는다 → 그쪽은 Apply가 못 박는다.
        if (t_fx == null) RevealUnlockedSections(t_focus, t_keywords, t_synergy);
        else              t_fx.OnComplete(() => RevealUnlockedSections(t_focus, t_keywords, t_synergy));
    }

    // 걷힌 줄로 스크롤을 옮기고 내용을 들여보낸 뒤, 이번에 열린 개념을 전면 안내로 넘긴다.
    void RevealUnlockedSections(GameObject _focus, CardKeyword _keywords, bool _synergy)
    {
        ScrollTo(_focus);

        // 마지막 축을 잡아 둔다 — 안내가 서지 않는 판에서는 이 안무가 끝나야 탭이 닫기로 돌아온다.
        Tween t_reveal = null;
        if (_keywords != CardKeyword.None) t_reveal = (this.keywordSectionReveal?.Play()) ?? t_reveal;
        if (_synergy)                      t_reveal = (this.synergySectionReveal?.Play()) ?? t_reveal;

        int               t_card   = CardAt(this.m_index);
        List<UnlockIntro> t_intros = CollectIntros(t_card, _keywords, _synergy);
        if (t_intros == null || t_intros.Count == 0) { ShowBottomBar(); EndUnlockFxAfter(t_reveal); return; }

        if (!UnlockIntroOverlay.TryGet(out UnlockIntroOverlay t_overlay))
        { ShowBottomBar(); EndUnlockFxAfter(t_reveal); return; }

        // 카드를 함께 넘긴다 — 안내 안의 데모 무대가 이 카드를 공격자로 세운다.
        t_overlay.Show(t_intros, t_card, () => { ShowBottomBar(); SetUnlockFxPlaying(false); });
    }

    /// <summary>이번에 열린 개념들(순서는 화면 순서 — 키워드 줄이 위, 시너지 줄이 아래). 없으면 null.</summary>
    List<UnlockIntro> CollectIntros(int _card, CardKeyword _keywords, bool _synergy)
    {
        List<UnlockIntro> t_list = null;

        if (_keywords != CardKeyword.None && this.keywordIconConfig != null)
            foreach (CardKeyword t_kw in (CardKeyword[])Enum.GetValues(typeof(CardKeyword)))
            {
                if (t_kw == CardKeyword.None || (_keywords & t_kw) == 0) continue;
                if (!UnlockIntro.TryForKeyword(this.keywordIconConfig, t_kw, out UnlockIntro t_intro)) continue;

                (t_list ??= new List<UnlockIntro>()).Add(t_intro);
            }

        // 시너지는 개념 하나라 카드가 여럿 물고 있어도 첫 장 하나면 된다.
        if (_synergy && _card > 0)
            foreach (SynergyData t_syn in CardCatalog.RequireSynergies(_card))
            {
                if (!UnlockIntro.TryForSynergy(t_syn, out UnlockIntro t_intro)) continue;

                (t_list ??= new List<UnlockIntro>()).Add(t_intro);
                break;
            }

        return t_list;
    }

    // 그 섹션이 화면에 들어오도록 스크롤을 옮긴다. verticalNormalizedPosition은 짧은 내용에서 튀어 쓰지 않는다.
    void ScrollTo(GameObject _section)
    {
        if (this.detailScroll == null || this.detailScroll.content == null || _section == null) return;

        RectTransform t_content = this.detailScroll.content;
        RectTransform t_view    = this.detailScroll.viewport != null ? this.detailScroll.viewport
                                                                     : (RectTransform)this.detailScroll.transform;

        float t_span = t_content.rect.height - t_view.rect.height;
        if (t_span <= 0f) return;   // 다 보이는 화면이라 옮길 자리가 없다

        // Content는 위에 매달려 있다(pivot y=1) → 목표 자리는 "섹션 상단까지 내린 거리"다.
        var   t_rect = (RectTransform)_section.transform;
        float t_top  = -(float)t_content.InverseTransformPoint(t_rect.TransformPoint(new Vector3(0f, t_rect.rect.yMax)))
                             .y;
        float t_to   = Mathf.Clamp(t_top, 0f, t_span);

        this.detailScroll.StopMovement();
        t_content.DOKill();

        if (this.unlockScrollDuration <= 0f)
        {
            t_content.anchoredPosition = new Vector2(t_content.anchoredPosition.x, t_to);
            return;
        }

        t_content.DOAnchorPosY(t_to, this.unlockScrollDuration)
                 .SetEase(Ease.OutCubic)
                 .SetLink(gameObject);
    }

    // 걷을 판이 없거나 연출이 미배선이면 null — 부른 쪽은 "기다릴 것이 없다"로 읽는다.
    static Tween PlayUnlockFx(GameObject _lock)
    {
        if (_lock == null || !_lock.activeSelf) return null;

        var t_fx = _lock.GetComponent<SectionUnlockFx>();
        if (t_fx == null) { _lock.SetActive(false); return null; }

        return t_fx.Play();
    }

    /// <summary>대기 중인 해금 연출을 버리고 판을 지금 상태에 맞춘다(카드 전환·창 닫힘).</summary>
    void DropPendingUnlockFx()
    {
        this.m_pendingUnlockedKeywords = CardKeyword.None;
        this.m_pendingSynergyUnlockFx  = false;

        // 잘린 안무는 끝 콜백이 오지 않는다 — 여기서 내리지 않으면 탭이 영영 닫기로 돌아오지 않는다.
        SetUnlockFxPlaying(false);
    }

    // 안내가 서지 않는 판의 마지막 축. 도중에 잘리는 경로는 DropPendingUnlockFx가 못 박는다.
    void EndUnlockFxAfter(Tween _reveal)
    {
        if (_reveal == null || !_reveal.IsActive()) { SetUnlockFxPlaying(false); return; }

        _reveal.OnComplete(() => SetUnlockFxPlaying(false));
    }

    // 끝나는 길이 여럿이라(안내 닫힘·마지막 트윈·스킵·중단) 켜짐이 실제로 꺼짐으로 바뀐 전이에서만 한 번 쏜다.
    void SetUnlockFxPlaying(bool _playing)
    {
        if (this.m_unlockFxPlaying == _playing) return;

        this.m_unlockFxPlaying = _playing;
        if (!_playing) OnAnyUnlockFxFinished?.Invoke();
    }

    /// <summary>키워드 줄이 통째로 잠겼는가. 짓는 쪽과 감지하는 쪽이 같은 답을 보게 판정을 여기 하나로 둔다.</summary>
    static bool KeywordSectionLocked(int _card, bool _owned)
        => _owned && CardVisualRules.LockedKeywords(_card) != CardKeyword.None
                  && CardVisualRules.InfoKeywords(_card) == CardKeyword.None;

    /// <summary>이 카드의 시너지가 열려 있는가. 관문 레벨은 GrowthRules가 소유하고 여기선 결과만 읽는다.</summary>
    static bool SynergyUnlocked(int _card) => CardGrowthManager.GrowthOf(_card).SynergyUnlocked;

    // 값이 없어도 행을 끄지 않는다 — 카드마다 패널 높이가 흔들린다.
    void ApplyGrowth(int _card, bool _owned)
    {
        if (this.levelValueText == null) return;

        if (_owned) SetLevelText(CardGrowthManager.GrowthOf(_card).Level);
        else        this.levelValueText.text = LockedValue;
    }

    // 규칙·비용·성공률은 전부 CardGrowthManager가 정본이고 여기선 표시만 한다.
    void RefreshGrowthActions(int _card, bool _owned)
    {
        GrowthStep t_step = default;
        bool t_hasStep = _owned && CardGrowthManager.TryGetNextStep(_card, out t_step);

        // 한계돌파는 강화 레벨과 무관한 별개 축이라 0성부터 선다 — 강화 버튼과 나란히 놓인다.
        LimitBreakStep t_lbStep = default;
        bool t_hasLimitBreak = _owned
                            && CardGrowthManager.TryGetNextLimitBreakStep(_card, out t_lbStep);
        int t_snack = _owned ? CardGrowthManager.SnackOf(_card) : 0;

        // 다음 한 방이 진화 관문이면 같은 버튼이 진화 얼굴로 갈아입는다 — 어느 쪽이든 버튼이 자리를 옮기지 않는다.
        bool t_evolve = t_hasStep && CardGrowthManager.IsEvolutionLevel(t_step.Level);

        // 열람 전용도 같은 길로 내린다 — 알파만 0인 채 살아 있는 버튼은 탭을 먹는다.
        bool t_limit = this.limitBreakButton != null && t_hasLimitBreak;

        // 강화·진화는 한 버튼이 얼굴만 갈아입고, 한계돌파는 그 옆에 따로 선다(ActionRow가 가로로 정렬).
        bool t_actions = _owned && !this.m_readOnly;
        if (this.enhanceButton != null) this.enhanceButton.gameObject.SetActive(t_actions);
        if (this.limitBreakButton != null) this.limitBreakButton.gameObject.SetActive(t_actions && t_limit);

        ApplyGrowthFace(t_evolve);

        // 안내 타깃은 지금 서 있는 성장 버튼을 따라간다 — 열릴 때마다 새로 서서 프리팹 표식으로는 잡을 수 없다.
        ApplyGrowthAnchor(!t_actions ? null
                        : t_hasStep ? this.enhanceButton
                        : t_limit  ? this.limitBreakButton
                        : this.enhanceButton);

        // 연출 중에는 공개 시점의 갱신이 버튼을 되살리지 않게 눌러둔다(복귀에서 다시 판정된다).
        bool t_unlocked = OutgameFeatureLock.IsUnlocked(EOutgameFeature.CardEnhance);

        bool t_canPayEnhance = t_hasStep && CurrencyManager.CanAfford(t_step.Currency, t_step.Cost);
        SetActionsEnabled(t_canPayEnhance && !this.m_ritualPlaying && t_unlocked);

        // 한계돌파는 무는 것이 재화가 아니라 그 카드 간식이라 활성 판정을 따로 낸다.
        bool t_canPayLimit = t_hasLimitBreak && t_snack >= t_lbStep.SnackCost;
        SetActionEnabled(this.limitBreakButton, this.m_limitBreakTone,
                         t_canPayLimit && !this.m_ritualPlaying && t_unlocked);

        ApplyCost(t_hasStep, t_step);
        ApplyLimitBreakCost(t_hasLimitBreak, t_lbStep, t_snack);

        // 결과판이 걷힌 뒤(또는 평상시)엔 다시 각자의 글자다 — 값 갱신이 지나는 이 길이 곧 글자의 복귀 지점이다.
        SetActionLabel(false);
        if (this.successRateText != null)
            this.successRateText.text = t_hasStep ? $"{Mathf.RoundToInt(t_step.SuccessRate * 100f)}%" : NoValue;
    }

    /// <summary>이번 강화(_from → _to)로 새로 열린 것을 한 문장으로. 아무것도 안 열렸으면 null.</summary>
    string UnlockLabel(int _card, int _from, int _to)
    {
        if (_card <= 0 || _to <= _from) return null;

        CardGrowth t_before = CardGrowthManager.GrowthAtLevel(_card, _from);
        CardGrowth t_after  = CardGrowthManager.GrowthAtLevel(_card, _to);

        var t_parts = new List<string>();

        CardKeyword t_newKeywords = NewKeywords(_card, _from, _to);
        if (t_newKeywords != CardKeyword.None && this.keywordIconConfig != null)
            foreach (CardKeyword t_kw in (CardKeyword[])Enum.GetValues(typeof(CardKeyword)))
            {
                if (t_kw == CardKeyword.None || (t_newKeywords & t_kw) == 0) continue;
                if (this.keywordIconConfig.TryGetEntry(t_kw, out KeywordIconConfig.Entry t_entry))
                    t_parts.Add($"{t_entry.displayName} 개방");
            }

        if (UnlockedSynergy(_card, _from, _to)) t_parts.Add("시너지 개방");

        // 진화는 그림이 바뀌는 큰 변화라 같이 알린다 — 결과판을 닫고 나서야 눈치채면 강화의 보람이 반감된다.
        if (t_after.EvolutionStage > t_before.EvolutionStage) t_parts.Add($"{t_after.EvolutionStage}단계 진화");

        return t_parts.Count > 0 ? string.Join(" · ", t_parts) : null;
    }

    /// <summary>이번 강화(_from → _to)로 새로 열린 키워드. 없으면 None.</summary>
    static CardKeyword NewKeywords(int _card, int _from, int _to)
    {
        if (_card <= 0 || _to <= _from) return CardKeyword.None;

        return CardGrowthManager.GrowthAtLevel(_card, _to).UnlockedKeywords
             & ~CardGrowthManager.GrowthAtLevel(_card, _from).UnlockedKeywords;
    }

    /// <summary>이번 강화(_from → _to)로 시너지가 새로 열렸는가.</summary>
    static bool UnlockedSynergy(int _card, int _from, int _to)
    {
        if (_card <= 0 || _to <= _from) return false;

        return !CardGrowthManager.GrowthAtLevel(_card, _from).SynergyUnlocked
            &&  CardGrowthManager.GrowthAtLevel(_card, _to).SynergyUnlocked;
    }

    /// <summary>강화 비용 표기. 더 올릴 단계가 없으면 빈값, 값을 묻지 않는 한 방이면 숫자 대신 문구.</summary>
    static string CostLabel(bool _hasStep, long _cost) => !_hasStep  ? NoValue
                                                       : _cost <= 0 ? FreeCost
                                                                    : _cost.ToString("N0");

    /// <summary>비용 재화 아이콘. 표에 그림이 없으면 null이고, 그때는 호출부가 프리팹 저작을 그대로 둔다.</summary>
    static Sprite CostIconOf(ECurrencyType _currency) => CurrencyLook.IconOf(_currency);

    /// <summary>비용 숫자·아이콘. 강화와 진화가 버튼 하나를 나눠 쓰므로 비용을 적는 칸도 하나뿐이다.</summary>
    void ApplyCost(bool _hasStep, GrowthStep _step)
    {
        string t_cost = CostLabel(_hasStep, _step.Cost);

        if (this.enhanceCostText != null) this.enhanceCostText.text = t_cost;

        // 무료 한 방에는 재화 그림도 걷는다 — 값을 치르는 물건이 아니라고 말하는 자리이기 때문이다.
        bool t_charged = _hasStep && _step.Cost > 0;

        ApplyCostIcon(this.enhanceCostIcon, t_charged, _step.Currency);
    }

    // 아이콘 칸을 두지 않는다 — 간식은 재화 enum이 아니라 CurrencyLook에 그림을 물어볼 창구가 없다.
    void ApplyLimitBreakCost(bool _hasStep, LimitBreakStep _step, int _snack)
    {
        if (this.limitBreakLabelText != null) this.limitBreakLabelText.text = this.limitBreakLabel;
        if (this.limitBreakCostText  == null) return;

        this.limitBreakCostText.text = _hasStep ? $"간식 {_snack:N0}/{_step.SnackCost:N0}" : NoValue;
    }

    void ApplyCostIcon(Image _target, bool _charged, ECurrencyType _currency)
    {
        if (_target == null) return;

        _target.enabled = _charged;

        Sprite t_icon = CostIconOf(_currency);
        if (t_icon != null) _target.sprite = t_icon;
    }

    /// <summary>성장 버튼의 조작 가능 여부. 강화든 진화든 무는 것도 결과도 같은 한 방이라 판정이 하나다.</summary>
    void SetActionsEnabled(bool _interactable)
    {
        SetActionEnabled(this.enhanceButton, this.m_enhanceTone, _interactable);
    }

    // 한 버튼이 두 얼굴을 갈아입는다 — 진화를 옆자리로 두면 같은 한 방이 버튼 둘로 읽히고, 그 자리는 한계돌파가 쓴다.
    void ApplyGrowthFace(bool _evolve)
    {
        if (this.enhanceLabelText != null) this.enhanceLabelText.gameObject.SetActive(!_evolve);
        if (this.enhanceIcon      != null) this.enhanceIcon.SetActive(!_evolve);

        if (this.evolveLabelText != null) this.evolveLabelText.gameObject.SetActive(_evolve);
        if (this.evolveIcon      != null) this.evolveIcon.SetActive(_evolve);
    }

    // 못 누르는 동안 버튼이 통째로 흑백이 된다. 알파를 낮추지 않는 이유는 하단 바 밑판이 어두워 반투명이 곧 사라짐이라서다.
    static void SetActionEnabled(Button _button, UIEffect _tone, bool _interactable)
    {
        if (_button == null) return;

        _button.interactable = _interactable;

        if (_tone != null) _tone.toneIntensity = _interactable ? 0f : 1f;
    }

    // 지금 강화가 왜 막혔는지 한 문장. 결과판의 "한 번 더" 아래에만 뜬다.
    static string GrowthNotice(bool _hasStep, bool _canPay, ECurrencyType _currency)
    {
        if (!_hasStep) return MaxLevelNotice;
        if (_canPay)   return string.Empty;

        string t_name = CurrencyLook.NameOf(_currency);
        return string.Format(NotAffordableFormat, t_name, KoreanText.Subject(t_name));
    }

    /// <summary>간식을 먹여 한계돌파 1단계. 강화 연출·결과판·튜토리얼 통지를 타지 않는다.</summary>
    void OnLimitBreakPressed()
    {
        if (this.m_ritualPlaying) return;

        int t_card = CardAt(this.m_index);
        if (t_card <= 0) return;

        // 유예를 먼저 세운다 — 판정이 서버로 나가 있는 동안 버튼이 살아 있으면 같은 차감이 여러 번 나간다.
        this.m_ritualPlaying = true;

        // 이 유예에는 무대가 없다 — 앞선 강화의 연출 참조를 두면 SkipPlayingFx가 왕복 내내 탭을 삼킨다.
        this.m_activeRitual = null;

        LimitBreakAsync(t_card).Forget();
    }

    // 서버 왕복 한계돌파. 오른 단계는 응답이 도착한 프레임에 처음 드러난다 — 강화와 같은 규율이다.
    async UniTaskVoid LimitBreakAsync(int _card)
    {
        ELimitBreakOutcome t_outcome;

        // Release는 반드시 finally에서 — 예외나 조기 반환으로 한 번이라도 새면 전역 오버레이가 화면을 영영 잠근다.
        ServerWaitOverlay.Hold(this);
        try
        {
            t_outcome = await CardGrowthManager.TryLimitBreakAsync(_card);
        }
        finally
        {
            this.m_ritualPlaying = false;
            ServerWaitOverlay.Release(this);
        }

        // 왕복 중 이 창이 사라졌다면 그릴 화면이 없다(단계·간식은 서버가 이미 확정했다).
        if (this == null) return;

        // 창이 닫혔으면 그릴 것도 깨울 안내도 없다 — 한계돌파는 튜토리얼 통지를 타지 않는다.
        if (!this.isActiveAndEnabled) return;

        // 거절 사유는 그리지 않는다 — 버튼이 간식 잔량으로 잠겨 있어 거절은 곧 화면이 낡았다는 뜻이고 답은 갱신이다.
        int t_now = CardAt(this.m_index);
        if (t_now > 0) RefreshGrowth(t_now, OwnershipManager.IsOwned(t_now));

        // 새 단계가 드러나는 그 한 박을 강조한다. 갱신 뒤라 섬광이 물러날 때 이미 오른 체력이 서 있다.
        if (t_outcome == ELimitBreakOutcome.Success && this.cardView != null) this.cardView.FlashGrowth();

        RefreshArrows();
    }

    void OnEnhancePressed()
    {
        // 결과를 읽는 중이면 이 버튼이 곧 "한 번 더"다 — 손이 이미 가 있는 하단 바 버튼을 그대로 쓴다.
        if (this.resultPanel != null && this.resultPanel.IsOpen)
        {
            this.resultPanel.RequestRetry();
            return;
        }

        if (this.m_ritualPlaying) return;

        int t_card = CardAt(this.m_index);
        if (t_card <= 0)
        {
            AbortEnhance(0);
            return;
        }

        // 유예를 먼저 세운다 — 판정이 서버로 나가 있는 동안 버튼이 살아 있으면 같은 결제가 여러 번 나간다.
        this.m_ritualPlaying = true;

        EnhanceAsync(t_card).Forget();
    }

    // 서버 왕복 강화. 잡아 둘 값도 고를 연출도 전부 왕복 "전"이다 — 레벨이 오르고 나면 다른 답이 된다.
    async UniTaskVoid EnhanceAsync(int _card)
    {
        int t_card = _card;

        // 시도 전에 잡아둔다 — 결과에는 오른 폭도 이전 값도 없다.
        int t_fromLevel = CardGrowthManager.GrowthOf(t_card).Level;
        int t_fromHp    = DeckPower.MaxHpOf(t_card);

        CardGrowthRitualView t_ritual = RitualFor(t_card);
        bool                 t_evolve = t_ritual == this.evolveRitual && this.evolveRitual != null;

        // 왕복 전의 문지기. 진실원은 여전히 서버라 이 검사는 낙관일 뿐이고, 통과한 뒤 거절이 오는 갈래는 정상 동작이다.
        if (CardGrowthManager.Precheck(t_card) != EEnhanceOutcome.Success)
        {
            AbortEnhance(t_card);
            return;
        }

        EnhanceResult t_result;

        // Release는 반드시 finally에서 — 예외나 조기 반환으로 한 번이라도 새면 전역 오버레이가 화면을 영영 잠근다.
        ServerWaitOverlay.Hold(this);
        try
        {
            t_result = await CardGrowthManager.TryEnhanceAsync(t_card);
        }
        finally
        {
            ServerWaitOverlay.Release(this);
        }

        // 왕복 중 이 창이 사라졌다면 되돌릴 화면도 태울 연출도 없다(레벨·잔액은 서버가 이미 확정했다).
        if (this == null) return;

        // 저작 실수(초기화 누락)는 조용히 넘기지 않는다 — 재화는 소모되지 않았고 원인이 화면 밖에 있다.
        if (t_result.Outcome == EEnhanceOutcome.NotReady && !CardGrowthManager.IsReady)
            Debug.LogError("[CardDetailOverlayView] 성장 데이터 미초기화 — CardGrowthManager.Init()이 초기화에서 호출되지 않았다.");

        bool t_played = t_result.Outcome == EEnhanceOutcome.Success || t_result.Outcome == EEnhanceOutcome.Failed;

        // 왕복 중 창이 닫혔어도 성립한 강화는 알린다 — 기다리던 안내가 영영 깨어나지 못하면 진행이 막힌다.
        if (!this.isActiveAndEnabled)
        {
            this.m_ritualPlaying = false;
            if (t_played) NotifyEnhanceSettled(t_result);
            return;
        }

        // 결제 전에 막힌 경우엔 보여줄 결과가 없다. 미배선도 같은 길로 — 배선 실패가 소프트락이 되면 안 된다.
        if (!t_played || t_ritual == null)
        {
            AbortEnhance(t_card);

            // 강화가 실제로 일어났는데 보여줄 연출만 없는 길이면 여기가 곧 "다 끝난" 시점이다.
            if (t_played) NotifyEnhanceSettled(t_result);
            return;
        }

        this.m_activeRitual = t_ritual;

        // 레벨은 이미 올랐고 화면은 아직 옛 상태라, "곧 켜질 것"이 정확히 나오는 유일한 시점이다.
        if (t_evolve && this.cardView != null)
        {
            this.cardView.CollectPendingKeywordFrames(t_card, OwnershipManager.IsOwned(t_card), this.m_emblemBuffer);
            this.evolveRitual.SetEmblems(this.m_emblemBuffer);
        }

        // 누른 순간엔 조작만 잠근다 — 여기서 값을 다시 그리면 상세 패널이 걷히는 0.15초 동안 새 값이 비친다.
        LockControls();

        // 무대를 쥐기 직전에 알린다 — 바깥의 안내가 결과판 위에 남지 않게.
        OnAnyEnhanceStarted?.Invoke();

        t_ritual.Play(
            t_result.Outcome, _awaitReturn: this.resultPanel != null,
            _onReveal: () =>
            {
                // 카드가 이미 바뀐 뒤 잘려 들어온 콜백이면 옛 값을 찍지 않는다.
                if (CardAt(this.m_index) != t_card) return;

                // 카드가 빛에 완전히 덮인 프레임이다 — 값은 전부 여기서 찍는다(물러나는 빛이 새 Lv·HP를 드러낸다).
                RefreshGrowth(t_card, OwnershipManager.IsOwned(t_card));

                // 그냥 바뀌어 있기만 하면 프레임 장식에 묻힌다 → 드러나는 한 박에 글자가 물들고 부푼다.
                if (t_result.Outcome == EEnhanceOutcome.Success && this.cardView != null)
                    this.cardView.FlashGrowth();
            },
            _onSettled: () =>
            {
                // 무대에 선 카드가 바뀌었으면 옛 결과를 띄우지 않는다. 다만 무대는 반드시 돌려보낸다 —
                // 결과판이 안 뜨면 복귀를 시작할 주체가 없어 오버레이가 통째로 굳는다.
                if (CardAt(this.m_index) != t_card)
                {
                    t_ritual.PlayReturn();
                    return;
                }

                ShowResultPanel(t_card, t_result, t_fromLevel, t_fromHp, t_evolve);
            },
            _onFinished: () =>
            {
                this.m_ritualPlaying = false;

                // 지금 보이는 카드로 다시 그린다 — 중간에 카드가 바뀌었어도 화면과 값이 어긋나지 않게.
                int t_now = CardAt(this.m_index);
                if (t_now > 0) RefreshGrowth(t_now, OwnershipManager.IsOwned(t_now));
                RefreshArrows();

                // 무대가 돌아와 줄이 다시 보이는 지금이 해금 연출의 자리다(하단 바 복귀도 여기가 쥔다 — 멱등).
                PlayPendingUnlockFx();

                // "한 번 더"는 여기서 이어간다 — 그 경로의 무대는 걷힌 채라(EndAwaitForChain) 곧장 물려받는다.
                // 체인 중에도 알린다 — 체인의 끝을 기다리면 실패·만렙으로 맺힐 때 성공 신호가 통째로 사라진다.
                NotifyEnhanceSettled(t_result);

                // 구독자가 이 결과를 듣고 창을 닫았다면 예약은 Hide가 이미 지웠다 — 체인은 여기서 끝난다.
                if (!this.m_retryQueued) return;

                this.m_retryQueued = false;
                OnEnhancePressed();
            });
    }

    // 강화·진화가 같은 키인 이유: 안내가 시키는 일은 "한 단계 키워라" 하나이고 관문에서는 버튼의 얼굴만 갈린다.
    /// <summary>안내 타깃을 지금 서 있는 성장 버튼으로 옮긴다(_button이 null이면 내린다).</summary>
    void ApplyGrowthAnchor(Button _button)
    {
        if (_button == this.m_anchoredGrowthButton) return;

        if (this.m_anchoredGrowthButton != null)
            TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.CardDetailEnhanceButton,
                                              this.m_anchoredGrowthButton.transform as RectTransform);

        this.m_anchoredGrowthButton = _button;

        if (_button != null)
            TutorialAnchorRegistry.Register(EOutgameTutorialAnchor.CardDetailEnhanceButton,
                                            _button.transform as RectTransform, _button);
    }

    // 성공한 강화가 다 끝났음을 알린다. 실패·미결제는 알리지 않는다 — 실패는 같은 자리에서 다시 누르는 일이다.
    static void NotifyEnhanceSettled(EnhanceResult _result)
    {
        OnAnyEnhanceSettled?.Invoke(_result);
    }

    // 보여줄 것 없이 끝난 강화(잔액 부족·최고 레벨·미초기화·연출 미배선). 잠금을 풀고 조작을 되살린다.
    // 무대까지 되돌리는 이유는 "한 번 더"로 이어온 길 때문이다 — 그 경로에선 패널이 걷힌 채로 넘어온다.
    void AbortEnhance(int _card)
    {
        this.m_ritualPlaying = false;

        CancelRituals();
        ShowBottomBar();   // 어느 경로로 잘렸든 조작 바는 돌아와야 한다(숨은 채 굳으면 화면이 죽는다)

        // 잔액부족은 통지가 없다 → 여기서 한 번(멱등)
        if (_card > 0) RefreshGrowth(_card, OwnershipManager.IsOwned(_card));
        RefreshArrows();
    }

    // 강화를 누른 직후의 잠금. 값은 손대지 않는다 — 여기서 RefreshGrowth를 부르면 공개할 것이 사라진다.
    void LockControls()
    {
        SetActionsEnabled(false);

        HideBottomBar();   // 담금질 구간에는 카드만 남는다
        RefreshArrows();   // 연출 중에 카드가 넘어가면 무대에 선 카드와 결과가 어긋난다.
    }

    // 결과를 읽는 중엔 강화 얼굴만 "한 번 더"가 된다 — 진화는 단계가 바뀌는 사건이라 이름이 흔들리면 안 된다.
    void SetActionLabel(bool _retry)
    {
        if (this.enhanceLabelText != null) this.enhanceLabelText.text = _retry ? this.retryLabel : this.enhanceLabel;
        if (this.evolveLabelText  != null) this.evolveLabelText.text  = this.evolveLabel;
    }

    // 걷기는 즉시(연출이 이미 시작됐다), 복귀는 페이드. 들어올 때 눈에 띄면 결과를 읽던 시선을 뺏는다.
    void HideBottomBar()
    {
        if (this.bottomBarGroup == null) return;

        this.bottomBarGroup.DOKill();
        this.bottomBarGroup.alpha          = 0f;
        this.bottomBarGroup.blocksRaycasts = false;
    }

    /// <summary>하단 바를 되돌린다. 결과 행이 다 뜬 시점·중단 경로 어디서 불려도 같은 상태로 끝난다(멱등).</summary>
    void ShowBottomBar()
    {
        if (this.bottomBarGroup == null) return;

        // 열람 전용에는 되돌릴 바가 없다 — 되부르는 경로가 여럿이라 각 호출처에 흩지 않고 여기서 막는다.
        if (this.m_readOnly) { HideBottomBar(); return; }

        this.bottomBarGroup.DOKill();
        this.bottomBarGroup.blocksRaycasts = true;
        this.bottomBarGroup.DOFade(1f, Mathf.Max(0.01f, this.bottomBarFadeDuration))
            .SetLink(this.bottomBarGroup.gameObject);
    }

    // 결과판을 띄운다. _evolve면 같은 판을 진화의 이름으로 쓴다 — 제목이 갈리면 무엇을 한 것인지 흐려진다.
    void ShowResultPanel(int _card, EnhanceResult _result, int _fromLevel, int _fromHp, bool _evolve)
    {
        if (this.resultPanel == null) return;   // 미배선이면 연출이 스스로 걷는다

        // 읽기를 결과판이 넘겨받는 자리다 — 카드 위의 강조는 여기서 원상복귀한다.
        if (this.cardView != null) this.cardView.RestoreGrowthFlash();

        // "한 번 더"의 가부는 오른 뒤의 다음 단계로 판정한다 — 방금 쓴 비용이 아니라 지금 낼 비용이 기준이다.
        bool t_hasNext = CardGrowthManager.TryGetNextStep(_card, out GrowthStep t_next);

        // 다음 한 방이 진화 관문이면 잇지 않는다 — 무대가 갈려 연타의 이득이 사라지고, 진화는 관람 대상이다.
        bool t_nextIsEvolve = t_hasNext && CardGrowthManager.IsEvolutionLevel(t_next.Level);

        // 이번 한 방으로 키워드·시너지가 열렸으면 같은 이유로 잇지 않는다 — 연타로 넘어가면 무엇을 열었는지 못 본다.
        bool t_unlocked = NewKeywords(_card, _fromLevel, _result.Level) != CardKeyword.None
                       || UnlockedSynergy(_card, _fromLevel, _result.Level);

        // 탭을 기다리지 않고 스스로 걷혀 상세로 돌아가는 판(= 이을 것이 없는 자리).
        bool t_selfReturn = t_nextIsEvolve || t_unlocked;

        // 안내가 시킨 한 방은 이 화면이 종착지다 — "한 번 더"를 되살리면 유저가 그걸 눌러 안내 밖으로 샌다.
        bool t_guided = OutgameTutorialGuide.IsCurrentAction(EOutgameTutorialAction.WaitEnhance);

        // 하단 바를 되살리지 않는 자리 — 안내가 얹은 말은 유저가 읽고 탭할 때까지 판이 서 있어야 한다.
        bool t_barStaysDown = t_selfReturn || t_guided;
        bool t_canRetry     = t_hasNext && !t_barStaysDown && CurrencyManager.CanAfford(t_next.Currency, t_next.Cost);

        var t_line = new EnhanceResultLine(_result.Outcome,
                                           _fromHp, DeckPower.MaxHpOf(_card),
                                           _fromLevel, _result.Level,
                                           // 못 잇는 이유가 잔액이 아니라 규칙이면 안내도 없다(거짓 문장이 뜬다).
                                           t_canRetry,
                                           t_barStaysDown ? string.Empty
                                                          : GrowthNotice(t_hasNext, t_canRetry, t_next.Currency),
                                           // 비용도 "지금 낼 값" 기준 — 판정과 같은 단계를 봐야 숫자와 가부가 어긋나지 않는다.
                                           CostLabel(t_hasNext, t_next.Cost),
                                           // 그림도 판정과 같은 단계에서 뽑는다 — 재화는 레벨마다 갈릴 수 있다.
                                           CostIconOf(t_next.Currency),
                                           UnlockLabel(_card, _fromLevel, _result.Level),
                                           _evolve ? this.evolveResultTitle : null);

        // 결과를 읽는 동안 하단 바 버튼이 "한 번 더"를 맡는다 — 값도 지금 낼 비용으로 갈아둔다.
        // 바가 걷힌 채로 남는 판에서는 아무것도 되살리지 않는다(복귀 도중 한 프레임 비친다).
        SetActionsEnabled(t_canRetry);
        if (!t_barStaysDown)
        {
            ApplyCost(t_hasNext, t_next);
            SetActionLabel(true);
        }

        this.resultPanel.Show(t_line,
                              _onClose: () => this.m_activeRitual.PlayReturn(),
                              // 무대는 돌려보내지 않는다 — 상세 패널이 돌아왔다 곧바로 다시 걷히면 연타의 리듬이 끊긴다.
                              // 단, 이어받을 수 있는 것은 같은 연출뿐이다(강화 ↔ 진화면 무대를 돌려보내고 새로 시작한다).
                              _onRetry: () =>
                              {
                                  this.m_retryQueued = true;

                                  if (RitualFor(_card) == this.m_activeRitual) this.m_activeRitual.EndAwaitForChain();
                                  else                                        this.m_activeRitual.PlayReturn();
                              },
                              // 읽을 것이 다 나왔다 — 이제 하단 바가 돌아와 "한 번 더"를 받는다.
                              // 스스로 걷히는 판·안내가 끝맺는 판에서는 걷은 채로 둔다(깜빡임이 더 눈에 걸린다).
                              _onRowsDone: () =>
                              {
                                  if (!t_barStaysDown) ShowBottomBar();
                                  OnAnyEnhanceResultReady?.Invoke(_result);
                              },
                              // 이을 것이 없는 판이라 탭을 기다리지 않는다 — 읽을 것이 다 나오면 스스로 상세로 돌아간다.
                              _autoReturn: t_selfReturn);
    }

    void SetLevelText(int _level)
    {
        if (this.levelValueText != null)
            this.levelValueText.text = GrowthStar.ProgressLabel(_level, CardGrowthManager.MaxLevel);
    }

    void BuildKeywordSection(int _card, bool _owned)
    {
        // 지금 지은 내용의 기준값 — 카드 전환(Apply)도 이 길을 지나므로 감지가 곧바로 한 번 더 짓지 않는다.
        this.m_keywordCard = _card;
        this.m_shownTrait  = _owned ? CardVisualRules.TraitKeywords(_card) : CardKeyword.None;
        this.m_shownInfo   = _owned ? CardVisualRules.InfoKeywordsWithLocked(_card) : CardKeyword.None;

        // 카드 키워드는 keywordUnlockLevel 하나로 통째로 열린다 → 열린 것이 하나도 없으면 섹션 전체가 잠긴 것이다.
        this.m_shownKeywordLocked = KeywordSectionLocked(_card, _owned);
        SetSectionLock(this.keywordSectionLock, this.m_shownKeywordLocked,
                       this.m_pendingUnlockedKeywords != CardKeyword.None);

        var t_lines = new List<string>();
        int t_used  = 0;

        if (_owned && this.keywordIconConfig != null && this.keywordChipRoot != null)
        {
            // 카드 타일과 달리 해금 전 키워드도 잠김 룩으로 목록에 넣는다 — 앞으로 무엇을 여는지도 읽는 자리다.
            CardKeyword t_all    = CardVisualRules.InfoKeywordsWithLocked(_card);
            CardKeyword t_locked = CardVisualRules.LockedKeywords(_card);

            // 순회 순서 = CardKeyword 선언 순. 카드 타일 아이콘 줄과 같은 순서다.
            foreach (CardKeyword t_kw in (CardKeyword[])Enum.GetValues(typeof(CardKeyword)))
            {
                if (t_kw == CardKeyword.None) continue;
                if ((t_all & t_kw) == 0) continue;
                if (!this.keywordIconConfig.TryGetEntry(t_kw, out KeywordIconConfig.Entry t_entry)) continue;

                bool t_open = (t_locked & t_kw) == 0;
                if (TryShowChip(this.keywordChipRoot, t_used, "키워드",
                                t_entry.icon, t_entry.displayName, 1f, t_open))
                    t_used++;

                // 설명은 잠겨도 그대로 적는다 — 무엇이 열릴지 모르면 강화할 이유가 안 읽힌다.
                if (!string.IsNullOrEmpty(t_entry.explain)) t_lines.Add(t_entry.explain);
            }
        }

        HideChipsFrom(this.keywordChipRoot, t_used);

        // 목록은 자동 안내와 같은 생성기(CollectIntros)로 만든다 — 다른 길로 만들면 문구·순서가 조용히 갈라진다.
        ApplySection(this.keywordSection, this.keywordDescText, t_lines, _owned,
                     IntroClick(_owned && !this.m_shownKeywordLocked
                                ? CollectIntros(_card, CardVisualRules.InfoKeywords(_card), false)
                                : null));
    }

    void BuildSynergySection(int _card, bool _owned)
    {
        // 지금 지은 잠김 상태. RefreshUnlockVisuals의 변경 감지가 이 값을 본다(키워드 줄과 같은 규약).
        this.m_shownSynergyOpen = _owned && SynergyUnlocked(_card);

        // 시너지는 1차 진화 관문 하나로 전부 열리고 전부 잠긴다 → 부분 잠김이 없어 항상 섹션째로 덮는다.
        IReadOnlyList<SynergyData> t_synergies = _card > 0
            ? CardCatalog.RequireSynergies(_card)
            : Array.Empty<SynergyData>();
        bool t_hasSynergy = t_synergies.Count > 0;
        SetSectionLock(this.synergySectionLock, _owned && t_hasSynergy && !this.m_shownSynergyOpen,
                       this.m_pendingSynergyUnlockFx);

        var t_lines = new List<string>();
        int t_used  = 0;

        if (_owned && t_synergies.Count > 0 && this.synergyChipRoot != null)
        {
            bool t_open = SynergyUnlocked(_card);

            var t_seen = new HashSet<SynergyData>();
            foreach (SynergyData t_syn in t_synergies)
            {
                if (t_syn == null || !t_seen.Add(t_syn)) continue;   // 중복 나열 방어

                // 요구치는 이름 뒤에 붙인다 — 칩 한 줄에서 "무엇이 몇 장에 켜지는가"가 끝난다.
                // 아이콘 배율은 시너지 PNG 투명 여백 보정 — 없으면 키워드 칩 옆에서 혼자 작아 보인다.
                string t_req  = SynergyText.Requirement(t_syn);
                string t_name = string.IsNullOrEmpty(t_req) ? SynergyText.Name(t_syn)
                                                            : $"{SynergyText.Name(t_syn)} {t_req}";

                if (TryShowChip(this.synergyChipRoot, t_used, "시너지",
                                t_syn.activeIcon, t_name,
                                SynergyIconStrip.IconPadCompensation, t_open))
                    t_used++;

                // 아래 줄에는 효과 설명만 남긴다 — 비어 있으면 요구치라도 적어 그 자리가 "없음"이 되지 않게 한다.
                string t_effect = SynergyText.Effect(t_syn);
                t_lines.Add(string.IsNullOrEmpty(t_effect) ? t_name : t_effect);
            }
        }

        HideChipsFrom(this.synergyChipRoot, t_used);

        ApplySection(this.synergySection, this.synergyDescText, t_lines, _owned,
                     IntroClick(_owned && this.m_shownSynergyOpen
                                ? CollectIntros(_card, CardKeyword.None, true)
                                : null));
    }

    /// <summary>이 목록을 여는 손잡이. 세울 것이 없으면 null — 그 자리는 눌리지 않는다.</summary>
    Action IntroClick(List<UnlockIntro> _intros)
    {
        if (_intros == null || _intros.Count == 0) return null;
        return () => ShowIntros(_intros);
    }

    // 카드가 바뀌면 읽던 자리도 되감는다. verticalNormalizedPosition은 내용이 짧을 때 튀어 좌표를 직접 0으로 둔다.
    void RewindScroll()
    {
        if (this.detailScroll == null || this.detailScroll.content == null) return;

        this.detailScroll.StopMovement();

        // Content는 위에 매달려 있다(pivot y=1, 상단 앵커) → y=0이 곧 맨 위다.
        Vector2 t_pos = this.detailScroll.content.anchoredPosition;
        this.detailScroll.content.anchoredPosition = new Vector2(t_pos.x, 0f);
    }

    // 카드 설명 한 문단. 빈값 규약(없음/???)만 다른 섹션과 맞춘다.
    void ApplyDescription(int _card, bool _owned)
    {
        if (this.descriptionText == null) return;

        string t_text = _owned && _card > 0 ? CardCatalog.RequireSpec(_card).CardExplain : null;

        this.descriptionText.text = !string.IsNullOrEmpty(t_text) ? t_text
                                  : _owned                        ? NoneValue
                                                                  : LockedName;
    }

    // 비어 있어도 섹션을 끄지 않는다 — 카드를 넘길 때마다 목록이 들쭉날쭉하면 어디를 읽던 중이었는지 잃는다.
    // _onClick이 있으면 섹션 띠 전체가 눌리는 자리가 된다(해금 안내 다시 보기). null이면 눌리지 않는다.
    static void ApplySection(GameObject _section, TMP_Text _desc, List<string> _lines, bool _owned,
                             Action _onClick = null)
    {
        if (_desc != null)
            _desc.text = _lines.Count > 0 ? string.Join("\n", _lines)
                       : _owned           ? NoneValue
                                          : LockedName;

        if (_section != null)
        {
            BindSectionClick(_section, _onClick);
            _section.SetActive(true);
        }
    }

    // 뿌리엔 그림이 없어 그대로면 탭이 딤으로 통과해 창이 닫힌다 → 안 보이는 판을 깔아 띠를 통째로 받는다.
    /// <summary>섹션 띠를 눌러 열 수 있게 만든다. 저작(프리팹)은 건드리지 않고 런타임에만 세운다.</summary>
    static void BindSectionClick(GameObject _section, Action _onClick)
    {
        var t_button = _section.GetComponent<Button>();
        if (t_button == null)
        {
            var t_hit = _section.GetComponent<Image>();
            if (t_hit == null)
            {
                t_hit = _section.AddComponent<Image>();
                t_hit.color = Color.clear;
            }

            t_button = _section.AddComponent<Button>();
            t_button.targetGraphic = t_hit;

            // 띠에 색 전이를 얹으면 잠김 룩의 회색과 섞여 "지금 눌리는가"가 오히려 안 읽힌다.
            t_button.transition = Selectable.Transition.None;
        }

        // 칩과 달리 이 노드는 카드를 넘겨도 그대로 재사용된다 — 지우지 않으면 앞 카드의 개념이 함께 열린다.
        t_button.onClick.RemoveAllListeners();

        if (t_button.targetGraphic != null) t_button.targetGraphic.raycastTarget = _onClick != null;
        t_button.interactable = _onClick != null;

        if (_onClick != null) t_button.onClick.AddListener(() => _onClick());
    }

    /// <summary>개념 안내를 전면에 다시 세운다(해금 순간의 자동 안내와 같은 화면).</summary>
    void ShowIntros(List<UnlockIntro> _intros)
    {
        if (_intros == null || _intros.Count == 0) return;
        if (!UnlockIntroOverlay.TryGet(out UnlockIntroOverlay t_overlay)) return;

        // 카드를 함께 넘긴다 — 안내 안의 데모 무대가 이 카드를 공격자로 세운다.
        t_overlay.Show(_intros, CardAt(this.m_index), null);
    }

    // 칩은 런타임에 만들지 않는다 — 깔아 두는 쪽은 Tools/UI/도감 상세창 칩 박기다.
    /// <summary>줄에 미리 깔아 둔 _index번째 칩을 채워 켠다. 칩이 모자라면 false — 호출부는 거기서 멈춘다.</summary>
    static bool TryShowChip(Transform _root, int _index, string _what,
                            Sprite _icon, string _name, float _iconScale, bool _open)
    {
        if (_root == null) return false;
        if (_index >= _root.childCount)
        {
            Debug.LogWarning($"[CardDetailOverlay] {_what} 칩이 모자라다 — 프리팹에 깔린 {_root.childCount}개까지만 보인다. " +
                             "Tools/UI/도감 상세창 칩 박기로 개수를 늘릴 것");
            return false;
        }

        Transform t_child = _root.GetChild(_index);
        var       t_chip  = t_child.GetComponent<KeywordExplainItem>();
        if (t_chip == null) return false;

        t_chip.Init(_icon, _name, null, _iconScale, _open);
        t_child.gameObject.SetActive(true);
        return true;
    }

    /// <summary>_from번째부터 남은 칩을 끈다. 앞 카드가 더 많은 칩을 쓰고 갔을 수 있다.</summary>
    static void HideChipsFrom(Transform _root, int _from)
    {
        if (_root == null) return;

        for (int t_i = _from; t_i < _root.childCount; t_i++)
            _root.GetChild(t_i).gameObject.SetActive(false);
    }
}
