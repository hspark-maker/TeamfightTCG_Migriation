using System.Collections.Generic;
using UnityEngine;

// 카드 앨범 탭 표면(Tab_Collection_New 루트 부착) — 전체 보상 요약 + 테마 갤러리
public class AlbumTabController : MonoBehaviour
{
    [Header("전체 보상")]
    [SerializeField] AlbumRewardSlotView[] rewardSlots;
    [SerializeField] AlbumGaugeView totalGauge = new AlbumGaugeView();
    [SerializeField] AlbumChestView albumChest = new AlbumChestView();

    [Header("갤러리")]
    [SerializeField] Transform galleryContent;
    [SerializeField] AlbumThemeCellView cellTemplate;

    [Header("오버레이")]
    [SerializeField] AlbumPageOverlayView pageOverlay;

    readonly List<AlbumThemeCellView> m_cells = new List<AlbumThemeCellView>();
    bool m_built;
    bool m_overflowWarned;

    void OnEnable()
    {
        if (!m_built) Build();

        OwnershipManager.OnOwnershipChanged += Refresh;
        AlbumRewardManager.OnChanged += Refresh;

        Refresh();
    }

    void OnDisable()
    {
        OwnershipManager.OnOwnershipChanged -= Refresh;
        AlbumRewardManager.OnChanged -= Refresh;
    }

    // 더미 정리 1회 기준 — 빈 앨범 0셀도 정상이라 셀 빌드 성공과는 무관
    void Build()
    {
        m_built = true;

        // 배선 누락은 예외 없이 "0셀"로만 나타나 원인 추적이 어렵다 → 조용히 끝내지 않는다
        if (galleryContent == null || cellTemplate == null || pageOverlay == null)
            Debug.LogError($"[AlbumTabController] 배선 누락 — galleryContent={galleryContent}, cellTemplate={cellTemplate}, pageOverlay={pageOverlay}.", this);

        if (galleryContent == null || cellTemplate == null) return;

        // Destroy는 프레임 말 지연이라 먼저 꺼야 같은 프레임 레이아웃이 더미까지 읽지 않는다
        for (int t_i = galleryContent.childCount - 1; t_i >= 0; t_i--)
        {
            var t_child = galleryContent.GetChild(t_i).gameObject;
            if (t_child == cellTemplate.gameObject) continue;
            t_child.SetActive(false);
            Destroy(t_child);
        }
        cellTemplate.gameObject.SetActive(false);
    }

    void Refresh()
    {
        var t_themes = CardAlbum.Themes;

        if (galleryContent != null && cellTemplate != null)
        {
            while (m_cells.Count < t_themes.Count)
                m_cells.Add(Instantiate(cellTemplate, galleryContent));

            for (int t_i = 0; t_i < m_cells.Count; t_i++)
            {
                bool t_use = t_i < t_themes.Count;
                m_cells[t_i].gameObject.SetActive(t_use);
                if (t_use) m_cells[t_i].Bind(t_themes[t_i], OpenTheme);
            }
        }

        // GetAlbumInfo의 Owned/Total은 테마 수 기준이라 카드 진행 게이지엔 못 쓴다
        int t_owned = 0;
        int t_total = 0;
        for (int t_i = 0; t_i < t_themes.Count; t_i++)
        {
            t_owned += CardAlbum.OwnedCountOf(t_themes[t_i]);
            t_total += CardAlbum.TotalCountOf(t_themes[t_i]);
        }
        totalGauge.Set(t_owned, t_total);

        var t_info = AlbumRewardManager.GetAlbumInfo();
        albumChest.Bind(t_info, ClaimAlbumReward);

        BindRewardSlots(CardAlbum.AlbumRewards);
    }

    void OpenTheme(AlbumTheme _theme)
    {
        if (pageOverlay != null) pageOverlay.Open(_theme);
    }

    void BindRewardSlots(IReadOnlyList<AlbumRewardDef> _rewards)
    {
        if (rewardSlots == null) return;

        for (int t_i = 0; t_i < rewardSlots.Length; t_i++)
        {
            if (rewardSlots[t_i] == null) continue;

            if (t_i < _rewards.Count) rewardSlots[t_i].Bind(_rewards[t_i]);
            else rewardSlots[t_i].Hide();
        }

        if (_rewards.Count > rewardSlots.Length && !m_overflowWarned)
        {
            m_overflowWarned = true;
            Debug.LogWarning($"[AlbumTabController] 앨범 보상 {_rewards.Count}건이 슬롯 {rewardSlots.Length}칸을 초과 — 앞칸만 표시한다.", this);
        }
    }

    void ClaimAlbumReward()
    {
        var t_rewards = CardAlbum.AlbumRewards;   // Claim 전에 캡처
        if (!AlbumRewardManager.ClaimAlbum()) return;

        if (!CurrencyGainEffectPlayer.TryGet(this, out var t_player)) return;

        var t_bucket = new CurrencyGainBucket();
        for (int t_i = 0; t_i < t_rewards.Count; t_i++)
            t_bucket.Add(t_rewards[t_i].currency, t_rewards[t_i].amount);
        t_player.Play(albumChest.Rect, t_bucket);
    }
}
