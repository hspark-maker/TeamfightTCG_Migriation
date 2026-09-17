using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 카드 상세 안에서 열리는 획득처 목록. 구매와 보상 지급은 기존 상점이 담당한다.
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

    public bool IsOpen => gameObject.activeSelf;

    public void Show(int _card, Action<string> _navigate, Action _closed)
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
        gameObject.SetActive(true);
        Refresh();
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
    }

    void OnEnable()
    {
        RankManager.OnChanged += Refresh;
        OutgameFeatureLock.OnChanged += Refresh;
    }

    void OnDisable()
    {
        RankManager.OnChanged -= Refresh;
        OutgameFeatureLock.OnChanged -= Refresh;
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

        for (int t_i = 0; t_i < t_sources.Count; t_i++)
        {
            if (t_i >= m_rows.Count) m_rows.Add(Instantiate(rowTemplate, scroll.content));
            m_rows[t_i].Bind(t_sources[t_i], t_canNavigate, _pack => m_navigate?.Invoke(_pack));
            m_rows[t_i].gameObject.SetActive(true);
        }
        for (int t_i = t_sources.Count; t_i < m_rows.Count; t_i++)
            m_rows[t_i].gameObject.SetActive(false);

        emptyText.gameObject.SetActive(t_sources.Count == 0);
        footer.text = t_canNavigate ? "카드팩에서 확률에 따라 획득합니다."
            : "상점 이용이 가능해지면 이동할 수 있습니다.";
    }

    public void ShowNavigationUnavailable()
        => footer.text = "지금은 상점으로 이동할 수 없습니다.";
}
