using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 랭크 보상 패널(RankRewardOverlay에 부착). 티어 행을 전부 생성하고 수령 흐름을 중계한다.
// 씬에 직접 저작되므로 PooledUIBase가 아니라 SetActive 토글로 열고 닫는다(UIPoolManager 캔버스와 해상도가 달라 좌표계가 어긋난다).
public class RankRewardPanel : MonoBehaviour
{
    [Tooltip("켜고 끌 대상(딤 + 패널). 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [SerializeField] ScrollRect scrollRect;
    [SerializeField] Transform content;              // 행이 세로로 쌓일 Content(VerticalLayoutGroup)
    [SerializeField] RankRewardRowView rowPrefab;    // 행 프리팹
    [SerializeField] Button closeButton;
    [SerializeField] RankRewardClaimPopup claimPopup;

    [Header("연출")]
    [Tooltip("panel에는 Root/Panel을 배선한다 — root를 물리면 전체화면 딤까지 함께 커진다.")]
    [SerializeField] PopupTransition transition = new PopupTransition();

    readonly List<RankRewardRowView> m_rows = new List<RankRewardRowView>();

    // 행 생성 여부. 티어 수는 런타임 불변이라 최초 1회만 만들고 이후엔 Refresh로만 갱신한다.
    bool m_built;

    // 씬 버튼 UnityEvent가 인자 없는 이 시그니처에 바인딩돼 있다 — 매개변수를 붙이면 배선이 끊긴다(진입점을 따로 추가할 것).
    public void Open()
    {
        // 받을 게 있으면 강조되는 최상위 행, 없으면 현재 도달 티어를 보여준다.
        int t_top = RankRewardManager.TopClaimableIndex;
        this.OpenAt(t_top >= 0 ? t_top : RankManager.GetInfo().TierIndex);
    }

    public void Close()
    {
        if (this.claimPopup != null) this.claimPopup.Hide();
        this.SetVisible(false);
    }

    void OnEnable()
    {
        // 재활성마다 중복 등록 방지.
        if (this.closeButton != null)
        {
            this.closeButton.onClick.RemoveAllListeners();
            this.closeButton.onClick.AddListener(this.Close);
        }

        RankRewardManager.OnChanged += this.RefreshRows;
    }

    void OnDisable()
    {
        RankRewardManager.OnChanged -= this.RefreshRows;

        // 오버레이 자체가 꺼지는 경로(씬 정리 등)에서만 온다 — 열고 닫기로는 불리지 않는다.
        this.transition.HandleDisabled(this.ResolveTarget());
    }

    // Open 경로 공통부. 스크롤 타겟만 호출자가 정한다.
    void OpenAt(int _scrollRow)
    {
        // 패널보다 먼저 닫는다 — 아직 화면에 없는 동안이라 팝업이 트윈 없이 즉시 정리된다(퇴장 중 열림 경합 차단).
        if (this.claimPopup != null) this.claimPopup.Hide();

        this.SetVisible(true);

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
        for (int t_i = 0; t_i < this.m_rows.Count; t_i++)
            if (this.m_rows[t_i] != null) this.m_rows[t_i].Refresh();
    }

    // 행 클릭 → 수령 팝업. 팝업이 미배선이면 확인 없이 바로 수령한다(배선 전에도 루프가 닫히도록).
    void OnRowClicked(int _tierIndex)
    {
        if (!RankRewardManager.CanClaim(_tierIndex)) return;

        if (this.claimPopup == null)
        {
            this.Claim(_tierIndex);
            return;
        }

        this.claimPopup.Show(RankRewardManager.GetInfo(_tierIndex), () => this.Claim(_tierIndex));
    }

    // 지급·영속·통지는 매니저가 처리하고 OnChanged가 RefreshRows를 유발한다.
    // 팝업 닫기는 여기서 하지 않는다 — 팝업이 이 반환값을 보고 연출 여부를 정한 뒤 스스로 닫는다.
    // 반환값을 버리면 팝업이 뜬 사이 상태가 바뀌어 가드에 걸렸을 때 안 준 골드를 준 것처럼 연출한다.
    bool Claim(int _tierIndex)
    {
        return RankRewardManager.Claim(_tierIndex);
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

    // 오버레이는 항상 활성이고 root만 토글되므로 이 뷰의 OnDisable은 열고 닫아도 오지 않는다 —
    // 재진입마다 트윈을 걷고 시작값을 다시 잡는 PopupTransition 쪽 처리가 실질 방어선이다.
    void SetVisible(bool _visible)
    {
        this.transition.SetVisible(this.ResolveTarget(), _visible);
    }

    GameObject ResolveTarget() => this.root != null ? this.root : this.gameObject;
}
