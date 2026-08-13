using System.Collections.Generic;
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

    readonly List<KeywordGrowthCellView> m_cells = new List<KeywordGrowthCellView>();

    // 칸 생성 여부. 대상 키워드는 런타임 불변이라 최초 1회만 만들고 이후엔 Refresh로만 갱신한다.
    bool m_built;

    // 하단 버튼이 올릴 대상. None = 미선택(버튼 비활성).
    CardKeyword m_selected = CardKeyword.None;

    // 씬 버튼 UnityEvent가 인자 없는 이 시그니처에 바인딩된다 — 매개변수를 붙이면 배선이 끊긴다.
    public void Open()
    {
        this.SetVisible(true);

        if (this.m_built) this.RefreshAll();
        else this.Build();
    }

    public void Close()
    {
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
        KeywordGrowthManager.OnChanged    -= this.RefreshAll;
        CurrencyManager.OnCurrencyChanged -= this.HandleCurrencyChanged;

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
        this.m_selected = _keyword;
        this.RefreshAll();
    }

    void OnUpgradePressed()
    {
        if (this.m_selected == CardKeyword.None) return;

        // 지급·영속·통지는 매니저가 처리하고 OnChanged가 RefreshAll을 유발한다.
        KeywordGrowthManager.TryEnhance(this.m_selected);
    }

    void HandleCurrencyChanged(ECurrencyType _type, long _balance)
    {
        if (_type != ECurrencyType.Gold) return;

        this.RefreshAll();
    }

    void RefreshAll()
    {
        for (int t_i = 0; t_i < this.m_cells.Count; t_i++)
            if (this.m_cells[t_i] != null)
                this.m_cells[t_i].Refresh(this.m_cells[t_i].Keyword == this.m_selected);

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

        bool t_affordable = t_hasNext && CurrencyManager.CanAfford(t_next.Currency, t_next.Cost);
        if (this.upgradeButton != null) this.upgradeButton.interactable = t_affordable;
        if (this.upgradeGroup != null) this.upgradeGroup.alpha = t_affordable ? 1f : this.disabledAlpha;
    }

    void SetVisible(bool _visible)
    {
        this.transition.SetVisible(this.ResolveTarget(), _visible);
    }

    GameObject ResolveTarget() => this.root != null ? this.root : this.gameObject;
}
