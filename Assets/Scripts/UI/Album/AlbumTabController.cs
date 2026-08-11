using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 카드 앨범 탭 표면(Tab_Collection_New 루트 부착) — 전체 보상 요약 + 테마 갤러리
public class AlbumTabController : MonoBehaviour
{
    [Header("전체 보상")]
    [SerializeField] CurrencyRewardSlotView[] rewardSlots;
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

    AlbumInsertSession m_insertSession;
    bool m_insertPending;   // 시작 코루틴이 떠 있는 동안의 중복 진입 방지(탭 활성화와 큐 충전이 같은 프레임에 겹친다)

    // 삽입 세션이 페이지 오버레이를 직접 몰아야 한다
    public AlbumPageOverlayView PageOverlay => pageOverlay;

    // 세션이 지정 페이지로 바로 여는 경로. 셀 콜백은 여전히 0페이지 진입이다
    public void OpenThemePage(AlbumTheme _theme, int _pageIndex)
    {
        if (pageOverlay != null) pageOverlay.Open(_theme, _pageIndex);
    }

    // 대기 중인 신규 카드가 있으면 삽입 연출을 시작한다. 탭이 꺼져 있으면 무연산 —
    // 세션이 설 수 있는 조건("탭이 켜져 있고 · 큐에 카드가 있고 · 돌고 있는 세션이 없다")의 단일 판정처라
    // 큐가 채워지는 순간(획득 연출 끝)과 탭이 켜지는 순간(유저 진입) 양쪽에서 이 하나를 부르면 된다
    public void TryBeginInsert()
    {
        if (!isActiveAndEnabled || m_insertPending) return;
        if (!AlbumInsertQueue.HasPending || AlbumInsertSession.IsRunning) return;

        // 안내 중에는 유저가 직접 테마를 열어야 한다 — 세션은 시작하면서 오버레이를 스스로 열기 때문에,
        // 여기서 막지 않으면 테마를 누르라는 스텝을 세션이 대신 해 버린다.
        if (OutgameTutorialRunner.IsRunning && (pageOverlay == null || !pageOverlay.gameObject.activeSelf)) return;

        var t_session = ResolveInsertSession();
        if (t_session == null)
        {
            // 위장이 남으면 그 카드가 도감에서 영영 빈 칸이다 — 연출을 못 하면 그냥 꽂는다
            Debug.LogError("[AlbumTabController] AlbumInsertSession을 찾지 못해 삽입 연출을 건너뛴다.", this);
            AlbumInsertQueue.Clear();
            AlbumInsertMask.Clear();
            return;
        }

        m_insertPending = true;
        StartCoroutine(BeginInsertNextFrame(t_session));
    }

    void OnEnable()
    {
        if (!m_built) Build();

        OwnershipManager.OnOwnershipChanged += Refresh;
        AlbumRewardManager.OnChanged += Refresh;
        AlbumInsertMask.OnChanged += Refresh;

        Refresh();

        // 유저가 직접 탭을 눌러 들어온 경우 — 획득 연출은 이미 큐만 채워두고 물러났다
        TryBeginInsert();
    }

    void OnDisable()
    {
        OwnershipManager.OnOwnershipChanged -= Refresh;
        AlbumRewardManager.OnChanged -= Refresh;
        AlbumInsertMask.OnChanged -= Refresh;

        // 비활성화가 시작 코루틴을 끊는다 — 플래그가 남으면 다음 진입에서 영영 시작하지 못한다
        m_insertPending = false;
    }

    // 탭이 켜진 그 프레임엔 그리드 cellSize가 아직 없다 — 양보 후 강제 갱신해야 세션이 슬롯 rect를 실측할 수 있다
    IEnumerator BeginInsertNextFrame(AlbumInsertSession _session)
    {
        yield return null;
        Canvas.ForceUpdateCanvases();

        m_insertPending = false;
        _session.Begin();
    }

    // 삽입 패널은 페이지 오버레이의 자식이고 평소엔 꺼져 있다
    AlbumInsertSession ResolveInsertSession()
    {
        if (m_insertSession != null) return m_insertSession;
        if (pageOverlay == null) return null;

        m_insertSession = pageOverlay.GetComponentInChildren<AlbumInsertSession>(true);
        return m_insertSession;
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

        // 안내는 아직 안 꽂은 카드가 있는 첫 테마 한 칸만 지목한다(앵커는 키당 1건)
        bool t_anchorTaken = false;

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

                bool t_target = !t_anchorTaken && AlbumInsertMask.HiddenCountIn(t_themes[t_i]) > 0;
                t_anchorTaken |= t_target;

                m_cells[t_i].Bind(t_themes[t_i], OpenTheme, t_target);
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
            t_owned -= AlbumInsertMask.HiddenCountIn(t_themes[t_i]);   // 아직 안 꽂은 몫은 화면에서 뺀다
            t_total += CardAlbum.TotalCountOf(t_themes[t_i]);
        }
        totalGauge.Set(t_owned, t_total);

        var t_info = AlbumRewardManager.GetAlbumInfo();
        albumChest.Bind(t_info, ClaimAlbumReward);

        BindRewardSlots(CardAlbum.AlbumRewards);
    }

    void OpenTheme(AlbumTheme _theme)
    {
        OpenThemePage(_theme, 0);

        // 안내 중이면 이 클릭이 세션의 출발 신호다 — 그전까지는 오버레이가 닫혀 있어 대기했다
        TryBeginInsert();
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

            if (t_i < _rewards.Count) rewardSlots[t_i].Bind(_rewards[t_i].icon, _rewards[t_i].amount);
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
        // 팝업을 띄우기 전에 막는다 — 지급은 [획득]에서 일어난다.
        if (!AlbumRewardManager.CanClaimAlbum()) return;

        AlbumRewardClaimFlow.Open("앨범 완성!",
                                  CardAlbum.AlbumRewards,
                                  () => AlbumRewardManager.ClaimAlbum());
    }
}
