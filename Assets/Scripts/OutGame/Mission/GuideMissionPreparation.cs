using System.Collections.Generic;

public enum EGuideCardPreparation { Missing, GrowthNeeded, Ready }

/// <summary>돌보미 안내 카드의 표시 상태. 미션 완료는 서버가 판정한다.</summary>
public static class GuideMissionPreparation
{
    static readonly IReadOnlyList<int> s_cards = System.Array.AsReadOnly(new[] { 3, 4, 1 });
    public static IReadOnlyList<int> CardIds => s_cards;
    public static bool IsReady => StatusOf(3) == EGuideCardPreparation.Ready
        && StatusOf(4) == EGuideCardPreparation.Ready && StatusOf(1) == EGuideCardPreparation.Ready;
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
