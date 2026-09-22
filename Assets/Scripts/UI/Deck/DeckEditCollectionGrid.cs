using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 덱 편집 화면 하단의 컬렉션 그리드(ScrollView에 부착). 기본은 소유 카드만 3열로 나열한다.
// 강화 화면에서 재사용할 때는 Build 옵션으로 미소유 카드도 표시할 수 있다.
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
    [Tooltip("덱 편집 밖에서 재사용할 때는 덱 튜토리얼 앵커를 건드리지 않는다.")]
    [SerializeField] bool manageTutorialAnchor = true;
    [SerializeField, Range(0f, 1f)] float pickedCardDimAlpha = 0.2f;

    [Tooltip("빈 목록 안내 문구. 배선하면 소유 0과 검색 무결과를 다른 문구로 가른다(미배선이면 저작 문구 그대로).")]
    [SerializeField] TMP_Text emptyHintText;
    [SerializeField, TextArea] string emptyOwnedMessage  = "소지한 카드가 없습니다.\n카드팩을 열어 카드를 모아보세요.";
    [SerializeField, TextArea] string emptySearchMessage = "검색 결과가 없습니다.\n다른 이름으로 찾아보세요.";

    sealed class Entry
    {
        public int Card;
        public string Name;
        public RectTransform Slot;
        public DeckEditCardTile Tile;
        public bool Visible;
        public bool Wanted;
    }

    // 빈 RectTransform만 전체 배치하고 카드 그래픽은 화면과 여유 한 행에만 만든다.
    // GridRatioFitter/ContentSizeFitter의 저작값과 검색 레이아웃은 그대로 사용한다.
    readonly List<Entry> m_entries = new List<Entry>();
    readonly List<DeckEditCardTile> m_tiles = new List<DeckEditCardTile>();
    readonly Vector3[] m_corners = new Vector3[4];
    int m_entryCount;
    RectTransform m_poolRoot;
    Action<DeckEditCardTile, PointerEventData> m_onDragRequest;
    Action<DeckEditCardTile> m_onClick;
    int[] m_deck;
    SynergyData m_synergy;
    int m_pickedCard;
    bool m_includeUnowned;
    bool m_buildPending;
    bool m_dirty;
    Vector2 m_lastPosition;
    Vector2 m_lastViewportSize;
    Vector2 m_lastCellSize;

    // 걸려 있는 검색어(없으면 null). Build가 목록을 다시 만들어도 이 값은 살아남아 재적용된다.
    string m_filter;
    CardListFilter m_cardFilter = new CardListFilter();
    bool m_filterPending;
    bool m_resetFilterScroll;

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

    public void Build(Action<DeckEditCardTile, PointerEventData> _onDragRequest, Action<DeckEditCardTile> _onClick,
                      bool _includeUnowned = false)
    {
        m_onDragRequest = _onDragRequest;
        m_onClick = _onClick;
        m_includeUnowned = _includeUnowned;
        if (HasPressedTile())
        {
            m_buildPending = true;
            return;
        }
        m_buildPending = false;
        if (manageTutorialAnchor) TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.DeckEditCollectionCard);
        m_anchorCard = 0;
        if (content == null || tilePrefab == null)
        {
            Clear();
            return;
        }

        if (!CardCatalog.IsReady)
        {
            Clear();
            Debug.LogError("[DeckEditCollectionGrid] CardCatalog is not initialized — it did not go through initialization (InitializationRunner).");
            return;
        }

        ReleaseTiles();
        m_entryCount = 0;
        var t_cards = CardCatalog.AllIds;
        for (int t_i = 0; t_i < t_cards.Count; t_i++)
        {
            var t_card = t_cards[t_i];
            if (t_card <= 0) continue;
            if (!m_includeUnowned && !OwnershipManager.IsOwned(t_card)) continue;

            if (m_entryCount == m_entries.Count)
            {
                var t_slot = new GameObject("CardSlot", typeof(RectTransform));
                t_slot.transform.SetParent(content, false);
                m_entries.Add(new Entry { Slot = (RectTransform)t_slot.transform });
            }
            Entry t_entry = m_entries[m_entryCount++];
            t_entry.Card = t_card;
            t_entry.Name = NameOf(t_card);
        }

        for (int t_i = m_entryCount; t_i < m_entries.Count; t_i++)
            m_entries[t_i].Slot.gameObject.SetActive(false);

        // 소유가 바뀌어 다시 그려도 걸려 있던 검색어가 풀리지 않게 여기서 재적용한다.
        // 재적용을 호출측에 흩뿌리면 Build 호출처 두 곳 중 하나를 반드시 빠뜨린다.
        // 신규 유저는 소유 0으로 시작하므로(OwnershipManager.Init) 빈 목록 경로는 반드시 한 번은 탄다 — 옵션 취급 금지.
        ApplyFilter();

        // 이전 편집 세션의 스크롤 위치가 남아 첫 화면이 중간부터 보이는 것을 막는다.
        if (scrollRect != null)
        {
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 1f;
        }
    }

    /// <summary>카드 이름으로 목록을 거른다. 비우면(null·공백) 전부 다시 보인다.</summary>
    public void SetNameFilter(string _query)
    {
        string t_next = string.IsNullOrWhiteSpace(_query) ? null : _query.Trim().ToLowerInvariant();

        // 한글 입력은 조합 중에도 발화가 오므로 같은 값이 연달아 들어온다 — 여기서 끊으면 디바운스가 필요 없다.
        if (t_next == m_filter) return;

        m_filter = t_next;
        m_resetFilterScroll = true;
        ApplyFilter();
    }

    public void SetCardFilter(CardListFilter _filter)
    {
        m_cardFilter = _filter?.Clone() ?? new CardListFilter();
        m_resetFilterScroll = true;
        ApplyFilter();
    }

    // 이름 검색은 빈 슬롯 배치를 갱신하고 화면에 들어온 슬롯만 카드 타일을 확보한다.
    void ApplyFilter()
    {
        // 누른 타일의 위치와 수명은 포인터가 해제될 때까지 유지한다.
        if (HasPressedTile())
        {
            m_filterPending = true;
            return;
        }
        m_filterPending = false;
        int t_visible = 0;
        for (int t_i = 0; t_i < m_entryCount; t_i++)
        {
            Entry t_entry = m_entries[t_i];
            t_entry.Visible = t_entry.Card == m_anchorCard
                || ((m_filter == null || t_entry.Name.Contains(m_filter)) && m_cardFilter.Matches(t_entry.Card));
            t_entry.Slot.gameObject.SetActive(t_entry.Visible);
            if (t_entry.Visible) t_visible++;
        }

        if (emptyHint     != null) emptyHint.SetActive(t_visible == 0);
        if (emptyHintText != null) emptyHintText.text = m_cardFilter.IsActive
            ? "필터 조건에 맞는 카드가 없습니다.\n조건을 변경하거나 초기화해 주세요."
            : m_filter == null ? emptyOwnedMessage : emptySearchMessage;
        if (content != null) LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        if (m_resetFilterScroll && scrollRect != null)
        {
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 1f;
        }
        m_resetFilterScroll = false;
        m_dirty = true;
        RefreshVisible();
    }

    void LateUpdate()
    {
        if (m_buildPending && !HasPressedTile())
        {
            int t_anchor = m_anchorCard;
            Build(m_onDragRequest, m_onClick, m_includeUnowned);
            ApplyTutorialAnchor(t_anchor);
            if (t_anchor > 0) EnsureVisible(t_anchor);
        }
        if (m_filterPending && !HasPressedTile()) ApplyFilter();
        if (m_entryCount == 0 || content == null) return;
        if (m_dirty || m_lastPosition != content.anchoredPosition || m_lastViewportSize != ListArea.rect.size || m_lastCellSize != CellSize)
            RefreshVisible();
    }

    bool HasPressedTile()
    {
        for (int t_i = 0; t_i < m_tiles.Count; t_i++)
            if (m_tiles[t_i].HasActivePointer) return true;
        return false;
    }

    void RefreshVisible()
    {
        if (content == null || tilePrefab == null || !content.gameObject.activeInHierarchy) return;
        RectTransform t_viewport = ListArea;
        t_viewport.GetWorldCorners(m_corners);
        float t_bottom = float.MaxValue;
        float t_top = float.MinValue;
        for (int t_i = 0; t_i < m_corners.Length; t_i++)
        {
            float t_y = content.InverseTransformPoint(m_corners[t_i]).y;
            t_bottom = Mathf.Min(t_bottom, t_y);
            t_top = Mathf.Max(t_top, t_y);
        }
        float t_margin = CellSize.y + (m_grid != null ? Mathf.Abs(m_grid.spacing.y) : 0f);
        bool t_pinnedOutside = false;
        for (int t_i = 0; t_i < m_entryCount; t_i++)
        {
            Entry t_entry = m_entries[t_i];
            float t_y = t_entry.Slot.localPosition.y;
            bool t_inRange = t_entry.Visible && t_y + t_entry.Slot.rect.yMax >= t_bottom - t_margin
                                               && t_y + t_entry.Slot.rect.yMin <= t_top + t_margin;
            bool t_pressed = t_entry.Tile != null && t_entry.Tile.HasActivePointer;
            t_entry.Wanted = t_inRange || (t_entry.Visible && t_entry.Card == m_anchorCard) || t_pressed;
            t_pinnedOutside |= t_pressed && !t_inRange;
            if (!t_entry.Wanted && t_entry.Tile != null) ReleaseTile(t_entry);
        }
        for (int t_i = 0; t_i < m_entryCount; t_i++)
        {
            Entry t_entry = m_entries[t_i];
            if (t_entry.Wanted && t_entry.Tile == null) BindTile(t_entry);
        }
        m_lastPosition = content.anchoredPosition;
        m_lastViewportSize = t_viewport.rect.size;
        m_lastCellSize = CellSize;
        // 화면 밖에서 잡고 있던 타일은 손을 떼면 스크롤 없이도 반환한다.
        m_dirty = t_pinnedOutside;
    }

    void BindTile(Entry _entry)
    {
        DeckEditCardTile t_tile = null;
        for (int t_i = 0; t_i < m_tiles.Count; t_i++)
            if (m_tiles[t_i].Card == 0) { t_tile = m_tiles[t_i]; break; }
        if (t_tile == null)
        {
            t_tile = Instantiate(tilePrefab, _entry.Slot);
            m_tiles.Add(t_tile);
        }
        RectTransform t_rect = (RectTransform)t_tile.transform;
        t_rect.SetParent(_entry.Slot, false);
        t_rect.anchorMin = Vector2.zero;
        t_rect.anchorMax = Vector2.one;
        t_rect.offsetMin = Vector2.zero;
        t_rect.offsetMax = Vector2.zero;
        t_tile.Bind(_entry.Card, m_onDragRequest, m_onClick);
        t_tile.SetInDeck(m_deck != null && Contains(m_deck, _entry.Card));
        if (m_synergy != null) t_tile.SetFocus(true, SynergyPreview.Has(_entry.Card, m_synergy));
        else t_tile.SetFocus(m_pickedCard > 0, _entry.Card == m_pickedCard, pickedCardDimAlpha);
        _entry.Tile = t_tile;
        t_tile.gameObject.SetActive(true);
    }

    void ReleaseTile(Entry _entry)
    {
        DeckEditCardTile t_tile = _entry.Tile;
        _entry.Tile = null;
        t_tile.Unbind();
        t_tile.gameObject.SetActive(false);
        if (m_poolRoot == null)
        {
            m_poolRoot = (RectTransform)new GameObject("InactiveCardTiles", typeof(RectTransform)).transform;
            m_poolRoot.SetParent(content, false);
            m_poolRoot.gameObject.SetActive(false);
        }
        t_tile.transform.SetParent(m_poolRoot, false);
    }

    void ReleaseTiles()
    {
        for (int t_i = 0; t_i < m_entries.Count; t_i++)
            if (m_entries[t_i].Tile != null) ReleaseTile(m_entries[t_i]);
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
        m_deck = _deck;
        for (int t_i = 0; t_i < m_tiles.Count; t_i++)
        {
            var t_tile = m_tiles[t_i];
            if (t_tile == null || t_tile.Card <= 0) continue;

            t_tile.SetInDeck(_deck != null && Contains(_deck, t_tile.Card));
        }
    }

    // 시너지 아이콘 롱프레스 중 해당 시너지를 가진 타일만 남기고 나머지를 죽인다. null이면 전부 해제.
    public void SetSynergyFocus(SynergyData _synergy)
    {
        m_synergy = _synergy;
        m_pickedCard = 0;
        for (int t_i = 0; t_i < m_tiles.Count; t_i++)
        {
            var t_tile = m_tiles[t_i];
            if (t_tile == null || t_tile.Card <= 0) continue;

            t_tile.SetFocus(_synergy != null, SynergyPreview.Has(t_tile.Card, _synergy));
        }
    }

    /// <summary>슬롯 선택 모드에서 고른 카드 한 장만 남기고 나머지를 흐리게 한다. 0이면 전부 해제.</summary>
    // SetSynergyFocus와 같은 알파 축을 쓴다 — 두 강조는 배타라(컨트롤러가 보장) 서로 덮어써도 흐린 채 굳지 않는다.
    public void SetPickedCard(int _card)
    {
        m_pickedCard = _card;
        m_synergy = null;
        for (int t_i = 0; t_i < m_tiles.Count; t_i++)
        {
            var t_tile = m_tiles[t_i];
            if (t_tile == null || t_tile.Card <= 0) continue;

            t_tile.SetFocus(_card > 0, t_tile.Card == _card, pickedCardDimAlpha);
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
        if (!manageTutorialAnchor) return;
        // 검색어가 안내 대상을 숨기는 것을 막는다 — 컨트롤러의 입력 잠금과 별개로 두는 2차 방어다.
        if (m_anchorCard != _card)
        {
            m_anchorCard = _card;
            if (m_filter != null || m_cardFilter.IsActive) ApplyFilter();
        }

        Entry t_entry = FindEntry(_card);
        if (t_entry != null && t_entry.Visible && t_entry.Tile == null) BindTile(t_entry);
        m_dirty = true;
        var t_tile = t_entry != null ? t_entry.Tile : null;
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

        Entry t_entry = FindEntry(_card);
        if (t_entry == null || !t_entry.Visible) return;

        // 방금 Build한 타일은 아직 배치 전이라 좌표가 0이다.
        Canvas.ForceUpdateCanvases();

        var t_viewport = scrollRect.viewport != null ? scrollRect.viewport : scrollRect.transform as RectTransform;
        if (t_viewport == null) return;

        float t_range = content.rect.height - t_viewport.rect.height;
        if (t_range <= 0f)
        {
            scrollRect.verticalNormalizedPosition = 1f;
            RefreshVisible();
            return;
        }

        var t_rect = t_entry.Slot;
        if (t_rect == null) return;

        float t_offset = Mathf.Clamp(-t_rect.anchoredPosition.y - t_viewport.rect.height * 0.5f, 0f, t_range);

        scrollRect.StopMovement();
        scrollRect.verticalNormalizedPosition = 1f - t_offset / t_range;
        RefreshVisible();
    }

    Entry FindEntry(int _card)
    {
        if (_card <= 0) return null;
        for (int t_i = 0; t_i < m_entryCount; t_i++)
            if (m_entries[t_i].Card == _card) return m_entries[t_i];

        return null;
    }

    public void Clear()
    {
        if (manageTutorialAnchor) TutorialAnchorRegistry.Unregister(EOutgameTutorialAnchor.DeckEditCollectionCard);

        SetScrollLocked(false);
        ReleaseTiles();
        for (int t_i = 0; t_i < m_entries.Count; t_i++) m_entries[t_i].Slot.gameObject.SetActive(false);
        m_entryCount = 0;
        m_anchorCard = 0;
        m_buildPending = false;
        m_filterPending = false;
        m_resetFilterScroll = false;
        m_deck = null;
        m_synergy = null;
        m_pickedCard = 0;

        // m_filter는 남긴다 — 소유 변경 재빌드(DeckEditController.OnOwnershipChanged)에서 검색어가 풀리면 안 된다.
    }

    void OnDisable() => Clear();

    static bool Contains(int[] _deck, int _card)
    {
        if (_card <= 0) return false;

        for (int t_i = 0; t_i < _deck.Length; t_i++)
            if (_deck[t_i] == _card) return true;

        return false;
    }
}
