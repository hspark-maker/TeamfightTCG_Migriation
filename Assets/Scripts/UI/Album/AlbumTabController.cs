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
    [Tooltip("갤러리 기본 셀 프리팹. 테마가 cellPrefab을 저작하면 그 테마만 교체된다.")]
    [SerializeField] AlbumThemeCellView cellTemplate;

    [Header("오버레이")]
    [SerializeField] AlbumPageOverlayView pageOverlay;

    readonly List<AlbumThemeCellView> m_cells = new List<AlbumThemeCellView>();
    // m_cells와 인덱스 정합 — 저작이 바뀌어 다른 프리팹이 되면 그 칸만 다시 만든다
    readonly List<AlbumThemeCellView> m_cellSources = new List<AlbumThemeCellView>();
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

        // 템플릿이 프리팹 에셋이면 끄지 않는다 — 에셋을 SetActive하면 프리팹 파일 자체가 변한다
        if (cellTemplate.gameObject.scene.IsValid()) cellTemplate.gameObject.SetActive(false);
    }

    void Refresh()
    {
        var t_themes = CardAlbum.Themes;

        if (galleryContent != null && cellTemplate != null)
        {
            for (int t_i = 0; t_i < t_themes.Count; t_i++)
            {
                var t_source = ResolveCellPrefab(t_themes[t_i]);

                if (t_i >= m_cells.Count)
                {
                    m_cells.Add(Instantiate(t_source, galleryContent));
                    m_cellSources.Add(t_source);
                }
                else if (m_cellSources[t_i] != t_source)
                {
                    Destroy(m_cells[t_i].gameObject);
                    m_cells[t_i] = Instantiate(t_source, galleryContent);
                    m_cellSources[t_i] = t_source;
                }

                // Instantiate는 항상 맨 뒤에 붙는다 — 교체된 셀이 그리드 순서를 잃지 않게 고정
                m_cells[t_i].transform.SetSiblingIndex(t_i);
                m_cells[t_i].gameObject.SetActive(true);
                m_cells[t_i].Bind(t_themes[t_i], OpenTheme);
            }

            for (int t_i = t_themes.Count; t_i < m_cells.Count; t_i++)
                m_cells[t_i].gameObject.SetActive(false);
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

    // 저작이 GameObject라 잘못된 프리팹도 꽂힐 수 있다 — 기본 셀로 떨어뜨리고 저작자에게 알린다
    AlbumThemeCellView ResolveCellPrefab(AlbumTheme _theme)
    {
        if (_theme.CellPrefab == null) return cellTemplate;

        var t_view = _theme.CellPrefab.GetComponent<AlbumThemeCellView>();
        if (t_view != null) return t_view;

        Debug.LogError($"[AlbumTabController] 테마 '{_theme.Key}'의 cellPrefab '{_theme.CellPrefab.name}'에 AlbumThemeCellView가 없다 — 기본 셀로 대체한다.", this);
        return cellTemplate;
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
