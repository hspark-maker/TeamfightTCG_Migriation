using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 로비 컬렉션 탭의 카드 상세 오버레이(CardDetailOverlay.prefab 루트에 부착).
// 카드 타일을 길게 누르면 열리고, 누른 카드의 이름·체력·키워드·시너지를 채운다.
//
// 인게임 카드 정보창(PooledCardElement)과 달리 풀드 UI가 아니라 로비 씬에 직접 배치한다 —
// 로비 전용 풀스크린 한 장이라 Addressables("UIPrefab" 라벨) 등록까지 갈 이유가 없다(PackOpenOverlay와 같은 결).
//
// 표시 규칙은 복제하지 않는다: 카드 그림 한 장은 CardVisualView.Bind, 시너지 이름은 SynergyText,
// 키워드 아이콘·표시명·설명은 KeywordIconConfig가 정본이다.
public class CardDetailOverlayView : MonoBehaviour
{
    /// <summary>미소유 카드의 이름 자리. 카드 그림 자체는 CardVisualView가 실루엣으로 가린다.</summary>
    const string LockedName  = "???";
    /// <summary>미소유 카드의 수치 자리(체력).</summary>
    const string LockedValue = "?";
    /// <summary>보유 카드인데 해당 섹션에 내용이 없을 때. 섹션을 숨기지 않는 이유는 ApplySection 주석 참고.</summary>
    const string NoneValue   = "없음";

    [Header("배선")]
    [SerializeField] TMP_Text       titleText;       // 상단 카드 이름
    [SerializeField] CardVisualView cardView;        // CardArea 안의 CardUIView 인스턴스
    [SerializeField] TMP_Text       powerValueText;  // 체력 수치(프리팹 목업의 "파워" 행을 체력으로 쓴다)

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
    [SerializeField] Button             closeButton;
    [SerializeField] PopupTransition    transition = new PopupTransition();

    [Header("이전/다음 (선택 — 미배선이면 넘기기 없이 지금까지와 동일하게 동작)")]
    [SerializeField] Button prevButton;
    [SerializeField] Button nextButton;
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

        if (this.closeButton != null)
        {
            this.closeButton.onClick.RemoveAllListeners();
            this.closeButton.onClick.AddListener(Hide);
        }
    }

    // 화살표·스와이프는 Awake가 아니라 여기서 배선한다 — 오버레이는 열 때마다 꺼졌다 켜지므로
    // Awake 한 번으로는 부족하고, Remove 후 Add라 중복 등록도 남지 않는다.
    void OnEnable()
    {
        if (this.prevButton != null)
        {
            this.prevButton.onClick.RemoveListener(OnPrevPressed);
            this.prevButton.onClick.AddListener(OnPrevPressed);
        }
        if (this.nextButton != null)
        {
            this.nextButton.onClick.RemoveListener(OnNextPressed);
            this.nextButton.onClick.AddListener(OnNextPressed);
        }

        // 대입 — 구독자는 언제나 이 오버레이 하나뿐이다.
        if (this.swipeDetector != null) this.swipeDetector.OnSwipe = Step;

        RefreshArrows();
    }

    void OnDisable()
    {
        if (this.prevButton != null) this.prevButton.onClick.RemoveListener(OnPrevPressed);
        if (this.nextButton != null) this.nextButton.onClick.RemoveListener(OnNextPressed);
        if (this.swipeDetector != null) this.swipeDetector.OnSwipe = null;

        // 전환 도중에 닫히면 slideTarget이 옆으로 밀린 채·반투명인 채 굳는다 → 다음 열기에 그대로 보인다.
        // pending 카드는 버린다 — 안 보이는 채로 칩을 재생성할 이유가 없고, 씬 언로드 경로에서 Instantiate/Destroy를 도는 건 위험하다.
        CancelSlide();

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
        this.transition.SetVisible(gameObject, true);
        Apply(CardAt(this.m_index));
        RefreshArrows();
    }

    void Hide()
    {
        // 퇴장 중 입력부터 죽인다 — 닫히는 도중 화살표·스와이프가 전환을 시작하면 close 시퀀스와 같은 노드를 두고 싸운다.
        // 다시 열 때는 SetVisible(true) → OnEnable → RefreshArrows()가 되살린다.
        if (this.swipeDetector != null) this.swipeDetector.Interactable = false;
        if (this.prevButton    != null) this.prevButton.interactable    = false;
        if (this.nextButton    != null) this.nextButton.interactable    = false;

        this.transition.SetVisible(gameObject, false);
    }

    void OnPrevPressed() => Step(-1);
    void OnNextPressed() => Step(1);

    // 그 방향의 다음 "유효" 카드로 한 칸. 목록 끝에서는 반대편 끝으로 이어진다(순환) —
    // 상점 캐러셀(PackCarouselView)과 같은 규약이라 두 화면의 손맛이 갈리지 않는다.
    void Step(int _dir)
    {
        if (_dir == 0) return;

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

    // 진행 중 전환을 취소한다: 자리·투명도를 authoring 값으로 되돌리고 아직 반영 못 한 카드는 버린다.
    // 버려도 되는 이유는 호출처가 셋뿐이기 때문이다 — 닫힘(안 보임), Show(직후 Apply), 연타 인계(새 카드가 덮어씀).
    // 한 프레임도 안 보일 중간 카드에 칩 전량을 재생성하지 않는다.
    //
    // ⚠ slideTarget.DOKill()을 쓰면 안 된다 — 같은 노드에 PopupTransition의 등장·퇴장 DOScale/DOFade가 걸려 있어
    //   통째로 자르면 localScale이 0.9 같은 중간값에 굳는다. 그래서 슬라이드 시퀀스에만 id(this)를 달고 그것만 자른다.
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
        bool t_multi = HasMultipleCards();

        if (this.prevButton != null)
        {
            this.prevButton.gameObject.SetActive(t_multi);
            this.prevButton.interactable = t_multi;
        }
        if (this.nextButton != null)
        {
            this.nextButton.gameObject.SetActive(t_multi);
            this.nextButton.interactable = t_multi;
        }

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

    void Apply(CardData _card)
    {
        bool t_owned = OwnershipManager.IsOwned(_card);

        // 그림·이름·체력·키워드 아이콘·잠김 오버레이는 도감 타일과 같은 컴포넌트에 그대로 위임한다.
        if (this.cardView != null) this.cardView.Bind(_card, t_owned);

        if (this.titleText != null)
            this.titleText.text = t_owned ? _card.displayName : LockedName;

        // CardData에 파워 필드가 없어 프리팹 목업의 "파워" 행을 체력으로 쓴다(라벨/아이콘은 프리팹 쪽 값).
        if (this.powerValueText != null)
            this.powerValueText.text = !t_owned          ? LockedValue
                                     : _card.bonusHp > 0 ? $"{_card.maxHp} (+{_card.bonusHp})"
                                                         : _card.maxHp.ToString();

        BuildKeywordSection(_card, t_owned);
        BuildSynergySection(_card, t_owned);
    }

    void BuildKeywordSection(CardData _card, bool _owned)
    {
        ClearChildren(this.keywordChipRoot);

        var t_lines = new List<string>();

        if (_owned && this.keywordIconConfig != null && this.chipPrefab != null && this.keywordChipRoot != null)
        {
            // 판정 기준은 인게임 카드 정보창(CardElement)과 같은 keywords | explainKeywords —
            // 설명 전용 키워드까지 보여주는 것이 정보창의 규약이다(카드 타일의 아이콘 줄과는 목적이 다르다).
            CardKeyword t_all = _card.keywords | _card.explainKeywords;

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
