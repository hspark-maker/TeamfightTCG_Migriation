using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 도감 테마 아코디언 목록(Panel_Themes에 부착). 한 번에 한 행만 펼친다.
// 행 생성은 최초 1회 — 재빌드하면 스크롤 위치가 초기화된다.
public class CollectionThemeListController : MonoBehaviour
{
    [Header("배선")]
    [SerializeField] ScrollRect scrollRect;
    [SerializeField] Transform  content;                  // 행이 세로로 쌓일 Content(VerticalLayoutGroup + ContentSizeFitter)
    [SerializeField] CollectionThemeRowView rowPrefab;
    [SerializeField] GameObject emptyNotice;              // 선택 — 테마 0개 안내

    [Header("동작")]
    [SerializeField] bool scrollToExpandedRow = true;

    readonly List<CollectionThemeRowView> m_rows = new List<CollectionThemeRowView>();

    // 펼침 상태의 단일 진실원(행은 사본을 들지 않는다).
    int m_expandedIndex = -1;

    bool m_built;

    void CollapseAll()
    {
        m_expandedIndex = -1;

        for (int t_i = 0; t_i < m_rows.Count; t_i++)
            if (m_rows[t_i] != null) m_rows[t_i].SetExpanded(false);
    }

    void RefreshProgress()
    {
        for (int t_i = 0; t_i < m_rows.Count; t_i++)
            if (m_rows[t_i] != null) m_rows[t_i].RefreshProgress();
    }

    void OnEnable()
    {
        if (!m_built) Build();

        OwnershipManager.OnOwnershipChanged += OnCollectionChanged;
        // 강화도 슬롯의 체력 표시를 바꾼다 → 소유 변경과 같은 재바인딩 경로를 탄다.
        CardGrowthManager.OnGrowthChanged += OnCollectionChanged;

        // 탭이 꺼져 있는 동안 팩을 깠을 수 있다.
        OnCollectionChanged();
    }

    void OnDisable()
    {
        OwnershipManager.OnOwnershipChanged -= OnCollectionChanged;
        CardGrowthManager.OnGrowthChanged -= OnCollectionChanged;
    }

    // 테마를 건너뛰지 않는다 — 행 인덱스와 CollectionTheme.Index가 어긋나면 헤더 클릭이 남의 행을 편다.
    void Build()
    {
        m_rows.Clear();
        if (content == null || rowPrefab == null) return;

        // 목업 행 제거. Destroy는 프레임 말 지연이라 먼저 꺼야 같은 프레임의 ScrollToRow가 잔여 높이를 읽지 않는다.
        for (int t_i = content.childCount - 1; t_i >= 0; t_i--)
        {
            var t_mock = content.GetChild(t_i).gameObject;
            t_mock.SetActive(false);
            Destroy(t_mock);
        }

        var t_themes = CollectionThemes.Themes;
        int t_count  = t_themes.Count;

        for (int t_i = 0; t_i < t_count; t_i++)
        {
            var t_row = Instantiate(rowPrefab, content);
            t_row.Bind(t_themes[t_i], OnHeaderClicked);
            m_rows.Add(t_row);
        }

        if (emptyNotice != null) emptyNotice.SetActive(t_count == 0);

        // 프리팹 저작 상태와 무관하게 전부 접힌 상태에서 시작한다.
        CollapseAll();

        // 행이 하나도 안 나왔으면(테마 SO 미배선) 다음 활성화에서 다시 시도한다 — 빈 목록으로 세션 내내 고착되지 않게.
        m_built = t_count > 0;
    }

    // 같은 헤더 재탭 = 접기, 다른 헤더 = 이전 행을 접고 새 행을 펼침.
    // _themeIndex는 CollectionTheme.Index(= 목록 순서)라 m_rows 인덱스와 같다.
    void OnHeaderClicked(int _themeIndex)
    {
        if (_themeIndex < 0 || _themeIndex >= m_rows.Count) return;

        int t_next = _themeIndex == m_expandedIndex ? -1 : _themeIndex;

        for (int t_i = 0; t_i < m_rows.Count; t_i++)
            if (m_rows[t_i] != null) m_rows[t_i].SetExpanded(t_i == t_next);

        m_expandedIndex = t_next;

        if (t_next >= 0 && scrollToExpandedRow) ScrollToRow(t_next);
    }

    void OnCollectionChanged()
    {
        RefreshProgress();
        RefreshOwnership();
    }

    void RefreshOwnership()
    {
        for (int t_i = 0; t_i < m_rows.Count; t_i++)
            if (m_rows[t_i] != null) m_rows[t_i].RefreshOwnership();
    }

    // 중첩 ContentSizeFitter라 안쪽(행)→바깥(Content) 순으로 강제 리빌드해야 방금 펼친 높이가 반영된다.
    void ScrollToRow(int _index)
    {
        if (scrollRect == null || _index < 0 || _index >= m_rows.Count) return;
        if (m_rows[_index] == null) return;

        if (!(content is RectTransform t_content)) return;
        if (!(m_rows[_index].transform is RectTransform t_row)) return;

        RectTransform t_viewport = scrollRect.viewport != null ? scrollRect.viewport
                                                              : scrollRect.transform as RectTransform;
        if (t_viewport == null) return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(t_row);
        LayoutRebuilder.ForceRebuildLayoutImmediate(t_content);

        // 위에서 아래로 쌓이는 저작(Content pivot y=1) 기준이라 행의 anchoredPosition.y가 음수다.
        // pivot 조합이 다르면 부호가 반전될 수 있다(실기 1회 검증 예정).
        float t_max = Mathf.Max(0f, t_content.rect.height - t_viewport.rect.height);
        float t_y   = Mathf.Clamp(-t_row.anchoredPosition.y, 0f, t_max);

        t_content.anchoredPosition = new Vector2(t_content.anchoredPosition.x, t_y);
    }
}
