using System;
using System.Collections.Generic;
using DG.Tweening;
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

    [Header("탭 전환 슬라이드")]
    [Tooltip("콘텐츠 패널이 좌우로 미끄러지는 시간(초). 0이면 즉시 교체.\n"
           + "하단 탭바 알약과 한 박자로 읽히려면 LobbyTabBarView.focusSlideSeconds와 같은 값이어야 한다.")]
    [SerializeField] float contentSlideSeconds = 0.35f;

    // 나가는 패널과 들어오는 패널이 같은 거리를 같은 박자로 함께 움직인다 —
    // 화면 폭을 통째로 건너므로 둘이 겹쳐 보이는 구간이 없고, 한 장의 넓은 띠를 가로지르는 것으로 읽힌다.
    const Ease SLIDE_EASE = Ease.OutCubic;

    int m_currentIndex = -1;

    // 탭바를 풀 오버레이 위로 올렸을 때의 중첩 캔버스. 되돌릴 때 overrideSorting만 끈다 —
    // 컴포넌트를 떼면 같은 탭을 다시 열 때마다 붙였다 떼기를 반복한다.
    Canvas m_liftedTabBar;

    // 패널 루트의 저작된 x. 미끄러지던 중간 좌표를 제자리로 착각하지 않게 한 번만 잡는다.
    float[] m_homeX;

    // 미끄러져 나가는 중인 패널. 다음 전환이 들어오면 이 트윈부터 완결시킨다.
    LobbyTabPanel m_leaving;

    // 새 패널이 제자리에 선 뒤로 미뤄 둔 일. 전환이 새로 시작되면 폐기한다.
    Action m_pendingArrive;

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

        m_homeX = new float[tabs.Count];

        for (int i = 0; i < tabs.Count; i++)
        {
            Tab t_tab = tabs[i];
            t_tab.panel?.Initialize(t_services);

            RectTransform t_root = t_tab.panel != null ? t_tab.panel.Root : null;
            m_homeX[i] = t_root != null ? t_root.anchoredPosition.x : 0f;

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

    /// <summary>탭을 실제로 갈아 끼운다. 슬라이드도 여기서만 시작한다 —
    /// 앞선 RequestLeave가 취소되면 전환 자체가 무산되기 때문이다.</summary>
    void CommitSelection(int _index, bool _fireTrigger)
    {
        FinishPendingSlide();

        // 화면을 세우거나(첫 선택) 코드가 되돌려 놓는 선택(_fireTrigger=false)은 사용자가 넘긴 게 아니라 소리를 내지 않는다.
        if (m_currentIndex >= 0 && _fireTrigger) SoundManager.Instance?.PlayCue(EOutgameSound.TabTurn);

        int t_from = m_currentIndex;
        bool t_slide = t_from >= 0 && contentSlideSeconds > 0f;
        int t_direction = _index > t_from ? 1 : -1;

        LobbyTabPanel t_previous = CurrentPanel;
        if (t_previous != null)
        {
            t_previous.OnLeave();
            if (t_slide) SlideOut(t_previous, t_from, t_direction);
            else         t_previous.gameObject.SetActive(false);
        }

        for (int i = 0; i < tabs.Count; i++)
        {
            LobbyTabPanel t_panel = tabs[i].panel;
            // 떠나는 패널은 아직 화면 밖으로 미끄러지는 중이라 여기서 끄지 않는다 — 끄는 시점은 트윈이 끝날 때다.
            if (t_panel != null && i != _index && t_panel != t_previous)
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

        // 화면 좌표를 재는 일은 패널이 제자리에 선 뒤로 미룬다 — 도중에 재면 화면 밖을 짚는다.
        m_pendingArrive = () =>
        {
            t_next?.OnSettled();
            if (_fireTrigger) TriggeredTutorialRunner.Fire(tabs[_index].tutorialTrigger);
        };

        if (t_slide && t_next != null) SlideIn(t_next, _index, t_direction);
        else                           InvokePendingArrive();
    }

    /// <summary>떠나는 패널을 화면 밖으로 밀어낸다. 끄는 것은 도착한 뒤다.</summary>
    void SlideOut(LobbyTabPanel _panel, int _index, int _direction)
    {
        RectTransform t_root = _panel.Root;
        if (t_root == null)
        {
            _panel.gameObject.SetActive(false);
            return;
        }

        float t_home = HomeX(_index);
        m_leaving = _panel;

        t_root.DOKill();
        t_root.DOAnchorPosX(t_home - _direction * SlideDistance(t_root), contentSlideSeconds)
              .SetEase(SLIDE_EASE)
              .SetUpdate(true)   // timeScale이 눌려도 같은 속도로 돈다
              .SetLink(t_root.gameObject)
              .OnComplete(() =>
              {
                  t_root.anchoredPosition = new Vector2(t_home, t_root.anchoredPosition.y);
                  _panel.gameObject.SetActive(false);
                  if (m_leaving == _panel) m_leaving = null;
              });
    }

    /// <summary>새 패널을 화면 밖에서 제자리로 들인다. 도착한 뒤에 할 일은 m_pendingArrive가 들고 있다.</summary>
    void SlideIn(LobbyTabPanel _panel, int _index, int _direction)
    {
        RectTransform t_root = _panel.Root;
        if (t_root == null)
        {
            InvokePendingArrive();
            return;
        }

        float t_home = HomeX(_index);

        t_root.DOKill();
        t_root.anchoredPosition = new Vector2(t_home + _direction * SlideDistance(t_root), t_root.anchoredPosition.y);
        t_root.DOAnchorPosX(t_home, contentSlideSeconds)
              .SetEase(SLIDE_EASE)
              .SetUpdate(true)
              .SetLink(t_root.gameObject)
              .OnComplete(InvokePendingArrive);
    }

    // 도착 처리는 한 번만 돈다. 강제 완결로 콜백이 다시 들어와도 여기서 걸린다.
    void InvokePendingArrive()
    {
        Action t_arrive = m_pendingArrive;
        m_pendingArrive = null;
        t_arrive?.Invoke();
    }

    /// <summary>진행 중인 전환을 그 자리에서 끝낸다 — 탭을 연타해도 기다리게 하지 않는다.
    /// 완료 콜백까지 강제로 돌려(DOKill(true)) 비활성화와 좌표 복원을 빠뜨리지 않는다.</summary>
    void FinishPendingSlide()
    {
        // 강제 완결이 떠나는 탭의 도착 처리까지 돌리면, 화면 밖 앵커를 기다리며 튜토리얼이 잠긴다.
        m_pendingArrive = null;

        if (m_leaving != null && m_leaving.Root != null) m_leaving.Root.DOKill(true);
        m_leaving = null;

        LobbyTabPanel t_current = CurrentPanel;
        if (t_current != null && t_current.Root != null) t_current.Root.DOKill(true);
    }

    float HomeX(int _index)
        => m_homeX != null && _index >= 0 && _index < m_homeX.Length ? m_homeX[_index] : 0f;

    /// <summary>패널이 화면 밖으로 완전히 빠지는 거리.
    /// SafeAreaFitter가 좌우 여백을 주면 부모 폭으로는 모자라, 캔버스 폭과 견줘 큰 쪽을 쓴다.</summary>
    float SlideDistance(RectTransform _root)
    {
        float t_width = _root.parent is RectTransform t_parent ? t_parent.rect.width : 0f;

        Canvas t_canvas = _root.GetComponentInParent<Canvas>();
        RectTransform t_canvasRect = t_canvas != null ? t_canvas.rootCanvas.transform as RectTransform : null;
        if (t_canvasRect != null) t_width = Mathf.Max(t_width, t_canvasRect.rect.width);

        return t_width;
    }
}
