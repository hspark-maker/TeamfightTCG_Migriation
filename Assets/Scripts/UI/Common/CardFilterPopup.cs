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
    [SerializeField] ScrollRect scroll;
    [SerializeField] RectTransform optionContent;
    [SerializeField] Button optionTemplate;
    [SerializeField] TMP_Text sectionTemplate;
    [SerializeField] Color selectedColor = new Color(1f, 0.79f, 0.28f);
    [SerializeField] Color unselectedColor = new Color(0.22f, 0.27f, 0.36f);
    [SerializeField] Color selectedTextColor = new Color(0.12f, 0.14f, 0.20f);
    [SerializeField] Color unselectedTextColor = Color.white;

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
        optionTemplate.gameObject.SetActive(false);
        sectionTemplate.gameObject.SetActive(false);
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
                () => Toggle(m_edit.Keywords, value));
        }

        AddSection("시너지");
        foreach (KeyValuePair<string, SynergyData> pair in synergies)
        {
            string value = pair.Key;
            AddOption(SynergyText.Name(pair.Value), () => m_edit.SynergyIds.Contains(value),
                () => Toggle(m_edit.SynergyIds, value));
        }
        FinishRow();
        optionContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, m_y + 20f);
    }

    void AddOwnership(CardOwnershipFilter value, string label)
        => AddOption(label, () => m_edit.Ownership == value, () => m_edit.Ownership = value);

    void AddSection(string caption)
    {
        FinishRow();
        if (m_y > 0f) m_y += 32f;
        TMP_Text header = Instantiate(sectionTemplate, optionContent);
        header.text = caption;
        header.gameObject.SetActive(true);
        Place(header.rectTransform, 0f, m_y, optionContent.rect.width, 66f);
        m_generated.Add(header.gameObject);
        m_y += 78f;
    }

    void AddOption(string caption, Func<bool> isSelected, Action toggle)
    {
        Button button = Instantiate(optionTemplate, optionContent);
        button.gameObject.SetActive(true);
        float width = (optionContent.rect.width - 32f) / 3f;
        Place((RectTransform)button.transform, m_column * (width + 16f), m_y, width, 92f);
        var option = new Option
        {
            Button = button,
            Label = button.GetComponentInChildren<TMP_Text>(true),
            Caption = caption,
            IsSelected = isSelected
        };
        button.onClick.AddListener(() => { toggle(); RefreshSelection(); });
        m_options.Add(option);
        m_generated.Add(button.gameObject);
        if (++m_column == 3) FinishRow();
    }

    void FinishRow()
    {
        if (m_column == 0) return;
        m_y += 108f;
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
        includeLockedButton.targetGraphic.color = includeLocked ? selectedColor : unselectedColor;
        includeLockedText.color = includeLocked ? selectedTextColor : unselectedTextColor;
        includeLockedText.text = "미개방 시너지·키워드 포함 " + (includeLocked ? "ON" : "OFF");
        hintText.text = "같은 항목은 하나만 맞아도 표시 · 다른 항목은 모두 일치\n"
            + (includeLocked ? "미개방 시너지·키워드도 조건에 포함합니다." : "개방된 시너지·키워드만 조건에 포함합니다.");
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
