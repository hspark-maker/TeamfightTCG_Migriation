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

    readonly List<RankRewardRowView> m_rows = new List<RankRewardRowView>();

    public void Open()
    {
        this.SetVisible(true);
        if (this.claimPopup != null) this.claimPopup.Hide();

        this.Build();
        this.ScrollToFirstClaimable();
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
    void Claim(int _tierIndex)
    {
        RankRewardManager.Claim(_tierIndex);
        if (this.claimPopup != null) this.claimPopup.Hide();
    }

    // 첫 수령 가능 행으로 스크롤. 레이아웃이 확정되기 전에 세팅하면 무시되므로 강제 리빌드 후 적용한다.
    void ScrollToFirstClaimable()
    {
        if (this.scrollRect == null) return;

        int t_count = this.m_rows.Count;
        if (t_count <= 1) return;

        int t_target = RankRewardManager.ClaimedCount;
        if (t_target < 0 || t_target >= t_count) return;

        Canvas.ForceUpdateCanvases();
        if (this.content is RectTransform t_rect) LayoutRebuilder.ForceRebuildLayoutImmediate(t_rect);

        // 행은 인덱스 0이 맨 위 → 위쪽이 normalized 1.
        float t_ratio = (float)t_target / (t_count - 1);
        this.scrollRect.verticalNormalizedPosition = Mathf.Clamp01(1f - t_ratio);
    }

    void SetVisible(bool _visible)
    {
        var t_target = this.root != null ? this.root : this.gameObject;
        t_target.SetActive(_visible);
    }
}
