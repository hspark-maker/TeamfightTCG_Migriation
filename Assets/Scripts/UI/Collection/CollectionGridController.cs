using System.Collections.Generic;
using UnityEngine;

// 도감 그리드 컨트롤러(Panel_Grid의 Content에 부착).
// 전체 카드(CardCatalog.All)를 4열 그리드로 평면 나열한다.
//
// 한 칸은 카드가 아니라 **슬롯**이다 — 미소유 칸도 번호가 적힌 빈 자리로 남는다.
// 그래야 도감 전체 크기와 "이 카드가 앉을 자리"가 처음부터 보이고, 카드를 얻으면 제자리에 채워진다.
// 칸 안의 표현(빈 자리 ↔ 카드)은 CollectionThemeSlotView가 정본이며 테마 행과 같은 슬롯 프리팹을 쓴다 —
// 여기서 카드 프리팹을 직접 깔면 같은 도감인데 그리드와 테마 행의 빈 칸 모양이 갈라진다.
// 이 컨트롤러가 정하는 건 "몇 번 칸에 어떤 카드"뿐이다.
//
// 생산/보상은 다루지 않는다(그 흐름은 CollectionGalleryController 담당). 오직 나열 + 소유표시.
// 소유·강화 변경은 OnOwnershipChanged/OnGrowthChanged로 즉시 갱신한다(그리드는 시간 함수가 아니라 폴링 불필요).
public class CollectionGridController : MonoBehaviour
{
    [Header("배선")]
    [SerializeField] Transform content;                  // 슬롯이 채워질 Content(GridLayoutGroup)
    [SerializeField] CollectionThemeSlotView slotPrefab;  // 도감 한 칸 프리팹(CollectionSlot.prefab)

    [Header("독립 실행 부트스트랩 (테스트 씬 전용)")]
    [Tooltip("CardCatalog가 아직 주입 안 된 독립 씬에서만 사용. 실제 통합 시엔 부트가 이미 주입해 무시된다(마스터목록 아님).")]
    [SerializeField] List<CardData> fallbackAllCards = new List<CardData>();

    readonly List<CollectionThemeSlotView> m_slots = new List<CollectionThemeSlotView>();

    // 상세 오버레이가 좌우로 넘겨볼 순서. m_slots와 인덱스 1:1(같은 루프에서 같이 채운다).
    // 오버레이가 참조로 들고 있으므로 재빌드 때도 이 List 인스턴스를 갈아치우지 않고 내용만 비우고 다시 채운다.
    readonly List<CardData> m_order = new List<CardData>();

    void OnEnable()
    {
        EnsureBoot();
        Build();
        OwnershipManager.OnOwnershipChanged += Rebind;
        // 강화도 타일의 체력 표시를 바꾼다 → 소유 변경과 같은 재바인딩 경로를 탄다.
        CardGrowthManager.OnGrowthChanged += Rebind;
    }

    void OnDisable()
    {
        OwnershipManager.OnOwnershipChanged -= Rebind;
        CardGrowthManager.OnGrowthChanged -= Rebind;
    }

    // 독립 실행 시 카탈로그/소유 부트를 보장. 이미 준비됐으면(실제 통합) 아무것도 하지 않는다.
    void EnsureBoot()
    {
        if (CardCatalog.IsReady) return;

        // 폴백이 비었으면 손대지 않는다 — SetSource는 빈 목록으로도 IsReady를 켜기 때문에,
        // 부트보다 먼저 이 화면이 열리면 카탈로그가 "준비된 빈 목록"으로 굳어 도감이 0칸이 된다.
        if (fallbackAllCards == null || fallbackAllCards.Count == 0) return;

        DataSaveManager.Load();
        CardCatalog.SetSource(fallbackAllCards);
        OwnershipManager.Init();
    }

    // Content의 목업 하드코딩 칸을 지우고 CardCatalog.All로 재생성.
    void Build()
    {
        ClearSlots();

        // 배선 누락·빈 카탈로그는 예외 없이 "칸 0개"로만 나타나 원인 추적이 어렵다 → 조용히 끝내지 않는다.
        if (content == null || slotPrefab == null)
        {
            Debug.LogError($"[CollectionGridController] 배선 누락 — content={content}, slotPrefab={slotPrefab}. 도감 그리드를 만들지 않는다.", this);
            return;
        }
        if (CardCatalog.Count == 0)
            Debug.LogWarning("[CollectionGridController] CardCatalog가 비어 도감이 0칸이다 — 부트(BootInstaller)보다 이 화면이 먼저 열렸는지 확인할 것.", this);

        // Destroy는 프레임 말 지연이라 먼저 꺼야 같은 프레임의 레이아웃이 목업까지 더한 높이를 읽지 않는다.
        for (int t_i = content.childCount - 1; t_i >= 0; t_i--)
        {
            var t_mock = content.GetChild(t_i).gameObject;
            t_mock.SetActive(false);
            Destroy(t_mock);
        }

        var t_cards = CardCatalog.All;
        for (int t_i = 0; t_i < t_cards.Count; t_i++)
        {
            var t_card = t_cards[t_i];
            if (t_card == null) continue;

            var t_slot = Instantiate(slotPrefab, content);
            // 칸 번호는 화면에 깔린 순번(1부터)이다 — 카탈로그 인덱스를 쓰면 중간에 null이 섞였을 때 번호가 건너뛴다.
            t_slot.Bind(t_card, OwnershipManager.IsOwned(t_card), m_slots.Count + 1);

            // 길게 누르면 상세 오버레이. 칸과 카드의 짝은 재빌드 전까지 고정이라 여기서만 배선하면 된다
            // (소유·강화 변경 시 도는 Rebind는 같은 칸에 같은 카드를 다시 Bind할 뿐이다).
            // 넘겨볼 순서는 화면에 깔리는 순서와 같아야 하므로 칸을 만든 그 자리에서 목록에 넣는다.
            m_order.Add(t_card);
            CardDetailOverlayView.BindTile(t_slot.CardView, m_order, m_order.Count - 1);
            m_slots.Add(t_slot);
        }
    }

    // 소유·강화 변경 시 갱신. 카드 수 그대로면 재바인딩만, 바뀌었으면 전체 재빌드.
    void Rebind()
    {
        var t_cards = CardCatalog.All;
        int t_nonNull = 0;
        for (int t_i = 0; t_i < t_cards.Count; t_i++)
            if (t_cards[t_i] != null) t_nonNull++;

        if (t_nonNull != m_slots.Count) { Build(); return; }

        int t_slotIdx = 0;
        for (int t_i = 0; t_i < t_cards.Count; t_i++)
        {
            var t_card = t_cards[t_i];
            if (t_card == null) continue;
            if (m_slots[t_slotIdx] != null)
                m_slots[t_slotIdx].Bind(t_card, OwnershipManager.IsOwned(t_card), t_slotIdx + 1);
            t_slotIdx++;
        }
    }

    void ClearSlots()
    {
        for (int t_i = 0; t_i < m_slots.Count; t_i++)
            if (m_slots[t_i] != null) Destroy(m_slots[t_i].gameObject);
        m_slots.Clear();
        m_order.Clear();
    }
}
