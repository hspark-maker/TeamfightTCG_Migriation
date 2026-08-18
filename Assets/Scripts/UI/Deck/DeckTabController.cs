using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 덱 탭 루트(Tab_Deck에 부착). 목록/편집 두 패널의 SetActive 전환만 담당한다(씬 로드 없음).
// 탭 셸(LobbyTabController)이 단순 SetActive 토글이라 라이프사이클 훅이 없으므로,
// "탭이 켜지면 항상 목록부터"를 OnEnable로 보장한다.
public class DeckTabController : LobbyTabPanel
{
    [SerializeField] GameObject         listPanel;
    [SerializeField] GameObject         editPanel;
    [SerializeField] DeckEditController editController;   // 옵션 — 미배선이면 패널 토글만 한다

    [Tooltip("덱 탭에 있는 동안 숨길 로비 상단 바. 미배선이면 LobbyRoot/TopBar를 찾아 쓴다.")]
    [SerializeField] GameObject topBar;

    [Tooltip("상단 바가 접히고 펴지는 시간(초). 0이면 연출 없이 즉시.")]
    [SerializeField] float topBarSlideSeconds = 0.22f;

    // 탭 셸(LobbyRoot)은 이 오브젝트의 상위 계층에 있다 — 인스펙터 배선 없이 첫 사용 시 찾아 캐시한다.

    // 상단 바 접기에 쓰는 것들. 높이는 LobbyRoot의 세로 3분할이 읽는 값이라, 이걸 줄이면
    // Content가 그만큼 위로 올라온다(바가 위로 말려 들어가는 그림).
    LayoutElement m_topBarLayout;
    CanvasGroup   m_topBarGroup;
    float         m_topBarHeight = -1f;   // 펼친 높이. 접힌 상태를 원본으로 기억하지 않게 한 번만 잡는다
    Tween         m_topBarTween;
    bool          m_topBarHidden;
    bool          m_editing;

    /// <summary>로비가 넘긴 서비스를 편집 화면에 전달한다. 드래그 레이어는 탭 콘텐츠 위에 떠야 해서
    /// 이 프리팹 밖(로비 캔버스)에 사는데, 인스펙터로 물리면 그 배선이 탭 인스턴스 오버라이드로 남는다.</summary>
    public override void Initialize(LobbyTabServices _services)
    {
        if (editController != null) editController.SetDragController(_services?.DragController);
    }

    // 덱 탭을 단독 배치한 테스트 씬에서는 셸이 없을 수 있다 → 호출측은 항상 null을 감안한다.
    void OnEnable()
    {
        // 편집 중 탭이 꺼졌다 켜지면 여기서 무저장 폐기된다.
        // 편집은 DeckEditController의 메모리 사본에서만 일어나고 세이브는 손대지 않으므로
        // 손실은 "이번 편집분"뿐이고 기존 덱은 온전하다 — 그래서 확인 팝업 없이 목록으로 되돌려도 안전하다.
        ShowList();
        SetTopBarHidden(true);
    }

    // 탭 전환이 아닌 경로(로비 캔버스 비활성·씬 전환)로 덱 탭이 꺼지면 ShowList를 거치지 않는다 →
    // 가드가 셸에 남아 이후 모든 탭 전환이 죽은 편집기에게 넘어간다.
    void OnDisable()
    {
        SetTopBarHidden(false);
    }

    // 덱 화면은 상단 재화 바를 쓰지 않는다 — 카드 6칸과 컬렉션 목록이 세로를 다 쓴다.
    //
    // SetActive로 끄지 않는다. 끄면 그 프레임에 Content가 180px만큼 튀어 오르고, 되돌아올 때 또 튄다.
    // 대신 높이를 0으로 접어 위로 말려 들어가게 하고, 알파로 내용을 지운다 —
    // 높이가 곧 세로 3분할의 몫이라 Content가 그 변화를 그대로 따라 올라온다.
    void SetTopBarHidden(bool _hide)
    {
        if (this.m_topBarHidden == _hide) return;   // 중복 호출로 트윈을 다시 시작하지 않게

        GameObject t_bar = TopBar;
        if (t_bar == null) return;

        // 다른 이유로 이미 꺼져 있는 바는 우리가 켜 주지 않는다.
        if (!t_bar.activeSelf) return;

        if (this.m_topBarLayout == null) this.m_topBarLayout = t_bar.GetComponent<LayoutElement>();
        if (this.m_topBarLayout == null) return;

        if (this.m_topBarHeight < 0f) this.m_topBarHeight = this.m_topBarLayout.preferredHeight;

        if (this.m_topBarGroup == null)
        {
            this.m_topBarGroup = t_bar.GetComponent<CanvasGroup>();
            if (this.m_topBarGroup == null) this.m_topBarGroup = t_bar.AddComponent<CanvasGroup>();
        }

        this.m_topBarHidden = _hide;

        float t_targetHeight = _hide ? 0f : this.m_topBarHeight;
        float t_targetAlpha  = _hide ? 0f : 1f;

        // 접힌 바 위로 손가락이 지나가도 메뉴 버튼이 눌리면 안 된다.
        this.m_topBarGroup.blocksRaycasts = !_hide;
        this.m_topBarGroup.interactable   = !_hide;

        this.m_topBarTween?.Kill();
        this.m_topBarTween = null;

        // 탭이 꺼지는 경로(OnDisable)에서도 걸린다 — 트윈 주인은 이 컨트롤러가 아니라 상단 바다.
        // SetLink도 바에 건다: 이 오브젝트에 걸면 탭이 꺼지는 순간 트윈이 같이 죽어 바가 접힌 채 남는다.
        if (this.topBarSlideSeconds <= 0f || !t_bar.activeInHierarchy)
        {
            this.m_topBarLayout.preferredHeight = t_targetHeight;
            this.m_topBarGroup.alpha            = t_targetAlpha;

            return;
        }

        this.m_topBarTween = DOTween.Sequence()
            .SetLink(t_bar)
            .SetUpdate(true)   // 결과창 등에서 timeScale이 눌려도 UI 전환은 같은 속도로 돈다
            .Join(DOTween.To(() => this.m_topBarLayout.preferredHeight,
                             _v => this.m_topBarLayout.preferredHeight = _v,
                             t_targetHeight, this.topBarSlideSeconds).SetEase(Ease.OutCubic))
            .Join(this.m_topBarGroup.DOFade(t_targetAlpha, this.topBarSlideSeconds));
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

            Transform t_bar = t_root.Find("TopBar");

            return this.topBar = t_bar != null ? t_bar.gameObject : null;
        }
    }

    // 기존 덱 편집 진입. _slotIndex는 DeckSaveManager 슬롯 좌표.
    public void OpenEditor(int _slotIndex)
    {
        if (_slotIndex < 0 || _slotIndex >= DeckSaveManager.SLOT_COUNT) return;

        ShowEditor();
        if (editController != null) editController.Open(_slotIndex);
    }

    // 신규 덱 진입. 좌표는 저장이 확정되는 순간(TryInsertFront)에 생기므로 여기서는 만석만 막는다.
    public void OpenNewDeckEditor()
    {
        if (DeckSaveManager.IsFull) return;

        ShowEditor();
        if (editController != null) editController.OpenNew();
    }

    // 편집 종료 = 편집 진입 이전 화면(덱 탭 목록)으로 복귀. 탭은 건드리지 않는다.
    public void CloseEditor()
    {
        ShowList();
    }

    // 탭 셸이 넘긴 이탈 요청. 저장 판정과 미완성 확인은 편집기가 하고(경로가 뒤로가기와 한 벌이어야 한다),
    // 허가가 떨어지면 목록으로 되돌린 뒤 유저가 원래 누른 탭으로 보낸다.
    public override void RequestLeave(Action _proceed)
    {
        if (!m_editing)
        {
            _proceed?.Invoke();
            return;
        }

        if (editController == null)
        {
            _proceed();

            return;
        }

        editController.RequestLeave(() =>
        {
            CloseEditor();
            _proceed();
        });
    }

    void ShowEditor()
    {
        m_editing = true;
        if (listPanel != null) listPanel.SetActive(false);
        if (editPanel != null) editPanel.SetActive(true);

        // 탭 버튼은 편집 패널 위에 그대로 노출돼 있다 → 뒤로가기와 같은 확인을 거치게 가로챈다.
    }

    void ShowList()
    {
        m_editing = false;
        // 가드를 먼저 내린다 — 허가 경로가 이 뒤에 원래 탭 전환을 재개하는데, 남아 있으면 그게 다시 가드로 들어온다.
        if (editController != null) editController.Close();
        if (editPanel != null) editPanel.SetActive(false);
        if (listPanel != null) listPanel.SetActive(true);
    }
}
