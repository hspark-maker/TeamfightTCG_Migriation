using System.Collections.Generic;

/// <summary>서버 가이드 정의를 읽어 다음 투자 후보를 안내한다. 달성·지급 판정은 하지 않는다.</summary>
internal static class CardEnhancementGuide
{
    internal static string HintFor(int _cardId)
    {
        MissionDefinition t_current = null;
        foreach (MissionDefinition t_definition in MissionManager.Definitions)
            if (t_definition.Period == "guide" && !MissionManager.IsClaimed(t_definition.Id) &&
                (t_current == null || t_definition.SortOrder < t_current.SortOrder)) t_current = t_definition;
        if (t_current == null) return null;
        if (MissionManager.IsComplete(t_current)) return $"가이드: {t_current.Title} — 보상을 받아 주세요";

        int t_target = TargetLevel(t_current.Event);
        if (t_target == 0) return $"가이드: {t_current.Title}";
        var t_candidates = new List<int>();
        foreach (int t_id in CardCatalog.AllIds)
            if (OwnershipManager.IsOwned(t_id) && IsCandidate(t_id, t_current.Event, t_target)) t_candidates.Add(t_id);
        t_candidates.Sort((a, b) =>
        {
            int t_level = CardGrowthManager.LevelOf(b).CompareTo(CardGrowthManager.LevelOf(a));
            return t_level != 0 ? t_level : a.CompareTo(b);
        });
        if (t_candidates.Count == 0)
            return $"가이드: {t_current.Title}\n대상 카드를 덱에 편성하고 가이드 진행을 확인해 주세요";
        int t_suggested = t_candidates.Contains(_cardId) ? _cardId : t_candidates[0];
        string t_name = t_suggested == _cardId ? "이 카드" : CardCatalog.RequireSpec(t_suggested).DisplayName;
        string t_cost = CostToLevel(t_suggested, t_target);
        return $"가이드: {t_current.Title}\n추천: {t_name} {GrowthStar.FromLevel(t_target)}성까지 {t_cost}";
    }

    static int TargetLevel(string _event)
    {
        switch (_event)
        {
            case "Guide.CaretakerCardsAtStar1": return 2;
            case "Guide.CaretakerDeckAtStar2":
            case "Guide.CaretakerTraceDeck":
            case "Guide.DeckCardsAtStar2": return 3;
            case "Guide.DeckCardsAtStar3": return 4;
            default: return 0;
        }
    }

    static bool IsCandidate(int _id, string _event, int _target)
    {
        int t_level = CardGrowthManager.LevelOf(_id);
        if (t_level >= _target || !CardCatalog.TryGetSpec(_id, out CardSpec t_spec)) return false;
        if (_event == "Guide.CaretakerCardsAtStar1" || _event == "Guide.CaretakerDeckAtStar2")
            return IsCaretaker(t_spec) && CaretakerCountAtLevel(_target) < 3;
        if (_event == "Guide.CaretakerTraceDeck")
            return (IsCaretaker(t_spec) && CaretakerCountAtLevel(_target) < 3) ||
                   t_spec.AssetName == "Data_Card_Nightchestnut" || t_spec.AssetName == "Data_Card_MushroomCat";
        int t_slot = DeckSaveManager.SelectedSlot;
        var t_deck = t_slot >= 0 ? DeckSaveManager.GetSlot(t_slot) : null;
        // 첫 3성 안내는 앞서 키운 2성 중에서 고른다. 다른 카드의 강화 자체는 막지 않는다.
        return t_deck != null && t_deck.Contains(_id) && (_target != 4 || t_level == 3);
    }

    static bool IsCaretaker(CardSpec _spec)
    {
        foreach (string t_name in _spec.SynergyNames)
            if (t_name == "Data_Synergy_Caretaker") return true;
        return false;
    }

    static int CaretakerCountAtLevel(int _level)
    {
        int t_count = 0;
        foreach (int t_id in CardCatalog.AllIds)
            if (OwnershipManager.IsOwned(t_id) && CardGrowthManager.LevelOf(t_id) >= _level &&
                CardCatalog.TryGetSpec(t_id, out CardSpec t_spec) && IsCaretaker(t_spec)) t_count++;
        return t_count;
    }

    internal static string CostToLevel(int _id, int _target)
    {
        int t_from = CardGrowthManager.LevelOf(_id);
        var t_costs = new SortedDictionary<ECurrencyType, long>();
        for (int t_level = t_from + 1; t_level <= _target; t_level++)
        {
            // 무료 튜토리얼은 다음 한 단계에만 반영한다. 그 뒤 단계는 정식 표 비용이다.
            GrowthStep t_step;
            bool t_found = t_level == t_from + 1
                ? CardGrowthManager.TryGetEvolutionStep(_id, out t_step)
                : GrowthRules.TryGetStep(_id, t_level, out t_step);
            if (!t_found) return "비용 확인 필요";
            t_costs.TryGetValue(t_step.Currency, out long t_cost);
            t_costs[t_step.Currency] = checked(t_cost + t_step.Cost);
        }
        var t_parts = new List<string>();
        foreach (var t_pair in t_costs)
            if (t_pair.Value > 0) t_parts.Add($"{CurrencyLook.NameOf(t_pair.Key)} {t_pair.Value:N0}");
        return t_parts.Count == 0 ? "무료" : string.Join(" + ", t_parts);
    }
}
