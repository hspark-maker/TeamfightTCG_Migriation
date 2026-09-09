using System;
using System.Collections.Generic;
using TMPro;
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

    [Tooltip("빈 목록 안내 문구. 배선하면 소유 0과 검색 무결과를 다른 문구로 가른다(미배선이면 저작 문구 그대로).")]
    [SerializeField] TMP_Text emptyHintText;
    [SerializeField, TextArea] string emptyOwnedMessage  = "소지한 카드가 없습니다.\n카드팩을 열어 카드를 모아보세요.";
    [SerializeField, TextArea] string emptySearchMessage = "검색 결과가 없습니다.\n다른 이름으로 찾아보세요.";

    readonly List<DeckEditCardTile> m_tiles = new List<DeckEditCardTile>();

    // m_tiles와 같은 순서로 카드 이름을 소문자로 눕혀 들고 있는다 — 타이핑 한 글자마다 스펙을 다시 조회하지 않으려는 캐시다.
    readonly List<string> m_tileNames = new List<string>();

    // 걸려 있는 검색어(없으면 null). Build가 목록을 다시 만들어도 이 값은 살아남아 재적용된다.
    string m_filter;

    // 튜토리얼이 지목한 카드. 검색어가 무엇이든 이 카드만은 목록에서 숨기지 않는다.
    int m_anchorCard;

    GridLayoutGroup m_grid;

    // 스크롤 잠금 전 축 설정. 잠금은 겹쳐 걸리지 않는다(m_scrollLocked) — 잠긴 상태에서 또 저장하면
    // 꺼둔 값(false)을 원래 값으로 기억해 영영 못 푼다.
    bool m_scrollLocked;
    bool m_scrollWasVertical;
    bool m_scrollWasHorizontal;

    public ScrollRect Scroll => scrollRect;

    /// <summary>목록이 차지한 화면 영역. 카드를 목록으로 돌려보내는 연출이 겨누는 지점이다 —
    /// 개별 타일은 스크롤 밖에 있을 수 있어(뷰포트 마스크에 잘린다) 목적지로 쓸 수 없다.</summary>
    public RectTransform ListArea => scrollRect != null && scrollRect.viewport != null
                                     ? scrollRect.viewport
                                     : (RectTransform)transform;

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
            Debug.LogError("[DeckEditCollectionGrid] CardCatalog is not initialized — it did not go through initialization (InitializationRunner).");
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
            m_tileNames.Add(NameOf(t_card));
        }

        // 소유가 바뀌어 다시 그려도 걸려 있던 검색어가 풀리지 않게 여기서 재적용한다.
        // 재적용을 호출측에 흩뿌리면 Build 호출처 두 곳 중 하나를 반드시 빠뜨린다.
        // 신규 유저는 소유 0으로 시작하므로(OwnershipManager.Init) 빈 목록 경로는 반드시 한 번은 탄다 — 옵션 취급 금지.
        ApplyFilter();

        // 이전 편집 세션의 스크롤 위치가 남아 첫 화면이 중간부터 보이는 것을 막는다.
        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
    }

    /// <summary>카드 이름으로 목록을 거른다. 비우면(null·공백) 전부 다시 보인다.</summary>
    public void SetNameFilter(string _query)
    {
        string t_next = string.IsNullOrWhiteSpace(_query) ? null : _query.Trim().ToLowerInvariant();

        // 한글 입력은 조합 중에도 발화가 오므로 같은 값이 연달아 들어온다 — 여기서 끊으면 디바운스가 필요 없다.
        if (t_next == m_filter) return;

        m_filter = t_next;
        ApplyFilter();

        if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
    }

    // 타일을 다시 만들지 않고 표시 여부만 바꾼다 — GridLayoutGroup이 비활성 자식을 배치에서 빼 주므로 재배치는 공짜다.
    void ApplyFilter()
    {
        int t_visible = 0;
        for (int t_i = 0; t_i < m_tiles.Count; t_i++)
        {
            var t_tile = m_tiles[t_i];
            if (t_tile == null) continue;

            bool t_on = m_filter == null
                     || (m_anchorCard > 0 && t_tile.Card == m_anchorCard)
                     || m_tileNames[t_i].Contains(m_filter);

            t_tile.gameObject.SetActive(t_on);
            if (t_on) t_visible++;
        }

        if (emptyHint     != null) emptyHint.SetActive(t_visible == 0);
        if (emptyHintText != null) emptyHintText.text = m_filter == null ? emptyOwnedMessage : emptySearchMessage;
    }

    static string NameOf(int _card)
    {
        return CardCatalog.TryGetSpec(_card, out var t_spec) && !string.IsNullOrEmpty(t_spec.DisplayName)
               ? t_spec.DisplayName.ToLowerInvariant()
               : string.Empty;
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

            t_tile.SetFocus(_synergy != null, SynergyPreview.Has(t_tile.Card, _synergy));
        }
    }

    /// <summary>슬롯 선택 모드에서 고른 카드 한 장만 남기고 나머지를 흐리게 한다. 0이면 전부 해제.</summary>
    // SetSynergyFocus와 같은 알파 축을 쓴다 — 두 강조는 배타라(컨트롤러가 보장) 서로 덮어써도 흐린 채 굳지 않는다.
    public void SetPickedCard(int _card)
    {
        for (int t_i = 0; t_i < m_tiles.Count; t_i++)
        {
            var t_tile = m_tiles[t_i];
            if (t_tile == null) continue;

            t_tile.SetFocus(_card > 0, t_tile.Card == _card);
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
        // 검색어가 안내 대상을 숨기는 것을 막는다 — 컨트롤러의 입력 잠금과 별개로 두는 2차 방어다.
        if (m_anchorCard != _card)
        {
            m_anchorCard = _card;
            if (m_filter != null) ApplyFilter();
        }

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
        m_tileNames.Clear();
        m_anchorCard = 0;

        // m_filter는 남긴다 — 소유 변경 재빌드(DeckEditController.OnOwnershipChanged)에서 검색어가 풀리면 안 된다.
    }

    static bool Contains(int[] _deck, int _card)
    {
        if (_card <= 0) return false;

        for (int t_i = 0; t_i < _deck.Length; t_i++)
            if (_deck[t_i] == _card) return true;

        return false;
    }
}
