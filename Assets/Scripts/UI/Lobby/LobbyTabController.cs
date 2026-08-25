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
        public GameObject background;
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

        // 로비 버튼 전체에 공통 클릭음을 한 번에 건다. 꺼져 있는 탭 패널까지 훑으므로 여기 한 번으로 끝난다.
        // 탭바는 제외한다 — 탭은 아래 CommitSelection이 제 소리를 내므로 얹으면 한 번 눌러 두 소리가 난다.
        LobbyClickSoundBinder.Clear();
        LobbyClickSoundBinder.Bind(transform.root, tabBar != null ? tabBar.transform : null);

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
    }

    void Start()
    {
        // 이전 화면이 바 걷기 요청을 흘렸으면 여기서 회수한다 — 안 그러면 하단 탭바가 눌리지 않아
        // 다른 탭으로 나갈 수도 없다(스스로 못 빠져나오는 상태가 된다).
        LobbyShellBars.Refresh();

        Select(defaultIndex, false);
    }

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
        // 화면을 세우거나(첫 선택) 코드가 되돌려 놓는 선택(_fireTrigger=false)은 사용자가 넘긴 게 아니라 소리를 내지 않는다.
        if (m_currentIndex >= 0 && _fireTrigger) SoundManager.Instance?.PlayCue(EOutgameSound.TabTurn);

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

            if (tabs[i].background != null)
                tabs[i].background.SetActive(i == _index);
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
