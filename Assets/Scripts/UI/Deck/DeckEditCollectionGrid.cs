using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 덱 편집 화면 하단의 컬렉션 그리드(ScrollView에 부착). 소유 카드만 3열로 나열한다.
// 도감(CollectionGridController)과 달리 미소유 카드는 아예 만들지 않는다 — 덱에 넣을 수 없는 카드라 자리만 차지한다.
//
// OnEnable에서 스스로 Build 하지 않는다. 유일한 트리거는 DeckEditController.Open()이다 —
// 타일의 "장착중 딤"은 현재 편집중인 덱 상태를 알아야 정해지는데, 그 상태를 아는 쪽은 컨트롤러뿐이라
// 여기서 먼저 그리면 딤 없는 한 프레임(혹은 영구 미갱신)이 생긴다.
public class DeckEditCollectionGrid : MonoBehaviour
{
    [SerializeField] ScrollRect       scrollRect;
    [SerializeField] RectTransform    content;     // GridLayoutGroup 3열
    [SerializeField] DeckEditCardTile tilePrefab;
    [SerializeField] GameObject       emptyHint;

    readonly List<DeckEditCardTile> m_tiles = new List<DeckEditCardTile>();

    GridLayoutGroup m_grid;

    public ScrollRect Scroll => scrollRect;

    // 드래그 고스트를 타일과 같은 크기로 띄우기 위한 값. 저작 시점 값을 쓸 수 없다 —
    // 매치 편집 패널은 GridRatioFitter가 cellSize를 컨테이너 폭에서 런타임에 다시 정한다.
    // 그리드를 못 찾으면 zero를 주고, 호출측이 자기 폴백을 쓰게 한다.
    public Vector2 CellSize
    {
        get
        {
            if (m_grid == null && content != null) m_grid = content.GetComponent<GridLayoutGroup>();

            return m_grid != null ? m_grid.cellSize : Vector2.zero;
        }
    }

    public void Build(Action<DeckEditCardTile, PointerEventData> _onDragRequest, Action<DeckEditCardTile> _onClick)
    {
        Clear();
        if (content == null || tilePrefab == null) return;

        if (!CardCatalog.IsReady)
        {
            Debug.LogError("[DeckEditCollectionGrid] CardCatalog 미초기화 — 부트(BootInstaller)를 거치지 않았다.");
            return;
        }

        var t_cards = CardCatalog.All;
        for (int t_i = 0; t_i < t_cards.Count; t_i++)
        {
            var t_card = t_cards[t_i];
            if (t_card == null) continue;                    // CardRegistry의 ID 보존용 빈 칸
            if (!OwnershipManager.IsOwned(t_card)) continue;  // 소유 카드만 편성 가능

            var t_tile = Instantiate(tilePrefab, content);
            t_tile.Bind(t_card, _onDragRequest, _onClick);
            m_tiles.Add(t_tile);
        }

        // 신규 유저는 소유 0으로 시작하므로(OwnershipManager.Init) 이 경로는 반드시 한 번은 탄다 — 옵션 취급 금지.
        if (emptyHint != null) emptyHint.SetActive(m_tiles.Count == 0);

        // 이전 편집 세션의 스크롤 위치가 남아 첫 화면이 중간부터 보이는 것을 막는다.
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
    }

    // _deck에 들어있는 카드 타일만 딤 처리. _deck이 null이면 전부 해제.
    public void RefreshInDeck(CardData[] _deck)
    {
        for (int t_i = 0; t_i < m_tiles.Count; t_i++)
        {
            var t_tile = m_tiles[t_i];
            if (t_tile == null) continue;

            t_tile.SetInDeck(_deck != null && Contains(_deck, t_tile.Card));
        }
    }

    public void Clear()
    {
        if (content != null)
        {
            // DeckListController.Build()와 동일한 순서: Destroy는 프레임 끝에 반영되므로
            // 먼저 SetActive(false)로 꺼야 이번 프레임 GridLayoutGroup 배치에 옛 타일이 끼지 않는다.
            for (int t_i = content.childCount - 1; t_i >= 0; t_i--)
            {
                var t_child = content.GetChild(t_i).gameObject;
                t_child.SetActive(false);
                Destroy(t_child);
            }
        }

        m_tiles.Clear();
    }

    static bool Contains(CardData[] _deck, CardData _card)
    {
        if (_card == null) return false;

        for (int t_i = 0; t_i < _deck.Length; t_i++)
            if (_deck[t_i] == _card) return true;

        return false;
    }
}
