using System;
using System.Collections.Generic;

public enum CardOwnershipFilter { All, Owned, Unowned }

/// <summary>같은 분류 안에서는 하나만 일치하면 통과하고, 서로 다른 분류는 모두 만족해야 한다.</summary>
public sealed class CardListFilter
{
    public readonly HashSet<ECardGrade> Grades = new HashSet<ECardGrade>();
    public readonly HashSet<CardKeyword> Keywords = new HashSet<CardKeyword>();
    public readonly HashSet<string> SynergyIds = new HashSet<string>(StringComparer.Ordinal);
    public CardOwnershipFilter Ownership;
    public bool IncludeLockedAbilities;

    public bool IsActive => Grades.Count > 0 || Keywords.Count > 0 || SynergyIds.Count > 0
                            || Ownership != CardOwnershipFilter.All || IncludeLockedAbilities;

    public CardListFilter Clone()
    {
        var t_copy = new CardListFilter { Ownership = Ownership, IncludeLockedAbilities = IncludeLockedAbilities };
        t_copy.Grades.UnionWith(Grades);
        t_copy.Keywords.UnionWith(Keywords);
        t_copy.SynergyIds.UnionWith(SynergyIds);
        return t_copy;
    }

    public void Clear()
    {
        Grades.Clear();
        Keywords.Clear();
        SynergyIds.Clear();
        Ownership = CardOwnershipFilter.All;
        IncludeLockedAbilities = false;
    }

    public bool Matches(int cardId, string nameQuery = null)
    {
        if (!CardCatalog.TryGetSpec(cardId, out var t_spec)) return false;
        if (Ownership == CardOwnershipFilter.Owned && !OwnershipManager.IsOwned(cardId)) return false;
        if (Ownership == CardOwnershipFilter.Unowned && OwnershipManager.IsOwned(cardId)) return false;
        if (Grades.Count > 0 && !Grades.Contains(t_spec.Grade)) return false;
        if (!string.IsNullOrWhiteSpace(nameQuery)
            && (t_spec.DisplayName ?? string.Empty).IndexOf(nameQuery.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        if (Keywords.Count > 0)
        {
            CardKeyword t_keywords = IncludeLockedAbilities ? t_spec.Keywords
                : OwnershipManager.IsOwned(cardId) ? CardGrowthManager.GrowthOf(cardId).UnlockedKeywords
                : CardKeyword.None;
            bool t_match = false;
            foreach (CardKeyword t_keyword in Keywords)
                if ((t_keywords & t_keyword) != 0) { t_match = true; break; }
            if (!t_match) return false;
        }

        if (SynergyIds.Count > 0)
        {
            if (!IncludeLockedAbilities && (!OwnershipManager.IsOwned(cardId)
                || !CardGrowthManager.GrowthOf(cardId).SynergyUnlocked)) return false;
            bool t_match = false;
            foreach (string t_id in CardCatalog.RequireSynergyIds(cardId))
                if (SynergyIds.Contains(t_id)) { t_match = true; break; }
            if (!t_match) return false;
        }
        return true;
    }
}
