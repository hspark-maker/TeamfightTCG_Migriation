using System.Collections.Generic;

public enum EGuideCardPreparation { Missing, GrowthNeeded, Ready }

/// <summary>돌보미 안내 카드의 표시 상태. 미션 완료는 서버가 판정한다.</summary>
public static class GuideMissionPreparation
{
    // 보유·성장 상태가 좋은 돌보미부터 세 장을 안내한다. 특정 카드의 소유를 강제하지 않는다.
    public static IReadOnlyList<int> CardIds
    {
        get
        {
            var t_cards = new List<int>();
            foreach (int t_card in CardCatalog.AllIds)
                if (IsCaretaker(t_card)) t_cards.Add(t_card);
            t_cards.Sort((a, b) =>
            {
                int t_owned = OwnershipManager.IsOwned(b).CompareTo(OwnershipManager.IsOwned(a));
                if (t_owned != 0) return t_owned;
                int t_star = GuideMissionTrack.StarOf(b).CompareTo(GuideMissionTrack.StarOf(a));
                return t_star != 0 ? t_star : a.CompareTo(b);
            });
            if (t_cards.Count > 3) t_cards.RemoveRange(3, t_cards.Count - 3);
            return t_cards;
        }
    }

    public static bool IsCaretaker(int _card)
    {
        if (!CardCatalog.IsReady || !CardCatalog.Contains(_card)) return false;
        foreach (string t_synergy in CardCatalog.RequireSynergyIds(_card))
            if (t_synergy == "Caretaker") return true;
        return false;
    }

    public static bool IsReady
    {
        get
        {
            var t_cards = CardIds;
            if (t_cards.Count < 3) return false;
            foreach (int t_card in t_cards)
                if (StatusOf(t_card) != EGuideCardPreparation.Ready) return false;
            return true;
        }
    }
    public static bool IsActive
    {
        get
        {
            for (int t_slot = 0; t_slot < DeckSaveManager.SLOT_COUNT; t_slot++)
            {
                if (!DeckSaveManager.IsSlotValid(t_slot)) continue;
                var t_cards = DeckSaveManager.GetSlot(t_slot);
                if (new HashSet<int>(t_cards).Count != DeckSaveManager.DECK_SIZE) continue;
                bool t_owned = true;
                foreach (int t_card in t_cards) t_owned &= OwnershipManager.IsOwned(t_card);
                if (!t_owned) continue;
                foreach (var t_progress in DeckSynergyEligibility.Resolve(t_cards))
                    if (t_progress.IsActive && t_progress.Synergy.SynergyId == "Caretaker") return true;
            }
            return false;
        }
    }

    public static EGuideCardPreparation StatusOf(int _card)
        => !OwnershipManager.IsOwned(_card) ? EGuideCardPreparation.Missing
            : GrowthStar.FromLevel(CardGrowthManager.LevelOf(_card)) >= 2
                ? EGuideCardPreparation.Ready : EGuideCardPreparation.GrowthNeeded;

    public static string StatusTextOf(int _card)
    {
        switch (StatusOf(_card))
        {
            case EGuideCardPreparation.Missing: return "미보유";
            case EGuideCardPreparation.Ready: return "준비 완료";
            default: return "2성으로 성장 필요";
        }
    }
}
