using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 덱 편집 화면 하단의 컬렉션 그리드(ScrollView에 부착). 소유 카드만 3열로 나열한다.
// 도감과 달리 미소유 카드는 아예 만들지 않는다 — 덱에 넣을 수 없는 카드라 자리만 차지한다.
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

    // 스크롤 잠금 전 축 설정. 잠금은 겹쳐 걸리지 않는다(m_scrollLocked) — 잠긴 상태에서 또 저장하면
    // 꺼둔 값(false)을 원래 값으로 기억해 영영 못 푼다.
    bool m_scrollLocked;
    bool m_scrollWasVertical;
    bool m_scrollWasHorizontal;

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
            Debug.LogError("[DeckEditCollectionGrid] CardCatalog 미초기화 — 초기화(부트 초기화(InitializationRunner))를 거치지 않았다.");
            return;
        }

        var t_cards = CardCatalog.AllIds;
        for (int t_i = 0; t_i < t_cards.Count; t_i++)
        {
            var t_card = t_cards[t_i];
            if (t_card <= 0) continue;
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
    public void RefreshInDeck(int[] _deck)
    {
        for (int t_i = 0; t_i < m_tiles.Count; t_i++)
        {
            var t_tile = m_tiles[t_i];
            if (t_tile == null) continue;

            t_tile.SetInDeck(_deck != null && Contains(_deck, t_tile.Card));
        }
    }

    // 시너지 아이콘 롱프레스 중 해당 시너지를 가진 타일만 남기고 나머지를 죽인다. null이면 전부 해제.
    public void SetSynergyFocus(SynergyData _synergy)
    {
        for (int t_i = 0; t_i < m_tiles.Count; t_i++)
        {
            var t_tile = m_tiles[t_i];
            if (t_tile == null) continue;

            t_tile.SetSynergyFocus(_synergy != null, SynergyPreview.Has(t_tile.Card, _synergy));
        }
    }

    // 시너지 설명창이 떠 있는 동안 목록을 세운다. 아이콘을 누른 손가락이 위아래로 움직여도
    // 그 이동이 목록 스크롤로 흘러가면, 강조해서 보라고 띄운 화면이 그대로 흘러가 버린다.
    //
    // ScrollRect를 통째로 끄지 않는 이유: 드래그 도중 비활성화되면 OnEndDrag를 못 받아
    // 내부 드래그 상태가 켜진 채 남고 다음 터치에서 관성이 튄다. 축만 닫으면 이벤트는 정상적으로 끝난다.
    public void SetScrollLocked(bool _on)
    {
        if (scrollRect == null || _on == m_scrollLocked) return;

        m_scrollLocked = _on;

        if (_on)
        {
            m_scrollWasVertical   = scrollRect.vertical;
            m_scrollWasHorizontal = scrollRect.horizontal;
            scrollRect.vertical   = false;
            scrollRect.horizontal = false;
        }
        else
        {
            scrollRect.vertical   = m_scrollWasVertical;
            scrollRect.horizontal = m_scrollWasHorizontal;
        }

        // 잠글 때는 굴러가던 관성을 끊고, 풀 때는 잠긴 동안 쌓인 이동량이 튀어나오지 않게 한다.
        scrollRect.StopMovement();
        scrollRect.velocity = Vector2.zero;
    }

    /// <summary>튜토리얼이 지목한 카드의 타일에만 앵커를 건다(null이면 해제).
    /// 타일이 런타임 생성이라 프리팹에 TutorialAnchor를 저작할 수 없다 — AlbumCardSlotView와 같은 관용구다.</summary>
    public void ApplyTutorialAnchor(int _card)
    {
        var t_tile = _card > 0 ? FindTile(_card) : null;
        if (t_tile == null)
        {
            TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.DeckEditCollectionCard);
            return;
        }

        // 타일에는 Button이 없다(DeckEditCardTile이 IPointerClickHandler로 직접 받는다) —
        // 이 스텝의 완료는 클릭이 아니라 장착 신호라 누를 대상 없이 강조만으로 충분하다.
        TutorialAnchorRegistry.Register(EOutgameTutorialAnchor.DeckEditCollectionCard,
                                        t_tile.transform as RectTransform, null);
    }

    /// <summary>지목된 타일이 뷰포트 안에 오도록 스크롤한다. 게이트가 타깃을 승격하면 RectMask2D 클리핑이 끊겨
    /// 목록 밖 카드가 화면에 그대로 새므로, 가리키기 전에 반드시 안으로 들여놔야 한다.</summary>
    public void EnsureVisible(int _card)
    {
        if (scrollRect == null || content == null || _card <= 0) return;

        var t_tile = FindTile(_card);
        if (t_tile == null) return;

        // 방금 Build한 타일은 아직 배치 전이라 좌표가 0이다.
        Canvas.ForceUpdateCanvases();

        var t_viewport = scrollRect.viewport != null ? scrollRect.viewport : scrollRect.transform as RectTransform;
        if (t_viewport == null) return;

        float t_range = content.rect.height - t_viewport.rect.height;
        if (t_range <= 0f)
        {
            scrollRect.verticalNormalizedPosition = 1f;
            return;
        }

        var t_rect = t_tile.transform as RectTransform;
        if (t_rect == null) return;

        float t_offset = Mathf.Clamp(-t_rect.anchoredPosition.y - t_viewport.rect.height * 0.5f, 0f, t_range);

        scrollRect.StopMovement();
        scrollRect.verticalNormalizedPosition = 1f - t_offset / t_range;
    }

    DeckEditCardTile FindTile(int _card)
    {
        for (int t_i = 0; t_i < m_tiles.Count; t_i++)
            if (m_tiles[t_i] != null && m_tiles[t_i].Card == _card) return m_tiles[t_i];

        return null;
    }

    public void Clear()
    {
        TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.DeckEditCollectionCard);

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

    static bool Contains(int[] _deck, int _card)
    {
        if (_card <= 0) return false;

        for (int t_i = 0; t_i < _deck.Length; t_i++)
            if (_deck[t_i] == _card) return true;

        return false;
    }
}
