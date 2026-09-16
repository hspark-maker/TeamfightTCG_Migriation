using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>열어둔 컬렉션에 속한 카드의 검색·필터 결과 목록.</summary>
public sealed class CollectionFilterResults : ContentsUIBehaviour
{
    [SerializeField] Button closeButton;
    [SerializeField] Button filterButton;
    [SerializeField] TMP_InputField searchInput;
    [SerializeField] TMP_Text resultCountText;
    [SerializeField] TMP_Text emptyText;
    [SerializeField] ScrollRect scrollRect;
    [SerializeField] RectTransform gridContent;
    [SerializeField] CardVisualView cardTemplate;
    [SerializeField] GameObject templateRoot;

    static CollectionFilterResults s_openView;
    public static bool IsOpen => s_openView != null && s_openView.IsViewVisible;
    public event Action Closed;
    public static event Action OnAnyClosed;

    sealed class Entry
    {
        public RectTransform Slot;
        public CollectionResultCardInput Tile;
        public int Card;
        public bool Wanted;
    }

    readonly List<Entry> m_entries = new List<Entry>();
    readonly List<CollectionResultCardInput> m_tiles = new List<CollectionResultCardInput>();
    readonly Vector3[] m_corners = new Vector3[4];
    List<int> m_cards = new List<int>();
    CardListFilter m_filter = new CardListFilter();
    AlbumTheme m_theme;
    readonly List<int> m_scopeCards = new List<int>();
    UnityEngine.Object m_owner;
    RectTransform m_poolRoot;
    GridLayoutGroup m_grid;
    Vector2 m_lastPosition, m_lastSize, m_lastCellSize;
    int m_entryCount;
    bool m_rebuildPending, m_dirty;
    TMP_Text m_filterLabel;

    public void SetCollection(AlbumTheme theme)
    {
        if (ReferenceEquals(m_theme, theme)) return;
        Hide();
        m_theme = theme;
        m_filter.Clear();
        if (searchInput != null) searchInput.SetTextWithoutNotify(string.Empty);
        m_scopeCards.Clear();
        var seen = new HashSet<int>();
        if (theme == null) return;
        foreach (int card in theme.CardIds)
            if (CardCatalog.Contains(card) && seen.Add(card)) m_scopeCards.Add(card);
    }

    public void EditFilter(UnityEngine.Object owner, Action onApplied)
    {
        if (owner == null || m_theme == null || HasPressedTile() || CardDetailOverlayView.IsOpen) return;
        InitializeUI();
        m_owner = owner;
        if (searchInput != null) searchInput.DeactivateInputField();
        CardFilterPopup.Open(m_filter, true, searchInput != null ? searchInput.text : null, filter =>
        {
            if (this == null || m_owner == null || !gameObject.activeInHierarchy) return;
            m_filter = filter.Clone();
            if (!m_filter.IsActive && string.IsNullOrWhiteSpace(searchInput != null ? searchInput.text : null))
            {
                Hide();
                onApplied?.Invoke();
                return;
            }
            s_openView = this;
            SetContentsVisible(true);
            Rebuild();
            onApplied?.Invoke();
        }, this, m_scopeCards);
    }

    public void Hide()
    {
        CardFilterPopup.CloseFor(this);
        SetContentsVisible(false);
    }

    protected override void OnInitializeUI()
    {
        if (closeButton != null) closeButton.onClick.AddListener(Hide);
        if (filterButton != null)
        {
            filterButton.onClick.AddListener(OpenFilter);
            m_filterLabel = filterButton.GetComponentInChildren<TMP_Text>(true);
        }
        if (searchInput != null) searchInput.onValueChanged.AddListener(OnSearchChanged);
        if (gridContent != null) m_grid = gridContent.GetComponent<GridLayoutGroup>();
        if (templateRoot != null) templateRoot.SetActive(false);
    }

    protected override void OnViewShown()
    {
        OwnershipManager.OnOwnershipChanged += Rebuild;
        CardGrowthManager.OnGrowthChanged += Rebuild;
    }

    protected override void OnViewHidden()
    {
        OwnershipManager.OnOwnershipChanged -= Rebuild;
        CardGrowthManager.OnGrowthChanged -= Rebuild;
        CardFilterPopup.CloseFor(this);
        if (s_openView == this) s_openView = null;
        m_owner = null;
        m_rebuildPending = false;
        ReleaseTiles();
        Closed?.Invoke();
        OnAnyClosed?.Invoke();
    }

    void OpenFilter()
    {
        if (IsViewVisible) EditFilter(m_owner, null);
    }

    void OnSearchChanged(string query)
    {
        if (!m_filter.IsActive && string.IsNullOrWhiteSpace(query)) Hide();
        else Rebuild();
    }

    void Rebuild()
    {
        if (!IsViewVisible || gridContent == null || cardTemplate == null) return;
        if (HasPressedTile()) { m_rebuildPending = true; return; }
        m_rebuildPending = false;
        ReleaseTiles();
        // 상세 화면이 이전 목록을 참조하고 있을 수 있으므로 새 스냅샷으로 교체한다.
        var cards = new List<int>();
        string query = searchInput != null ? searchInput.text : null;
        foreach (int card in m_scopeCards)
            if (m_filter.Matches(card, query)) cards.Add(card);
        m_cards = cards;
        m_entryCount = cards.Count;
        for (int i = 0; i < m_entryCount; i++)
        {
            if (i == m_entries.Count)
            {
                var slot = new GameObject("CardSlot", typeof(RectTransform));
                slot.transform.SetParent(gridContent, false);
                m_entries.Add(new Entry { Slot = (RectTransform)slot.transform });
            }
            m_entries[i].Card = cards[i];
            m_entries[i].Slot.gameObject.SetActive(true);
        }
        for (int i = m_entryCount; i < m_entries.Count; i++)
            m_entries[i].Slot.gameObject.SetActive(false);
        if (resultCountText != null) resultCountText.text = $"카드 {m_entryCount:N0}장";
        if (emptyText != null)
        {
            emptyText.text = "조건에 맞는 카드가 없습니다.\n검색어나 필터를 변경해 주세요.";
            emptyText.gameObject.SetActive(m_entryCount == 0);
        }
        if (m_filterLabel != null) m_filterLabel.text = m_filter.IsActive ? "필터 적용 중" : "필터";
        if (scrollRect != null)
        {
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 1f;
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(gridContent);
        m_dirty = true;
    }

    bool HasPressedTile()
    {
        foreach (var tile in m_tiles)
            if (tile.HasActivePointer) return true;
        return false;
    }

    void LateUpdate()
    {
        if (!IsViewVisible) return;
        if (m_owner == null) { Hide(); return; }
        if (m_rebuildPending && !HasPressedTile()) Rebuild();
        if (scrollRect == null || gridContent == null || m_grid == null) return;
        var viewport = scrollRect.viewport != null ? scrollRect.viewport : (RectTransform)scrollRect.transform;
        if (m_dirty || m_lastPosition != gridContent.anchoredPosition
            || m_lastSize != viewport.rect.size || m_lastCellSize != m_grid.cellSize)
            RefreshVisible(viewport);
    }

    void RefreshVisible(RectTransform viewport)
    {
        viewport.GetWorldCorners(m_corners);
        float bottom = float.MaxValue, top = float.MinValue;
        foreach (var corner in m_corners)
        {
            float y = gridContent.InverseTransformPoint(corner).y;
            bottom = Mathf.Min(bottom, y);
            top = Mathf.Max(top, y);
        }
        float margin = m_grid.cellSize.y + Mathf.Abs(m_grid.spacing.y);
        bool pinned = false;
        for (int i = 0; i < m_entryCount; i++)
        {
            Entry entry = m_entries[i];
            float y = entry.Slot.localPosition.y;
            bool inRange = y + entry.Slot.rect.yMax >= bottom - margin
                && y + entry.Slot.rect.yMin <= top + margin;
            bool pressed = entry.Tile != null && entry.Tile.HasActivePointer;
            entry.Wanted = inRange || pressed;
            pinned |= pressed && !inRange;
            if (!entry.Wanted && entry.Tile != null) ReleaseTile(entry);
        }
        for (int i = 0; i < m_entryCount; i++)
        {
            Entry entry = m_entries[i];
            if (!entry.Wanted || entry.Tile != null) continue;
            CollectionResultCardInput tile = null;
            foreach (var candidate in m_tiles)
                if (candidate.Card == 0) { tile = candidate; break; }
            if (tile == null)
            {
                CardVisualView visual = Instantiate(cardTemplate, entry.Slot);
                tile = visual.gameObject.AddComponent<CollectionResultCardInput>();
                tile.Initialize(visual);
                m_tiles.Add(tile);
            }
            var rect = (RectTransform)tile.transform;
            rect.SetParent(entry.Slot, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            tile.gameObject.SetActive(true);
            tile.Bind(entry.Card, m_cards, i);
            entry.Tile = tile;
        }
        m_lastPosition = gridContent.anchoredPosition;
        m_lastSize = viewport.rect.size;
        m_lastCellSize = m_grid.cellSize;
        m_dirty = pinned;
    }

    void ReleaseTile(Entry entry)
    {
        var tile = entry.Tile;
        entry.Tile = null;
        tile.Unbind();
        tile.gameObject.SetActive(false);
        if (m_poolRoot == null)
        {
            m_poolRoot = (RectTransform)new GameObject("InactiveCardTiles", typeof(RectTransform)).transform;
            m_poolRoot.SetParent(gridContent, false);
            m_poolRoot.gameObject.SetActive(false);
        }
        tile.transform.SetParent(m_poolRoot, false);
    }

    void ReleaseTiles()
    {
        foreach (var entry in m_entries)
            if (entry.Tile != null) ReleaseTile(entry);
    }
}
