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

    int m_currentIndex = -1;

    public LobbyTabPanel CurrentPanel
        => m_currentIndex >= 0 && m_currentIndex < tabs.Count
            ? tabs[m_currentIndex].panel
            : null;

    void Awake()
    {
        if (tabBar != null) tabBar.Selected += HandleTabSelected;
        for (int i = 0; i < tabs.Count; i++)
        {
            Tab t_tab = tabs[i];
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
