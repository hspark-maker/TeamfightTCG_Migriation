using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 도감 갤러리 컨트롤러(CollectionScreen에 부착).
// CatalogRows로부터 행/카드 타일을 생성하고 소유상태(잠김)·생산상태를 반영한다.
// 생산 누적은 시간 함수라 매니저가 통지하지 않으므로, 열린 동안 폴링 틱으로 각 행 + 푸터 일괄수령 버튼을 주기 갱신한다.
// 수확/리셋은 OnChanged로, 소유변경은 OnOwnershipChanged로 즉시 갱신한다.
public class CollectionGalleryController : MonoBehaviour
{
    [Header("배선")]
    [SerializeField] Transform content;            // 행이 세로로 쌓일 Content(VerticalLayoutGroup)
    [SerializeField] CollectionRowView rowPrefab;  // 행 프리팹
    [SerializeField] Button harvestAllButton;      // 푸터 일괄 수령 버튼(선택 — 미배선 시 null 가드)

    [Header("생산 표시 폴링")]
    [Tooltip("열린 동안 각 행 생산상태(누적량)를 이 간격(초)마다 갱신. 시간 누적은 이벤트가 없어 폴링이 필요하다.")]
    [SerializeField] float refreshInterval = 0.5f;

    [Header("독립 실행 부트스트랩 (테스트 씬 전용)")]
    [Tooltip("CardCatalog가 아직 주입 안 된 독립 씬에서만 사용. 실제 통합 시엔 부트가 이미 주입해 무시된다(4번째 마스터목록 아님).")]
    [SerializeField] List<CardData> fallbackAllCards = new List<CardData>();

    readonly List<CollectionRowView> m_rows = new List<CollectionRowView>();

    // 폴링 누적 타이머(열린 동안만 누산).
    float m_refreshTimer;

    void OnEnable()
    {
        EnsureBoot();
        Build();

        // 재활성마다 중복 등록 방지.
        if (harvestAllButton != null)
        {
            harvestAllButton.onClick.RemoveAllListeners();
            harvestAllButton.onClick.AddListener(OnHarvestAllClicked);
        }

        OwnershipManager.OnOwnershipChanged += OnOwnershipChanged;
        CollectionProductionManager.OnChanged += OnProductionChanged;
        m_refreshTimer = 0f;
        RefreshProduction();
    }

    void OnDisable()
    {
        OwnershipManager.OnOwnershipChanged -= OnOwnershipChanged;
        CollectionProductionManager.OnChanged -= OnProductionChanged;
    }

    // 폴링 틱: 열린 동안 refreshInterval마다 생산 표시를 갱신(시간 누적 반영).
    void Update()
    {
        m_refreshTimer += Time.deltaTime;
        if (m_refreshTimer < refreshInterval) return;
        m_refreshTimer = 0f;

        RefreshProduction();
    }

    // 수확/리셋 시 매니저가 통지 — 즉시 갱신.
    void OnProductionChanged()
    {
        RefreshProduction();
    }

    // 각 행 생산 표시 + 일괄수령 버튼 활성(수확 가능 총량이 1 이상일 때만).
    void RefreshProduction()
    {
        for (int t_i = 0; t_i < m_rows.Count; t_i++)
            if (m_rows[t_i] != null) m_rows[t_i].RefreshProduction();

        if (harvestAllButton != null)
            harvestAllButton.interactable = CollectionProductionManager.GetTotalHarvestable() >= 1;
    }

    // 일괄 수령 클릭 → 매니저에 위임. 지급·영속·통지는 매니저가 처리하고 OnChanged가 갱신을 유발한다.
    void OnHarvestAllClicked()
    {
        CollectionProductionManager.HarvestAll();
    }

    // 독립 실행 시 카탈로그/소유/생산 부트를 보장. 이미 준비됐으면(실제 통합) 아무것도 하지 않는다.
    void EnsureBoot()
    {
        if (CardCatalog.IsReady) return;

        DataSaveManager.Load();      // 저장된 진행도·재화 로드(독립 씬에서도 영속 검증 가능)
        CardCatalog.SetSource(fallbackAllCards);
        OwnershipManager.Init();
        CurrencyManager.Init();      // 저장된 골드로 HUD 초기값 맞춤
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
