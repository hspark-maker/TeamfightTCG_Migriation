using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 키워드 강화 패널(KeywordGrowthOverlay에 부착). 키워드 칸을 전부 생성하고 강화 흐름을 중계한다.
// 씬에 직접 저작되므로 PooledUIBase가 아니라 SetActive 토글로 열고 닫는다(RankRewardPanel과 같은 규약).
public class KeywordGrowthPanel : MonoBehaviour
{
    [Tooltip("켜고 끌 대상(딤 + 패널). 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [SerializeField] Transform grid;                    // 칸이 격자로 쌓일 Content(GridLayoutGroup)
    [SerializeField] KeywordGrowthCellView cellPrefab;  // 칸 프리팹
    [SerializeField] Button closeButton;
    [SerializeField] TMP_Text energyText;
    [SerializeField] Image energyIcon;

    [Header("하단 액션")]
    [SerializeField] TMP_Text nextBonusText;            // "다음 보너스: +1"
    [SerializeField] TMP_Text costText;                 // 강화 비용
    [SerializeField] Button upgradeButton;
    [SerializeField] CanvasGroup upgradeGroup;

    [Tooltip("비용을 감당 못 하거나 만렙일 때 버튼에 씌울 알파.")]
    [SerializeField] float disabledAlpha = 0.5f;

    [Header("연출")]
    [Tooltip("panel에는 Root/Panel을 배선한다 — root를 물리면 전체화면 딤까지 함께 커진다.")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    [Tooltip("공용 ScreenDim(Full)에 요청할 암막 짙기. 예전 Root/Dim 저작값과 같은 0.75다.")]
    [Range(0f, 1f)] [SerializeField] float dimAlpha = 0.75f;

    readonly List<KeywordGrowthCellView> m_cells = new List<KeywordGrowthCellView>();

    // 칸 생성 여부. 대상 키워드는 런타임 불변이라 최초 1회만 만들고 이후엔 Refresh로만 갱신한다.
    bool m_built;

    // 하단 버튼이 올릴 대상. None = 미선택(버튼 비활성).
    CardKeyword m_selected = CardKeyword.None;

    CoinBurstEffect m_upgradeBurst;
    Sequence m_upgradeFx;
    bool m_fxPlaying;

    // 업그레이드 버튼이 안내 타깃으로 등록된 상태(자기 것만 해제하려고 들고 있다)
    bool m_upgradeAnchored;

    // 화면이 서 있는가. 이 컴포넌트는 늘 켜져 있는 루트에 붙어 닫혀도 OnDisable이 안 도는데,
    // 구독은 살아 있어 RefreshAll이 계속 불린다 — 그때 꺼진 칸을 안내 타깃으로 다시 올리지 않게 한다.
    bool m_visible;

    // 씬 버튼 UnityEvent가 인자 없는 이 시그니처에 바인딩된다 — 매개변수를 붙이면 배선이 끊긴다.
    public void Open()
    {
        this.SetVisible(true);

        if (this.m_built) this.RefreshAll();
        else this.Build();

        // 화면이 다 선 뒤에 깨운다 — 안내가 가리킬 칸·버튼이 그때야 등록돼 있다.
        TriggeredTutorialRunner.Fire(EOutgameTutorialTrigger.KeywordGrowthFirstOpen);
    }

    public void Close()
    {
        this.KillUpgradeFx();
        this.SetVisible(false);
    }

    void OnEnable()
    {
        // 재활성마다 중복 등록 방지.
        if (this.closeButton != null)
        {
            this.closeButton.onClick.RemoveAllListeners();
            this.closeButton.onClick.AddListener(this.Close);
        }
        if (this.upgradeButton != null)
        {
            this.upgradeButton.onClick.RemoveAllListeners();
            this.upgradeButton.onClick.AddListener(this.OnUpgradePressed);
        }

        KeywordGrowthManager.OnChanged    += this.RefreshAll;
        CurrencyManager.OnCurrencyChanged += this.HandleCurrencyChanged;
    }

    void OnDisable()
    {
        this.KillUpgradeFx();

        KeywordGrowthManager.OnChanged    -= this.RefreshAll;
        CurrencyManager.OnCurrencyChanged -= this.HandleCurrencyChanged;

        // 안전망 — Close를 거치지 않고 꺼지면 공용 딤도, 죽은 안내 타깃도 남는다.
        ScreenDim.Hide(this);
        this.ApplyUpgradeAnchor(false);

        // 오버레이 자체가 꺼지는 경로(씬 정리 등)에서만 온다 — 열고 닫기로는 불리지 않는다.
        this.transition.HandleDisabled(this.ResolveTarget());
    }

    // grid의 목업 하드코딩 칸을 지우고 대상 키워드 수만큼 재생성(칸 수는 Config에서 파생 — 상수 하드코딩 금지).
    void Build()
    {
        this.m_cells.Clear();
        if (this.grid == null || this.cellPrefab == null) return;

        CardKeyword[] t_keywords = KeywordGrowthManager.Config.SupportedKeywords;
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
            if (!KeywordGrowthManager.Config.Supports(t_keyword) || (t_drawn & t_keyword) != 0) continue;

            t_drawn |= t_keyword;

            var t_cell = Instantiate(this.cellPrefab, this.grid);
            t_cell.gameObject.SetActive(true);   // 위에서 원본을 숨겼을 수 있다 — 사본은 항상 보이게.
            t_cell.Bind(t_keyword, this.OnCellSelected);
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
        if (this.m_fxPlaying) return;

        this.m_selected = _keyword;
        this.RefreshAll();
    }

    void OnUpgradePressed()
    {
        if (this.m_fxPlaying || this.m_selected == CardKeyword.None) return;

        if (!KeywordGrowthManager.TryGetNextStep(this.m_selected, out GrowthStep t_step)) return;

        KeywordGrowthCellView t_cell = null;
        for (int t_i = 0; t_i < this.m_cells.Count; t_i++)
            if (this.m_cells[t_i] != null && this.m_cells[t_i].Keyword == this.m_selected)
            {
                t_cell = this.m_cells[t_i];
                break;
            }

        // 지급·영속·통지는 매니저가 처리하고 OnChanged가 RefreshAll을 유발한다.
        this.m_fxPlaying = true;
        this.RefreshAction();

        EnhanceResult t_result = KeywordGrowthManager.TryEnhance(this.m_selected);
        if (t_result.Outcome != EEnhanceOutcome.Success)
        {
            this.m_fxPlaying = false;
            this.RefreshAction();
            return;
        }

        this.PlayUpgradeFx(t_step.Cost, t_cell);
    }

    void HandleCurrencyChanged(ECurrencyType _type, long _balance)
    {
        if (_type != ECurrencyType.Gold) return;

        this.RefreshAll();
    }

    void RefreshAll()
    {
        for (int t_i = 0; t_i < this.m_cells.Count; t_i++)
        {
            if (this.m_cells[t_i] == null) continue;

            bool t_selected = this.m_cells[t_i].Keyword == this.m_selected;
            this.m_cells[t_i].Refresh(t_selected);

            // 안내는 "지금 올릴 칸"을 가리킨다 — 선택이 바뀌면 타깃도 따라간다.
            this.m_cells[t_i].ApplyTutorialAnchor(t_selected && this.m_visible);
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

    void PlayUpgradeFx(long _cost, KeywordGrowthCellView _cell)
    {
        CoinBurstEffect t_burst = this.EnsureUpgradeBurst();
        int t_count = (int)System.Math.Min(_cost, 50L);
        t_burst.Configure(this.energyIcon != null ? this.energyIcon.sprite : null,
                          this.energyIcon != null ? this.energyIcon.rectTransform : null,
                          this.upgradeButton != null ? (RectTransform)this.upgradeButton.transform : null,
                          t_count, _scatterRadius: 0f, _gatherDuration: 0.32f,
                          _coinSize: 54f, _coinInterval: 0.02f,
                          _scatterDuration: 0.05f, _arcHeight: 70f);

        Sequence t_sequence = t_burst.BuildBurst((_arrived, _total) =>
        {
            if (_arrived == _total) _cell?.PlayUpgradePop();
        });
        this.m_upgradeFx = t_sequence;
        t_sequence.SetLink(this.ResolveTarget(), LinkBehaviour.KillOnDisable);
        t_sequence.OnKill(() => this.HandleUpgradeFxKilled(t_sequence));
        t_sequence.Play();
    }

    CoinBurstEffect EnsureUpgradeBurst()
    {
        if (this.m_upgradeBurst != null) return this.m_upgradeBurst;

        var t_go = new GameObject("UpgradeEnergyBurst", typeof(RectTransform), typeof(CoinBurstEffect));
        var t_rt = (RectTransform)t_go.transform;
        t_rt.SetParent(this.root != null ? this.root.transform : transform, false);
        t_rt.anchorMin = t_rt.anchorMax = t_rt.pivot = new Vector2(0.5f, 0.5f);
        t_rt.sizeDelta = Vector2.zero;
        t_rt.localPosition = Vector3.zero;

        this.m_upgradeBurst = t_go.GetComponent<CoinBurstEffect>();
        return this.m_upgradeBurst;
    }

    void KillUpgradeFx()
    {
        Sequence t_sequence = this.m_upgradeFx;
        this.m_upgradeFx = null;
        if (t_sequence != null && t_sequence.IsActive()) t_sequence.Kill();

        // Kill은 BuildBurst 마지막의 ClearCoins 콜백을 건너뛴다. 연출 노드까지 걷어 취소 중이던 아이콘을 남기지 않는다.
        if (this.m_upgradeBurst != null) Destroy(this.m_upgradeBurst.gameObject);
        this.m_upgradeBurst = null;
        this.m_fxPlaying = false;
    }

    void HandleUpgradeFxKilled(Sequence _sequence)
    {
        if (this.m_upgradeFx != _sequence) return;

        this.m_upgradeFx = null;
        this.m_fxPlaying = false;
        if (this.isActiveAndEnabled) this.RefreshAction();
    }

    void SetVisible(bool _visible)
    {
        this.m_visible = _visible;

        // 암막은 공용 ScreenDim(Full)이 그린다 — Root/Dim은 알파 0으로 남아 뒤쪽 입력만 삼킨다.
        if (_visible) ScreenDim.Show(this, this.dimAlpha, true, this.transition.OpenDuration);
        else ScreenDim.Hide(this);

        this.transition.SetVisible(this.ResolveTarget(), _visible);

        // 안내 타깃은 이 화면이 서 있는 동안만 유효하다 — 닫히고도 남으면 로비 표면에 죽은 타깃이 남는다.
        this.ApplyUpgradeAnchor(_visible);
        if (!_visible) this.ClearCellAnchors();
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

    // 칸도 스스로 놓지만(OnDisable) 퇴장 트윈이 끝나야 꺼진다 — 닫는 순간 바로 거둔다.
    void ClearCellAnchors()
    {
        for (int t_i = 0; t_i < this.m_cells.Count; t_i++)
            if (this.m_cells[t_i] != null)
                this.m_cells[t_i].ApplyTutorialAnchor(false);
    }

    GameObject ResolveTarget() => this.root != null ? this.root : this.gameObject;
}
