using UnityEngine;

/// <summary>상점의 팩 진열과 상품 목록을 기존 패널 수명에 맞춰 전환한다.</summary>
public sealed class LobbyStoreTabPanel : LobbyTabPanel
{
    [SerializeField] LobbyTabPanel packPanel;
    [SerializeField] LobbyTabPanel shopPanel;
    [SerializeField] TabButtonView packTab;
    [SerializeField] TabButtonView shopTab;

    bool m_showShop;

    public bool IsPackSelected => !m_showShop;
    public LobbyTabPanel PackPanel => packPanel;
    public LobbyTabPanel ShopPanel => shopPanel;
    LobbyTabPanel SelectedPanel => m_showShop ? shopPanel : packPanel;

    protected override void OnInitializeUI()
    {
        packTab.BindClick(() => SelectFeature(EOutgameFeature.LobbyPackTab));
        shopTab.BindClick(() => SelectFeature(EOutgameFeature.LobbyShopTab));
        FeatureLockView.Attach(packTab.gameObject, EOutgameFeature.LobbyPackTab);
        FeatureLockView.Attach(shopTab.gameObject, EOutgameFeature.LobbyShopTab);
    }

    public override void Initialize(LobbyTabServices _services)
    {
        packPanel.Initialize(_services);
        shopPanel.Initialize(_services);
    }

    public bool CanSelectFeature(EOutgameFeature _feature)
    {
        if (_feature != EOutgameFeature.LobbyPackTab && _feature != EOutgameFeature.LobbyShopTab)
            return false;
        EOutgameTutorialAnchor t_anchor = _feature == EOutgameFeature.LobbyPackTab
            ? EOutgameTutorialAnchor.LobbyPackTab : EOutgameTutorialAnchor.None;
        return OutgameFeatureLock.IsUnlocked(_feature)
            && GuidanceCoordinator.AllowsUserNavigation(t_anchor)
            && !PackPurchaseFlow.IsPurchasing;
    }

    public bool SelectFeature(EOutgameFeature _feature)
    {
        if (!CanSelectFeature(_feature)) return false;
        ShowFeature(_feature);
        return true;
    }

    // 셸에서 이동 허가를 받은 요청은 이탈 확인 콜백 안에서도 같은 목적지를 유지한다.
    internal void ShowFeature(EOutgameFeature _feature)
    {
        bool t_showShop = _feature == EOutgameFeature.LobbyShopTab;
        if (m_showShop == t_showShop) return;
        if (IsViewVisible) SelectedPanel.OnLeave();
        m_showShop = t_showShop;
        ApplySelection();
        if (IsViewVisible)
        {
            SelectedPanel.OnEnter();
            SelectedPanel.OnSettled();
        }
    }

    protected override void OnViewShown()
    {
        ApplySelection();
        base.OnViewShown();
    }

    protected override void OnViewHidden()
    {
        packPanel.SetViewVisible(false);
        shopPanel.SetViewVisible(false);
        base.OnViewHidden();
    }

    void ApplySelection()
    {
        packPanel.SetViewVisible(IsViewVisible && !m_showShop);
        shopPanel.SetViewVisible(IsViewVisible && m_showShop);
        packTab.SetSelected(!m_showShop);
        shopTab.SetSelected(m_showShop);
    }

    public override void OnEnter() => SelectedPanel.OnEnter();
    public override void OnLeave() => SelectedPanel.OnLeave();
    public override void OnSettled() => SelectedPanel.OnSettled();
    public override void OnSlideBegin(bool _entering) => SelectedPanel.OnSlideBegin(_entering);
    public override void RequestLeave(System.Action _proceed) => SelectedPanel.RequestLeave(_proceed);
}
