using UnityEngine;

/// <summary>가이드 미션 "이동" 버튼의 목적지 실행. 잠금·준비 실패는 조용히 무시한다 — 가이드는 부가 기능이다.
/// 강화 안내는 도착 뒤 실제 강화 가능한 보유 카드를 고른다.</summary>
internal static class GuideMissionNavigator
{
    internal static bool CanGo(MissionDefinition _definition)
        => GuideMissionTrack.RouteOf(_definition).Kind != GuideMissionTrack.ERouteKind.None;

    internal static string UnavailableReason(MissionDefinition _definition)
    {
        var t_route = GuideMissionTrack.RouteOf(_definition);
        EOutgameFeature t_feature;
        switch (t_route.Kind)
        {
            case GuideMissionTrack.ERouteKind.Match: t_feature = EOutgameFeature.LobbyMatchTab; break;
            case GuideMissionTrack.ERouteKind.DeckEditor: t_feature = EOutgameFeature.LobbyDeckTab; break;
            case GuideMissionTrack.ERouteKind.AdventureNode:
                if (AdventureProgress.IndexOf(t_route.NodeId) < 0) return "모험 준비 중…";
                t_feature = EOutgameFeature.Adventure;
                break;
            case GuideMissionTrack.ERouteKind.CardGrowth:
                if (GuideMissionTrack.PickGrowthCard(_definition) <= 0) return "덱 편성 확인 필요";
                t_feature = EOutgameFeature.LobbyCollectionTab;
                break;
            case GuideMissionTrack.ERouteKind.CollectionEnhance: t_feature = EOutgameFeature.LobbyCollectionTab; break;
            default: return "이동할 곳 없음";
        }
        return OutgameFeatureLock.IsUnlocked(t_feature) ? null : "콘텐츠 해금 대기";
    }

    internal static void Go(MissionDefinition _definition)
    {
        if (_definition != null && GuidanceCoordinator.TryRequestMission(_definition.Id)) return;
        GuideMissionTrack.GuideRoute t_route = GuideMissionTrack.RouteOf(_definition);
        switch (t_route.Kind)
        {
            case GuideMissionTrack.ERouteKind.Match: MissionContentNavigation.TryNavigate(_definition, null); break;
            case GuideMissionTrack.ERouteKind.DeckEditor: GoDeckEditor(); break;
            case GuideMissionTrack.ERouteKind.AdventureNode: GoAdventure(t_route.NodeId); break;
            case GuideMissionTrack.ERouteKind.CollectionEnhance: GoCollection(); break;
            case GuideMissionTrack.ERouteKind.CardGrowth: GoCardGrowth(_definition); break;
        }
    }

    static LobbyTabController FindShell() => Object.FindFirstObjectByType<LobbyTabController>();

    static void GoDeckEditor()
    {
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.LobbyDeckTab)) return;
        LobbyTabController t_shell = FindShell();
        DeckTabController t_tab = t_shell != null ? t_shell.GetComponentInChildren<DeckTabController>(true) : null;
        if (t_tab == null) return;

        t_shell.TrySelectFeature(EOutgameFeature.LobbyDeckTab, _onArrived: () =>
        {
            int t_slot = DeckSaveManager.SelectedSlot;
            if (DeckSaveManager.IsSlotValid(t_slot)) t_tab.OpenEditor(t_slot);
            else t_tab.OpenNewDeckEditor();
        });
    }

    static void GoAdventure(string _nodeId)
    {
        LobbyMatchLauncher t_launcher = Object.FindFirstObjectByType<LobbyMatchLauncher>();
        if (t_launcher == null) return;
        t_launcher.TryOpenAdventureMapAt(AdventureProgress.IndexOf(_nodeId));
    }

    static void GoCollection()
    {
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.LobbyCollectionTab)) return;
        LobbyTabController t_shell = FindShell();
        AlbumTabController t_tab = t_shell != null ? t_shell.GetComponentInChildren<AlbumTabController>(true) : null;
        if (t_tab == null) return;
        t_shell.TrySelectFeature(EOutgameFeature.LobbyCollectionTab);
    }

    static void GoCardGrowth(MissionDefinition _definition)
    {
        int t_card = GuideMissionTrack.PickGrowthCard(_definition);
        if (t_card <= 0) return;
        CardDetailOverlayView.Open(t_card);
    }
}
