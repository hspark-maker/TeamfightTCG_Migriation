using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 카드 상세·강화 화면의 획득처 목록. 팩 구매와 카드 제작은 각 화면에서 진행한다.
public sealed class CardAcquisitionView : MonoBehaviour
{
    [SerializeField] Button closeButton;
    [SerializeField] Button dimButton;
    [SerializeField] ScrollRect scroll;
    [SerializeField] CardAcquisitionRow rowTemplate;
    [SerializeField] TMP_Text cardName;
    [SerializeField] TMP_Text emptyText;
    [SerializeField] TMP_Text footer;

    readonly List<CardAcquisitionRow> m_rows = new List<CardAcquisitionRow>();
    int m_card;
    bool m_initialized;
    Action<string> m_navigate;
    Action m_closed;
    Action m_craftOpened;

    public bool IsOpen => gameObject.activeSelf;

    public void Show(int _card, Action<string> _navigate, Action _closed, Action _craftOpened = null)
    {
        if (!m_initialized)
        {
            m_initialized = true;
            closeButton.onClick.AddListener(Hide);
            dimButton.onClick.AddListener(Hide);
            rowTemplate.gameObject.SetActive(false);
        }
        m_card = _card;
        m_navigate = _navigate;
        m_closed = _closed;
        m_craftOpened = _craftOpened;
        gameObject.SetActive(true);
        Refresh();
        CardCraftCommands.RefreshAsync().Forget();
        scroll.StopMovement();
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 1f;
    }

    public void Hide()
    {
        if (!IsOpen) return;
        gameObject.SetActive(false);
        m_closed?.Invoke();
        m_closed = null;
        m_navigate = null;
        m_craftOpened = null;
    }

    void OnEnable()
    {
        RankManager.OnChanged += Refresh;
        OutgameFeatureLock.OnChanged += Refresh;
        CardCraftCommands.OnChanged += Refresh;
    }

    void OnDisable()
    {
        RankManager.OnChanged -= Refresh;
        OutgameFeatureLock.OnChanged -= Refresh;
        CardCraftCommands.OnChanged -= Refresh;
    }

    void Refresh()
    {
        if (m_card <= 0) return;
        cardName.text = OwnershipManager.IsOwned(m_card) && CardCatalog.TryGetSpec(m_card, out CardSpec t_card)
            ? t_card.DisplayName : "미보유 카드";
        List<CardAcquisitionSources.Source> t_sources = CardAcquisitionSources.Resolve(m_card);
        bool t_canNavigate = OutgameFeatureLock.IsUnlocked(EOutgameFeature.LobbyPackTab)
            && OutgameFeatureLock.IsUnlocked(EOutgameFeature.PackCarousel)
            && !OutgameTutorialRunner.TryGetForcedPack(out _, out _)
            && GuidanceCoordinator.CanCloseCardDetail
            && GuidanceCoordinator.AllowsUserNavigation(EOutgameTutorialAnchor.LobbyPackTab);

        int t_count = t_sources.Count + 1;
        for (int t_i = m_rows.Count; t_i < t_count; t_i++)
            m_rows.Add(Instantiate(rowTemplate, scroll.content));

        m_rows[0].BindCraft(m_card, CanNavigateToCraft(), NavigateToCraft);
        m_rows[0].gameObject.SetActive(true);
        for (int t_i = 0; t_i < t_sources.Count; t_i++)
        {
            m_rows[t_i + 1].Bind(t_sources[t_i], t_canNavigate, _pack => m_navigate?.Invoke(_pack));
            m_rows[t_i + 1].gameObject.SetActive(true);
        }
        for (int t_i = t_count; t_i < m_rows.Count; t_i++)
            m_rows[t_i].gameObject.SetActive(false);

        emptyText.gameObject.SetActive(false);
        footer.text = "획득 방법을 선택하면 해당 화면으로 이동합니다.";
    }

    bool CanNavigateToCraft()
        => GuidanceCoordinator.CanCloseCardDetail
            && GuidanceCoordinator.AllowsUserNavigation(EOutgameTutorialAnchor.None)
            && !OwnershipManager.IsOwned(m_card) && !CardCraftCommands.IsCrafting
            && LobbyEnhanceTabPanel.CanOpenForCard(m_card, _allowUnowned: true);

    void NavigateToCraft()
    {
        if (!CanNavigateToCraft()) return;
        if (CardCraftCommands.IsReady &&
            (!CardCraftCommands.TryGetRecipe(m_card, out var t_recipe) || !t_recipe.Available)) return;
        var t_opened = m_craftOpened;
        if (!LobbyEnhanceTabPanel.TryOpenForCard(m_card, () =>
        {
            Hide();
            t_opened?.Invoke();
        }, _allowUnowned: true)) footer.text = "지금은 제작 화면으로 이동할 수 없습니다.";
    }

    public void ShowNavigationUnavailable()
        => footer.text = "지금은 상점으로 이동할 수 없습니다.";
}
