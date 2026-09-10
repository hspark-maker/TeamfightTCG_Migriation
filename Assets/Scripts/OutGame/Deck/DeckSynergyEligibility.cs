using System.Collections.Generic;

/// <summary>덱 표시와 성장 안내가 공유하는 카드별 시너지 해금 판정.</summary>
public static class DeckSynergyEligibility
{
    public static bool IsEligible(int _cardId, bool _ignoreGrowth = false)
    {
        if (_cardId <= 0 || !CardCatalog.Contains(_cardId)) return false;
        if (_ignoreGrowth) return true;
        CardGrowth t_growth = CardGrowthManager.GrowthOf(_cardId);
        return !t_growth.Applied || t_growth.SynergyUnlocked;
    }

    public static void Collect(IEnumerable<int> _cards, List<int> _result, bool _ignoreGrowth = false)
    {
        _result.Clear();
        if (_cards == null) return;
        foreach (int t_card in _cards)
            if (IsEligible(t_card, _ignoreGrowth) && !_result.Contains(t_card)) _result.Add(t_card);
    }

    public static List<SynergyProgress> Resolve(IEnumerable<int> _cards)
    {
        var t_eligible = new List<int>();
        Collect(_cards, t_eligible);
        return SynergyPreview.Resolve(t_eligible);
    }
}
