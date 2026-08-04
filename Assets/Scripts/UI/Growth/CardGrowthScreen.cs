using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 카드 강화·진화 화면(좌: 소유 카드 그리드 / 우: 선택 카드 패널) 한 장.
// 성장 규칙·비용·성공률은 전부 CardGrowthManager가 정본이고 여기서는 표시와 입력만 한다.
//
// 그리드는 도감(CollectionGridController)과 같은 관용구로 CardCatalog.All을 돌며 CardVisualView를 찍는다 —
// 강화 대상은 소유 카드뿐이라 미소유는 아예 만들지 않는다(DeckEditCollectionGrid와 같은 결).
// 카드 수·행 수는 어디에도 박지 않는다(데이터에서 파생).
//
// 재화 잔액 표시는 이 스크립트가 하지 않는다 — 골드/다이아 HUD는 GoldHud(type만 바꿔 재사용)의 몫이다.
public class CardGrowthScreen : MonoBehaviour
{
    // 미선택·만렙 등으로 보여줄 수치가 없을 때의 자리. 빈 문자열을 넣으면 라벨만 떠 있어 배선 실수처럼 보인다.
    const string NoValue = "-";

    // 실패 펀치는 성공보다 작게 준다 — 같은 크기로 튀면 결과를 색·문구로만 구분하게 된다.
    const float FailPunch = 0.12f;

    [Header("좌: 소유 카드 그리드")]
    [SerializeField] Transform      gridContent;    // 타일이 채워질 Content(GridLayoutGroup)
    [SerializeField] CardVisualView cardPrefab;     // 카드 타일 프리팹(Card.prefab)
    [SerializeField] ScrollRect     gridScroll;     // 재빌드 시 맨 위로(선택)
    [SerializeField] GameObject     emptyHint;      // 소유 카드 0장 안내(선택)
    [Tooltip("선택 표시 틀. 선택된 타일 밑으로 옮겨 붙인다 — Content 안에 두지 말 것(재빌드 때 함께 파괴된다).")]
    [SerializeField] RectTransform  selectionFrame;

    [Header("우: 선택 카드")]
    [Tooltip("선택이 없을 때 통째로 끌 노드. 미배선이면 텍스트만 비운다.")]
    [SerializeField] GameObject     detailRoot;
    [SerializeField] CardVisualView selectedCardView;
    [SerializeField] TMP_Text       cardNameText;
    [SerializeField] TMP_Text       levelText;        // "Lv 3 / 10"
    [SerializeField] TMP_Text       hpText;           // "24 → 26"
    [SerializeField] TMP_Text       evolutionText;    // "미진화" / "진화 1단계"
    [Tooltip("지금 왜 막혔는지 알려주는 상시 문구(진화 필요·최고 레벨·잔액 부족).")]
    [SerializeField] TMP_Text       noticeText;

    [Header("강화")]
    [SerializeField] Button   enhanceButton;
    [SerializeField] TMP_Text enhanceCostText;    // 다음 레벨 골드 비용
    [SerializeField] TMP_Text successRateText;    // 다음 레벨 성공률(%)

    [Header("진화")]
    [Tooltip("진화 UI 묶음. 게이트에 걸렸을 때만 켠다.")]
    [SerializeField] GameObject evolveRoot;
    [SerializeField] Button     evolveButton;
    [SerializeField] TMP_Text   evolveCostText;   // 진화 비용(다이아)

    [Header("결과 피드백")]
    [Tooltip("강화 성공/실패 결과 한 줄. 잠시 뒤 저절로 사라진다.")]
    [SerializeField] TMP_Text resultText;
    [Tooltip("결과에 반응해 튈 대상. 미배선이면 선택 카드 뷰를 쓴다.")]
    [SerializeField] RectTransform punchTarget;
    [SerializeField] Color successColor = new Color(0.45f, 1f, 0.55f);
    [SerializeField] Color failColor    = new Color(1f, 0.45f, 0.4f);
    [Min(0f)] [SerializeField] float resultHoldSeconds = 1.6f;
    [SerializeField] AudioClip successSfx;
    [SerializeField] AudioClip failSfx;

    [Header("독립 실행 부트스트랩 (테스트 씬 전용)")]
    [Tooltip("CardCatalog가 아직 주입 안 된 독립 씬에서만 사용. 실제 통합 시엔 부트가 이미 주입해 무시된다(마스터목록 아님).")]
    [SerializeField] List<CardData> fallbackAllCards = new List<CardData>();

    readonly List<CardVisualView> m_tiles = new List<CardVisualView>();

    // m_tiles와 인덱스 1:1(같은 루프에서 같이 채운다). 선택 카드를 타일 인덱스로 되찾는 용도.
    readonly List<CardData> m_cards = new List<CardData>();

    CardData m_selected;

    // 선택 표시 틀의 원래 부모. 타일 밑으로 옮겨 붙이므로 재빌드 전에 여기로 돌려놔야 함께 파괴되지 않는다.
    Transform m_selectionHome;

    Coroutine m_resultRoutine;

    /// <summary>_card를 선택 상태로 만든다(미소유·null이면 선택 해제). 다른 화면에서 특정 카드로 진입할 때도 쓴다.</summary>
    public void Select(CardData _card)
    {
        this.m_selected = _card;

        this.AttachSelectionFrame(this.m_cards.IndexOf(_card));
        this.ClearResult();
        this.Refresh();
    }

    void Awake()
    {
        // 타일 밑으로 옮기기 전의 자리를 잡아둔다(재빌드 때 돌려놓을 곳).
        if (this.selectionFrame != null)
        {
            this.m_selectionHome = this.selectionFrame.parent;
            this.selectionFrame.gameObject.SetActive(false);
        }
    }

    void OnEnable()
    {
        this.EnsureBoot();

        // 재활성마다 중복 등록 방지(RemoveListener 후 AddListener — 씬에 저작된 다른 리스너는 건드리지 않는다).
        if (this.enhanceButton != null)
        {
            this.enhanceButton.onClick.RemoveListener(this.OnEnhancePressed);
            this.enhanceButton.onClick.AddListener(this.OnEnhancePressed);
        }
        if (this.evolveButton != null)
        {
            this.evolveButton.onClick.RemoveListener(this.OnEvolvePressed);
            this.evolveButton.onClick.AddListener(this.OnEvolvePressed);
        }

        // 강화 실패에도 OnGrowthChanged가 온다 — 핸들러는 "레벨이 올랐다"고 가정하지 않고 값을 다시 읽는다.
        CardGrowthManager.OnGrowthChanged   += this.Refresh;
        CurrencyManager.OnCurrencyChanged   += this.HandleCurrencyChanged;
        OwnershipManager.OnOwnershipChanged += this.Build;

        this.Build();
    }

    void OnDisable()
    {
        CardGrowthManager.OnGrowthChanged   -= this.Refresh;
        CurrencyManager.OnCurrencyChanged   -= this.HandleCurrencyChanged;
        OwnershipManager.OnOwnershipChanged -= this.Build;

        if (this.enhanceButton != null) this.enhanceButton.onClick.RemoveListener(this.OnEnhancePressed);
        if (this.evolveButton  != null) this.evolveButton.onClick.RemoveListener(this.OnEvolvePressed);

        // 꺼지는 동안 코루틴이 죽어 결과 문구가 켜진 채 굳는다 → 다음 진입에 남은 결과가 보이지 않게 여기서 지운다.
        this.ClearResult();
    }

    // 독립 실행 시 카탈로그/소유/재화/성장 부트를 보장. 이미 준비됐으면(실제 통합) 아무것도 하지 않는다.
    // 카탈로그 준비 여부로 나머지를 싸잡아 건너뛰지 않는다 — 다른 화면(CollectionGridController.EnsureBoot)이
    // 카탈로그만 세워둔 테스트 씬에서는 성장 캐시가 빈 채로 남아 강화가 통째로 거부된다.
    void EnsureBoot()
    {
        if (!CardCatalog.IsReady)
        {
            DataSaveManager.Load();   // 이미 로드된 세이브를 다시 읽지 않게 카탈로그 미준비일 때만.
            CardCatalog.SetSource(this.fallbackAllCards);
            OwnershipManager.Init();
            CurrencyManager.Init();
        }

        if (!CardGrowthManager.IsReady) CardGrowthManager.Init();
    }

    // Content의 목업 하드코딩 타일을 지우고 소유 카드로 재생성. 카드 수는 데이터에서 파생(상수 없음).
    void Build()
    {
        CardData t_keep = this.m_selected;   // 소유 변경으로 다시 그려도 보던 카드를 놓치지 않게.

        this.ClearTiles();
        if (this.gridContent == null || this.cardPrefab == null) return;

        for (int t_i = this.gridContent.childCount - 1; t_i >= 0; t_i--)
        {
            var t_child = this.gridContent.GetChild(t_i).gameObject;
            t_child.SetActive(false);   // Destroy는 프레임 끝에 반영 — 이번 프레임 레이아웃에 옛 타일이 끼지 않게.
            Destroy(t_child);
        }

        var t_cards = CardCatalog.All;
        for (int t_i = 0; t_i < t_cards.Count; t_i++)
        {
            CardData t_card = t_cards[t_i];
            if (t_card == null) continue;                     // CardRegistry의 ID 보존용 빈 칸
            if (!OwnershipManager.IsOwned(t_card)) continue;   // 강화 대상은 소유 카드뿐

            CardVisualView t_tile = Instantiate(this.cardPrefab, this.gridContent);
            t_tile.Bind(t_card, true);

            this.BindTileTap(t_tile, t_card);

            this.m_cards.Add(t_card);
            this.m_tiles.Add(t_tile);
        }

        if (this.emptyHint  != null) this.emptyHint.SetActive(this.m_tiles.Count == 0);
        if (this.gridScroll != null) this.gridScroll.verticalNormalizedPosition = 1f;

        // 우측 패널이 빈 채로 시작하지 않게 첫 카드를 자동 선택한다(보던 카드가 남아 있으면 그대로).
        this.Select(this.m_cards.Contains(t_keep) ? t_keep
                  : this.m_cards.Count > 0        ? this.m_cards[0]
                                                  : null);
    }

    // 타일 클릭 배선. 도감 타일과 같은 탭 판정(LongPressDetector.OnTap)을 쓴다 —
    // ScrollRect 안이라 스크롤 드래그가 클릭으로 새면 안 되고, 그 기준은 이 컴포넌트 하나가 쥐고 있다.
    // 카드 프리팹에 아직 안 붙어 있으면 붙여 준다(프리팹 편집 없이도 선택이 되게).
    void BindTileTap(CardVisualView _tile, CardData _card)
    {
        LongPressDetector t_press = _tile.GetComponent<LongPressDetector>();
        if (t_press == null) t_press = _tile.gameObject.AddComponent<LongPressDetector>();

        // 대입(+= 아님) — 타일이 재사용·재바인딩돼도 이전 콜백이 겹쳐 남지 않는다.
        t_press.OnTap = () => this.Select(_card);
    }

    void ClearTiles()
    {
        // 표시 틀은 타일의 자식이다 — 타일을 지우기 전에 원래 자리로 빼지 않으면 함께 파괴된다.
        this.DetachSelectionFrame();

        for (int t_i = 0; t_i < this.m_tiles.Count; t_i++)
            if (this.m_tiles[t_i] != null) Destroy(this.m_tiles[t_i].gameObject);

        this.m_tiles.Clear();
        this.m_cards.Clear();
    }

    // 표시 전량 갱신. 성장·재화 통지가 올 때마다 현재 값을 다시 읽는다(이벤트가 곧 레벨 상승은 아니다).
    void Refresh()
    {
        bool t_has = this.m_selected != null;
        if (this.detailRoot != null) this.detailRoot.SetActive(t_has);

        if (!t_has)
        {
            this.ApplyEmpty();
            return;
        }

        CardGrowth t_growth = CardGrowthManager.GrowthOf(this.m_selected);

        // 두 조회의 역할이 다르다: TryGetNextStep은 게이트를 모르는 비용 미리보기, TryGetPendingGate가 진짜 차단 판정이다.
        bool t_hasStep = CardGrowthManager.TryGetNextStep(this.m_selected, out GrowthStep t_step);
        bool t_gated   = CardGrowthManager.TryGetPendingGate(this.m_selected, out EvolutionGate t_gate);

        if (this.selectedCardView != null) this.selectedCardView.Bind(this.m_selected, true);
        if (this.cardNameText     != null) this.cardNameText.text = this.m_selected.displayName;   // 표시명 정본은 displayName
        if (this.levelText        != null) this.levelText.text    = $"Lv {t_growth.Level} / {CardGrowthManager.MaxLevel}";

        // 강화 화면은 내 카드라 성장 반영이 맞다. 환산은 DeckPower가 정본 — 여기서 maxHp + HpBonus를 다시 적지 않는다.
        int t_hp = DeckPower.MaxHpOf(this.m_selected);
        if (this.hpText != null)
            this.hpText.text = t_hasStep && !t_gated ? $"{t_hp} → {t_hp + t_step.HpGain}" : t_hp.ToString();

        if (this.evolutionText != null)
            this.evolutionText.text = t_growth.EvolutionStage <= 0 ? "미진화" : $"진화 {t_growth.EvolutionStage}단계";

        bool t_canPayEnhance = t_hasStep && CurrencyManager.CanAfford(ECurrencyType.Gold, t_step.Cost);
        if (this.enhanceButton   != null) this.enhanceButton.interactable = t_hasStep && !t_gated && t_canPayEnhance;
        if (this.enhanceCostText != null) this.enhanceCostText.text = t_hasStep ? t_step.Cost.ToString("N0") : NoValue;
        if (this.successRateText != null)
            this.successRateText.text = t_hasStep ? $"{Mathf.RoundToInt(t_step.SuccessRate * 100f)}%" : NoValue;

        bool t_canPayEvolve = t_gated && CurrencyManager.CanAfford(t_gate.costType, t_gate.cost);
        if (this.evolveRoot     != null) this.evolveRoot.SetActive(t_gated);
        if (this.evolveButton   != null) this.evolveButton.interactable = t_canPayEvolve;
        if (this.evolveCostText != null) this.evolveCostText.text = t_gated ? t_gate.cost.ToString("N0") : NoValue;

        // 만렙 게이트는 "다음 강화"가 없다 — 열어줄 강화가 없으니 최종 진화로 안내한다.
        if (this.noticeText != null)
            this.noticeText.text = t_gated && !t_hasStep ? $"최고 레벨 — {CurrencyName(t_gate.costType)}로 최종 진화할 수 있다"
                                 : t_gated               ? $"{t_gate.atLevel}레벨 — {CurrencyName(t_gate.costType)}로 진화해야 다음 강화가 열린다"
                                 : !t_hasStep            ? "최고 레벨에 도달했다"
                                 : !t_canPayEnhance      ? "골드가 부족하다"
                                                         : string.Empty;
    }

    // 선택이 없을 때(소유 0장 등). detailRoot 미배선 저작에서도 이전 카드 정보가 남아 보이지 않게 값을 비운다.
    void ApplyEmpty()
    {
        if (this.cardNameText    != null) this.cardNameText.text    = string.Empty;
        if (this.levelText       != null) this.levelText.text       = NoValue;
        if (this.hpText          != null) this.hpText.text          = NoValue;
        if (this.evolutionText   != null) this.evolutionText.text   = string.Empty;
        if (this.enhanceCostText != null) this.enhanceCostText.text = NoValue;
        if (this.successRateText != null) this.successRateText.text = NoValue;
        if (this.evolveCostText  != null) this.evolveCostText.text  = NoValue;
        if (this.noticeText      != null) this.noticeText.text      = "강화할 카드를 선택하세요";

        if (this.enhanceButton != null) this.enhanceButton.interactable = false;
        if (this.evolveButton  != null) this.evolveButton.interactable  = false;
        if (this.evolveRoot    != null) this.evolveRoot.SetActive(false);
    }

    void OnEnhancePressed()
    {
        if (this.m_selected == null) return;

        EnhanceResult t_result = CardGrowthManager.TryEnhance(this.m_selected);

        this.ShowEnhanceResult(t_result);

        // 성공·실패는 OnGrowthChanged가 이미 갱신했지만 차단·잔액부족은 통지가 없다 → 여기서 한 번 더(멱등).
        this.Refresh();
    }

    void OnEvolvePressed()
    {
        if (this.m_selected == null) return;
        if (!CardGrowthManager.TryGetPendingGate(this.m_selected, out EvolutionGate t_gate)) return;

        CardData t_card = this.m_selected;   // 팝업이 떠 있는 동안 선택이 바뀔 수 있다.

        // 확인 팝업이 없는 환경(테스트 씬)에서도 루프가 닫히도록 즉시 진화로 폴백한다.
        if (UIPoolManager.instance == null)
        {
            this.Evolve(t_card);
            return;
        }

        UIPoolManager.instance.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = $"{CurrencyName(t_gate.costType)} {t_gate.cost:N0}을(를) 사용해 진화하시겠습니까?",
            yesText   = "진화",
            yesAction = () => this.Evolve(t_card),
            noText    = "취소",
        });
    }

    // 진화는 실패 확률이 없다 — false는 잔액 부족이거나 그사이 게이트가 사라진 경우뿐이다.
    void Evolve(CardData _card)
    {
        // 미초기화도 false로 오지만 원인이 잔액이 아니다 → 잔액 문구로 뭉뚱그리지 않고 갈라낸다(재화는 소모되지 않았다).
        if (!CardGrowthManager.IsReady)
        {
            this.ShowResult("잠시 후 다시 시도하세요", false);
            Debug.LogError("[CardGrowthScreen] 성장 데이터 미초기화 — CardGrowthManager.Init()이 부트에서 호출되지 않았다.");
            return;
        }

        if (!CardGrowthManager.TryEvolve(_card))
        {
            this.ShowResult("다이아가 부족하다", false);
            this.Refresh();
            return;
        }

        this.ShowResult($"진화 성공!  {CardGrowthManager.GrowthOf(_card).EvolutionStage}단계", true);
        this.Refresh();
    }

    // 실패가 이 시스템의 엔진이라 결과가 한눈에 갈려야 한다 — 문구·색·펀치 세기·효과음을 함께 바꾼다.
    void ShowEnhanceResult(EnhanceResult _result)
    {
        switch (_result.Outcome)
        {
            case EEnhanceOutcome.Success:
                this.ShowResult($"강화 성공!  Lv {_result.Level}", true);
                UiPunch.Play(this.levelText != null ? this.levelText.transform : null);
                break;

            case EEnhanceOutcome.Failed:
                this.ShowResult($"강화 실패…  Lv {_result.Level} 유지", false);
                break;

            case EEnhanceOutcome.NotAffordable:
                this.ShowResult("골드가 부족하다", false);
                break;

            case EEnhanceOutcome.BlockedByEvolution:
                this.ShowResult("먼저 진화해야 한다", false);
                break;

            case EEnhanceOutcome.MaxLevel:
                this.ShowResult("이미 최고 레벨이다", false);
                break;

            // 결제 전에 거부된 경우 — 재화는 그대로다. 저작 실수(부트 누락)라 유저 문구보다 로그가 본체다.
            case EEnhanceOutcome.NotReady:
                this.ShowResult("잠시 후 다시 시도하세요", false);
                Debug.LogError("[CardGrowthScreen] 성장 데이터 미초기화 — CardGrowthManager.Init()이 부트에서 호출되지 않았다.");
                break;
        }
    }

    void ShowResult(string _text, bool _positive)
    {
        if (this.resultText != null)
        {
            this.resultText.text  = _text;
            this.resultText.color = _positive ? this.successColor : this.failColor;
            this.resultText.gameObject.SetActive(true);
        }

        UiPunch.Play(this.ResolvePunchTarget(), _positive ? UiPunch.DEFAULT_SCALE : FailPunch);

        SoundManager.Instance?.PlaySFX(_positive ? this.successSfx : this.failSfx);

        if (this.m_resultRoutine != null) StopCoroutine(this.m_resultRoutine);
        if (isActiveAndEnabled) this.m_resultRoutine = StartCoroutine(this.HideResultAfterHold());
    }

    IEnumerator HideResultAfterHold()
    {
        yield return new WaitForSeconds(this.resultHoldSeconds);

        this.m_resultRoutine = null;
        if (this.resultText != null) this.resultText.gameObject.SetActive(false);
    }

    void ClearResult()
    {
        if (this.m_resultRoutine != null)
        {
            StopCoroutine(this.m_resultRoutine);
            this.m_resultRoutine = null;
        }

        if (this.resultText != null) this.resultText.gameObject.SetActive(false);
    }

    Transform ResolvePunchTarget()
    {
        if (this.punchTarget != null) return this.punchTarget;

        return this.selectedCardView != null ? this.selectedCardView.transform : null;
    }

    // 재화 종류에 따라 버튼 활성만 바뀐다 — 어느 종류든 다시 판정하면 되므로 걸러내지 않는다.
    void HandleCurrencyChanged(ECurrencyType _type, long _balance) => this.Refresh();

    void AttachSelectionFrame(int _index)
    {
        if (this.selectionFrame == null) return;

        RectTransform t_tile = _index >= 0 && _index < this.m_tiles.Count && this.m_tiles[_index] != null
                             ? this.m_tiles[_index].transform as RectTransform
                             : null;

        if (t_tile == null)
        {
            this.DetachSelectionFrame();
            return;
        }

        this.selectionFrame.gameObject.SetActive(true);
        this.selectionFrame.SetParent(t_tile, false);
        this.selectionFrame.anchorMin        = Vector2.zero;
        this.selectionFrame.anchorMax        = Vector2.one;
        this.selectionFrame.offsetMin        = Vector2.zero;
        this.selectionFrame.offsetMax        = Vector2.zero;
        this.selectionFrame.localScale       = Vector3.one;
        this.selectionFrame.SetAsLastSibling();
    }

    void DetachSelectionFrame()
    {
        if (this.selectionFrame == null) return;

        if (this.m_selectionHome != null) this.selectionFrame.SetParent(this.m_selectionHome, false);
        this.selectionFrame.gameObject.SetActive(false);
    }

    // 안내 문구용 재화 이름. 표시 문자열이라 ECurrencyType에 얹지 않고 화면 쪽에 둔다(다른 진실원 없음).
    static string CurrencyName(ECurrencyType _type)
    {
        return _type == ECurrencyType.Diamond ? "다이아" : "골드";
    }
}
