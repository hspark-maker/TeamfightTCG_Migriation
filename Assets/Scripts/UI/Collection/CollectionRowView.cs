using System.Collections.Generic;
using UnityEngine;

// 도감 한 행(카드 3장)의 타일 생성·바인딩 관리(Row 프리팹에 부착).
// 생산 상태(Status: 상태칩/진행바/수확버튼)는 이번 범위 밖 — 카드 타일만 채운다.
public class CollectionRowView : MonoBehaviour
{
    [SerializeField] Transform cardsContainer;      // 카드 타일 부모(HorizontalLayoutGroup)
    [SerializeField] CollectionCardView cardPrefab; // 카드 타일 프리팹

    readonly List<CollectionCardView> m_cards = new List<CollectionCardView>();

    // 행 데이터로 카드 타일을 (재)생성한다. 기존 컨테이너 자식(목업 하드코딩 포함)을 먼저 비운다.
    public void Build(CatalogRow _row)
    {
        ClearContainer();
        m_cards.Clear();
        if (_row == null || cardsContainer == null || cardPrefab == null) return;

        var t_cards = _row.Cards;
        for (int t_i = 0; t_i < t_cards.Count; t_i++)
        {
            var t_view = Instantiate(cardPrefab, cardsContainer);
            var t_card = t_cards[t_i];
            t_view.Bind(t_card, IsOwned(t_card));
            m_cards.Add(t_view);
        }
    }

    // 소유 상태만 재바인딩(타일 구조는 유지). 소유 변경 이벤트에서 호출.
    public void RefreshOwnership(CatalogRow _row)
    {
        if (_row == null) return;

        var t_cards = _row.Cards;
        for (int t_i = 0; t_i < m_cards.Count && t_i < t_cards.Count; t_i++)
        {
            var t_card = t_cards[t_i];
            if (m_cards[t_i] != null) m_cards[t_i].Bind(t_card, IsOwned(t_card));
        }
    }

    static bool IsOwned(CardData _card)
    {
        return _card != null && OwnershipManager.IsOwned(CardCatalog.KeyOf(_card));
    }

    void ClearContainer()
    {
        if (cardsContainer == null) return;
        for (int t_i = cardsContainer.childCount - 1; t_i >= 0; t_i--)
            Destroy(cardsContainer.GetChild(t_i).gameObject);
    }
}
