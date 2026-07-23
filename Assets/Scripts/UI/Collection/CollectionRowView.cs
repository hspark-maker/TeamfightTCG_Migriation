using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 도감 한 행(카드 3장)의 타일 생성·바인딩 + 생산 상태 표시(Row 프리팹에 부착).
// 카드 타일(cardsContainer)에 더해 상태칩·누적량·수확버튼을 CollectionProductionManager API로 채운다.
// 생산 누적값은 시간 함수라 매니저가 통지하지 않으므로, 컨트롤러의 폴링 틱이 RefreshProduction()을 주기 호출한다.
public class CollectionRowView : MonoBehaviour
{
    [SerializeField] Transform cardsContainer;      // 카드 타일 부모(HorizontalLayoutGroup)
    [SerializeField] CollectionCardView cardPrefab; // 카드 타일 프리팹

    [Header("생산 상태(선택 — 미배선 시 null 가드)")]
    [SerializeField] TMP_Text stateLabel;           // 상태칩: 잠김/생산 중/만땅
    [SerializeField] TMP_Text amountText;           // 누적/상한 표시(예: "12 / 100")
    [SerializeField] Button harvestButton;          // 수확 버튼

    readonly List<CollectionCardView> m_cards = new List<CollectionCardView>();

    // 행 안정 키(생산 조회·수확의 식별자). Build에서 저장, RefreshProduction/OnHarvestClicked가 사용.
    string m_rowKey;

    // 행 데이터로 카드 타일을 (재)생성한다. 기존 컨테이너 자식(목업 하드코딩 포함)을 먼저 비운다.
    public void Build(CatalogRow _row)
    {
        ClearContainer();
        m_cards.Clear();

        m_rowKey = _row != null ? _row.Key : null;

        // 수확 버튼 리스너 1회 배선(재빌드마다 중복 등록 방지).
        if (harvestButton != null)
        {
            harvestButton.onClick.RemoveAllListeners();
            harvestButton.onClick.AddListener(OnHarvestClicked);
        }

        if (_row != null && cardsContainer != null && cardPrefab != null)
        {
            var t_cards = _row.Cards;
            for (int t_i = 0; t_i < t_cards.Count; t_i++)
            {
                var t_view = Instantiate(cardPrefab, cardsContainer);
                var t_card = t_cards[t_i];
                t_view.Bind(t_card, IsOwned(t_card));
                m_cards.Add(t_view);
            }
        }

        RefreshProduction();
    }

    // 소유 상태만 재바인딩(타일 구조는 유지). 소유 변경 이벤트에서 호출.
    // 소유가 바뀌면 행 완성 여부→생산 상태도 바뀌므로 생산 표시도 함께 갱신한다.
    public void RefreshOwnership(CatalogRow _row)
    {
        if (_row == null) return;

        var t_cards = _row.Cards;
        for (int t_i = 0; t_i < m_cards.Count && t_i < t_cards.Count; t_i++)
        {
            var t_card = t_cards[t_i];
            if (m_cards[t_i] != null) m_cards[t_i].Bind(t_card, IsOwned(t_card));
        }

        RefreshProduction();
    }

    // 생산 상태 표시 갱신. 시간 누적을 폴링하므로 컨트롤러 틱에서 주기 호출된다.
    // rowKey가 없으면(드리프트 미해결 행) 아무것도 하지 않는다.
    public void RefreshProduction()
    {
        if (string.IsNullOrEmpty(m_rowKey)) return;

        var t_info = CollectionProductionManager.GetInfo(m_rowKey);

        switch (t_info.State)
        {
            case EProductionState.Locked:
                if (stateLabel != null) stateLabel.text = "잠김";
                if (amountText != null) amountText.text = string.Empty;
                break;

            case EProductionState.Producing:
                if (stateLabel != null) stateLabel.text = "생산 중";
                if (amountText != null)
                    amountText.text = $"{t_info.Accumulated:N0} / {t_info.Cap:N0}";
                break;

            case EProductionState.Capped:
                if (stateLabel != null) stateLabel.text = "만땅";
                if (amountText != null)
                    amountText.text = $"{t_info.Cap:N0} / {t_info.Cap:N0}";
                break;
        }

        // 수확 버튼: 굳은 누적이 1 이상일 때만 활성(잠김이어도 굳은 누적은 청구 가능).
        if (harvestButton != null) harvestButton.interactable = t_info.CanHarvest;
    }

    // 수확 클릭 → 매니저에 위임. 지급·영속·통지는 매니저가 처리하고 OnChanged로 컨트롤러가 전체 갱신한다.
    // 즉시성을 위해 자기 행은 여기서 한 번 더 갱신.
    void OnHarvestClicked()
    {
        if (string.IsNullOrEmpty(m_rowKey)) return;

        CollectionProductionManager.Harvest(m_rowKey);
        RefreshProduction();
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
