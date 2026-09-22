using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>보유 칭호 장착과 미보유 칭호 미리보기를 제공한다.</summary>
public sealed class ProfileTitleTab : MonoBehaviour
{
    [SerializeField] GameObject previewRoot;
    [SerializeField] Image previewIcon;
    [SerializeField] TMP_Text previewName;
    [SerializeField] TMP_Text previewDescription;
    [SerializeField] RectTransform content;
    [SerializeField] TitleItemCell cellPrefab;
    [SerializeField] ScrollRect scrollRect;
    [SerializeField] TMP_Text emptyText;

    readonly List<TitleItemCell> m_cells = new List<TitleItemCell>();
    string m_selectedId;
    string m_draftTitleId;
    System.Action m_onSelectionChanged;
    TitleCatalog m_builtCatalog;

    bool m_visible;

    /// <summary>저장 전 칭호 변경 여부.</summary>
    public bool IsDirty => this.m_draftTitleId != TitleManager.EquippedId;

    /// <summary>프로필 편집 세션의 칭호 선택을 준비한다.</summary>
    public void BeginEdit(System.Action _onSelectionChanged)
    {
        this.m_draftTitleId = TitleManager.EquippedId;
        this.m_onSelectionChanged = _onSelectionChanged;
    }

    /// <summary>프로필 확정 시 선택한 칭호를 저장한다.</summary>
    public void Commit()
    {
        if (this.IsDirty) TitleManager.TryEquip(this.m_draftTitleId);
    }

    /// <summary>프로필 편집 안에서 칭호 목록을 표시한다.</summary>
    public void Show()
    {
        if (this.m_visible) return;
        this.Build();
        this.m_selectedId = this.m_draftTitleId;
        if (string.IsNullOrEmpty(this.m_selectedId) && this.m_cells.Count > 0)
            this.m_selectedId = this.m_cells[0].Id;
        this.m_visible = true;
        TitleManager.OnChanged += this.Refresh;
        this.gameObject.SetActive(true);
        if (this.previewRoot != null) this.previewRoot.SetActive(true);
        this.Refresh();
        if (this.scrollRect != null)
        {
            this.scrollRect.StopMovement();
            this.scrollRect.verticalNormalizedPosition = 1f;
        }
    }

    /// <summary>탭 전환과 편집창 닫기에서 표시 구독을 해제한다.</summary>
    public void Hide()
    {
        this.Unsubscribe();
        this.gameObject.SetActive(false);
    }

    void OnDisable() => this.Unsubscribe();
    void OnDestroy() => this.Unsubscribe();

    void Unsubscribe()
    {
        TitleManager.OnChanged -= this.Refresh;
        this.m_visible = false;
        if (this.previewRoot != null) this.previewRoot.SetActive(false);
    }

    void Build()
    {
        if (this.cellPrefab != null) this.cellPrefab.gameObject.SetActive(false);
        TitleCatalog t_catalog = TitleManager.Catalog;
        if (this.m_builtCatalog == t_catalog) return;
        foreach (TitleItemCell t_cell in this.m_cells)
        {
            t_cell.gameObject.SetActive(false);
            Destroy(t_cell.gameObject);
        }
        this.m_cells.Clear();
        this.m_builtCatalog = t_catalog;
        if (t_catalog == null || this.cellPrefab == null || this.content == null) return;
        var t_ids = new HashSet<string>();
        foreach (TitleEntry t_entry in t_catalog.Entries)
        {
            if (t_entry == null || string.IsNullOrEmpty(t_entry.id) || !t_ids.Add(t_entry.id)) continue;
            TitleItemCell t_cell = Instantiate(this.cellPrefab, this.content);
            t_cell.Bind(t_entry, this.Select);
            t_cell.gameObject.SetActive(true);
            this.m_cells.Add(t_cell);
        }
    }

    void Select(string _id)
    {
        if (!this.m_visible) return;
        this.m_selectedId = _id;
        if (TitleManager.CanEquip(_id))
        {
            this.m_draftTitleId = _id == this.m_draftTitleId ? string.Empty : _id;
            this.m_onSelectionChanged?.Invoke();
        }
        this.Refresh();
    }

    void Refresh()
    {
        TitleEntry t_entry = null;
        bool t_known = TitleManager.Catalog != null &&
            TitleManager.Catalog.TryGet(this.m_selectedId, out t_entry);
        if (this.previewName != null)
        {
            this.previewName.text = t_known ? t_entry.displayName : string.Empty;
        }
        if (this.previewIcon != null)
        {
            this.previewIcon.sprite = t_known ? t_entry.icon : null;
            this.previewIcon.enabled = t_known && t_entry.icon != null;
        }
        if (this.previewDescription != null)
            this.previewDescription.text = t_known
                ? TitleUnlocks.Description(t_entry.id, t_entry.description) : string.Empty;
        if (this.emptyText != null) this.emptyText.gameObject.SetActive(this.m_cells.Count == 0);
        foreach (TitleItemCell t_cell in this.m_cells)
            t_cell.Refresh(t_cell.Id == this.m_selectedId, t_cell.Id == this.m_draftTitleId,
                TitleManager.CanEquip(t_cell.Id));
    }
}
