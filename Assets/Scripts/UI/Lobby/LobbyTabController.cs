using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Coordinates tab policy; panels own lifecycle and the tab bar owns visuals.</summary>
public class LobbyTabController : MonoBehaviour
{
    [Serializable]
    public class Tab
    {
        public string name;
        public LobbyTabPanel panel;
        public string label;
        public EOutgameTutorialAnchor tutorialAnchor;
        public EOutgameTutorialTrigger tutorialTrigger;
        public EOutgameFeature unlockFeature;
    }

    [SerializeField] LobbyTabBarView tabBar;
    [SerializeField] List<Tab> tabs = new List<Tab>();
    [SerializeField] int defaultIndex = 2;
    [SerializeField] GameObject alertDotPrefab;

    [Header("탭에 넘길 캔버스 레벨 서비스")]
    [Tooltip("덱 편집 드래그(로비 캔버스의 DragLayer). 여기서 받아 탭에 넘긴다 —\n"
           + "탭 프리팹 안쪽을 인스펙터로 직접 배선하면 그 배선이 오버라이드로 남아 탭 diff를 흐린다.")]
    [SerializeField] DeckEditDragController dragController;

    int m_currentIndex = -1;

    public LobbyTabPanel CurrentPanel
        => m_currentIndex >= 0 && m_currentIndex < tabs.Count
            ? tabs[m_currentIndex].panel
            : null;

    void Awake()
    {
        if (tabBar != null) tabBar.Selected += HandleTabSelected;

        TriggeredTutorialRunner.OnRetryRequested += RetryCurrentTabTrigger;

        // 서비스 주입은 첫 Select(Start)보다 먼저 끝나야 한다 — OnEnter에서 이미 쓸 수 있어야 하므로.
        var t_services = new LobbyTabServices(dragController);

        for (int i = 0; i < tabs.Count; i++)
        {
            Tab t_tab = tabs[i];
            t_tab.panel?.Initialize(t_services);
            tabBar?.ConfigureItem(
                i,
                string.IsNullOrEmpty(t_tab.label) ? t_tab.name : t_tab.label,
                t_tab.tutorialAnchor,
                t_tab.tutorialTrigger,
                t_tab.unlockFeature,
                alertDotPrefab);
        }
    }

    void OnDestroy()
    {
        if (tabBar != null) tabBar.Selected -= HandleTabSelected;

        // static 이벤트에 죽은 씬 오브젝트가 남으면 다음 씬에서 오발화한다.
        TriggeredTutorialRunner.OnRetryRequested -= RetryCurrentTabTrigger;
    }

    void Start() => Select(defaultIndex, false);

    void HandleTabSelected(int _index) => Select(_index);

    public void Select(LobbyTabPanel _panel, bool _fireTrigger = true)
    {
        int t_index = tabs.FindIndex(_tab => _tab.panel == _panel);
        if (t_index >= 0) Select(t_index, _fireTrigger);
    }

    public void Select(int _index, bool _fireTrigger = true)
    {
        if (_index < 0 || _index >= tabs.Count) return;
        if (_fireTrigger &&
            !OutgameFeatureLock.IsUnlocked(tabs[_index].unlockFeature))
            return;
        if (_index == m_currentIndex)
        {
            tabBar?.SetSelected(_index);

            // 이미 서 있는 탭을 다시 눌러도 발화 기회다 — 알림 점은 상태를 보고 떠 있는데
            // 전이가 없다고 여기서 끊으면 그 점을 눌러도 아무 일이 없다.
            if (_fireTrigger) RetryCurrentTabTrigger();
            return;
        }

        LobbyTabPanel t_current = CurrentPanel;
        if (t_current == null)
        {
            CommitSelection(_index, _fireTrigger);
            return;
        }

        t_current.RequestLeave(() =>
        {
            if (this != null) CommitSelection(_index, _fireTrigger);
        });
    }

    /// <summary>지금 서 있는 탭의 트리거 튜토리얼을 다시 물어본다. 탭 발화는 전이 1회성이라
    /// 다른 경로로 탭이 켜진 동안(팩 개봉 후 도감 착지 등) 놓친 기회를 여기서 되찾는다.
    /// 띄울지 말지는 복제하지 않고 전적으로 Fire가 판정한다.</summary>
    public void RetryCurrentTabTrigger()
    {
        if (m_currentIndex < 0 || m_currentIndex >= tabs.Count) return;

        LobbyTabPanel t_panel = CurrentPanel;
        if (t_panel != null && !t_panel.isActiveAndEnabled) return;

        TriggeredTutorialRunner.Fire(tabs[m_currentIndex].tutorialTrigger);
    }

    void CommitSelection(int _index, bool _fireTrigger)
    {
        LobbyTabPanel t_previous = CurrentPanel;
        if (t_previous != null)
        {
            t_previous.OnLeave();
            t_previous.gameObject.SetActive(false);
        }

        for (int i = 0; i < tabs.Count; i++)
        {
            LobbyTabPanel t_panel = tabs[i].panel;
            if (t_panel != null && i != _index)
                t_panel.gameObject.SetActive(false);
        }

        m_currentIndex = _index;
        LobbyTabPanel t_next = CurrentPanel;
        if (t_next != null)
        {
            t_next.gameObject.SetActive(true);
            t_next.OnEnter();
        }

        tabBar?.SetSelected(_index);
        if (_fireTrigger)
            TriggeredTutorialRunner.Fire(tabs[_index].tutorialTrigger);
    }
}
