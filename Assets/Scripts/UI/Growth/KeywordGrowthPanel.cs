using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 키워드 강화 패널(KeywordGrowthOverlay에 부착). 키워드 칸을 전부 생성하고 강화 흐름을 중계한다.
// 풀(UIPoolManager)이 수명을 쥔다 — 규약은 RankRewardPanel과 같다(캔버스 기준 해상도 주의 포함).
public class KeywordGrowthPanel : ContentsPooledUI
{
    public override void Initialization(UIData _data) => this.InitializeUI();

    public override void Show() => this.Open();

    public override void Hide() => this.Close();

    [SerializeField] Transform grid;                    // 칸이 격자로 쌓일 Content(GridLayoutGroup)
    [SerializeField] KeywordGrowthCellView cellPrefab;  // 칸 프리팹
    [SerializeField] Button closeButton;

    [Tooltip("패널 밖(딤)을 눌러 닫는 판. Contents/Dim의 알파 0 Image에 배선한다 — 미배선이면 밖을 눌러도 닫히지 않는다.")]
    [SerializeField] Button dimButton;
    [SerializeField] TMP_Text energyText;
    [SerializeField] Image energyIcon;

    [Header("하단 액션")]
    [SerializeField] TMP_Text nextBonusText;            // "다음 보너스: +1"
    [SerializeField] TMP_Text costText;                 // 강화 비용
    [SerializeField] Button upgradeButton;
    [SerializeField] CanvasGroup upgradeGroup;

    [Tooltip("비용을 감당 못 하거나 만렙일 때 버튼에 씌울 알파.")]
    [SerializeField] float disabledAlpha = 0.5f;

    readonly List<KeywordGrowthCellView> m_cells = new List<KeywordGrowthCellView>();

    // 칸 생성 여부. 대상 키워드는 런타임 불변이라 최초 1회만 만들고 이후엔 Refresh로만 갱신한다.
    bool m_built;

    // 하단 버튼이 올릴 대상. None = 미선택(버튼 비활성).
    CardKeyword m_selected = CardKeyword.None;

    CoinBurstEffect m_upgradeBurst;
    Sequence m_upgradeFx;
    bool m_fxPlaying;

    // 연출이 끝나는(또는 걷히는) 자리. 서버 응답을 여기에 합류시킨다.
    UniTaskCompletionSource m_upgradeFxDone;

    // 서버 응답을 아직 기다리는 중. 연출을 걷는 자리가 잠금까지 풀어 버리면 왕복이 떠 있는 동안
    // 버튼이 되살아나 같은 결제가 두 번 나간다.
    bool m_upgradePending;

    // 강화 요청 한 건의 신원. 응답이 왔거나 연출이 걷히면 올려서, 그 요청을 따라다니던
    // 대기 표시 예약(HoldAfterUpgradeFxAsync)이 뒤늦게 깨어나도 빈 차단막을 세우지 못하게 한다.
    int m_upgradeGeneration;

    // 업그레이드 버튼이 안내 타깃으로 등록된 상태(자기 것만 해제하려고 들고 있다)
    bool m_upgradeAnchored;

    // 패널 본체가 "함께 밝힐 영역"으로 등록된 상태(업그레이드 버튼과 같은 이유로 자기 것만 해제한다)
    bool m_panelAnchored;

    // 풀 컨테이너에서 떨어져 나오려고 확보한 Canvas(LiftToOverlayLayer 참조)
    Canvas m_sortingCanvas;

    // 씬 버튼 UnityEvent가 인자 없는 이 시그니처에 바인딩된다 — 매개변수를 붙이면 배선이 끊긴다.
    public void Open()
    {
        this.SetContentsVisible(true);
        // Contents가 켜진 뒤 등록해야 안내의 등록 콜백이 활성 타깃을 받는다.
        this.ApplyUpgradeAnchor(true);
        this.ApplyPanelAnchor(true);
        this.ApplyEnergyIcon();

        if (this.m_built) this.RefreshAll();
        else this.Build();

    }

    public void Close() => this.SetContentsVisible(false);

    protected override void OnInitializeUI()
    {
        this.LiftToOverlayLayer();
        if (this.closeButton != null)
        {
            this.closeButton.onClick.RemoveAllListeners();
            this.closeButton.onClick.AddListener(this.Close);
        }
        if (this.dimButton != null)
        {
            this.dimButton.onClick.RemoveAllListeners();
            this.dimButton.onClick.AddListener(this.Close);
        }
        if (this.upgradeButton != null)
        {
            this.upgradeButton.onClick.RemoveAllListeners();
            this.upgradeButton.onClick.AddListener(this.OnUpgradePressed);
        }
    }

    protected override void OnViewShown()
    {
        OutgameTutorialRunner.OnGuidedChanged += this.LiftToOverlayLayer;
        this.LiftToOverlayLayer();
        KeywordGrowthManager.OnChanged    += this.RefreshAll;
        CurrencyManager.OnCurrencyChanged += this.HandleCurrencyChanged;
    }

    protected override void OnViewHidden()
    {
        OutgameTutorialRunner.OnGuidedChanged -= this.LiftToOverlayLayer;
        UiSortingOrder.DropNested(this.m_sortingCanvas);
        this.KillUpgradeFx();

        KeywordGrowthManager.OnChanged    -= this.RefreshAll;
        CurrencyManager.OnCurrencyChanged -= this.HandleCurrencyChanged;

        this.ApplyUpgradeAnchor(false);
        this.ApplyPanelAnchor(false);
        this.ClearCellAnchors();
    }

    // grid의 목업 하드코딩 칸을 지우고 대상 키워드 수만큼 재생성(칸 수는 Config에서 파생 — 상수 하드코딩 금지).
    void Build()
    {
        this.m_cells.Clear();
        if (this.grid == null || this.cellPrefab == null) return;

        CardKeyword[] t_keywords = KeywordGrowthRules.SupportedKeywords;
        if (t_keywords == null) return;

        // cellPrefab이 grid 안의 목업 칸으로 배선되는 저작도 허용해야 하므로 원본은 지우지 않고 숨기기만 한다.
        var t_template = this.cellPrefab.gameObject;
        for (int t_i = this.grid.childCount - 1; t_i >= 0; t_i--)
        {
            var t_child = this.grid.GetChild(t_i).gameObject;
            t_child.SetActive(false);
            if (t_child != t_template) Destroy(t_child);
        }

        // 설정에 같은 키워드가 두 번 들어가도 칸과 보너스가 겹치지 않게 이미 그린 비트를 걸러낸다.
        CardKeyword t_drawn = CardKeyword.None;
        for (int t_i = 0; t_i < t_keywords.Length; t_i++)
        {
            CardKeyword t_keyword = t_keywords[t_i];
            if (!KeywordGrowthRules.Supports(t_keyword) || (t_drawn & t_keyword) != 0) continue;

            t_drawn |= t_keyword;

            var t_cell = Instantiate(this.cellPrefab, this.grid);
            t_cell.Bind(t_keyword, this.OnCellSelected);
            t_cell.gameObject.SetActive(true);   // 비활성 사본도 초기화·바인딩을 마친 뒤 표시한다.
            this.m_cells.Add(t_cell);
        }

        // 칸이 하나도 안 나왔으면(설정 미주입 등) 다음 열기에서 다시 시도한다 — 빈 패널로 고착되지 않게.
        this.m_built = this.m_cells.Count > 0;

        if (this.m_built && this.m_selected == CardKeyword.None)
            this.m_selected = this.m_cells[0].Keyword;

        this.RefreshAll();
    }

    void OnCellSelected(CardKeyword _keyword)
    {
        if (!this.isShow || this.m_fxPlaying) return;

        this.m_selected = _keyword;
        this.RefreshAll();
    }

    void OnUpgradePressed()
    {
        if (!this.isShow || this.m_fxPlaying || this.m_selected == CardKeyword.None) return;

        if (!KeywordGrowthManager.TryGetNextStep(this.m_selected, out GrowthStep t_step)) return;

        // 지급·영속·통지는 매니저가 처리하고 OnChanged가 RefreshAll을 유발한다.
        // 잠금은 왕복 "전"에 세운다 — 판정이 서버로 나가 있는 동안 버튼이 살아 있으면 같은 결제가 여러 번 나간다.
        this.m_fxPlaying      = true;
        this.m_upgradePending = true;
        this.RefreshAction();

        this.UpgradeAsync(this.m_selected, t_step.Cost).Forget();
    }

    // 누른 프레임에 연출을 태우고 서버 응답을 그 끝자리에 합류시킨다 — 키워드 강화는 결말이 성공 하나뿐이라
    // 앞세운 그림을 되돌릴 일이 원리상 거의 없다(거절은 잔액 부족·최고 레벨 같은 사전 조건 위반뿐).
    // 실패로 끝나는 모든 갈래에서 잠금을 반드시 되돌린다(안 풀면 이 패널이 통째로 굳는다).
    async UniTaskVoid UpgradeAsync(CardKeyword _keyword, long _cost)
    {
        int t_visibilityVersion = this.VisibilityVersion;
        // 연출 대상은 누른 시점의 키워드로 굳힌다 — 늦게 온 응답이 그 사이 바뀐 선택을 건드리지 않게.
        KeywordGrowthCellView t_cell = this.FindCell(_keyword);

        // 아래 두 줄은 첫 await 앞이라 누른 프레임에 함께 선다(요청 쪽 낙관 차감도 같은 프레임에 걸린다).
        UniTask                t_fxDone  = this.PlayUpgradeFx(_cost);
        UniTask<EnhanceResult> t_request = KeywordGrowthManager.TryEnhanceAsync(_keyword);

        this.HoldAfterUpgradeFxAsync(t_fxDone, ++this.m_upgradeGeneration).Forget();

        EnhanceResult t_result;

        // Release가 한 번이라도 새면 전역 오버레이가 화면을 영영 잠근다.
        try
        {
            t_result = await t_request;
        }
        finally
        {
            this.m_upgradeGeneration++;
            this.m_upgradePending = false;
            ServerWaitOverlay.Release(this);
        }

        // 왕복 중 패널이 사라졌으면 맺을 무대가 없다(레벨·잔액은 서버가 이미 확정했다).
        if (this == null) return;

        // 닫거나 다시 연 뒤 도착한 응답은 이전 화면의 연출을 재생하지 않고 잠금만 되돌린다.
        if (!this.isShow || this.VisibilityVersion != t_visibilityVersion
            || t_result.Outcome != EEnhanceOutcome.Success)
        {
            this.KillUpgradeFx();
            this.RefreshAction();
            return;
        }

        await t_fxDone;

        if (this == null) return;

        // 왕복이 연출보다 빨랐다면 이 자리가 곧 코인이 닿는 순간이고, 늦었다면 연출 끝에서 기다렸다 여기서 맺는다.
        if (this.isShow && this.VisibilityVersion == t_visibilityVersion) t_cell?.PlayUpgradePop();

        this.m_fxPlaying = false;
        this.RefreshAction();
    }

    // 왕복 전체를 덮지 않는 이유는 앞세운 코인 연출이 지연을 감추는 장치 그 자체이기 때문이다.
    async UniTaskVoid HoldAfterUpgradeFxAsync(UniTask _fxDone, int _generation)
    {
        await _fxDone;

        // 연출을 걷는 쪽(KillUpgradeFx)이 TrySetResult로 이 await를 그 자리에서 인라인 재개시키므로,
        // "이미 걷혔는가"를 플래그로 물으면 아직 내려가지 않은 값을 읽는다 — 세대만이 정확히 답한다.
        if (this == null || _generation != this.m_upgradeGeneration) return;

        ServerWaitOverlay.Hold(this);
    }

    KeywordGrowthCellView FindCell(CardKeyword _keyword)
    {
        for (int t_i = 0; t_i < this.m_cells.Count; t_i++)
            if (this.m_cells[t_i] != null && this.m_cells[t_i].Keyword == _keyword)
                return this.m_cells[t_i];

        return null;
    }

    void HandleCurrencyChanged(ECurrencyType _type, long _balance)
    {
        // 이 패널이 무는 재화는 에너지다 — 골드만 보면 잔액 표시도 버튼 활성도 따라오지 않는다.
        if (_type != ECurrencyType.Energy) return;

        this.RefreshAll();
    }

    // 표에 에너지 그림이 저작돼 있을 때만 갈아낀다(비면 프리팹 그림 그대로).
    void ApplyEnergyIcon()
    {
        if (this.energyIcon == null) return;

        Sprite t_icon = CurrencyLook.IconOf(ECurrencyType.Energy);
        if (t_icon != null) this.energyIcon.sprite = t_icon;
    }

    void RefreshAll()
    {
        if (!this.isShow) return;

        for (int t_i = 0; t_i < this.m_cells.Count; t_i++)
        {
            if (this.m_cells[t_i] == null) continue;

            bool t_selected = this.m_cells[t_i].Keyword == this.m_selected;
            this.m_cells[t_i].Refresh(t_selected);

            // 안내는 "지금 올릴 칸"을 가리킨다 — 선택이 바뀌면 타깃도 따라간다.
            this.m_cells[t_i].ApplyTutorialAnchor(t_selected);
        }

        this.RefreshAction();

        if (this.energyText != null) this.energyText.text = CurrencyManager.Energy.ToString("N0");
    }

    // 하단 액션 줄. 표시 비용과 실제 소모가 갈리지 않게 매니저의 같은 스텝 하나만 본다.
    void RefreshAction()
    {
        GrowthStep t_next    = default;
        bool       t_hasNext = this.m_selected != CardKeyword.None
                               && KeywordGrowthManager.TryGetNextStep(this.m_selected, out t_next);

        if (this.nextBonusText != null)
            this.nextBonusText.text = t_hasNext ? $"다음 보너스: +{t_next.HpGain}" : "최대 강화";

        if (this.costText != null)
        {
            this.costText.gameObject.SetActive(t_hasNext);
            if (t_hasNext) this.costText.text = t_next.Cost.ToString("N0");
        }

        bool t_affordable  = t_hasNext && CurrencyManager.CanAfford(t_next.Currency, t_next.Cost);
        bool t_interactive = t_affordable && !this.m_fxPlaying;
        if (this.upgradeButton != null) this.upgradeButton.interactable = t_interactive;
        if (this.upgradeGroup != null) this.upgradeGroup.alpha = t_interactive ? 1f : this.disabledAlpha;
    }

    // 에너지가 빨려 나가는 연출을 세우고, 코인이 다 닿거나 연출이 걷히면 끝나는 대기를 돌려준다.
    UniTask PlayUpgradeFx(long _cost)
    {
        CoinBurstEffect t_burst = this.EnsureUpgradeBurst();
        int t_count = (int)System.Math.Min(_cost, 50L);
        t_burst.Configure(this.energyIcon != null ? this.energyIcon.sprite : null,
                          this.energyIcon != null ? this.energyIcon.rectTransform : null,
                          this.upgradeButton != null ? (RectTransform)this.upgradeButton.transform : null,
                          t_count, _scatterRadius: 0f, _gatherDuration: 0.32f,
                          _coinSize: 54f, _coinInterval: 0.02f,
                          _scatterDuration: 0.05f, _arcHeight: 70f);

        var t_done = new UniTaskCompletionSource();
        this.m_upgradeFxDone = t_done;

        // 칸 팝은 여기서 태우지 않는다 — 성립을 확인하기 전에 튀기면 거절당한 강화를 축하하게 된다.
        Sequence t_sequence = t_burst.BuildBurst(null);
        this.m_upgradeFx = t_sequence;
        t_sequence.SetLink(this.contents, LinkBehaviour.KillOnDisable);

        // 종료와 걷힘에 모두 건다 — OnKill 하나에 기대면 DOTween 전역 defaultAutoKill이 꺼지는 순간
        // 정상 종료에서 대기가 맺히지 않아 잠금이 선 채로 남는다(CompleteUpgradeFxWait는 멱등이라 둘 다 불려도 안전).
        t_sequence.OnComplete(() => this.HandleUpgradeFxSettled(t_sequence));
        t_sequence.OnKill(() => this.HandleUpgradeFxKilled(t_sequence));
        t_sequence.Play();

        return t_done.Task;
    }

    CoinBurstEffect EnsureUpgradeBurst()
    {
        if (this.m_upgradeBurst != null) return this.m_upgradeBurst;

        var t_go = new GameObject("UpgradeEnergyBurst", typeof(RectTransform), typeof(CoinBurstEffect));
        var t_rt = (RectTransform)t_go.transform;
        t_rt.SetParent(this.contents.transform, false);
        t_rt.anchorMin = t_rt.anchorMax = t_rt.pivot = new Vector2(0.5f, 0.5f);
        t_rt.sizeDelta = Vector2.zero;
        t_rt.localPosition = Vector3.zero;

        this.m_upgradeBurst = t_go.GetComponent<CoinBurstEffect>();
        return this.m_upgradeBurst;
    }

    void KillUpgradeFx()
    {
        // 아래 CompleteUpgradeFxWait가 대기 중인 HoldAfterUpgradeFxAsync를 인라인으로 깨우므로,
        // 세대를 먼저 올려야 걷힌 요청이 차단막을 세우지 못한다.
        this.m_upgradeGeneration++;

        Sequence t_sequence = this.m_upgradeFx;
        this.m_upgradeFx = null;
        if (t_sequence != null && t_sequence.IsActive()) t_sequence.Kill();

        // Kill은 BuildBurst 마지막의 ClearCoins 콜백을 건너뛴다. 연출 노드까지 걷어 취소 중이던 아이콘을 남기지 않는다.
        if (this.m_upgradeBurst != null) Destroy(this.m_upgradeBurst.gameObject);
        this.m_upgradeBurst = null;

        // 왕복이 아직 떠 있으면 잠금은 응답이 맺는다 — 여기서 풀면 닫았다 다시 연 유저가 같은 결제를 두 번 낸다.
        if (!this.m_upgradePending) this.m_fxPlaying = false;

        this.CompleteUpgradeFxWait();
    }

    // 정상 종료. 핸들은 남겨 둔다 — autoKill이 꺼진 설정에서는 이 시퀀스가 아직 살아 있어 걷을 대상이다.
    void HandleUpgradeFxSettled(Sequence _sequence)
    {
        if (this.m_upgradeFx != _sequence) return;

        this.CompleteUpgradeFxWait();
    }

    // 잠금은 UpgradeAsync가 쥔다 — 연출이 왕복보다 먼저 끝나는 것은 흔한 일이라 여기서 풀면 응답 전에 버튼이 되살아난다.
    void HandleUpgradeFxKilled(Sequence _sequence)
    {
        if (this.m_upgradeFx != _sequence) return;

        this.m_upgradeFx = null;
        this.CompleteUpgradeFxWait();
    }

    // 정상 종료든 걷힘이든 대기를 반드시 맺는다 — 안 맺으면 응답을 기다리던 흐름이 영영 서 있다.
    void CompleteUpgradeFxWait()
    {
        UniTaskCompletionSource t_done = this.m_upgradeFxDone;
        this.m_upgradeFxDone = null;
        t_done?.TrySetResult();
    }

    // 하단 업그레이드 버튼을 안내 타깃으로 세우거나 내린다(칸과 달리 하나뿐이라 패널이 직접 쥔다).
    void ApplyUpgradeAnchor(bool _on)
    {
        if (_on == this.m_upgradeAnchored) return;

        var t_rect = this.upgradeButton != null ? this.upgradeButton.transform as RectTransform : null;
        if (t_rect == null) return;   // 미배선이면 플래그도 그대로 둔다(등록하지 않은 것을 등록했다고 기억하지 않게)

        this.m_upgradeAnchored = _on;

        if (_on) TutorialAnchorRegistry.Register(EOutgameTutorialAnchor.KeywordGrowthUpgradeButton, t_rect, this.upgradeButton);
        else     TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.KeywordGrowthUpgradeButton, t_rect);
    }

    // 패널 본체를 "함께 밝힐 영역"으로 세우거나 내린다. 안내가 칸·버튼을 가리키는 동안에도 화면 전체가 딤 위에 남는다.
    // 딤을 품은 root가 아니라 transition의 패널을 쓴다 — root를 올리면 전체화면 판이 게이트 딤을 덮어 암막이 사라진 것처럼 보인다.
    void ApplyPanelAnchor(bool _on)
    {
        if (_on == this.m_panelAnchored) return;

        var t_rect = this.transition.Panel;
        if (t_rect == null) return;   // 패널 미배선이면 가리킬 영역이 없다(root로 대신하면 암막이 걷힌 것처럼 보인다)

        this.m_panelAnchored = _on;

        if (_on) TutorialAnchorRegistry.Register(EOutgameTutorialAnchor.KeywordGrowthPanel, t_rect, null);
        else     TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.KeywordGrowthPanel, t_rect);
    }

    // 칸도 스스로 놓지만(OnDisable) 퇴장 트윈이 끝나야 꺼진다 — 닫는 순간 바로 거둔다.
    void ClearCellAnchors()
    {
        for (int t_i = 0; t_i < this.m_cells.Count; t_i++)
            if (this.m_cells[t_i] != null)
                this.m_cells[t_i].ApplyTutorialAnchor(false);
    }

    // 강화 안내 중에만 게이트 아래로 내리고, 직접 연 화면은 PassOverlay처럼 풀 층을 상속한다.
    void LiftToOverlayLayer()
    {
        if (this.isShow && OutgameTutorialRunner.GuidedTrigger == EOutgameTutorialTrigger.KeywordGrowthFirstOpen)
            this.m_sortingCanvas = UiSortingOrder.LiftNested(gameObject, UiSortingOrder.PooledOverlay);
        else UiSortingOrder.DropNested(this.m_sortingCanvas);
    }
}
