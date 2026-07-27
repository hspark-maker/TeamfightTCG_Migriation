using System.Collections.Generic;
using UnityEngine;

// 도감 그리드 컨트롤러(Panel_Grid의 Content에 부착).
// 전체 카드(CardCatalog.All)를 4열 그리드로 평면 나열하고 소유상태(잠김)를 반영한다.
// 생산/보상은 다루지 않는다(그 흐름은 CollectionGalleryController 담당). 오직 나열 + 소유표시.
// 소유 변경은 OnOwnershipChanged로 즉시 갱신한다(그리드는 시간 함수가 아니라 폴링 불필요).
public class CollectionGridController : MonoBehaviour
{
    [Header("배선")]
    [SerializeField] Transform content;             // 카드가 채워질 Content(GridLayoutGroup)
    [SerializeField] CollectionCardView cardPrefab;  // 카드 타일 프리팹(Card.prefab)

    [Header("독립 실행 부트스트랩 (테스트 씬 전용)")]
    [Tooltip("CardCatalog가 아직 주입 안 된 독립 씬에서만 사용. 실제 통합 시엔 부트가 이미 주입해 무시된다(마스터목록 아님).")]
    [SerializeField] List<CardData> fallbackAllCards = new List<CardData>();

    readonly List<CollectionCardView> m_tiles = new List<CollectionCardView>();

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

    // 독립 실행 시 카탈로그/소유 부트를 보장. 이미 준비됐으면(실제 통합) 아무것도 하지 않는다.
    void EnsureBoot()
    {
        if (CardCatalog.IsReady) return;

        DataSaveManager.Load();
        CardCatalog.SetSource(fallbackAllCards);
        OwnershipManager.Init();
    }

    // Content의 목업 하드코딩 타일을 지우고 CardCatalog.All로 재생성.
    void Build()
    {
        ClearTiles();
        if (content == null || cardPrefab == null) return;

        for (int t_i = content.childCount - 1; t_i >= 0; t_i--)
            Destroy(content.GetChild(t_i).gameObject);

        var t_cards = CardCatalog.All;
        for (int t_i = 0; t_i < t_cards.Count; t_i++)
        {
            var t_card = t_cards[t_i];
            if (t_card == null) continue;

            var t_tile = Instantiate(cardPrefab, content);
            t_tile.Bind(t_card, OwnershipManager.IsOwned(t_card));
            m_tiles.Add(t_tile);
        }
    }

    // 소유 변경 시 갱신. 카드 수 그대로면 재바인딩만, 바뀌었으면 전체 재빌드.
    void OnOwnershipChanged()
    {
        var t_cards = CardCatalog.All;
        int t_nonNull = 0;
        for (int t_i = 0; t_i < t_cards.Count; t_i++)
            if (t_cards[t_i] != null) t_nonNull++;

        if (t_nonNull != m_tiles.Count) { Build(); return; }

        int t_tileIdx = 0;
        for (int t_i = 0; t_i < t_cards.Count; t_i++)
        {
            var t_card = t_cards[t_i];
            if (t_card == null) continue;
            if (m_tiles[t_tileIdx] != null)
                m_tiles[t_tileIdx].Bind(t_card, OwnershipManager.IsOwned(t_card));
            t_tileIdx++;
        }
    }

    void ClearTiles()
    {
        for (int t_i = 0; t_i < m_tiles.Count; t_i++)
            if (m_tiles[t_i] != null) Destroy(m_tiles[t_i].gameObject);
        m_tiles.Clear();
    }
}
