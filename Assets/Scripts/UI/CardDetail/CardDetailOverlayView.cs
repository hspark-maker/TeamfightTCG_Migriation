using System;
using System.Collections.Generic;
using Coffee.UIEffects;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

// 로비 컬렉션 탭의 카드 상세 오버레이(CardDetailOverlay.prefab 루트에 부착).
// 카드 타일을 길게 누르면 열리고, 누른 카드의 이름·체력·키워드·시너지를 채운다.
// 닫기 버튼은 두지 않는다 — 배경(딤)을 탭하면 닫힌다. 카드·상세 패널·조작 바 위의 탭은 닫지 않는다.
//
// 인게임 카드 정보창(PooledCardElement)과 달리 풀드 UI가 아니라 로비 씬에 직접 배치한다 —
// 로비 전용 풀스크린 한 장이라 Addressables("UIPrefab" 라벨) 등록까지 갈 이유가 없다(PackOpenOverlay와 같은 결).
//
// 표시 규칙은 복제하지 않는다: 카드 그림 한 장은 CardVisualView.Bind, 시너지 이름은 SynergyText,
// 키워드 아이콘·표시명·설명은 KeywordIconConfig가 정본이다.

/// <summary>상세를 어떤 모습으로 띄울지. <b>기본값(default)이 곧 도감에서 여는 평상시</b>다 —
/// 축이 하나 더 늘어도 기존 호출처가 그대로 성립하려면 "아무것도 켜지 않은 것"이 현행이어야 한다.
///
/// 축을 하나로 뭉치지 않는 이유: 지금은 카드팩 개봉 한 곳이 셋을 함께 켜서 같은 스위치처럼 보이지만,
/// 서로 다른 질문에 답한다 — 조작을 줄 것인가 / 누구 위에 뜰 것인가 / 상단 바 자리를 쓸 것인가.
/// 뭉쳐두면 "로비 팝업 위에는 뜨되 상단 바는 남긴다" 같은 조합을 표현할 수 없다.</summary>
public readonly struct CardDetailOpenOptions
{
    /// <summary>강화·진화 조작을 통째로 걷고 표시만 한다(개봉 결과처럼 "확인하는 자리").</summary>
    public readonly bool ReadOnly;

    /// <summary>지금 떠 있는 모든 캔버스 위로 올라탄다. 순서 값은 상세가 스스로 구한다 —
    /// 여는 쪽은 "위에 떠라"만 말하면 된다.</summary>
    public readonly bool LiftAboveAll;

    /// <summary>로비 상단 재화 바를 비켜 앉은 크기를 부모 가득 편다.
    /// 그 바가 없는 화면 위에 뜰 때 필요하다 — 비운 띠로 아래 화면이 그대로 비친다.</summary>
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
    const string FreeCost    = "무료";   // 안내가 대준 한 방. 숫자 0을 띄우면 값을 치르는 칸처럼 읽힌다

    // 강화가 왜 막혔는지. 결과판의 "한 번 더" 아래 문구가 쓴다.
    // 재화 표시명의 공용 진실원은 아직 없다 — 강화가 쓰는 재화가 둘뿐이라 표를 만들지 않았다.
    const string MaxLevelNotice          = "최고 레벨에 도달했다";
    const string NotAffordableNotice     = "골드가 부족하다";
    const string NotAffordableDiaNotice  = "다이아가 부족하다";
    const string NotAffordableEnergyNotice = "에너지가 부족하다";

    [Header("배선")]
    [SerializeField] CardVisualView cardView;        // CardArea 안의 CardUIView 인스턴스
    [SerializeField] TMP_Text       powerValueText;  // 체력 수치(프리팹 목업의 "파워" 행을 체력으로 쓴다)
    [Tooltip("상세 목록 스크롤. 카드를 넘길 때 맨 위로 되감는다. 미배선이면 되감기만 없다.")]
    [SerializeField] ScrollRect     detailScroll;

    [Header("성장 (선택 — 미배선이면 성장 표시 없이 지금까지와 동일하게 동작)")]
    [SerializeField] TMP_Text levelValueText;      // 강화 레벨 "Lv 3 / 10"

    [Header("강화 조작 (선택 — 미배선이면 조작 없이 표시만 한다)")]
    [SerializeField] Button     enhanceButton;
    [SerializeField] TMP_Text   enhanceCostText;    // 다음 레벨 비용(재화는 레벨마다 다르다 — 아래 아이콘이 말한다)
    [Tooltip("비용 옆 재화 아이콘. 다음 단계가 진화(다이아)면 그림이 바뀐다(옵션 — 미배선 무시).")]
    [SerializeField] Image      enhanceCostIcon;
    [Tooltip("골드 비용 레벨에 쓸 아이콘. 아래 다이아 아이콘과 둘 다 채워야 전환이 돈다(한쪽만 비면 프리팹 그림 그대로).")]
    [SerializeField] Sprite     goldIcon;
    [Tooltip("다이아 비용 레벨(진화)에 쓸 아이콘. 그 외 재화는 골드 아이콘을 쓴다.")]
    [SerializeField] Sprite     diamondIcon;
    [Tooltip("에너지 비용 레벨(일반 강화)에 쓸 아이콘. 비우면 골드 아이콘으로 떨어진다.")]
    [SerializeField] Sprite     energyIcon;
    [SerializeField] TMP_Text   successRateText;    // 다음 레벨 성공률(%)

    [Header("진화 조작 (선택 — 미배선이면 진화 구간에도 강화 버튼이 그대로 선다)")]
    [Tooltip("진화 관문 레벨(CardGrowthConfig의 1·2차 진화 레벨)에서 강화 버튼 대신 서는 버튼. " +
             "누르는 결과는 강화와 같다 — 진화는 다이아를 무는 레벨업 1회일 뿐이다.")]
    [SerializeField] Button   evolveButton;
    [SerializeField] TMP_Text evolveLabelText;
    [SerializeField] TMP_Text evolveCostText;
    [SerializeField] Image    evolveCostIcon;
    [SerializeField] string   evolveLabel = "진화";

    [Header("일러스트만 보기 (선택 — 미배선이면 기능만 빠진다)")]
    [Tooltip("누를 때마다 카드 위 정보(이름·이름판·체력·레벨·키워드 아이콘·프레임 장식·시너지)를 통째로 가렸다 되돌린다. 프레임과 일러스트만 남는다.")]
    [SerializeField] Button artOnlyButton;
    [Tooltip("선택 — 켜짐/꺼짐을 색으로 알리는 아이콘. 미배선이면 색 피드백만 빠진다(동작은 그대로).")]
    [SerializeField] Image  artOnlyIcon;
    [SerializeField] Color  artOnlyOffColor = Color.white;
    [SerializeField] Color  artOnlyOnColor  = new Color(1f, 0.82f, 0.25f, 1f);

    [Header("강화 연출 (선택 — 미배선이면 연출 없이 지금까지처럼 값만 즉시 갱신)")]
    [SerializeField] CardEnhanceRitualView ritual;

    [Tooltip("진화 관문(CardGrowthConfig의 1·2차 진화 레벨)에서 담금질 대신 서는 연출. " +
             "미배선이면 진화도 담금질로 보여준다(기능은 그대로).")]
    [SerializeField] CardEvolveRitualView evolveRitual;

    [Tooltip("진화 결과판의 제목. 같은 판을 쓰되 이름만 갈아끼운다 — 진화는 실패가 없어 문구가 하나뿐이다.")]
    [SerializeField] string evolveResultTitle = "진화 성공!";

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

    [Header("설명 섹션")]
    [Tooltip("카드 설명(CardData.cardExplain) 한 문단. 미배선이면 설명 없이 지금까지와 동일하게 동작한다.")]
    [SerializeField] TMP_Text descriptionText;

    [Header("공용")]
    // 키워드/시너지 칩 공용 프리팹. 인게임 정보창의 설명 행과 같은 컴포넌트를 쓰되,
    // 칩에는 설명 줄이 없으므로 프리팹의 explainText를 미배선으로 비워둔다(Init이 null 가드).
    // 연출 동안 걷었다가 결과를 다 읽은 뒤 돌아오는 하단 바. 담금질 구간에서는 카드만 남기고,
    // 결과 행이 다 떠오른 시점(또는 그 전에 탭으로 당긴 시점)에 되돌아와 그 버튼이 "한 번 더"를 맡는다.
    // 연출(CardEnhanceRitualView.retractPanels)이 아니라 여기가 쥐는 이유: 복귀 시점이 연출의 끝이 아니라
    // **결과판이 다 읽힌 시점**이라 연출 시퀀스의 박자와 다르다.
    [SerializeField] CanvasGroup bottomBarGroup;
    [Tooltip("하단 바가 돌아오는 시간. 결과를 읽는 눈을 방해하지 않게 짧게.")]
    [SerializeField] float bottomBarFadeDuration = 0.18f;

    // 같은 버튼이 두 가지 일을 맡으므로 글자로 그때의 뜻을 밝힌다 — 결과를 읽는 중엔 "한 번 더".
    [Tooltip("강화 버튼의 글자. 미배선이면 글자는 그대로 두고 동작만 바뀐다.")]
    [SerializeField] TMP_Text enhanceLabelText;
    [SerializeField] string   enhanceLabel = "강화";
    [SerializeField] string   retryLabel   = "한 번 더";

    // 섹션(칩 줄 + 설명)을 통째로 덮는 잠김 판. 칩 안의 자물쇠는 칩 rect를 못 벗어나 설명까지 가리지 못한다 →
    // "이 섹션이 통째로 잠겼다"는 섹션 레벨에서 덮어야 한다. 부분 해금일 때는 칩별 자물쇠가 맡는다.
    [SerializeField] GameObject keywordSectionLock;
    [SerializeField] GameObject synergySectionLock;

    // 판이 걷힌 **뒤** 그 아래 내용이 들어오는 연출(옵션). 미배선이면 걷히자마자 완성된 글자가 그대로 있다 —
    // 동작은 같고 "무엇이 드러났는지"만 덜 읽힌다.
    [SerializeField] SectionRevealFx keywordSectionReveal;
    [SerializeField] SectionRevealFx synergySectionReveal;

    [Tooltip("해금된 줄로 스크롤이 따라가는 시간. 0이면 즉시 옮긴다(연출 없이 자리만 맞춘다).")]
    [SerializeField] float unlockScrollDuration = 0.3f;

    // 런타임은 이 프리팹을 만들지 않는다 — 칩은 프리팹에 미리 깔려 있다(TryShowChip 주석).
    // 남겨 둔 이유는 깔아 주는 에디터 도구(CardDetailChipBaker)가 "무엇을 깔지"를 여기서 읽기 때문이다.
    [SerializeField] KeywordExplainItem chipPrefab;
    [SerializeField] KeywordIconConfig  keywordIconConfig;
    [SerializeField] PopupTransition    transition = new PopupTransition();

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

    /// <summary>강화가 무대를 쥐었다(연출 시작). 바깥의 안내는 여기서 자기 표시를 접어야 한다 —
    /// 결과판이 하단 바 버튼을 "한 번 더"로 되살리므로, 접지 않으면 그 버튼 위에 손가락이 다시 떠서
    /// 유저를 무한 재강화로 이끈다(결과판을 닫아야 오는 완료 신호에는 영영 닿지 못한다).</summary>
    public static event Action OnAnyEnhanceStarted;

    /// <summary>강화 한 방이 **연출·결과판까지 끝나 상세로 돌아온** 순간(성공·실패 모두). 강화를 기다리는
    /// 바깥(튜토리얼 안내)이 듣는 신호다 — 성장 통지(CardGrowthManager.OnGrowthChanged)는 판정 그 프레임에 오므로,
    /// 그걸로 화면을 넘겨받으면 방금 쓴 비용의 결과를 보지 못한 채 연출이 잘린다.</summary>
    public static event Action<EnhanceResult> OnAnyEnhanceSettled;

    /// <summary>이 창이 닫혔다. 유저가 스스로 화면을 정리하기를 기다리는 쪽(온보딩 안내)이 듣는다.</summary>
    public static event Action OnAnyClosed;

    /// <summary>강화 결과판에 읽을 것이 다 떠오른 순간(성공·실패 모두). 결과판이 아직 떠 있는 이 시점이
    /// 바깥(튜토리얼)이 결과 화면을 무대로 쓸 수 있는 유일한 자리다 — 그 위에 말을 얹든,
    /// <see cref="CloseEnhanceResult"/>로 대신 걷든 그쪽이 정한다.
    /// 판정을 함께 넘기는 이유는 성공과 실패가 다른 길로 가기 때문이다(실패는 같은 자리에서 다시 누르는 일이다).</summary>
    public static event Action<EnhanceResult> OnAnyEnhanceResultReady;

    /// <summary>떠 있는 강화 결과판을 밖에서 걷는다(튜토리얼 자동 복귀). 탭과 **같은 길**로 흘려보내므로
    /// 무대 복귀·완료 신호(<see cref="OnAnyEnhanceSettled"/>)가 그대로 이어진다. 떠 있지 않으면 아무 일도 없다.</summary>
    public static void CloseEnhanceResult()
    {
        if (s_instance == null || s_instance.resultPanel == null) return;

        s_instance.resultPanel.RequestClose();
    }

    /// <summary>지금 이 창이 화면을 덮고 있는가.</summary>
    public static bool IsOpen => s_instance != null && s_instance.gameObject.activeInHierarchy;

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

    // 진화 연출에 넘길 문양 목록. 매번 새 List를 만들지 않기 위한 재사용 버퍼다(연타하는 조작).
    readonly List<Graphic> m_emblemBuffer = new List<Graphic>();

    // 지금 무대를 쥔 연출(강화 = 담금질, 진화 = 탈각). 누른 순간에 골라 고정한다 —
    // 레벨은 그 직후 올라가므로, 나중에 다시 고르면 방금 시작한 것과 다른 연출을 붙들게 된다.
    CardGrowthRitualView m_activeRitual;

    // 프레임·아트만 보는 열람 모드. 창이 열려 있는 동안만 유지한다(OnDisable에서 내린다).
    bool m_artOnly;

    // 지금 튜토리얼 안내 타깃으로 등록해 둔 성장 버튼(강화 또는 진화). 자기가 올린 것만 내린다.
    Button m_anchoredGrowthButton;

    // 강화·진화 조작을 통째로 걷은 채 여는 모드. 카드팩 개봉 결과처럼 "방금 뽑은 것을 확인하는 자리"에서 쓴다 —
    // 그 자리에서 재화를 쓰게 두면 개봉 흐름이 갈라지고, 담금질·진화 연출이 개봉 화면 위에서 한 번 더 돈다.
    // 여는 쪽이 매번 정하므로(Open) 내릴 곳은 따로 두지 않는다.
    bool m_readOnly;

    // 다른 캔버스 위로 올라타기 위해 확보한 Canvas. 창이 열려 있는 동안만 순서를 덮어쓰고 닫히면 되돌린다 —
    // 상시 최상단으로 두면 로비 쪽 레이어(획득 연출 등)와의 현재 순서까지 뒤집힌다.
    Canvas m_sortingCanvas;

    // 로비에 배치된 authoring 크기(상단 재화 바를 비켜 앉은 값). 다른 화면 위로 올라탈 때 화면 전체로 폈다가
    // 여기로 되돌린다. 최초 1회만 잡는다 — 매번 읽으면 이미 편 값을 기준으로 잡아 되돌아갈 자리를 잃는다
    // (EnsureSlideBase와 같은 관용구).
    Vector2 m_baseOffsetMin;
    Vector2 m_baseOffsetMax;
    bool    m_baseRectCaptured;

    // 결과판의 "한 번 더". 무대가 돌아오기 전에 다음 연출을 시작하면 두 연출이 같은 노드를 두고 싸운다 →
    // 복귀가 끝나는 시점까지 눌린 사실만 들고 있는다.
    bool m_retryQueued;

    // 지금 화면에 지어 둔 키워드 표시의 기준값. 강화 통지마다 아이콘·칩을 다시 짓지 않기 위한 변경 감지용이며,
    // 두 마스크를 따로 드는 이유는 기준이 다르기 때문이다 — 카드 위(아이콘·프레임 장식)는 TraitKeywords,
    // 칩 줄은 InfoKeywords(설명 전용 포함)라 해금 키워드가 설명 전용에도 적혀 있으면 한쪽만 움직인다.
    CardData    m_keywordCard;
    CardKeyword m_shownTrait;
    CardKeyword m_shownInfo;

    // 시너지 줄은 칩마다가 아니라 관문 하나(1차 진화)로 통째로 잠긴다 → 기준값도 불리언 하나면 된다.
    bool m_shownSynergyOpen;

    // 각 버튼 밑판의 흑백 효과. 자식(라벨·아이콘·숫자)은 UIEffectReplica로 이걸 따라오므로
    // 코드가 쥐는 것은 버튼당 이 하나뿐이다. 없으면 조작 여부만 바뀐다.
    UIEffect m_enhanceTone;
    UIEffect m_evolveTone;

    // 지금 화면에 지어 둔 키워드 줄의 잠김 상태. 해금 순간을 잡으려면 마스크만으로는 부족하다 —
    // 잠김 판정은 "열린 것이 하나도 없는가"라 마스크가 그대로여도 상태가 바뀔 수 있다.
    bool m_shownKeywordLocked;

    // 방금 해금됐지만 아직 연출로 걷지 못한 판. 강화 연출이 화면을 덮고 있는 동안 해금이 확정되므로,
    // 그 사이엔 판을 남겨 두고 무대가 돌아온 뒤에 걷는다(SetSectionLock 주석 참고).
    //
    // 키워드 쪽은 불리언이 아니라 **열린 키워드 자체**를 든다 — 판을 걷고 나면 "무엇이 열렸나"에 답해야
    // 처음 보는 것만 골라 전면 안내를 세울 수 있다(None = 걷을 판 없음).
    // 시너지는 관문 하나로 통째로 열려 답이 하나뿐이라 불리언 그대로다.
    CardKeyword m_pendingUnlockedKeywords;
    bool        m_pendingSynergyUnlockFx;

    /// <summary>_card의 상세를 띄운다. 오버레이가 씬에 없으면 경고 1회 후 무시.
    /// 넘길 이웃이 없는 1장짜리 목록으로 취급한다(화살표·스와이프가 꺼진다).</summary>
    public static void Open(CardData _card)
    {
        if (_card == null) return;

        Open(new[] { _card }, 0);
    }

    /// <summary>_cards[_index]의 상세를 띄우고, 좌우로 같은 목록 안을 순환하며 넘겨볼 수 있게 한다.
    /// _cards는 "화면에 보이는 순서" 그대로여야 한다 — 넘기는 방향과 도감 배열이 어긋나면 길을 잃는다.
    /// null 슬롯(미authoring 카드)은 그대로 넘겨도 된다. 넘기기가 알아서 건너뛴다.
    ///
    /// 어떤 모습으로 띄울지는 <see cref="CardDetailOpenOptions"/>가 쥔다(기본값 = 도감에서 여는 평상시).</summary>
    public static void Open(IReadOnlyList<CardData> _cards, int _index, CardDetailOpenOptions _options = default)
    {
        if (_cards == null || _cards.Count == 0) return;

        CardDetailOverlayView t_view = Resolve();
        if (t_view == null) return;

        // 세 축을 각각 세운다. 창을 닫을 때 셋 다 내려가므로(OnDisable) 여기서 매번 다시 세우면 그만이다.
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

    /// <summary>타일에 "상세 열기 + 목록 안에서 좌우로 넘기기"를 배선한다.
    /// _index는 _cards 안에서 이 타일의 자리(= 화면에 보이는 순서상 위치).
    /// 타일 프리팹에 LongPressDetector가 아직 안 붙어 있으면 조용히 넘어간다(배선 전 상태).
    ///
    /// 탭 판정을 이 컴포넌트에 맡기는 이유는 <see cref="LongPressDetector.OnTap"/> 주석 참고 —
    /// 도감/생산 타일은 ScrollRect 안에 있어서 스크롤 드래그가 클릭으로 새면 안 된다.
    ///
    /// 목록을 값이 아니라 **참조**로 잡아둔다 — 컨트롤러가 같은 List 인스턴스를 재사용해 다시 채우면
    /// 이미 배선된 타일들도 재배선 없이 최신 내용을 넘겨보게 된다(대신 인덱스 정합은 컨트롤러 책임이다).
    ///
    /// _options는 탭이 일어나는 시점의 <see cref="Open(IReadOnlyList{CardData}, int, CardDetailOpenOptions)"/>에 그대로 실려 간다.</summary>
    public static void BindTile(CardVisualView _tile, IReadOnlyList<CardData> _cards, int _index,
                                CardDetailOpenOptions _options = default)
    {
        if (_tile == null || _cards == null) return;

        LongPressDetector t_press = _tile.GetComponent<LongPressDetector>();
        if (t_press == null) return;

        // 대입(+= 아님) — 타일이 재사용·재바인딩돼도 이전 콜백이 겹쳐 남지 않는다(CardElement와 같은 관용구).
        t_press.OnTap = () => Open(_cards, _index, _options);
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

    /// <summary>지금 떠 있는 모든 캔버스 위로 올라타거나(_on), 로비 캔버스 안의 제자리로 되돌린다.
    ///
    /// 필요한 이유: 이 화면은 로비 캔버스 안에 있어(sortingOrder 0) 그보다 위에 뜨는 별도 캔버스 —
    /// 카드팩 개봉 화면 같은 것 — 위에서 열면 그 뒤에 가려 보이지 않는다. 두 캔버스 모두 Overlay 루트라
    /// 계층상의 앞뒤(sibling)로는 순서가 정해지지 않고 sortingOrder만이 답이다.
    ///
    /// <b>순서 값을 여는 쪽에서 받지 않는다.</b> 받으면 상세를 여는 화면마다 "누구보다 위인가"를 각자 계산하고,
    /// 그중 하나만 옛 값으로 남는다. 지금 화면을 보고 여기서 구하는 편이 계산 지점을 하나로 묶는다.
    ///
    /// GraphicRaycaster를 함께 붙이는 이유: overrideSorting을 켠 중첩 캔버스는 부모의 레이캐스터가 쥔 정렬에서
    /// 떨어져 나온다 — 없으면 눈에는 위에 보이는데 탭은 밑 화면이 먹는다.
    /// 컴포넌트를 없으면 붙여 쓰는 것은 이 파일의 CanvasGroup 확보(EnsureSlideBase)와 같은 관용구다.
    ///
    /// ⚠ 한 번 붙인 두 컴포넌트는 떼지 않는다(다음 열기에 다시 쓴다) → 내릴 때 **값 전부**를 원위치시킨다.
    ///   sortingOrder까지 0으로 되돌리는 이유: Overlay 캔버스의 레이캐스트 우선순위는 overrideSorting이 아니라
    ///   sortingOrder 값 그 자체를 읽는다. 숫자를 남겨두면 렌더는 제자리인데 입력만 위에 남아,
    ///   나중에 상세 위에 뜨는 화면이 생기면 "보이는 건 위 화면인데 탭은 상세가 먹는" 역전이 된다.</summary>
    void LiftAbove(bool _on)
    {
        if (!_on)
        {
            if (this.m_sortingCanvas == null) return;

            this.m_sortingCanvas.overrideSorting = false;
            this.m_sortingCanvas.sortingOrder    = 0;
            return;
        }

        if (this.m_sortingCanvas == null)
        {
            this.m_sortingCanvas = GetComponent<Canvas>();
            if (this.m_sortingCanvas == null) this.m_sortingCanvas = gameObject.AddComponent<Canvas>();
        }

        if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

        // 순서를 먼저 끈다 — 켜진 채로 재면 지난번에 올라탄 자기 값이 후보에 끼어 열 때마다 한 칸씩 올라간다.
        this.m_sortingCanvas.overrideSorting = false;

        this.m_sortingCanvas.sortingOrder    = TopSortingOrder() + 1;
        this.m_sortingCanvas.overrideSorting = true;
    }

    /// <summary>지금 화면에서 가장 위에 그려지는 순서. 꺼져 있는 캔버스는 세지 않는다 —
    /// 안 뜬 화면까지 넘으려 들면 값만 커지고 넘을 이유는 없다.
    ///
    /// 순서를 실제로 정하는 것은 루트 캔버스이거나 overrideSorting을 켠 캔버스뿐이다.
    /// 그 외 중첩 캔버스의 sortingOrder는 그려지는 자리와 무관한 값이라 후보에서 뺀다.
    ///
    /// 튜토리얼 게이트(350)만은 넘을 대상이 아니라 예외다 — 이 창을 **가리키는** 층이라 항상 위에 있어야 한다.
    /// 세면 상세가 그 위로 올라타, 상세를 무대로 쓰는 안내(강화·진화)의 딤·문구가 상세 뒤에 깔려 보이지 않는다.</summary>
    int TopSortingOrder()
    {
        int t_top = 0;

        OutgameTutorialGateUI t_gate = OutgameTutorialGateUI.Instance;

        Canvas[] t_canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        for (int t_i = 0; t_i < t_canvases.Length; t_i++)
        {
            Canvas t_canvas = t_canvases[t_i];
            if (t_canvas == null || t_canvas == this.m_sortingCanvas) continue;
            if (t_canvas != t_canvas.rootCanvas && !t_canvas.overrideSorting) continue;
            if (t_gate != null && t_canvas.transform.IsChildOf(t_gate.transform)) continue;

            t_top = Mathf.Max(t_top, t_canvas.sortingOrder);
        }

        return t_top;
    }

    /// <summary>이 오버레이를 부모(SafeArea) 가득 펴거나 authoring 크기로 되돌린다.
    /// 자식들은 위쪽 앵커라 루트가 위로 펴지면 함께 올라온다 — 상단 바가 없는 화면에서는 그 자리를 회수하는 것이 맞다.
    /// 되돌릴 값은 최초 1회만 잡는다(위 m_baseOffset* 주석).</summary>
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
        if (this.evolveButton  != null) this.m_evolveTone  = this.evolveButton.GetComponent<UIEffect>();

        // 카드 그림 위 탭은 루트의 OnPointerClick으로 오지 않는다 —
        // LongPressDetector가 pointerPress를 가져가 클릭 대상 비교가 어긋난다.
        // 카드는 배경이 아니므로 여기서 닫지 않는다. 연출 중 스킵만 받는다.
        if (this.cardView != null)
        {
            LongPressDetector t_tap = this.cardView.GetComponent<LongPressDetector>();
            if (t_tap != null) t_tap.OnTap = SkipRitual;
        }
    }

    // 화살표·스와이프는 Awake가 아니라 여기서 배선한다 — 오버레이는 열 때마다 꺼졌다 켜지므로
    // Awake 한 번으로는 부족하고, Remove 후 Add라 중복 등록도 남지 않는다.
    void OnEnable()
    {
        // 상세·강화 화면은 하단 탭바만 걷는다 — 상단 재화 바는 강화 비용을 보는 자리라 되돌려 놓는다.
        // (아래에 깔린 페이지 오버레이가 둘 다 걷어둔 상태여도 이 요청이 가장 위라 상단바가 다시 나온다.)
        LobbyShellBars.Hide(this, transform, EShellBars.Bottom);

        if (this.enhanceButton != null)
        {
            this.enhanceButton.onClick.RemoveListener(OnEnhancePressed);
            this.enhanceButton.onClick.AddListener(OnEnhancePressed);
        }

        // 진화 버튼도 같은 핸들러다 — 겉모습만 갈릴 뿐 누르는 결과는 같은 레벨업 1회다.
        if (this.evolveButton != null)
        {
            this.evolveButton.onClick.RemoveListener(OnEnhancePressed);
            this.evolveButton.onClick.AddListener(OnEnhancePressed);
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

        // 잔액이 바뀌면 버튼 활성이 따라와야 한다(다른 화면·디버그 지급으로도 바뀐다).
        CurrencyManager.OnCurrencyChanged += HandleCurrencyChanged;

        RefreshArrows();
    }

    void OnDisable()
    {
        // 요청을 물리면 아래에 깔린 화면(페이지 오버레이)의 범위가 다시 적용된다.
        LobbyShellBars.Show(this);

        if (this.swipeDetector != null) this.swipeDetector.OnSwipe = null;

        if (this.enhanceButton != null) this.enhanceButton.onClick.RemoveListener(OnEnhancePressed);
        if (this.evolveButton  != null) this.evolveButton.onClick.RemoveListener(OnEnhancePressed);

        if (this.artOnlyButton != null) this.artOnlyButton.onClick.RemoveListener(ToggleArtOnly);

        // 열람 모드는 창을 닫으면 풀린다 — 다음에 열었을 때 체력·레벨이 사라진 이유를 설명해 줄 화면이 없다.
        // 카드 쪽 플래그까지 같이 내린다(cardView는 이 오버레이 전용 인스턴스라 남겨두면 다음 열기에 그대로 따라온다).
        this.m_artOnly = false;
        this.cardView?.SetArtOnly(false);
        ApplyArtOnlyChrome();

        // 창이 닫히면 안내 타깃도 놓는다 — 안 보이는 버튼을 가리키는 등록이 남으면 다음 안내가 허공에 뜬다.
        ApplyGrowthAnchor(null);

        CardGrowthManager.OnGrowthChanged -= OnGrowthChanged;
        CurrencyManager.OnCurrencyChanged -= HandleCurrencyChanged;

        // 전환 도중에 닫히면 slideTarget이 옆으로 밀린 채·반투명인 채 굳는다 → 다음 열기에 그대로 보인다.
        // pending 카드는 버린다 — 안 보이는 채로 칩을 재생성할 이유가 없고, 씬 언로드 경로에서 Instantiate/Destroy를 도는 건 위험하다.
        CancelSlide();

        // 연출 중에 닫히면 카드가 확대·회색인 채 굳는다. 잘라내되 콜백은 흘러나오므로 유예도 함께 풀린다.
        // 무대를 먼저 자른다 — 잘리며 흘러나오는 공개 콜백이 결과판을 한 번 더 띄우므로, 결과판 정리가 뒤여야 한다.
        CancelRituals();
        this.resultPanel?.HideImmediate();
        this.m_retryQueued = false;

        // 안 보이는 채로 터뜨릴 판은 없다. 대기만 버리고 판은 다음 열기의 Build가 지금 상태로 맞춘다.
        DropPendingUnlockFx();

        // 퇴장 트윈이 완료 전에 잘렸으면(부모가 먼저 꺼짐) 여기서 마무리해야 다음 열기에 유령 프레임이 안 뜬다.
        this.transition.HandleDisabled(gameObject);

        // 빌린 순서와 크기를 돌려준다. 세 축 모두 여는 쪽이 매번 정하는 값이라 여기서 함께 내린다 —
        // 남겨두면 도감에서 연 다음 창이 상단 바를 덮은 채, 조작도 없는 화면이 된다.
        LiftAbove(false);
        SetFullScreen(false);
        this.m_readOnly = false;

        // 정리가 다 끝난 뒤에 알린다 — 구독자가 이 창의 상태를 다시 물어볼 수 있어야 한다.
        OnAnyClosed?.Invoke();
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
        CancelRituals();                      // 순서는 OnDisable 주석 참고 — 무대가 먼저다.
        this.resultPanel?.HideImmediate();
        this.m_retryQueued = false;
        ShowBottomBar();   // 연출 도중에 닫았다 다시 연 경우 걷힌 상태가 남아 있을 수 있다
        this.transition.SetVisible(gameObject, true);
        Apply(CardAt(this.m_index));
        RefreshArrows();
    }

    void Hide()
    {
        // 퇴장 중 입력부터 죽인다 — 닫히는 도중 화살표·스와이프가 전환을 시작하면 close 시퀀스와 같은 노드를 두고 싸운다.
        // 다시 열 때는 SetVisible(true) → OnEnable → RefreshArrows()가 되살린다.
        if (this.swipeDetector != null) this.swipeDetector.Interactable = false;

        // 연타 예약도 여기서 끊는다. 퇴장은 트윈이라 OnDisable이 곧바로 오지 않는다 —
        // 그 사이 예약이 살아 있으면 사라지는 창 위에서 다음 담금질이 시작되고, 그 연출이 퇴장을 덮어
        // 창이 닫히지 않은 것처럼 보인다(바깥이 결과를 듣고 화면을 넘겨받는 경로에서 실제로 그랬다).
        this.m_retryQueued = false;

        this.transition.SetVisible(gameObject, false);
    }

    /// <summary>닫기는 <b>배경(딤)</b> 탭만이다. 배경 = 이 루트 자신의 전면 Image —
    /// 카드·상세 패널·조작 바 위의 탭은 닫지 않는다(내용을 읽다 손이 스쳐 창이 사라지던 문제).
    ///
    /// 판정을 "무엇을 눌렀나"로 하는 이유: 이 경로의 pointerPress는 언제나 루트라 영역을 알려주지 못한다.
    /// pointerPressRaycast가 곧 루트면 그 위에 아무 UI도 없었다는 뜻이고, 그게 배경이다.</summary>
    public void OnPointerClick(PointerEventData _e)
    {
        if (_e == null || _e.button != PointerEventData.InputButton.Left) return;

        // 스와이프로 소비된 포인터는 탭이 아니다 — 없으면 카드를 넘긴 뒤 손 떼는 순간 닫힌다.
        if (_e.dragging) return;

        // 연출 중의 탭은 어디를 눌렀든 스킵이다 — 연타하는 조작이라 기다리게만 두면 지겹다.
        if (this.m_ritualPlaying) { SkipRitual(); return; }

        if (_e.pointerPressRaycast.gameObject != gameObject) return;

        Hide();
    }

    /// <summary>강화 연출 스킵. 닫기와 갈라 둔다 — 방금 쓴 골드의 결과를 못 보고 화면이 사라지면 안 된다.
    /// "연출 중"의 진실원은 m_ritualPlaying 하나다 — ritual.IsPlaying은 유예를 세운 뒤 Play 전까지,
    /// 그리고 OnKill 콜백 구간에서 어긋난다.</summary>
    void SkipRitual()
    {
        if (this.m_ritualPlaying) this.m_activeRitual?.RequestSkip();
    }

    /// <summary>어느 연출이 서 있었든 무대를 잘라낸다(카드 전환·닫힘·중단 경로).
    /// 쥐고 있던 쪽만 자르면 배선이 바뀌거나 중간에 갈린 경우 다른 쪽이 자세를 남긴 채 굳는다 — 둘 다 자른다.</summary>
    void CancelRituals()
    {
        this.ritual?.CancelImmediate();
        this.evolveRitual?.CancelImmediate();
        this.m_activeRitual = null;
    }

    /// <summary>이 카드의 다음 한 방을 맡을 연출. 진화 관문은 담금질과 다른 얼굴을 쓴다 —
    /// 진화는 실패가 없어 기다릴 것이 없고, 감출 것도 없다(바뀌는 그림 자체가 볼거리다).
    /// 관문 레벨 숫자는 CardGrowthConfig가 소유하고 여기선 그 판정만 읽는다.</summary>
    CardGrowthRitualView RitualFor(CardData _card)
    {
        if (this.evolveRitual != null
         && CardGrowthManager.TryGetNextStep(_card, out GrowthStep t_step)
         && CardGrowthManager.IsEvolutionLevel(t_step.Level)) return this.evolveRitual;

        return this.ritual;
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
    // 칩 섹션은 키워드가 실제로 바뀐 통지에만 다시 지어진다(RefreshKeywordVisuals의 변경 감지).
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

    // 카드 위 정보를 가렸다 되돌린다. 다시 그리는 것은 cardView.Bind 하나 —
    // Apply를 통째로 돌리면 값이 그대로인 키워드·시너지 칩까지 Destroy + Instantiate 된다.
    void ToggleArtOnly()
    {
        if (this.m_ritualPlaying) return;   // 연출이 화면을 덮은 동안 카드를 다시 그리면 담금질 자세가 풀린다

        this.m_artOnly = !this.m_artOnly;
        ApplyArtOnlyChrome();

        // 모드는 Bind보다 먼저 세운다 — 키워드 아이콘은 Bind가 지었다 부수므로 나중이면 이번 판만 옛 모습이 남는다.
        this.cardView?.SetArtOnly(this.m_artOnly);

        CardData t_card = CardAt(this.m_index);
        if (t_card != null && this.cardView != null)
            this.cardView.Bind(t_card, OwnershipManager.IsOwned(t_card));
    }

    void ApplyArtOnlyChrome()
    {
        if (this.artOnlyIcon != null)
            this.artOnlyIcon.color = this.m_artOnly ? this.artOnlyOnColor : this.artOnlyOffColor;
    }

    // 카드가 바뀔 때의 전량 갱신. 조건 없는 칩 재생성은 여기뿐이다
    // (해금으로 키워드가 바뀐 통지만 RefreshKeywordVisuals가 키워드 칩을 다시 짓는다).
    void Apply(CardData _card)
    {
        bool t_owned = OwnershipManager.IsOwned(_card);

        // 다른 카드를 그리는 참이다 — 앞 카드의 해금 대기는 여기서 버린다(안 그러면 이 카드의 판이 이유 없이 터진다).
        DropPendingUnlockFx();

        // 해금 연출이 도는 중에 카드가 갈리면 그 연출이 잘려 끝 콜백이 오지 않는다 —
        // 그때 돌아왔어야 할 하단 바를 여기서 못 박는다(멱등).
        ShowBottomBar();

        // 그림·이름·체력·키워드 아이콘·잠김 오버레이는 도감 타일과 같은 컴포넌트에 그대로 위임한다.
        if (this.cardView != null) this.cardView.Bind(_card, t_owned);
        

        BuildKeywordSection(_card, t_owned);
        BuildSynergySection(_card, t_owned);
        ApplyDescription(_card, t_owned);
        RewindScroll();

        RefreshGrowth(_card, t_owned);
    }

    // 성장에 따라 움직이는 것만 다시 그린다. 강화는 연타하는 조작이라, 통지마다 Apply를 통째로 돌리면
    // 값이 그대로인 키워드·시너지 칩까지 매번 Destroy + Instantiate 된다.
    void RefreshGrowth(CardData _card, bool _owned)
    {
        // 진화 관문을 넘은 공개 프레임에 그림도 함께 바뀐다. 이미지가 없으면 표시 규칙이 이전 단계/기본으로 폴백한다.
        if (this.cardView != null) this.cardView.RefreshArt(_card);

        // 키워드·시너지 관문을 넘긴 프레임엔 아이콘 줄·프레임 장식·칩 줄의 잠김 룩도 같이 풀린다.
        // 진짜 바뀐 때만 다시 짓는다(위 주석 — 아이콘·칩은 Destroy + Instantiate라 통지마다 지으면 매번 새로 짓는다).
        RefreshUnlockVisuals(_card, _owned);

        // 카드 그림의 HP도 강화를 따라와야 한다. Bind가 아니라 RefreshHp인 이유는 그쪽 주석 참고
        // (Bind는 키워드 아이콘·시너지 배지까지 전부 다시 짓는다).
        if (this.cardView != null) this.cardView.RefreshHp(_card, _owned);

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

    // 해금으로 바뀌는 표시(카드 위 아이콘 줄·프레임 장식, 키워드 칩 줄, 시너지 칩 줄)를 지금 상태에 맞춘다.
    // 기준값이 그대로면 아무것도 하지 않는다 — 강화는 연타하는 조작이라 매 통지마다 지으면 그때마다 다시 짓는다.
    // 각 줄의 기준값 갱신은 Build*Section이 직접 한다(짓는 곳과 기록하는 곳을 갈라두면 조용히 어긋난다).
    void RefreshUnlockVisuals(CardData _card, bool _owned)
    {
        CardKeyword t_trait = _owned ? CardVisualRules.TraitKeywords(_card) : CardKeyword.None;
        CardKeyword t_info  = _owned ? CardVisualRules.InfoKeywordsWithLocked(_card) : CardKeyword.None;
        bool        t_syn   = _owned && SynergyUnlocked(_card);

        bool t_sameCard = _card == this.m_keywordCard;

        // 같은 카드가 서 있는 채로 잠김이 풀렸다 = 방금 해금됐다. 카드를 넘겨 온 경우는 해금이 아니므로 제외한다.
        // 판을 걷는 일은 여기서 하지 않는다 — 무대가 돌아온 뒤 PlayPendingUnlockFx가 연출로 걷는다.
        if (t_sameCard)
        {
            if (t_syn && !this.m_shownSynergyOpen) this.m_pendingSynergyUnlockFx = true;

            // 섹션째 잠겨 있다가 풀렸다 = 지금 열려 있는 것 전부가 방금 열린 것이다
            // (KeywordSectionLocked가 "열린 것이 하나도 없는가"라서 성립한다).
            if (this.m_shownKeywordLocked && !KeywordSectionLocked(_card, _owned))
                this.m_pendingUnlockedKeywords = CardVisualRules.InfoKeywords(_card);
        }

        // 시너지 관문(1차 진화)은 키워드 마스크를 안 건드리고 넘어갈 수 있다 — 따로 보지 않으면
        // Lv5를 찍어도 잠긴 시너지 칩이 그대로 남는다.
        if (t_sameCard && t_syn != this.m_shownSynergyOpen) BuildSynergySection(_card, _owned);

        if (t_sameCard && t_trait == this.m_shownTrait && t_info == this.m_shownInfo) return;

        if (this.cardView != null) this.cardView.RefreshKeywords(_card, _owned);
        BuildKeywordSection(_card, _owned);
    }

    // 미배선이면 조용히 건너뛴다(다른 옵션 배선과 같은 규약 — 판 없는 프리팹에서도 칩별 자물쇠는 그대로 뜬다).
    //
    // _pendingFx면 **판을 걷지 않는다** — 방금 해금된 줄이라 걷는 일은 연출(SectionUnlockFx)이 맡는다.
    // 여기서 먼저 꺼버리면 강화 연출에 가려 있는 동안 판이 사라져, 화면이 돌아왔을 때 보여줄 것이 없다.
    static void SetSectionLock(GameObject _lock, bool _locked, bool _pendingFx = false)
    {
        if (_lock == null) return;
        if (!_locked && _pendingFx) return;

        _lock.SetActive(_locked);
    }

    /// <summary>방금 해금된 줄의 잠김 판을 연출로 걷고, 그 아래 내용을 읽게 만든다.
    /// 연출이 미배선이면 예전처럼 즉시 걷는다 — 배선 실패가 "판이 안 걷혀 내용이 영영 가려짐"이 되면 안 된다.
    ///
    /// 세 박이다: <b>판이 걷힌다 → 그 줄로 스크롤이 따라가며 내용이 들어온다 → (처음 보는 개념이면) 전면 안내</b>.
    /// 안내를 판보다 먼저 세우지 않는 이유는 순서가 곧 인과이기 때문이다 —
    /// 무엇이 열렸는지를 카드 화면에서 먼저 보여준 뒤에야 "그게 뭔지"를 말하는 것이 읽힌다.
    ///
    /// 그동안 하단 바는 걷은 채로 둔다 — 지금 화면이 말하는 것은 "무엇이 열렸는가"이고,
    /// 그 위에 다음 강화 버튼이 서 있으면 손이 먼저 간다. 바는 이 흐름의 **마지막 축**이 돌아온 자리에서 되돌린다.</summary>
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

        HideBottomBar();

        // 걷힌 판이 하나뿐이어도 스크롤은 그 줄로 간다. 두 줄이 함께 열렸으면 위쪽(키워드)을 기준으로 삼는다 —
        // 둘을 차례로 훑으면 화면이 두 번 미끄러져 어느 쪽을 읽어야 하는지가 흐려진다.
        GameObject t_focus = t_keywords != CardKeyword.None ? this.keywordSection : this.synergySection;

        // 판이 걷힌 뒤가 이 축의 자리다. 도중에 잘리는 경로(카드 전환·창 닫힘)에는 이 콜백이 오지 않는다 →
        // 그쪽은 Apply가 못 박고, 바는 그 길의 ShowBottomBar가 되돌린다.
        if (t_fx == null) RevealUnlockedSections(t_focus, t_keywords, t_synergy);
        else              t_fx.OnComplete(() => RevealUnlockedSections(t_focus, t_keywords, t_synergy));
    }

    // 걷힌 줄로 스크롤을 옮기고 내용을 들여보낸 뒤, 처음 보는 개념이 있으면 전면 안내로 넘긴다.
    // 하단 바를 되돌리는 곳은 이 함수의 끝 **한 곳**이다(안내가 서면 그 닫힘이 곧 끝이다).
    void RevealUnlockedSections(GameObject _focus, CardKeyword _keywords, bool _synergy)
    {
        ScrollTo(_focus);

        if (_keywords != CardKeyword.None) this.keywordSectionReveal?.Play();
        if (_synergy)                      this.synergySectionReveal?.Play();

        List<UnlockIntro> t_intros = CollectUnseenIntros(CardAt(this.m_index), _keywords, _synergy);
        if (t_intros == null || t_intros.Count == 0) { ShowBottomBar(); return; }

        if (!UnlockIntroOverlay.TryGet(out UnlockIntroOverlay t_overlay)) { ShowBottomBar(); return; }

        // 낙인은 닫힐 때가 아니라 **띄우는 순간** 찍는다. 안 그러면 안내를 읽는 도중 앱이 죽었을 때
        // 다음 부팅에 같은 안내가 다시 선다 — 이미 상세창에 남아 있는 내용이라 다시 세울 값어치가 없다.
        for (int t_i = 0; t_i < t_intros.Count; t_i++)
            OutgameTutorialProgress.MarkUnlockIntroSeen(t_intros[t_i].Key);

        t_overlay.Show(t_intros, ShowBottomBar);
    }

    /// <summary>이번에 열린 것 중 <b>아직 전면으로 안내한 적 없는</b> 개념들. 없으면 null.
    /// 순서는 화면 순서와 같다(키워드 줄이 위, 시너지 줄이 아래).</summary>
    List<UnlockIntro> CollectUnseenIntros(CardData _card, CardKeyword _keywords, bool _synergy)
    {
        List<UnlockIntro> t_list = null;

        if (_keywords != CardKeyword.None && this.keywordIconConfig != null)
            foreach (CardKeyword t_kw in (CardKeyword[])Enum.GetValues(typeof(CardKeyword)))
            {
                if (t_kw == CardKeyword.None || (_keywords & t_kw) == 0) continue;
                if (!UnlockIntro.TryForKeyword(this.keywordIconConfig, t_kw, out UnlockIntro t_intro)) continue;
                if (OutgameTutorialProgress.IsUnlockIntroSeen(t_intro.Key)) continue;

                (t_list ??= new List<UnlockIntro>()).Add(t_intro);
            }

        // 시너지는 개념 하나라 어느 시너지로 배우든 키가 같다 → 카드가 여럿 물고 있어도 첫 장 하나면 된다.
        if (_synergy && _card != null && _card.synergies != null)
            foreach (SynergyData t_syn in _card.synergies)
            {
                if (!UnlockIntro.TryForSynergy(t_syn, out UnlockIntro t_intro)) continue;
                if (OutgameTutorialProgress.IsUnlockIntroSeen(t_intro.Key)) break;

                (t_list ??= new List<UnlockIntro>()).Add(t_intro);
                break;
            }

        return t_list;
    }

    // 그 섹션이 화면에 들어오도록 스크롤을 옮긴다. 내용이 짧아 스크롤이 필요 없으면 아무 일도 하지 않는다.
    // RewindScroll과 같은 이유로 verticalNormalizedPosition을 쓰지 않는다 — 짧은 내용에서 한 번 어긋난 자리를
    // 잡았다가 탄성으로 되돌아와 패널이 튄다. 여기서도 content 좌표를 직접 민다.
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

    /// <summary>대기 중인 해금 연출을 버리고 판을 지금 상태에 맞춘다.
    /// 카드를 넘기거나 창을 닫으면 "방금 해금됐다"는 맥락이 사라진다 — 남겨두면 다음 카드의 판이 이유 없이 터진다.</summary>
    void DropPendingUnlockFx()
    {
        this.m_pendingUnlockedKeywords = CardKeyword.None;
        this.m_pendingSynergyUnlockFx  = false;
    }

    /// <summary>키워드 줄이 통째로 잠겼는가. 판정이 두 곳에 갈리지 않게 여기 하나로 둔다
    /// (짓는 쪽 <see cref="BuildKeywordSection"/>과 감지하는 쪽 <see cref="RefreshUnlockVisuals"/>가 같은 답을 봐야 한다).</summary>
    static bool KeywordSectionLocked(CardData _card, bool _owned)
        => _owned && CardVisualRules.LockedKeywords(_card) != CardKeyword.None
                  && CardVisualRules.InfoKeywords(_card) == CardKeyword.None;

    /// <summary>이 카드의 시너지가 열려 있는가. 관문(1차 진화 레벨)은 CardGrowthConfig가 소유하고
    /// 여기선 그 결과만 읽는다 — 레벨 숫자를 이 화면이 직접 적으면 관문이 두 곳이 된다.</summary>
    static bool SynergyUnlocked(CardData _card) => CardGrowthManager.GrowthOf(_card).SynergyUnlocked;

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

        // 다음 한 방이 진화 관문이면 진화 버튼이 대신 선다. 만렙(다음 단계 없음)이면 강화 버튼이 남는다 —
        // 어느 쪽이든 바가 비지 않는다.
        bool t_evolve = this.evolveButton != null && t_hasStep && CardGrowthManager.IsEvolutionLevel(t_step.Level);

        // 미소유 카드에는 조작을 숨긴다(버튼만 — 바는 켜둔 채로 높이를 지킨다).
        // 열람 전용도 같은 길로 내린다: 바 자체는 이미 걷혀 있지만, 그 안에서 살아 있는 버튼을 남겨 두면
        // 알파만 0인 채로 탭을 먹는다(blocksRaycasts는 창을 여는 순서에 따라 늦게 내려갈 수 있다).
        bool t_actions = _owned && !this.m_readOnly;
        if (this.enhanceButton != null) this.enhanceButton.gameObject.SetActive(t_actions && !t_evolve);
        if (this.evolveButton  != null) this.evolveButton.gameObject.SetActive(t_actions &&  t_evolve);

        // 안내 타깃은 지금 서 있는 성장 버튼을 따라간다 — 창이 열릴 때마다 새로 서고 두 버튼이 자리를 번갈아 쓰므로
        // 프리팹 표식(TutorialAnchor)으로는 잡을 수 없다.
        ApplyGrowthAnchor(!t_actions ? null : (t_evolve ? this.evolveButton : this.enhanceButton));

        // 연출 중에는 공개 시점의 갱신이 버튼을 되살리지 않게 눌러둔다(복귀에서 다시 판정된다).
        bool t_canPayEnhance = t_hasStep && CurrencyManager.CanAfford(t_step.Currency, t_step.Cost);
        SetActionsEnabled(t_canPayEnhance && !this.m_ritualPlaying);

        ApplyCost(t_hasStep, t_step);

        // 결과판이 걷힌 뒤(또는 평상시)엔 다시 각자의 글자다. 값 갱신이 지나는 이 길이 곧 글자의 복귀 지점이다.
        SetActionLabel(false);
        if (this.successRateText != null)
            this.successRateText.text = t_hasStep ? $"{Mathf.RoundToInt(t_step.SuccessRate * 100f)}%" : NoValue;
    }

    /// <summary>이번 강화(_from → _to)로 **새로 열린 것**을 한 문장으로. 아무것도 안 열렸으면 null.
    ///
    /// 판정은 두 레벨의 성장 스냅샷을 비교하는 것뿐이다 — 관문 레벨(키워드·진화·시너지)을 이 화면이 직접 적으면
    /// 곡선을 바꿀 때 여기만 옛 숫자로 남는다. 레벨이 안 올랐으면(실패) 비교할 것도 없다.
    ///
    /// 키워드 이름은 아이콘 표에서 가져온다 — 화면마다 다른 이름으로 부르지 않게(표시명의 주인은 KeywordIconConfig).</summary>
    string UnlockLabel(CardData _card, int _from, int _to)
    {
        if (_card == null || _to <= _from) return null;

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

    /// <summary>이번 강화(_from → _to)로 <b>새로 열린 키워드</b>. 없으면 None.
    /// 키워드는 해금 레벨 하나로 통째로 열린다 → 새로 켜진 비트가 곧 이번에 열린 것들이다.
    ///
    /// 결과판의 문장(<see cref="UnlockLabel"/>)과 그 판의 자동 복귀 판정이 같은 답을 봐야 한다 —
    /// 갈라 두면 "키워드 개방"이라 적혀 있는데 판은 탭을 기다리는 어긋남이 생긴다.</summary>
    static CardKeyword NewKeywords(CardData _card, int _from, int _to)
    {
        if (_card == null || _to <= _from) return CardKeyword.None;

        return CardGrowthManager.GrowthAtLevel(_card, _to).UnlockedKeywords
             & ~CardGrowthManager.GrowthAtLevel(_card, _from).UnlockedKeywords;
    }

    /// <summary>이번 강화(_from → _to)로 <b>시너지가 새로 열렸는가</b>.
    /// 판정을 <see cref="NewKeywords"/>와 같은 자리에 두는 이유도 같다 — 결과판의 문장과 자동 복귀가 한 답을 본다.</summary>
    static bool UnlockedSynergy(CardData _card, int _from, int _to)
    {
        if (_card == null || _to <= _from) return false;

        return !CardGrowthManager.GrowthAtLevel(_card, _from).SynergyUnlocked
            &&  CardGrowthManager.GrowthAtLevel(_card, _to).SynergyUnlocked;
    }

    /// <summary>강화 비용 표기. 하단 바와 결과판의 "한 번 더"가 같은 값을 같은 모양으로 띄워야 한다 —
    /// 한쪽만 천 단위 구분이 빠지면 같은 비용이 다른 값처럼 읽힌다(문장 규약은 <see cref="GrowthNotice"/>와 같은 결).
    /// 더 올릴 단계가 없으면 숫자 대신 빈값 표기, 값을 묻지 않는 한 방(안내가 대주는 무료 강화)이면 숫자 대신 문구.</summary>
    static string CostLabel(bool _hasStep, long _cost) => !_hasStep  ? NoValue
                                                       : _cost <= 0 ? FreeCost
                                                                    : _cost.ToString("N0");

    /// <summary>비용 재화 아이콘. 한쪽만 배선하면 되돌아올 스프라이트가 없어 아이콘이 눌러붙는다 —
    /// 둘 다 있을 때만 바꾼다(카드팩 진열대의 <c>ResolveCurrencyIcon</c>과 같은 규약).
    /// 더 올릴 단계가 없으면 숫자가 "-"로 비므로 아이콘도 함께 걷는다(숫자 없이 그림만 남는 칸 방지).</summary>
    Sprite CostIconOf(ECurrencyType _currency)
    {
        if (this.goldIcon == null || this.diamondIcon == null) return null;

        // 에너지 아이콘은 아직 없을 수 있다(재화 그림이 미제작) → 없으면 골드 그림으로 떨어진다.
        if (_currency == ECurrencyType.Energy) return this.energyIcon != null ? this.energyIcon : this.goldIcon;

        return _currency == ECurrencyType.Diamond ? this.diamondIcon : this.goldIcon;
    }

    /// <summary>비용 숫자·아이콘을 두 버튼 **모두**에 채운다. 보이는 것은 서 있는 쪽뿐이지만,
    /// 물러나 있는 쪽에 옛 값을 남겨두면 교체되는 프레임에 그 값이 그대로 비친다.</summary>
    void ApplyCost(bool _hasStep, GrowthStep _step)
    {
        string t_cost = CostLabel(_hasStep, _step.Cost);

        if (this.enhanceCostText != null) this.enhanceCostText.text = t_cost;
        if (this.evolveCostText  != null) this.evolveCostText.text  = t_cost;

        // 무료 한 방에는 재화 그림도 걷는다 — 값을 치르는 물건이 아니라고 말하는 자리이기 때문이다
        // (상점이 튜토리얼 가격 문구에서 아이콘을 숨기는 것과 같은 규약).
        bool t_charged = _hasStep && _step.Cost > 0;

        ApplyCostIcon(this.enhanceCostIcon, t_charged, _step.Currency);
        ApplyCostIcon(this.evolveCostIcon,  t_charged, _step.Currency);
    }

    void ApplyCostIcon(Image _target, bool _charged, ECurrencyType _currency)
    {
        if (_target == null) return;

        _target.enabled = _charged;

        Sprite t_icon = CostIconOf(_currency);
        if (t_icon != null) _target.sprite = t_icon;
    }

    /// <summary>두 버튼의 조작 가능 여부. 서 있는 쪽이 어느 것이든 판정은 하나라(같은 레벨업 1회) 함께 건다.</summary>
    void SetActionsEnabled(bool _interactable)
    {
        SetActionEnabled(this.enhanceButton, this.m_enhanceTone, _interactable);
        SetActionEnabled(this.evolveButton,  this.m_evolveTone,  _interactable);
    }

    // 못 누르는 동안 버튼이 **통째로** 흑백이 된다. 밑판만 무채색으로 바꾸면 자식(라벨·불 아이콘·동전·숫자)이
    // 원색 그대로 남아 오히려 어수선해진다 — 색이 빠지는 일은 버튼 전체에 한 번에 걸려야 한다.
    // 알파를 낮추지 않는 이유: 하단 바 밑판이 어두워(Popup_FullWidth_Dark) 반투명은 곧 사라짐이 된다.
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

        switch (_currency)
        {
            case ECurrencyType.Diamond: return NotAffordableDiaNotice;
            case ECurrencyType.Energy:  return NotAffordableEnergyNotice;
            default:                    return NotAffordableNotice;
        }
    }

    void OnEnhancePressed()
    {
        // 결과를 읽는 중이면 이 버튼이 곧 "한 번 더"다 — 결과판이 자기 버튼을 따로 띄우지 않고
        // 손이 이미 가 있는 하단 바 버튼을 그대로 쓴다(연타가 이 시스템의 본체다).
        if (this.resultPanel != null && this.resultPanel.IsOpen)
        {
            this.resultPanel.RequestRetry();
            return;
        }

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

        // 연출도 시도 전에 고른다 — 다음 단계가 진화인지는 레벨이 오르고 나면 다른 답이 된다.
        CardGrowthRitualView t_ritual = RitualFor(t_card);
        bool                 t_evolve = t_ritual == this.evolveRitual && this.evolveRitual != null;

        // 유예를 먼저 세운다 — TryEnhance가 그 안에서 OnGrowthChanged를 동기로 발화한다.
        this.m_ritualPlaying = true;

        EnhanceResult t_result = CardGrowthManager.TryEnhance(t_card);

        // 저작 실수(부트 누락)는 조용히 넘기지 않는다 — 재화는 소모되지 않았고 원인이 화면 밖에 있다.
        if (t_result.Outcome == EEnhanceOutcome.NotReady)
            Debug.LogError("[CardDetailOverlayView] 성장 데이터 미초기화 — CardGrowthManager.Init()이 부트에서 호출되지 않았다.");

        bool t_played = t_result.Outcome == EEnhanceOutcome.Success || t_result.Outcome == EEnhanceOutcome.Failed;

        // 결제 전에 막힌 경우(잔액 부족·최고 레벨·미초기화)엔 보여줄 결과가 없다. 미배선도 같은 길로 — 배선 실패가 소프트락이 되면 안 된다.
        if (!t_played || t_ritual == null)
        {
            AbortEnhance(t_card);

            // 강화가 실제로 일어났는데 보여줄 연출만 없는 길이면 여기가 곧 "다 끝난" 시점이다.
            // 결제 전에 막힌 경우(잔액 부족·만렙)는 아무 일도 없었으므로 알리지 않는다.
            if (t_played) NotifyEnhanceSettled(t_result);
            return;
        }

        this.m_activeRitual = t_ritual;

        // 이번 진화로 새로 열리는 프레임 문양을 연출에 넘긴다. 이 자리가 유일한 시점이다 —
        // 레벨은 이미 올랐고(TryEnhance) 화면은 아직 옛 상태라, "곧 켜질 것"이 정확히 나온다.
        // 넘길 것이 없어도 부른다(앞 판의 문양이 남으면 이번 판에 이유 없이 다시 새겨진다).
        if (t_evolve && this.cardView != null)
        {
            this.cardView.CollectPendingKeywordFrames(t_card, OwnershipManager.IsOwned(t_card), this.m_emblemBuffer);
            this.evolveRitual.SetEmblems(this.m_emblemBuffer);
        }

        // 누른 순간엔 조작만 잠근다. 여기서 값을 다시 그리면 안 된다 — TryEnhance는 이미 끝난 거래라
        // RefreshGrowth가 곧바로 새 Lv·HP를 찍고, 그것이 상세 패널이 걷히는 0.15초 동안 그대로 비친다.
        LockControls();

        // 무대를 쥐기 직전에 알린다 — 바깥의 안내가 결과판 위에 남지 않게(OnAnyEnhanceStarted 주석 참고).
        OnAnyEnhanceStarted?.Invoke();

        t_ritual.Play(
            t_result.Outcome, _awaitReturn: this.resultPanel != null,
            _onReveal: () =>
            {
                // 카드가 이미 바뀐 뒤 잘려 들어온 콜백이면 옛 값을 찍지 않는다(Show/Step의 CancelImmediate 경로).
                if (CardAt(this.m_index) != t_card) return;

                // 카드가 빛에 완전히 덮인 프레임이자 백열이 물러나기 시작하는 프레임이다(BuildReveal = BuildBurst).
                // 값은 전부 여기서 찍는다 — 물러나는 빛이 곧 새 Lv·HP를 드러낸다. 걷혀 있는 상세 패널도 같이 갈리므로
                // 결과판을 닫고 돌아왔을 때 숫자가 튀지 않는다.
                RefreshGrowth(t_card, OwnershipManager.IsOwned(t_card));

                // 그냥 바뀌어 있기만 하면 프레임 장식에 묻힌다 → 드러나는 그 한 박에 글자가 물들고 부푼다.
                // 실패엔 강조할 것이 없다(값이 그대로다).
                if (t_result.Outcome == EEnhanceOutcome.Success && this.cardView != null)
                    this.cardView.FlashGrowth();
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
                    t_ritual.PlayReturn();
                    return;
                }

                ShowResultPanel(t_card, t_result, t_fromLevel, t_fromHp, t_evolve);
            },
            _onFinished: () =>
            {
                this.m_ritualPlaying = false;

                // 지금 보이는 카드로 다시 그린다 — 중간에 카드가 바뀌었어도 화면과 값이 어긋나지 않게.
                CardData t_now = CardAt(this.m_index);
                if (t_now != null) RefreshGrowth(t_now, OwnershipManager.IsOwned(t_now));
                RefreshArrows();

                // 무대가 돌아와 줄이 다시 보이는 지금이 해금 연출의 자리다(연출 중엔 가려 있어 보여줄 수 없다).
                // 하단 바의 복귀도 여기가 쥔다 — 걷을 판이 서면 그 연출이 끝난 뒤에, 아니면 곧바로(멱등).
                // 결과판을 못 띄운 경로(카드 전환 등)에선 복귀 신호도 못 받았으므로 이 자리가 유일한 못이다.
                PlayPendingUnlockFx();

                // "한 번 더"는 여기서 이어간다 — 그 경로의 무대는 걷힌 채라(EndAwaitForChain) 다음 연출이 곧장 물려받는다.
                // 재입력 가드(m_ritualPlaying)가 풀렸다 다시 서기까지 한 프레임도 벌어지지 않으므로 그 사이에 손이 낄 자리가 없고,
                // 잔액 부족·만렙은 TryEnhance가 알아서 되돌린다(AbortEnhance가 걷힌 무대를 되돌린다).
                // 결과를 다 읽고 무대가 돌아온 지금이 "강화가 끝났다"이다 — 바깥은 이 시점에야 화면을 넘겨받아도 된다.
                // "한 번 더"로 이어가는 중이어도 알린다: 체인의 끝을 기다리면 그 끝이 실패·만렙으로 맺힐 때
                // 성공 신호가 통째로 사라져, 기다리던 쪽(튜토리얼)이 영영 깨어나지 못한다.
                NotifyEnhanceSettled(t_result);

                // 결과판이 걷히고 하단 바에 진화 버튼이 선 지금이 첫 진화 안내의 자리다.
                // "한 번 더"로 이어가는 중이면 아직 무대가 돌지 않았다 — 그 체인이 끝난 뒤 같은 자리에서 다시 묻는다.
                if (!this.m_retryQueued) TryFireFirstEvolutionTutorial(t_now);

                // 구독자가 이 결과를 듣고 창을 닫았다면 예약은 Hide가 이미 지웠다 — 체인은 여기서 끝난다.
                if (!this.m_retryQueued) return;

                this.m_retryQueued = false;
                OnEnhancePressed();
            });
    }

    /// <summary>안내 타깃을 지금 서 있는 성장 버튼으로 옮긴다(_button이 null이면 내린다). 창이 닫히면 반드시 내린다 —
    /// 죽은 버튼을 가리키는 등록이 남으면 다음 안내가 안 보이는 자리에 손가락을 띄운다.
    ///
    /// 강화·진화를 같은 키로 다루는 이유: 안내가 시키는 일은 "카드를 한 단계 키워라" 하나이고,
    /// 그 한 방이 진화 관문이면 버튼의 얼굴만 갈릴 뿐 누르는 결과는 같은 레벨업이다.
    /// 키를 갈라 두면 관문 레벨의 카드에서 앵커가 영영 등록되지 않아 안내가 말없이 멈춘다.</summary>
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

    /// <summary>다음 한 방이 첫 진화면 무료 진화 안내를 깨운다. 관문 레벨을 여기서 적지 않는 이유는
    /// 그것을 곡선(CardGrowthConfig)이 소유하기 때문이다 — 1회성은 트리거 완주 낙인이 보장한다.</summary>
    void TryFireFirstEvolutionTutorial(CardData _card)
    {
        if (_card == null) return;
        if (CardGrowthManager.GrowthOf(_card).EvolutionStage != 0) return;
        if (!CardGrowthManager.TryGetNextStep(_card, out GrowthStep t_next)) return;
        if (!CardGrowthManager.IsEvolutionLevel(t_next.Level)) return;

        TriggeredTutorialRunner.Fire(EOutgameTutorialTrigger.FirstEvolutionReady);

        // 발화로 이 한 칸이 무료가 됐다 — 비용 표시·활성 판정을 다시 읽힌다.
        // 위(_onFinished)의 RefreshGrowth는 발화보다 앞이라 아직 다이아 값을 그려 뒀다.
        if (TriggeredTutorialRunner.IsRunningTrigger(EOutgameTutorialTrigger.FirstEvolutionReady))
            RefreshGrowth(_card, OwnershipManager.IsOwned(_card));
    }

    // 성공한 강화가 다 끝났음을 바깥에 알린다. 실패·미결제는 알리지 않는다 —
    // 기다리는 쪽(튜토리얼)은 "레벨이 올랐다"만 신호로 쓰고, 실패는 같은 자리에서 다시 누르는 일이다.
    static void NotifyEnhanceSettled(EnhanceResult _result)
    {
        OnAnyEnhanceSettled?.Invoke(_result);
    }

    // 보여줄 것 없이 끝난 강화(잔액 부족·최고 레벨·미초기화·연출 미배선). 잠금을 풀고 조작을 되살린다.
    //
    // 무대까지 되돌리는 이유는 "한 번 더"로 이어온 길 때문이다 — 그 경로에선 패널이 걷힌 채로 넘어오므로
    // 여기서 되돌리지 않으면 상세 패널이 사라진 채 굳는다(첫 시도에서 막힌 경우엔 되돌릴 것이 없어 무해하다).
    void AbortEnhance(CardData _card)
    {
        this.m_ritualPlaying = false;

        CancelRituals();
        ShowBottomBar();   // 어느 경로로 잘렸든 조작 바는 돌아와야 한다(숨은 채 굳으면 화면이 죽는다)

        // 잔액부족은 통지가 없다 → 여기서 한 번(멱등)
        if (_card != null) RefreshGrowth(_card, OwnershipManager.IsOwned(_card));
        RefreshArrows();
    }

    // 강화를 누른 직후의 잠금. 값은 손대지 않는다 — 여기서 RefreshGrowth를 부르면 공개할 것이 사라진다.
    void LockControls()
    {
        SetActionsEnabled(false);

        HideBottomBar();   // 담금질 구간에는 카드만 남는다
        RefreshArrows();   // 연출 중에 카드가 넘어가면 무대에 선 카드와 결과가 어긋난다.
    }

    // 결과를 읽는 중엔 강화 버튼만 "한 번 더"가 된다. 진화 버튼은 결과판 위에서도 "진화"다 —
    // 그 한 방은 방금 한 일의 반복이 아니라 다른 종류의 일이고, 무는 재화도 다르다.
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

        // 열람 전용에는 되돌릴 바가 없다. 바를 되부르는 경로가 여럿이라(창 열기·연출 복귀·결과판 종료)
        // 각 호출처에 조건을 흩지 않고 여기 한 곳에서 막는다 — 걷은 상태가 곧 이 모드의 평상시다.
        if (this.m_readOnly) { HideBottomBar(); return; }

        this.bottomBarGroup.DOKill();
        this.bottomBarGroup.blocksRaycasts = true;
        this.bottomBarGroup.DOFade(1f, Mathf.Max(0.01f, this.bottomBarFadeDuration))
            .SetLink(this.bottomBarGroup.gameObject);
    }

    // 결과판을 띄운다. 판정은 이미 끝났고 여기서는 "무엇이 얼마나 바뀌었나"만 모아 넘긴다.
    // _evolve면 같은 판을 진화의 이름으로 쓴다 — 방금 본 연출과 제목이 갈리면 무엇을 한 것인지 흐려진다.
    void ShowResultPanel(CardData _card, EnhanceResult _result, int _fromLevel, int _fromHp, bool _evolve)
    {
        if (this.resultPanel == null) return;   // 미배선이면 연출이 스스로 걷는다(Play의 _awaitReturn 참고).

        // 읽기를 결과판이 넘겨받는 자리다 — 카드 위의 강조는 여기서 원상복귀한다(드러나는 순간의 것이지 상태가 아니다).
        if (this.cardView != null) this.cardView.RestoreGrowthFlash();

        // "한 번 더"의 가부는 오른 뒤의 다음 단계로 판정한다 — 방금 쓴 비용이 아니라 지금 낼 비용이 기준이다.
        bool t_hasNext = CardGrowthManager.TryGetNextStep(_card, out GrowthStep t_next);

        // 다음 한 방이 진화 관문이면 여기서 잇지 않는다. 진화는 방금 한 일의 반복이 아니라 다른 재화를 무는
        // 다른 종류의 일이라, 골드를 연타하던 손에 그대로 걸리면 안 된다 — 상세로 돌아가 스스로 고르는 자리다.
        bool t_nextIsEvolve = t_hasNext && CardGrowthManager.IsEvolutionLevel(t_next.Level);

        // 이번 한 방으로 키워드·시너지가 열렸으면 같은 이유로 잇지 않는다 — 방금 연 칩 줄은 상세에 있고,
        // 그 잠김 판은 무대가 돌아온 뒤에야 연출로 걷힌다(PlayPendingUnlockFx).
        // 결과판에서 연타로 넘어가면 자기가 무엇을 열었는지 못 본 채 지나간다.
        bool t_unlocked = NewKeywords(_card, _fromLevel, _result.Level) != CardKeyword.None
                       || UnlockedSynergy(_card, _fromLevel, _result.Level);

        // 탭을 기다리지 않고 스스로 걷혀 상세로 돌아가는 판(= 이을 것이 없는 자리).
        bool t_selfReturn = t_nextIsEvolve || t_unlocked;

        // 안내가 시킨 한 방은 이 화면이 종착지다 — 여기에 "한 번 더"를 되살리면 안내가 얹은 말 옆에
        // 되돌아가는 문이 하나 더 서고, 유저는 그걸 눌러 안내 밖으로 샌다.
        bool t_guided = OutgameTutorialGuide.IsCurrentAction(EOutgameTutorialAction.WaitEnhance);

        // 하단 바를 되살리지 않는 자리. 스스로 걷히는 것은 t_selfReturn뿐이다 —
        // 안내가 얹은 말은 유저가 읽고 탭할 때까지 판이 서 있어야 한다.
        bool t_barStaysDown = t_selfReturn || t_guided;
        bool t_canRetry     = t_hasNext && !t_barStaysDown && CurrencyManager.CanAfford(t_next.Currency, t_next.Cost);

        var t_line = new EnhanceResultLine(_result.Outcome,
                                           _fromHp, DeckPower.MaxHpOf(_card),
                                           _fromLevel, _result.Level,
                                           // 못 잇는 이유가 잔액이 아니라 규칙이면 안내도 없다 —
                                           // GrowthNotice를 그대로 흘리면 "다이아가 부족"이라는 거짓 문장이 뜬다.
                                           t_canRetry,
                                           t_barStaysDown ? string.Empty
                                                          : GrowthNotice(t_hasNext, t_canRetry, t_next.Currency),
                                           // 비용도 "지금 낼 값" 기준 — 판정(t_canRetry)과 같은 단계를 봐야 숫자와 가부가 어긋나지 않는다.
                                           CostLabel(t_hasNext, t_next.Cost),
                                           // Lv4를 막 올린 참이면 다음 한 방은 다이아다 — 그림까지 같이 넘겨야 값이 거짓말을 안 한다.
                                           CostIconOf(t_next.Currency),
                                           UnlockLabel(_card, _fromLevel, _result.Level),
                                           _evolve ? this.evolveResultTitle : null);

        // 결과를 읽는 동안 하단 바 버튼이 "한 번 더"를 맡는다 — 연출 시작 때 LockControls가 꺼둔 것을 여기서 되살린다.
        // 값도 지금 낼 비용으로 갈아둔다(방금 쓴 비용이 남아 있으면 다음 한 방의 가격을 잘못 읽는다).
        // 어느 버튼이 설지는 이미 공개 시점의 RefreshGrowth가 다음 단계 기준으로 정해뒀다 —
        // 여기선 그 판정을 다시 하지 않고 둘 다 손봐 서 있는 쪽이 알아서 맞게 둔다.
        //
        // 바가 걷힌 채로 남는 판에서는 아무것도 되살리지 않는다(아래 _onRowsDone) —
        // 여기서 값·글자를 갈아두면 보이지도 않을 것을 준비하는 셈이고, 스스로 걷히는 판이면 복귀 도중 한 프레임 비친다.
        SetActionsEnabled(t_canRetry);
        if (!t_barStaysDown)
        {
            ApplyCost(t_hasNext, t_next);
            SetActionLabel(true);
        }

        this.resultPanel.Show(t_line,
                              _onClose: () => this.m_activeRitual.PlayReturn(),
                              // 무대는 돌려보내지 않는다 — 상세 패널이 0.35초 돌아왔다 곧바로 다시 걷히면
                              // 연타의 리듬이 그 왕복에서 끊긴다. 걷힌 채로 다음 담금질이 이어진다.
                              //
                              // 단, 이어받을 수 있는 것은 **같은 연출**뿐이다(EndAwaitForChain 주석) —
                              // 다음 한 방이 다른 얼굴이면(강화 ↔ 진화) 무대를 제대로 돌려보내고 새로 시작한다.
                              _onRetry: () =>
                              {
                                  this.m_retryQueued = true;

                                  if (RitualFor(_card) == this.m_activeRitual) this.m_activeRitual.EndAwaitForChain();
                                  else                                        this.m_activeRitual.PlayReturn();
                              },
                              // 읽을 것이 다 나왔다 — 이제 하단 바가 돌아와 "한 번 더"를 받는다.
                              // 결과판을 탭해 연출을 당긴 경우에도 같은 시점으로 앞당겨져 온다.
                              //
                              // 스스로 걷히는 판·안내가 끝맺는 판에서는 걷은 채로 둔다. 받을 "한 번 더"가 없어서인데,
                              // 못 누르는 버튼을 굳이 띄웠다가 상세에서 다시 켜면 그 깜빡임이 못 누르는 사실보다 더 눈에 걸린다.
                              // 바는 복귀가 끝나는 _onFinished가 되돌린다(멱등).
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
        if (this.levelValueText != null) this.levelValueText.text = $"Lv {_level} / {CardGrowthManager.MaxLevel}";
    }

    void BuildKeywordSection(CardData _card, bool _owned)
    {
        // 지금 지은 내용의 기준값. RefreshKeywordVisuals의 변경 감지가 이 값을 본다 —
        // 카드 전환(Apply)도 이 길을 지나므로 감지가 곧바로 한 번 더 짓는 일이 없다.
        this.m_keywordCard = _card;
        this.m_shownTrait  = _owned ? CardVisualRules.TraitKeywords(_card) : CardKeyword.None;
        this.m_shownInfo   = _owned ? CardVisualRules.InfoKeywordsWithLocked(_card) : CardKeyword.None;

        // 카드 키워드는 keywordUnlockLevel 하나로 통째로 열린다 → 열린 것이 하나도 없으면 섹션 전체가 잠긴 것이다.
        // (explainKeywords는 해금 개념이 없는 안내용이라, 그것만 남았으면 여전히 "통째로 잠김"이 맞다.)
        this.m_shownKeywordLocked = KeywordSectionLocked(_card, _owned);
        SetSectionLock(this.keywordSectionLock, this.m_shownKeywordLocked,
                       this.m_pendingUnlockedKeywords != CardKeyword.None);

        var t_lines = new List<string>();
        int t_used  = 0;

        if (_owned && this.keywordIconConfig != null && this.keywordChipRoot != null)
        {
            // 판정 기준은 인게임 카드 정보창(CardElement)과 같다 — 규칙 자체는 CardVisualRules가 소유한다.
            // 카드 타일과 달리 **해금 전 키워드도 목록에 넣는다**(잠김 룩으로) — 정보창은 지금 쓸 수 있는 것뿐
            // 아니라 이 카드가 앞으로 무엇을 여는지도 읽는 자리다. 카드 위 아이콘 줄은 여전히 열린 것만 띄운다.
            CardKeyword t_all    = CardVisualRules.InfoKeywordsWithLocked(_card);
            CardKeyword t_locked = CardVisualRules.LockedKeywords(_card);

            // 순회 순서 = CardKeyword 선언 순. 카드 타일 아이콘 줄(CardVisualRules.CollectKeywordIcons)과 같은 순서다.
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
        ApplySection(this.keywordSection, this.keywordDescText, t_lines, _owned);
    }

    void BuildSynergySection(CardData _card, bool _owned)
    {
        // 지금 지은 잠김 상태. RefreshUnlockVisuals의 변경 감지가 이 값을 본다(키워드 줄과 같은 규약).
        this.m_shownSynergyOpen = _owned && SynergyUnlocked(_card);

        // 시너지는 1차 진화 관문 하나로 전부 열리고 전부 잠긴다 → 부분 잠김이 없어 항상 섹션째로 덮는다.
        bool t_hasSynergy = _card != null && _card.synergies != null && _card.synergies.Length > 0;
        SetSectionLock(this.synergySectionLock, _owned && t_hasSynergy && !this.m_shownSynergyOpen,
                       this.m_pendingSynergyUnlockFx);

        var t_lines = new List<string>();
        int t_used  = 0;

        if (_owned && _card.synergies != null && this.synergyChipRoot != null)
        {
            // 시너지는 카드마다가 아니라 **1차 진화 도달 여부**로 통째로 열린다(관문은 CardGrowthConfig 소유).
            // 그래서 칩마다 판정하지 않고 카드 하나에 한 번만 묻는다.
            bool t_open = SynergyUnlocked(_card);

            var t_seen = new HashSet<SynergyData>();
            foreach (SynergyData t_syn in _card.synergies)
            {
                if (t_syn == null || !t_seen.Add(t_syn)) continue;   // 중복 나열 방어

                // 아이콘 배율은 시너지 PNG 투명 여백 보정 — 없으면 키워드 칩 옆에서 혼자 작아 보인다.
                if (TryShowChip(this.synergyChipRoot, t_used, "시너지",
                                t_syn.activeIcon, SynergyText.Name(t_syn),
                                SynergyIconStrip.IconPadCompensation, t_open))
                    t_used++;

                // 효과만이 아니라 발동 요구치까지 적는다 — "몇 장 모으면 켜지는가"가 시너지의 본체이고,
                // 해금 안내(UnlockIntro)와 같은 포맷이어야 방금 읽은 것을 여기서 다시 찾을 수 있다.
                t_lines.Add(SynergyText.Body(t_syn));
            }
        }

        HideChipsFrom(this.synergyChipRoot, t_used);
        ApplySection(this.synergySection, this.synergyDescText, t_lines, _owned);
    }

    // 카드가 바뀌면 읽던 자리도 같이 바뀐다 — 되감지 않으면 새 카드가 설명 중간부터 펼쳐진 채 들어온다.
    // verticalNormalizedPosition이 아니라 좌표를 직접 0으로 두는 이유: 내용이 뷰포트보다 짧을 때
    // 그쪽 계산은 음수 길이로 한 번 어긋난 자리를 잡았다가 탄성으로 되돌아와, 넘길 때마다 패널이 튄다.
    void RewindScroll()
    {
        if (this.detailScroll == null || this.detailScroll.content == null) return;

        this.detailScroll.StopMovement();

        // Content는 위에 매달려 있다(pivot y=1, 상단 앵커) → y=0이 곧 맨 위다.
        Vector2 t_pos = this.detailScroll.content.anchoredPosition;
        this.detailScroll.content.anchoredPosition = new Vector2(t_pos.x, 0f);
    }

    // 카드 설명 한 문단. 마스터 데이터 한 줄이라 칩도 재생성도 없고, 빈값 규약(없음/???)만 다른 섹션과 맞춘다.
    void ApplyDescription(CardData _card, bool _owned)
    {
        if (this.descriptionText == null) return;

        string t_text = _owned && _card != null ? _card.cardExplain : null;

        this.descriptionText.text = !string.IsNullOrEmpty(t_text) ? t_text
                                  : _owned                        ? NoneValue
                                                                  : LockedName;
    }

    // 비어 있어도 섹션을 끄지 않는다 — 스크롤 안에서 섹션이 통째로 사라지면 카드를 넘길 때마다 목록이
    // 들쭉날쭉해 어디를 읽던 중이었는지 잃는다. 빈 섹션은 자리를 지킨 채 "없음"(미소유는 ???)만 적는다.
    // 패널 자체의 높이는 프리팹 DetailFrame의 LayoutElement.preferredHeight가 고정하므로,
    // 안쪽 줄 수가 얼마든 카드 그림의 크기·위치는 카드마다 흔들리지 않는다.
    static void ApplySection(GameObject _section, TMP_Text _desc, List<string> _lines, bool _owned)
    {
        if (_desc != null)
            _desc.text = _lines.Count > 0 ? string.Join("\n", _lines)
                       : _owned           ? NoneValue
                                          : LockedName;

        if (_section != null) _section.SetActive(true);
    }

    /// <summary>줄에 <b>미리 깔아 둔</b> _index번째 칩을 채워 켠다. 칩이 모자라면 false —
    /// 호출부는 거기서 멈춘다(있는 만큼만 보여주고 나머지는 설명 줄로 읽힌다).
    ///
    /// 칩은 런타임에 만들지 않는다. 프리팹에 박아 두면 배치·간격을 씬에서 눈으로 잡을 수 있고,
    /// 카드를 넘길 때마다 Destroy/Instantiate가 돌지 않는다. 깔아 두는 쪽은 Tools/UI/도감 상세창 칩 박기.</summary>
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
