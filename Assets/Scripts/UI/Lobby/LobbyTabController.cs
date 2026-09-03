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

    // 탭바를 풀 오버레이 위로 올렸을 때의 중첩 캔버스. 되돌릴 때 overrideSorting만 끈다 —
    // 컴포넌트를 떼면 같은 탭을 다시 열 때마다 붙였다 떼기를 반복한다.
    Canvas m_liftedTabBar;

    /// <summary>탭바를 풀 오버레이(<see cref="UiSortingOrder.PooledOverlay"/>) 위로 올리거나 되돌린다.
    ///
    /// <para>덱 탭처럼 내용이 풀 UI로 뜨는 탭은 그 화면이 로비 캔버스를 통째로 덮어 탭바가 사라진다 —
    /// 나가는 길이 그 화면의 뒤로가기 하나만 남는다. 그동안만 탭바를 올려 하단 메뉴를 살려 둔다.</para></summary>
    public void LiftTabBar(bool _lift)
    {
        if (tabBar == null) return;

        if (_lift)
        {
            m_liftedTabBar = UiSortingOrder.LiftNested(tabBar.gameObject, UiSortingOrder.LobbyTabBarLifted);
            return;
        }

        // 로비 캔버스의 정렬로 되돌린다. 켜 둔 채 두면 탭바가 다른 탭에서도 풀 오버레이 위에 남는다.
        if (m_liftedTabBar != null) m_liftedTabBar.overrideSorting = false;
    }

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
        var t_services = new LobbyTabServices(dragController, this);

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

    /// <summary>기본 탭으로 되돌린다. 자기 화면을 스스로 떠나는 탭(덱 탭의 뒤로가기)의 복귀 지점이다.
    /// 잠금 검사를 건너뛰는 이유: 기본 탭이 아직 잠긴 온보딩 구간이면 Select가 조용히 물러나 유저가 갇힌다.</summary>
    public void SelectDefault() => Select(defaultIndex, false);

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
