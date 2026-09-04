using System;
using DG.Tweening;
using UnityEngine;

// 덱 탭 루트(Tab_Deck에 부착). 이 탭에는 내용이 없다 — 탭에 들어오면 풀 UI 덱 편집 화면(DeckEditController)을 열 뿐이다.
// 덱을 갈아타는 일은 편집 화면 하단의 덱 선택 바(DeckStripView)가 맡는다.
//
// 탭 셸(LobbyTabController)이 단순 SetActive 토글이라 라이프사이클 훅이 없으므로,
// "탭이 켜지면 항상 편집 화면"을 OnEnable로 보장한다.
//
// 편집 화면을 얻는 길은 둘이다: 이 탭 안에 놓인 인스턴스(editor 배선)를 직접 쓰거나,
// 미배선이면 매치 셸과 한 인스턴스를 나눠 쓰는 풀(DeckEditController.OpenPooled)에서 세운다.
public class DeckTabController : LobbyTabPanel
{
    [Tooltip("덱 탭에 있는 동안 끌 로비 상단 바.\n"
           + "미배선이면 LobbyRoot 아래에서 이름이 TopBar로 시작하는 자식을 찾는다.")]
    [SerializeField] GameObject topBar;

    [Tooltip("상단 바가 위로 걷히고 도로 내려오는 시간(초). 0이면 연출 없이 즉시 껐다 켠다.\n"
           + "탭 콘텐츠 슬라이드와 한 박자로 읽히려면 LobbyTabController.contentSlideSeconds와 같은 값이어야 한다.")]
    [SerializeField] float topBarSlideSeconds = 0.22f;

    [Tooltip("이 탭 안에 놓인 덱 편집 화면. 배선하면 풀을 거치지 않고 이 인스턴스를 직접 세운다.\n"
           + "그 인스턴스의 DeckEditController에는 hostEmbedded를 켜 두어야 한다.\n"
           + "미배선이면 풀이 세우는 전체화면 오버레이를 연다(덱 탭 단독 테스트 씬).")]
    [SerializeField] DeckEditController editor;

    // 탭 셸(LobbyRoot)은 이 오브젝트의 상위 계층에 있다 — 인스펙터 배선 없이 첫 사용 시 찾아 캐시한다.

    // 우리가 끈 상단 바인지. 되돌리는 쪽이 이 값만 보게 해서, 다른 이유로 꺼져 있던 바를 대신 켜지 않는다.
    bool m_topBarHidden;
    bool m_editing;

    // 상단 바의 저작된 제자리 y. 걷히는 중간값을 제자리로 굳히지 않게 한 번만 잡는다.
    float m_topBarHomeY = float.NaN;
    Tween m_topBarTween;

    // 로비가 넘긴 드래그 레이어. 편집 화면이 자기 레이어를 들고 있으면 무시된다
    // (풀드 화면은 자기 캔버스에 살아서 로비 캔버스의 고스트가 뒤로 깔린다 — SetDragController 주석).
    DeckEditDragController m_dragController;

    // 뒤로가기로 이 탭을 떠날 때 돌아갈 곳. 목록이 사라져 "편집 이전 화면"이 탭 밖에 있다.
    LobbyTabController m_shell;

    // 마지막으로 편집하던 저장 슬롯. 탭을 다시 열었을 때 보던 덱이 그대로 뜨게 한다(없으면 -1).
    int m_lastSlot = -1;

    /// <summary>로비가 넘긴 서비스를 받아둔다. 편집 화면은 열 때 세워지므로 여기서 전달하지 못한다.</summary>
    public override void Initialize(LobbyTabServices _services)
    {
        m_dragController = _services?.DragController;
        m_shell          = _services?.Shell;
    }

    // 덱 탭을 단독 배치한 테스트 씬에서는 셸이 없을 수 있다 → 호출측은 항상 null을 감안한다.
    void OnEnable()
    {
        // 편집 중 탭이 꺼졌다 켜지면 이전 편집분은 무저장 폐기된다.
        // 편집은 DeckEditController의 메모리 사본에서만 일어나고 세이브는 손대지 않으므로
        // 손실은 "이번 편집분"뿐이고 기존 덱은 온전하다 — 그래서 확인 팝업 없이 다시 열어도 안전하다.
        // 오버라이드 한 줄이라 Revert 한 번에 꺼진다 — 꺼지면 풀에 등록돼 매치 화면이 이것을 빌려 간다.
        if (this.editor != null && !this.editor.IsHostEmbedded)
            Debug.LogError("[DeckTabController] 탭 안에 놓인 편집기인데 hostEmbedded가 꺼져 있다 — "
                         + "DeckEditPanel 인스턴스의 DeckEditController에서 켤 것.", this.editor);

        OpenEditorForResolvedSlot();
        SetTopBarHidden(true);
    }

    // 상단 바는 떠나는 것이 확정된 순간 되돌린다 — OnDisable은 슬라이드가 끝난 뒤라 한 박자 늦다.
    public override void OnLeave() => SetTopBarHidden(false);

    // 탭 전환이 아닌 경로(로비 캔버스 비활성·씬 전환)로 덱 탭이 꺼지면 CloseEditor를 거치지 않는다 →
    // 가드가 셸에 남아 이후 모든 탭 전환이 죽은 편집기에게 넘어가고, 풀 캔버스의 편집 화면이 다른 탭 위에 남는다.
    void OnDisable()
    {
        HideEditor();

        // 편집기는 탭이 실제로 꺼진 지금 비운다 — 더 일찍 비우면 빈 판이 미끄러져 나간다.
        if (this.editor != null) this.editor.Hide();

        SetTopBarHidden(false);   // OnLeave를 거치지 않는 경로(캔버스 비활성·씬 전환)의 안전망
    }

    // 덱 화면은 상단 재화 바를 쓰지 않는다 — 카드 6칸과 컬렉션 목록이 세로를 다 쓴다.
    //
    // 높이를 접던 옛 방식은 로비에 세로 레이아웃 그룹이 없어 화면에 아무 변화도 주지 못했다.
    void SetTopBarHidden(bool _hide)
    {
        if (this.m_topBarHidden == _hide) return;

        GameObject t_bar = TopBar;
        if (t_bar == null) return;

        // 다른 이유로 이미 꺼져 있던 바를 우리가 껐다고 기록하면, 되돌릴 때 남의 상태를 대신 켜 준다.
        if (_hide && !t_bar.activeSelf) return;

        this.m_topBarHidden = _hide;

        RectTransform t_rect = t_bar.transform as RectTransform;
        if (t_rect == null || this.topBarSlideSeconds <= 0f)
        {
            t_bar.SetActive(!_hide);
            return;
        }

        if (float.IsNaN(this.m_topBarHomeY)) this.m_topBarHomeY = t_rect.anchoredPosition.y;

        // 완료 콜백은 부르지 않고 죽인다 — 걷던 트윈의 마무리(끄기)가 방금 켠 바를 도로 지운다.
        this.m_topBarTween?.Kill();
        this.m_topBarTween = null;

        // 자기 높이의 두 배만큼 올린다 — 한 배면 부모 상단선에 걸쳐 SafeArea 위쪽에서 끝이 스친다.
        float t_away = this.m_topBarHomeY + t_rect.rect.height * 2f;

        if (!_hide)
        {
            t_bar.SetActive(true);
            t_rect.anchoredPosition = new Vector2(t_rect.anchoredPosition.x, t_away);
            this.m_topBarTween = t_rect.DOAnchorPosY(this.m_topBarHomeY, this.topBarSlideSeconds)
                .SetEase(Ease.OutCubic)
                .SetUpdate(true)
                .SetLink(t_bar);

            return;
        }

        // 이미 꺼진 계층에서는 트윈이 돌 무대가 없다 — 그 자리에서 끝낸다.
        if (!t_bar.activeInHierarchy)
        {
            t_rect.anchoredPosition = new Vector2(t_rect.anchoredPosition.x, this.m_topBarHomeY);
            t_bar.SetActive(false);

            return;
        }

        // SetLink는 바에 건다 — 이 오브젝트에 걸면 탭이 꺼지는 순간 트윈이 죽어 바가 걷힌 자리에 남는다.
        // 나갈 때도 OutCubic이다: 가속을 쓰면 첫 몇 프레임이 멈춘 듯 보여 누른 반응이 늦게 느껴진다.
        this.m_topBarTween = t_rect.DOAnchorPosY(t_away, this.topBarSlideSeconds)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)   // 결과창 등에서 timeScale이 눌려도 UI 전환은 같은 속도로 돈다
            .SetLink(t_bar)
            .OnComplete(() =>
            {
                t_rect.anchoredPosition = new Vector2(t_rect.anchoredPosition.x, this.m_topBarHomeY);
                t_bar.SetActive(false);
            });
    }

    // 미배선이면 상위 계층의 LobbyRoot 아래 TopBar를 찾는다(LobbyTabs와 같은 관례).
    // 덱 탭을 단독 배치한 테스트 씬처럼 셸이 없으면 null — 그때는 상단 바 제어 없이 그대로 동작한다.
    GameObject TopBar
    {
        get
        {
            if (this.topBar != null) return this.topBar;

            Transform t_root = transform;
            while (t_root != null && t_root.name != "LobbyRoot") t_root = t_root.parent;
            if (t_root == null) return null;

            Transform t_bar = FindTopBar(t_root);

            return this.topBar = t_bar != null ? t_bar.gameObject : null;
        }
    }

    // 저작본이 사본 이름(TopBar (1))을 달고 있어, 정확한 일치로만 찾으면 상단 바 제어가 통째로 불발한다.
    static Transform FindTopBar(Transform _root)
    {
        Transform t_exact = _root.Find("TopBar");
        if (t_exact != null) return t_exact;

        for (int t_i = 0; t_i < _root.childCount; t_i++)
        {
            Transform t_child = _root.GetChild(t_i);
            if (t_child.name.StartsWith("TopBar", StringComparison.Ordinal)) return t_child;
        }

        return null;
    }

    // 기존 덱 편집 진입. _slotIndex는 DeckSaveManager 슬롯 좌표.
    public void OpenEditor(int _slotIndex)
    {
        if (_slotIndex < 0 || _slotIndex >= DeckSaveManager.SLOT_COUNT) return;

        m_lastSlot = _slotIndex;

        // 덱 탭에서 열어 둔 덱이 곧 출전 덱이다 — 매치 탭이 덱 확인 화면을 거치지 않게 되면서
        // "어느 덱으로 싸우는가"를 정하는 자리가 여기 하나로 남았다(별도 지정 버튼을 두지 않는 이유).
        DeckSaveManager.TrySelectSlot(_slotIndex);

        ShowEditor(new DeckEditData
        {
            slotIndex      = _slotIndex,
            onExit         = CloseEditor,
            dragController = m_dragController,
            showDeckStrip  = true,
            holdoutCard    = OutgameTutorialRunner.TryGetPendingEquipCard(out var t_equip) ? t_equip : 0,
        });
    }

    // 신규 덱 진입. 좌표는 저장이 확정되는 순간(TryInsertFront)에 생기므로 여기서는 만석만 막는다.
    public void OpenNewDeckEditor()
    {
        if (DeckSaveManager.IsFull) return;

        ShowEditor(new DeckEditData
        {
            isNew          = true,
            onExit         = CloseEditor,
            dragController = m_dragController,
            showDeckStrip  = true,
        });
    }

    /// <summary>편집 종료 = 덱 탭을 떠난다. 돌아갈 목록이 없어졌으므로 셸의 기본 탭으로 보낸다.
    ///
    /// 편집기를 <b>먼저</b> 내리는 것이 계약이다 — 아래 탭 전환이 RequestLeave를 다시 태우는데,
    /// 뒤로가기에서 "저장 안 함"을 고른 경우 m_dirty가 살아 있어 확인 팝업이 두 번 뜬다.</summary>
    public void CloseEditor()
    {
        HideEditor();

        LobbyTabController t_shell = Shell;
        if (t_shell == null)
        {
            // 나갈 곳을 못 찾으면 전체화면 오버레이에 갇힌다 — 조용히 넘어가지 않고 소리내어 잡는다.
            Debug.LogError("[DeckTabController] 탭 셸을 찾지 못해 덱 탭을 떠날 수 없다 — LobbyTabController 배선을 확인할 것.", this);
            return;
        }

        t_shell.SelectDefault();
    }

    // 셸은 Initialize로 주입받는다. 탭이 셸보다 먼저 켜지는 저작(Tab_Deck이 활성인 채 저장된 경우)에서는
    // 주입이 아직 없으므로 상위 계층에서 찾아 캐시한다 — TopBar 프로퍼티와 같은 관례다.
    LobbyTabController Shell
        => this.m_shell != null ? this.m_shell : (this.m_shell = GetComponentInParent<LobbyTabController>(true));

    DeckEditController Editor
        => this.editor != null ? this.editor : DeckEditController.Pooled();

    // 탭 셸이 넘긴 이탈 요청. 저장 판정과 미완성 확인은 편집기가 하고(경로가 뒤로가기와 한 벌이어야 한다),
    // 허가가 떨어지면 편집 화면만 내리고 유저가 원래 누른 탭으로 보낸다 —
    // 여기서 CloseEditor를 부르면 기본 탭으로 한 번 갔다가 원래 누른 탭으로 또 가게 된다.
    public override void RequestLeave(Action _proceed)
    {
        if (!m_editing)
        {
            _proceed?.Invoke();
            return;
        }

        DeckEditController t_editor = Editor;
        if (t_editor == null)
        {
            HideEditor();
            _proceed();

            return;
        }

        t_editor.RequestLeave(() =>
        {
            HideEditor();
            _proceed();
        });
    }

    // 이 탭이 열 덱을 정한다: 마지막으로 보던 덱 → 출전 중인 대표 덱 → 첫 유효 덱 → 하나도 없으면 신규 생성.
    // DeckEditController.Open(-1)은 에러만 남기고 화면을 안 세우므로 좌표 없는 상태를 여기서 걸러야 한다.
    void OpenEditorForResolvedSlot()
    {
        int t_slot = ResolveSlot(m_lastSlot);

        if (t_slot >= 0) OpenEditor(t_slot);
        else             OpenNewDeckEditor();
    }

    // 대표 덱을 첫 유효 덱보다 앞에 둔다 — 앱을 다시 켠 첫 진입에서 첫 유효 덱을 열면
    // 그 순간 OpenEditor가 그것을 대표로 굳혀, 유저가 골라 둔 출전 덱이 조용히 뒤바뀐다.
    static int ResolveSlot(int _requested)
    {
        if (IsValidSlot(_requested)) return _requested;

        int t_selected = DeckSaveManager.SelectedSlot;
        if (IsValidSlot(t_selected)) return t_selected;

        for (int t_i = 0; t_i < DeckSaveManager.SLOT_COUNT; t_i++)
            if (DeckSaveManager.IsSlotValid(t_i)) return t_i;

        return -1;
    }

    // DeckSaveManager.IsSlotValid는 범위 가드 없이 슬롯 배열을 직접 인덱싱한다 —
    // "선택 없음"을 -1로 표현하므로 범위 검사를 반드시 앞에 둔다.
    static bool IsValidSlot(int _slotIndex)
        => _slotIndex >= 0 && _slotIndex < DeckSaveManager.SLOT_COUNT && DeckSaveManager.IsSlotValid(_slotIndex);

    // 편집 화면을 세우지 못하면(풀 미초기화) 빈 탭에 머문다 — 나가는 길은 하단 탭바가 아니라 셸이 쥐고 있다.
    void ShowEditor(DeckEditData _data)
    {
        // 탭 안에 놓인 인스턴스는 풀을 거치지 않는다 — 이미 제자리에 있으므로 요청만 넘기고 세우면 된다.
        if (this.editor != null)
        {
            this.editor.Initialization(_data);
            this.editor.Show();
            m_editing = true;

            return;
        }

        if (DeckEditController.OpenPooled(_data) == null) return;

        m_editing = true;

        // 풀이 세운 편집 화면은 풀 캔버스(UiSortingOrder.PooledOverlay)라 로비 캔버스의 하단 탭바를 덮는다.
        // 그동안만 탭바를 그 위로 올려 다른 탭으로 나가는 길을 남긴다 — 이탈은 RequestLeave 를 그대로 거친다.
        Shell?.LiftTabBar(true);
    }

    // 편집 화면을 내린다. 가드를 먼저 내려야 허가 경로가 재개한 탭 전환이 다시 가드로 들어오지 않는다.
    void HideEditor()
    {
        if (!m_editing) return;   // 편집을 연 적이 없으면 풀에 묻지 않는다(GetUI가 "No Such UI" 로그를 남긴다)

        m_editing = false;

        // 탭 안에 놓인 인스턴스는 탭바를 덮은 적이 없어 되돌릴 것도 없다.
        if (this.editor == null) Shell?.LiftTabBar(false);

        // 내리기 직전의 편집 대상을 회수한다 — 하단 바로 덱을 갈아탄 것은 편집기만 알고 있다.
        // 그 갈아탄 덱이 곧 출전 덱이므로 대표 좌표도 여기서 함께 따라간다.
        DeckEditController t_editor = Editor;
        if (t_editor == null) return;

        if (t_editor.CurrentSlot >= 0)
        {
            m_lastSlot = t_editor.CurrentSlot;
            DeckSaveManager.TrySelectSlot(m_lastSlot);
        }

        // 탭 안에 놓인 인스턴스는 아직 미끄러져 나가는 중이다 — 비우는 자리는 OnDisable이다.
        if (this.editor == null) t_editor.Hide();
    }
}
