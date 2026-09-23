using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>덱과 컬렉션이 공유하는 카드 필터. 적용 전 편집은 원본 조건과 분리한다.</summary>
public sealed class CardFilterPopup : PooledOverlay<CardFilterPopup>
{
    [SerializeField] Button backdropButton;
    [SerializeField] Button closeButton;
    [SerializeField] Button resetButton;
    [SerializeField] Button applyButton;
    [SerializeField] TMP_Text resultCountText;
    [SerializeField] TMP_Text hintText;
    [SerializeField] Button includeLockedButton;
    [SerializeField] TMP_Text includeLockedText;
    [SerializeField] Image includeLockedTrack;
    [SerializeField] RectTransform includeLockedKnob;
    [SerializeField] ScrollRect scroll;
    [SerializeField] RectTransform optionContent;
    [SerializeField] Button optionTemplate;
    [SerializeField] TMP_Text sectionTemplate;
    [SerializeField] RectTransform sectionPanelTemplate;
    [SerializeField] Color selectedColor = new Color32(212, 255, 206, 255);
    [SerializeField] Color unselectedColor = Color.white;
    [SerializeField] Color selectedTextColor = new Color32(60, 23, 0, 255);
    [SerializeField] Color unselectedTextColor = new Color32(60, 23, 0, 255);
    [SerializeField] Color lockedOffColor = new Color32(212, 181, 137, 255);
    [SerializeField] Color lockedOnColor = new Color32(236, 220, 197, 255);

    const int OptionColumns = 2;
    const float OptionStartX = 248f;
    const float OptionGap = 16f;
    const float OptionHeight = 92f;

    sealed class Option
    {
        public Button Button;
        public TMP_Text Label;
        public string Caption;
        public Func<bool> IsSelected;
    }

    static CardFilterPopup s_openView;
    readonly List<GameObject> m_generated = new List<GameObject>();
    readonly List<Option> m_options = new List<Option>();
    CardListFilter m_edit;
    UnityEngine.Object m_owner;
    Action<CardListFilter> m_onApply;
    bool m_collectionMode;
    string m_query;
    HashSet<int> m_cardScope;
    float m_y;
    int m_column;
    RectTransform m_sectionPanel;
    TMP_Text m_sectionHeader;
    float m_sectionY;
    Vector2 m_lockedOffTextPosition;

    protected override int SortingOrder => UiSortingOrder.CardFilter;

    public static bool Open(CardListFilter current, bool collectionMode, string nameQuery,
        Action<CardListFilter> onApply, UnityEngine.Object owner, IEnumerable<int> cardScope = null)
    {
        if (owner == null || !TryGetOrCreate(out CardFilterPopup popup)) return false;
        if (s_openView != null) s_openView.Hide();
        popup.m_edit = current?.Clone() ?? new CardListFilter();
        popup.m_collectionMode = collectionMode;
        popup.m_cardScope = cardScope != null ? new HashSet<int>(cardScope) : null;
        if (!collectionMode) popup.m_edit.Ownership = CardOwnershipFilter.All;
        popup.m_query = nameQuery;
        popup.m_owner = owner;
        popup.m_onApply = onApply;
        s_openView = popup;
        MarkOpen();
        popup.SetContentsVisible(true);
        Canvas.ForceUpdateCanvases();
        popup.BuildOptions();
        popup.RefreshSelection();
        popup.scroll.verticalNormalizedPosition = 1f;
        return true;
    }

    public static void CloseFor(UnityEngine.Object owner)
    {
        if (s_openView != null && s_openView.m_owner == owner) s_openView.Hide();
    }

    protected override void OnInitializeUI()
    {
        backdropButton.onClick.AddListener(Hide);
        closeButton.onClick.AddListener(Hide);
        resetButton.onClick.AddListener(ResetFilter);
        applyButton.onClick.AddListener(ApplyFilter);
        includeLockedButton.onClick.AddListener(ToggleIncludeLocked);
        m_lockedOffTextPosition = includeLockedText.rectTransform.anchoredPosition;
        optionTemplate.gameObject.SetActive(false);
        sectionTemplate.gameObject.SetActive(false);
        sectionPanelTemplate.gameObject.SetActive(false);
    }

    protected override void OnViewHidden()
    {
        OwnershipManager.OnOwnershipChanged -= RefreshSelection;
        CardGrowthManager.OnGrowthChanged -= RefreshSelection;
        m_owner = null;
        m_onApply = null;
        m_cardScope = null;
        if (s_openView == this) s_openView = null;
        NotifyClosed(ConsumeOpen());
    }

    protected override void OnViewShown()
    {
        OwnershipManager.OnOwnershipChanged += RefreshSelection;
        CardGrowthManager.OnGrowthChanged += RefreshSelection;
    }

    void Update()
    {
        if (!IsViewVisible) return;
        if (m_owner == null || m_owner is Behaviour behaviour && !behaviour.isActiveAndEnabled ||
            m_owner is ContentsPooledUI pooled && !pooled.isShow ||
            m_owner is GameObject ownerObject && !ownerObject.activeInHierarchy)
            Hide();
    }

    void ResetFilter()
    {
        m_edit.Clear();
        RefreshSelection();
    }

    void ToggleIncludeLocked()
    {
        m_edit.IncludeLockedAbilities = !m_edit.IncludeLockedAbilities;
        RefreshSelection();
    }

    void ApplyFilter()
    {
        if (m_owner == null) { Hide(); return; }
        UnityEngine.Object owner = m_owner;
        Action<CardListFilter> callback = m_onApply;
        CardListFilter result = m_edit.Clone();
        Hide();
        if (owner != null) callback?.Invoke(result);
    }

    void BuildOptions()
    {
        foreach (GameObject generated in m_generated)
        {
            generated.SetActive(false);
            Destroy(generated);
        }
        m_generated.Clear();
        m_options.Clear();
        m_y = 0f;
        m_column = 0;

        if (m_collectionMode)
        {
            AddSection("보유 상태");
            AddOwnership(CardOwnershipFilter.All, "전체");
            AddOwnership(CardOwnershipFilter.Owned, "보유");
            AddOwnership(CardOwnershipFilter.Unowned, "미보유");
        }

        AddSection("등급");
        var grades = new SortedSet<ECardGrade>();
        CardKeyword availableKeywords = CardKeyword.None;
        var synergies = new SortedDictionary<string, SynergyData>(StringComparer.Ordinal);
        foreach (CardSpec spec in CardCatalog.AllSpecs)
        {
            if (m_cardScope != null && !m_cardScope.Contains(spec.Id)) continue;
            if (spec.Grade != ECardGrade.Unknown) grades.Add(spec.Grade);
            availableKeywords |= spec.Keywords;
            foreach (SynergyData synergy in CardCatalog.RequireSynergies(spec.Id))
                if (synergy != null) synergies[synergy.SynergyId] = synergy;
        }
        foreach (ECardGrade grade in grades)
        {
            ECardGrade value = grade;
            AddOption(GradeName(value), () => m_edit.Grades.Contains(value),
                () => Toggle(m_edit.Grades, value));
        }

        AddSection("키워드");
        foreach (CardKeyword keyword in Enum.GetValues(typeof(CardKeyword)))
        {
            if (keyword == CardKeyword.None || (availableKeywords & keyword) == 0) continue;
            CardKeyword value = keyword;
            AddOption(KeywordName(value), () => m_edit.Keywords.Contains(value),
                () => Toggle(m_edit.Keywords, value),
                DataLibrary.instance != null ? DataLibrary.instance.keywordIconConfig?.GetIcon(value) : null);
        }

        AddSection("시너지");
        foreach (KeyValuePair<string, SynergyData> pair in synergies)
        {
            string value = pair.Key;
            AddOption(SynergyText.Name(pair.Value), () => m_edit.SynergyIds.Contains(value),
                () => Toggle(m_edit.SynergyIds, value), pair.Value.activeIcon);
        }
        FinishSection();
        optionContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, m_y);
    }

    void AddOwnership(CardOwnershipFilter value, string label)
        => AddOption(label, () => m_edit.Ownership == value, () => m_edit.Ownership = value);

    void AddSection(string caption)
    {
        FinishSection();
        m_sectionY = m_y;
        m_sectionPanel = Instantiate(sectionPanelTemplate, optionContent);
        m_sectionPanel.gameObject.SetActive(true);
        Place(m_sectionPanel, 0f, m_y, optionContent.rect.width, 0f);
        m_generated.Add(m_sectionPanel.gameObject);
        m_sectionHeader = Instantiate(sectionTemplate, optionContent);
        m_sectionHeader.text = caption;
        m_sectionHeader.gameObject.SetActive(true);
        m_generated.Add(m_sectionHeader.gameObject);
        m_y += 24f;
    }

    void FinishSection()
    {
        if (m_sectionPanel == null) return;
        FinishRow();
        float height = Mathf.Max(OptionHeight + 48f, m_y - m_sectionY + 8f);
        m_sectionPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        Place(m_sectionHeader.rectTransform, 48f, m_sectionY + (height - 66f) * 0.5f, 184f, 66f);
        m_y = m_sectionY + height + 24f;
        m_sectionPanel = null;
        m_sectionHeader = null;
    }

    void AddOption(string caption, Func<bool> isSelected, Action toggle, Sprite iconSprite = null)
    {
        Button button = Instantiate(optionTemplate, optionContent);
        button.gameObject.SetActive(true);
        float width = (optionContent.rect.width - OptionStartX - 24f - OptionGap) / OptionColumns;
        Place((RectTransform)button.transform, OptionStartX + m_column * (width + OptionGap),
            m_y, width, OptionHeight);
        var option = new Option
        {
            Button = button,
            Label = button.GetComponentInChildren<TMP_Text>(true),
            Caption = caption,
            IsSelected = isSelected
        };
        Image icon = button.transform.Find("Icon").GetComponent<Image>();
        icon.sprite = iconSprite;
        icon.gameObject.SetActive(iconSprite != null);
        option.Label.rectTransform.offsetMin = new Vector2(iconSprite != null ? 90f : 10f, 4f);
        option.Label.horizontalAlignment = iconSprite != null
            ? HorizontalAlignmentOptions.Left : HorizontalAlignmentOptions.Center;
        button.onClick.AddListener(() => { toggle(); RefreshSelection(); });
        m_options.Add(option);
        m_generated.Add(button.gameObject);
        if (++m_column == OptionColumns) FinishRow();
    }

    void FinishRow()
    {
        if (m_column == 0) return;
        m_y += OptionHeight + OptionGap;
        m_column = 0;
    }

    static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    void RefreshSelection()
    {
        bool includeLocked = m_edit.IncludeLockedAbilities;
        includeLockedTrack.color = includeLocked ? lockedOnColor : lockedOffColor;
        includeLockedText.text = includeLocked ? "ON" : "OFF";
        float travel = (includeLockedTrack.rectTransform.rect.width - includeLockedKnob.rect.width) * 0.5f - 3f;
        includeLockedKnob.anchoredPosition = new Vector2(includeLocked ? travel : -travel,
            includeLockedKnob.anchoredPosition.y);
        includeLockedText.rectTransform.anchoredPosition = new Vector2(
            includeLocked ? -m_lockedOffTextPosition.x : m_lockedOffTextPosition.x,
            m_lockedOffTextPosition.y);
        hintText.text = "같은 항목은 하나만 맞아도 표시 · 다른 항목은 모두 일치";
        foreach (Option option in m_options)
        {
            bool selected = option.IsSelected();
            option.Button.targetGraphic.color = selected ? selectedColor : unselectedColor;
            option.Label.color = selected ? selectedTextColor : unselectedTextColor;
            option.Label.text = option.Caption;
        }
        int count = 0;
        foreach (int card in CardCatalog.AllIds)
        {
            if (m_cardScope != null && !m_cardScope.Contains(card)) continue;
            if (!m_collectionMode && !OwnershipManager.IsOwned(card)) continue;
            if (m_edit.Matches(card, m_query)) count++;
        }
        resultCountText.text = $"{count:N0}장의 카드";
    }

    static void Toggle<T>(HashSet<T> values, T value)
    {
        if (!values.Remove(value)) values.Add(value);
    }

    static string GradeName(ECardGrade grade)
    {
        switch (grade)
        {
            case ECardGrade.Common: return "일반";
            case ECardGrade.Rare: return "희귀";
            case ECardGrade.Arcane: return "신비";
            case ECardGrade.Mythic: return "신화";
            default: return grade.ToString();
        }
    }

    static string KeywordName(CardKeyword keyword)
    {
        KeywordIconConfig config = DataLibrary.instance != null ? DataLibrary.instance.keywordIconConfig : null;
        if (config != null && config.TryGetEntry(keyword, out KeywordIconConfig.Entry entry) &&
            !string.IsNullOrEmpty(entry.displayName)) return entry.displayName;
        return keyword.ToString();
    }
}
