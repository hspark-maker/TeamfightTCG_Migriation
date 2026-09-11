using System;
using UnityEngine;

/// <summary>미완료 미션에서 콘텐츠 선택 화면으로 이동한다. 구매와 전투는 실행하지 않는다.</summary>
internal static class MissionContentNavigation
{
    internal static bool HasDestination(MissionDefinition _definition)
        => DestinationOf(_definition) != EOutgameFeature.None;

    internal static bool TryNavigate(MissionDefinition _definition, Action _beforeNavigate)
    {
        if (_definition == null || MissionManager.IsComplete(_definition)
            || MissionManager.IsClaimed(_definition.Id) || !MissionManager.IsGuideUnlocked(_definition)
            || MissionCommands.IsInFlight(_definition.Id)) return false;

        EOutgameFeature t_destination = DestinationOf(_definition);
        if (t_destination == EOutgameFeature.None) return false;

        if (t_destination == EOutgameFeature.Adventure)
        {
            var t_launcher = UnityEngine.Object.FindFirstObjectByType<LobbyMatchLauncher>();
            return t_launcher != null && t_launcher.TryOpenAdventureMap(_beforeNavigate);
        }

        var t_shell = UnityEngine.Object.FindFirstObjectByType<LobbyTabController>();
        return t_shell != null && t_shell.TrySelectFeature(t_destination, _beforeNavigate);
    }

    static EOutgameFeature DestinationOf(MissionDefinition _definition)
    {
        string t_event = _definition?.Event;
        switch (t_event)
        {
            case "OpenPack": return EOutgameFeature.LobbyPackTab;
            case "EnhanceCard":
            case "LimitBreakCard":
            case "Guide.CaretakerCardsAtStar1":
            case "Guide.DeckCardsAtStar2":
                return EOutgameFeature.LobbyCollectionTab;
            case "Guide.DeckSaved6":
            case "Guide.CaretakerDeckAtStar2":
            case "Guide.CaretakerTraceDeck":
            case "Guide.DeckCardsAtStar3":
                return EOutgameFeature.LobbyDeckTab;
            case "CompleteBattle":
            case "WinBattle":
            case "WinRankedBattle":
            case "DestroyCards":
            case "TriggerSynergy":
            case "TriggerKeyword":
            case "AttackTimes":
                return EOutgameFeature.LobbyMatchTab;
            case "Guide.AdventureChapter01": return EOutgameFeature.Adventure;
        }

        const string ADVENTURE_NODE = "Guide.AdventureNode";
        if (t_event != null && t_event.StartsWith(ADVENTURE_NODE, StringComparison.Ordinal)
            && int.TryParse(t_event.Substring(ADVENTURE_NODE.Length), out int t_node) && t_node > 0)
            return EOutgameFeature.Adventure;
        return EOutgameFeature.None;
    }
}
