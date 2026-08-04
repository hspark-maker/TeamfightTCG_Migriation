using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// 도감 갤러리 컨트롤러(CollectionScreen에 부착).
// CatalogRows로부터 행/카드 타일을 생성하고 소유상태(잠김)·생산상태를 반영한다.
// 생산 누적은 시간 함수라 매니저가 통지하지 않으므로, 열린 동안 폴링 틱으로 각 행 + 푸터 일괄수령 버튼을 주기 갱신한다.
// 수확/리셋은 OnChanged로, 소유·강화 변경은 OnOwnershipChanged/OnGrowthChanged로 즉시 갱신한다.
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

    // 생성된 행 뷰가 각각 물고 있는 행 데이터(m_rows와 인덱스 정합). 비정상 행을 건너뛰면 원본 목록과 길이가 달라지므로
    // 소유 갱신 때 CatalogRows.Rows를 인덱스로 다시 찍지 않고 이쪽을 쓴다.
    readonly List<CatalogRow> m_rowData = new List<CatalogRow>();

    // 마지막 빌드 시점의 원본 행 수(재빌드 필요 판정용).
    int m_sourceRowCount;

    // 전 행을 행 순서대로 이어붙인 평면 목록(상세 오버레이의 좌우 넘기기 순서 = 화면에 보이는 순서).
    // authoring이 빈 null 슬롯도 그대로 담는다 — 오버레이가 넘길 때 건너뛰므로 여기서 걸러 인덱스를 어긋내지 않는다.
    // 오버레이가 참조로 들고 있으므로 재빌드 때도 인스턴스를 갈아치우지 않고 내용만 다시 채운다.
    readonly List<CardData> m_flat = new List<CardData>();

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

        OwnershipManager.OnOwnershipChanged += RebindRows;
        // 강화도 행 타일의 체력 표시를 바꾼다 → 소유 변경과 같은 재바인딩 경로를 탄다.
        CardGrowthManager.OnGrowthChanged += RebindRows;
        CollectionProductionManager.OnChanged += OnProductionChanged;
        m_refreshTimer = 0f;
        RefreshProduction();
    }

    void OnDisable()
    {
        OwnershipManager.OnOwnershipChanged -= RebindRows;
        CardGrowthManager.OnGrowthChanged -= RebindRows;
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

        // 잠금 변화는 이 폴링이 따라잡는다(0.5초) — 별도 구독 없이도 어긋난 채로 남지 않는다.
        if (harvestAllButton != null)
            harvestAllButton.interactable = CollectionProductionManager.GetTotalHarvestable() >= 1
                                         && OutgameFeatureLock.IsUnlocked(EOutgameFeature.CollectionHarvest);
    }

    // 일괄 수령 클릭 → 매니저에 위임. 지급·영속·통지는 매니저가 처리하고 OnChanged가 갱신을 유발한다.
    // 총합을 골드로 취급한다 — 현재 배치는 전 행이 골드고, 섞이더라도 롤업 시작점만 낮아질 뿐 끝값은 실잔액을 다시 읽는다.
    void OnHarvestAllClicked()
    {
        long t_earned = CollectionProductionManager.HarvestAll();
        if (t_earned <= 0 || harvestAllButton == null) return;

        if (GoldGainEffectPlayer.TryGet(this, out var t_player))
            t_player.Play((RectTransform)harvestAllButton.transform, t_earned);
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
        m_sourceRowCount = t_rows.Count;

        // 평면 목록은 행을 만들기 전에 완성해야 한다 — 행이 배선하는 시점에 뒤쪽 행 카드까지 이미 들어 있어야
        // 마지막 행에서도 "다음"이 성립한다(목록을 참조로 넘기므로 이후 추가는 반영되지만, 순서 계산은 지금 기준이다).
        // 비정상 행(null 또는 Cards null)은 여기서도, 아래 생성 루프에서도 **같은 기준으로** 건너뛴다 —
        // 한쪽만 건너뛰면 평면 목록 오프셋이 한 행씩 밀린다.
        for (int t_i = 0; t_i < t_rows.Count; t_i++)
        {
            var t_cards = RowCards(t_rows[t_i]);
            if (t_cards == null) continue;

            for (int t_c = 0; t_c < t_cards.Count; t_c++)
                m_flat.Add(t_cards[t_c]);
        }

        int t_offset = 0;
        for (int t_i = 0; t_i < t_rows.Count; t_i++)
        {
            var t_row   = t_rows[t_i];
            var t_cards = RowCards(t_row);
            if (t_cards == null) continue;

            var t_rowView = Instantiate(rowPrefab, content);
            t_rowView.Build(t_row, m_flat, t_offset);
            m_rows.Add(t_rowView);
            m_rowData.Add(t_row);

            t_offset += t_cards.Count;
        }
    }

    static IReadOnlyList<CardData> RowCards(CatalogRow _row)
    {
        return _row != null ? _row.Cards : null;
    }

    // 소유·강화 변경 시 갱신. 원본 행 수가 그대로면 재바인딩만, 바뀌었으면 전체 재빌드.
    // 비교 기준은 m_rows.Count가 아니라 빌드 당시의 원본 행 수다 — 건너뛴 행이 있으면 둘이 애초에 다르다.
    void RebindRows()
    {
        if (CatalogRows.Rows.Count != m_sourceRowCount) { Build(); return; }

        // 뷰가 실제로 물고 있는 행을 그대로 다시 넘긴다(건너뛴 행이 있어도 짝이 어긋나지 않는다).
        for (int t_i = 0; t_i < m_rows.Count; t_i++)
            if (m_rows[t_i] != null) m_rows[t_i].RefreshOwnership(m_rowData[t_i]);
    }

    void ClearRows()
    {
        for (int t_i = 0; t_i < m_rows.Count; t_i++)
            if (m_rows[t_i] != null) Destroy(m_rows[t_i].gameObject);
        m_rows.Clear();
        m_rowData.Clear();
        m_flat.Clear();
    }
}
