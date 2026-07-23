using System.Collections.Generic;
using UnityEngine;

// 도감 갤러리 컨트롤러(CollectionScreen에 부착).
// CatalogRows로부터 행/카드 타일을 생성하고 소유상태(잠김)를 반영한다.
// 범위: 카드목록 연동만 — 골드 HUD·행 생산상태·수확·완성보상은 다음 패스.
public class CollectionGalleryController : MonoBehaviour
{
    [Header("배선")]
    [SerializeField] Transform content;            // 행이 세로로 쌓일 Content(VerticalLayoutGroup)
    [SerializeField] CollectionRowView rowPrefab;  // 행 프리팹

    [Header("독립 실행 부트스트랩 (테스트 씬 전용)")]
    [Tooltip("CardCatalog가 아직 주입 안 된 독립 씬에서만 사용. 실제 통합 시엔 부트가 이미 주입해 무시된다(4번째 마스터목록 아님).")]
    [SerializeField] List<CardData> fallbackAllCards = new List<CardData>();

    readonly List<CollectionRowView> m_rows = new List<CollectionRowView>();

    void OnEnable()
    {
        EnsureBoot();
        Build();
        OwnershipManager.OnOwnershipChanged += OnOwnershipChanged;
    }

    void OnDisable()
    {
        OwnershipManager.OnOwnershipChanged -= OnOwnershipChanged;
    }

    // 독립 실행 시 카탈로그/소유/생산 부트를 보장. 이미 준비됐으면(실제 통합) 아무것도 하지 않는다.
    void EnsureBoot()
    {
        if (CardCatalog.IsReady) return;

        CardCatalog.SetSource(fallbackAllCards);
        OwnershipManager.Init();
        CollectionProductionManager.Init();
    }

    // Content의 목업 하드코딩 행을 지우고 CatalogRows.Rows로 재생성.
    void Build()
    {
        ClearRows();
        if (content == null || rowPrefab == null) return;

        for (int t_i = content.childCount - 1; t_i >= 0; t_i--)
            Destroy(content.GetChild(t_i).gameObject);

        var t_rows = CatalogRows.Rows;
        for (int t_i = 0; t_i < t_rows.Count; t_i++)
        {
            var t_rowView = Instantiate(rowPrefab, content);
            t_rowView.Build(t_rows[t_i]);
            m_rows.Add(t_rowView);
        }
    }

    // 소유 변경 시 갱신. 행 수 그대로면 재바인딩만, 바뀌었으면 전체 재빌드.
    void OnOwnershipChanged()
    {
        var t_rows = CatalogRows.Rows;
        if (t_rows.Count != m_rows.Count) { Build(); return; }

        for (int t_i = 0; t_i < m_rows.Count; t_i++)
            if (m_rows[t_i] != null) m_rows[t_i].RefreshOwnership(t_rows[t_i]);
    }

    void ClearRows()
    {
        for (int t_i = 0; t_i < m_rows.Count; t_i++)
            if (m_rows[t_i] != null) Destroy(m_rows[t_i].gameObject);
        m_rows.Clear();
    }
}
