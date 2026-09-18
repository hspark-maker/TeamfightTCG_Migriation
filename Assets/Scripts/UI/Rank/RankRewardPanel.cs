using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

// 랭크 보상 패널(RankRewardOverlay에 부착). 티어 행을 전부 생성하고 수령 흐름을 중계한다.
//
// 풀(UIPoolManager)이 수명을 쥔다 — 로비 프리팹에 상주하지 않고 필요할 때 세워진다.
// 풀의 uiRoot(Initialize 캔버스)와 로비 캔버스가 같은 1080x1920 기준이라 좌표계가 어긋나지 않는다.
// **두 캔버스의 기준 해상도가 갈리면 이 화면 배치가 통째로 어긋난다** — 바꿀 땐 같이 맞출 것.
public class RankRewardPanel : ContentsPooledUI
{
    // 풀 계약. 열고 닫는 실제 동작은 예전부터 있던 Open/Close가 그대로 쥔다 —
    // 표시 데이터는 RankRewardManager에서 스스로 당기므로 UIData가 필요 없다.
    public override void Initialization(UIData _data) => this.InitializeUI();

    public override void Show() => this.Open();

    public override void Hide() => this.Close();

    [SerializeField] ScrollRect scrollRect;
    [SerializeField] Transform content;              // 행이 세로로 쌓일 Content(VerticalLayoutGroup)
    [SerializeField] RankRewardRowView rowPrefab;    // 행 프리팹
    [SerializeField] Button closeButton;

    readonly List<RankRewardRowView> m_rows = new List<RankRewardRowView>();

    // 풀 컨테이너에서 떨어져 나오려고 확보한 Canvas(LiftToOverlayLayer 참조)
    Canvas m_sortingCanvas;

    // 행 생성 여부. 티어 수는 런타임 불변이라 최초 1회만 만들고 이후엔 Refresh로만 갱신한다.
    bool m_built;
    bool m_guiding;
    bool m_claimPending;
    bool m_guidedRewardReceived;
    Button m_guideTarget;
    Canvas m_guidePopupCanvas;
    int m_guidePopupOrder;

    // 승급 직후 자동으로 열린 목록에서만 수령 → 닫기를 안내한다.
    public void BeginRewardGuide()
    {
        this.m_guiding = true;
        this.m_guidedRewardReceived = false;
    }

    void Update()
    {
        if (!this.m_guiding || !this.isShow || this.m_claimPending) return;
        Button t_target;
        string t_message;
        Canvas t_popupCanvas = null;
        if (RewardClaimPopup.IsOpen)
        {
            if (!RewardClaimPopup.TryGet(out var t_popup)) return;
            t_target = t_popup.ClaimButton;
            t_popupCanvas = t_popup.GetComponent<Canvas>();
            t_message = "보상받기를 눌러 승급 보상을 받아보세요!";
        }
        else if (!this.m_guidedRewardReceived && RankRewardManager.TopClaimableIndex >= 0)
        {
            int t_index = RankRewardManager.TopClaimableIndex;
            if (t_index >= this.m_rows.Count) return;
            t_target = this.m_rows[t_index].RewardButton;
            t_message = "도달한 랭크의 보상을 눌러 받아보세요!";
        }
        else
        {
            t_target = this.closeButton;
            t_message = "닫기를 눌러 다음 안내로 이동하세요!";
        }
        if (t_target == null || !t_target.isActiveAndEnabled || !t_target.interactable) return;
        if (this.m_guideTarget == t_target && OutgameTutorialGateUI.IsShowing) return;
        this.ClearRewardGuide();
        // 다른 안내가 먼저 무대를 잡았다면 덮어쓰지 않는다.
        if (OutgameTutorialGateUI.IsShowing) return;
        var t_gate = OutgameTutorialBridge.EnsureGateForGuidance();
        if (t_gate == null) return;
        if (t_popupCanvas != null)
        {
            // 기본 팝업 층(410)은 손가락(352)을 덮는다. 안내 중에만 내리고 수령 연출 전에 복원한다.
            this.m_guidePopupCanvas = t_popupCanvas;
            this.m_guidePopupOrder = t_popupCanvas.sortingOrder;
            t_popupCanvas.sortingOrder = UiSortingOrder.GuidedRewardClaim;
        }
        this.m_guideTarget = t_target;
        t_gate.ShowGate(this, (RectTransform)t_target.transform, t_target, t_message, null);
    }

    void ClearRewardGuide()
    {
        OutgameTutorialGateUI.Instance?.Clear(this);
        this.m_guideTarget = null;
        if (this.m_guidePopupCanvas != null) this.m_guidePopupCanvas.sortingOrder = this.m_guidePopupOrder;
        this.m_guidePopupCanvas = null;
    }

    // 씬 버튼 UnityEvent가 인자 없는 이 시그니처에 바인딩돼 있다 — 매개변수를 붙이면 배선이 끊긴다(진입점을 따로 추가할 것).
    public void Open()
    {
        // 받을 게 있으면 강조되는 최상위 행, 없으면 현재 도달 티어를 보여준다.
        int t_top = RankRewardManager.TopClaimableIndex;
        this.OpenAt(t_top >= 0 ? t_top : RankManager.GetInfo().TierIndex);
    }

    public void Close()
    {
        HideClaimPopup();
        this.SetContentsVisible(false);
    }

    protected override void OnInitializeUI()
    {
        this.LiftToOverlayLayer();
        if (this.closeButton != null)
        {
            this.closeButton.onClick.RemoveAllListeners();
            this.closeButton.onClick.AddListener(this.Close);
        }

    }

    protected override void OnViewShown()
    {
        RankRewardManager.OnChanged += this.RefreshRows;
        // 수령한 보상이 날아가 꽂히는 재화 HUD를 패널 위에 유지한다.
        LobbyShellBars.LiftTop(this, this.transform);
    }

    protected override void OnViewHidden()
    {
        this.m_guiding = false;
        this.m_claimPending = false;
        this.ClearRewardGuide();
        RankRewardManager.OnChanged -= this.RefreshRows;
        // 정상 닫기는 판의 퇴장이 끝난 뒤 상단바를 내린다.
        LobbyShellBars.DropTopAfter(this, this.transition.CloseDuration);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        LobbyShellBars.DropTop(this);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        LobbyShellBars.DropTop(this);
    }

    // Open 경로 공통부. 스크롤 타겟만 호출자가 정한다.
    void OpenAt(int _scrollRow)
    {
        // 패널보다 먼저 닫는다 — 아직 화면에 없는 동안이라 팝업이 트윈 없이 즉시 정리된다(퇴장 중 열림 경합 차단).
        HideClaimPopup();

        this.SetContentsVisible(true);

        // 열 때마다 재생성하면 등장 첫 프레임에 20행 Destroy+Instantiate가 얹힌다 — 생성은 1회, 이후엔 표시만 갱신.
        if (this.m_built) this.RefreshRows();
        else this.Build();

        this.ScrollToRow(_scrollRow);
    }

    // Content의 목업 하드코딩 행을 지우고 티어 수만큼 재생성(행 수는 RankConfig에서 파생 — 상수 하드코딩 금지).
    void Build()
    {
        this.m_rows.Clear();
        if (this.content == null || this.rowPrefab == null) return;

        // Destroy는 프레임 끝에 처리되므로 먼저 비활성화한다 — 레이아웃 계산에서 빠져야 이번 프레임 스크롤 위치가 맞는다.
        // rowPrefab이 Content 안의 목업 행으로 배선되는 저작도 허용해야 하므로 원본은 지우지 않고 숨기기만 한다(지우면 다음 Build가 행 0개).
        var t_template = this.rowPrefab.gameObject;
        for (int t_i = this.content.childCount - 1; t_i >= 0; t_i--)
        {
            var t_child = this.content.GetChild(t_i).gameObject;
            t_child.SetActive(false);
            if (t_child != t_template) Destroy(t_child);
        }

        int t_count = RankRewardManager.TierCount;
        for (int t_i = 0; t_i < t_count; t_i++)
        {
            var t_row = Instantiate(this.rowPrefab, this.content);
            t_row.gameObject.SetActive(true); // 위에서 원본을 숨겼을 수 있다 — 사본은 항상 보이게.
            t_row.Bind(t_i, t_i == t_count - 1, this.OnRowClicked);
            this.m_rows.Add(t_row);
        }

        // 행이 하나도 안 나왔으면(설정 미주입 등) 다음 열기에서 다시 시도한다 — 빈 패널로 세션 내내 고착되지 않게.
        this.m_built = t_count > 0;
    }

    // 수령 통지 → 전 행 재바인딩(수령한 행 = 완료, 다음 행 = 수령 가능). 재빌드가 아니라 Refresh라 스크롤 위치가 보존된다.
    void RefreshRows()
    {
        if (!this.isShow) return;
        for (int t_i = 0; t_i < this.m_rows.Count; t_i++)
            if (this.m_rows[t_i] != null) this.m_rows[t_i].Refresh();
    }

    // 행 클릭 → 수령 팝업. 팝업이 씬에 없으면 확인 없이 바로 수령한다(배선 전에도 루프가 닫히도록).
    void OnRowClicked(int _tierIndex)
    {
        if (!RankRewardManager.CanClaim(_tierIndex)) return;
        this.ClearRewardGuide();

        if (!RewardClaimPopup.TryGet(out var t_popup))
        {
            // 팝업이 없으면 연출도 없다 — 지급 결과를 볼 곳이 없으니 결과를 기다릴 이유도 없다.
            RewardClaimPopup.ClaimWithoutPopup(() => this.ClaimAsync(_tierIndex)).Forget();
            return;
        }

        var t_info = RankRewardManager.GetInfo(_tierIndex);

        // 획득 빛은 팝업이 닫힌 뒤 이 행의 보상 칸에서 핀다 — 행은 티어 순서 그대로 쌓이므로 티어 인덱스가 곧 행 색인이다.
        // 행이 없으면(재빌드 직후 등) 팝업 안에서 피는 종전 경로로 내려간다.
        var t_row = _tierIndex < this.m_rows.Count ? this.m_rows[_tierIndex] : null;
        t_popup.Show(t_info.DisplayName, t_info.Rewards, () => this.ClaimAsync(_tierIndex), true,
                     _gainSlotsAfterClose: t_row != null ? t_row.RewardSlots : null);
    }

    // 팝업은 이 패널의 소유가 아니라 씬 공용이다 — 없을 수도 있으므로 로케이터를 거친다.
    static void HideClaimPopup()
    {
        if (RewardClaimPopup.TryGet(out var t_popup)) t_popup.Hide();
    }

    // 판정·지급·낙인은 서버가 하고 응답 채택 후 매니저의 OnChanged가 RefreshRows를 유발한다.
    // 팝업 닫기는 여기서 하지 않는다 — 팝업이 획득 연출을 태운 뒤 스스로 닫는다.
    // 팝업은 왕복을 기다리지 않으므로, 서버까지 가서 거절당한 수령은 연출이 이미 다 돈 뒤에 잔액으로만 드러난다.
    async UniTask<RewardClaimOutcome> ClaimAsync(int _tierIndex)
    {
        this.ClearRewardGuide();
        this.m_claimPending = true;
        int t_version = this.VisibilityVersion;
        RewardClaimOutcome t_outcome;
        try
        {
            t_outcome = await RankRewardManager.ClaimAsync(_tierIndex);
            if (this != null && this.VisibilityVersion == t_version && t_outcome.Succeeded)
                this.m_guidedRewardReceived = true;
        }
        finally
        {
            if (this != null && this.VisibilityVersion == t_version) this.m_claimPending = false;
        }
        if (t_outcome.HasCards && this != null)
        {
            // 팩 개봉보다 높은 목록만 걷는다. 공용 보상 팝업의 합산 연출은 계속 재생한다.
            this.SetContentsVisible(false);
            await UniTask.Delay(Mathf.CeilToInt(this.transition.CloseDuration * 1000f), DelayType.UnscaledDeltaTime);
        }
        return t_outcome;
    }

    // 지정한 행으로 스크롤. 레이아웃이 확정되기 전에 세팅하면 무시되므로 강제 리빌드 후 적용한다.
    // 행 높이를 무시한 인덱스 비율 근사다(행 높이가 균일한 지금은 충분).
    void ScrollToRow(int _index)
    {
        if (this.scrollRect == null) return;

        int t_count = this.m_rows.Count;
        if (t_count <= 1) return;

        if (_index < 0 || _index >= t_count) return;

        Canvas.ForceUpdateCanvases();
        if (this.content is RectTransform t_rect) LayoutRebuilder.ForceRebuildLayoutImmediate(t_rect);

        // 행은 인덱스 0이 맨 위 → 위쪽이 normalized 1.
        float t_ratio = (float)_index / (t_count - 1);
        this.scrollRect.verticalNormalizedPosition = Mathf.Clamp01(1f - t_ratio);
    }

    // 풀 컨테이너(UiSortingOrder.Pool)에서 떨어져 나와 로비 오버레이 층에 내려앉는다(절차는 UiSortingOrder가 쥔다).
    void LiftToOverlayLayer()
        => this.m_sortingCanvas = UiSortingOrder.LiftNested(gameObject, UiSortingOrder.PooledOverlay);

}
