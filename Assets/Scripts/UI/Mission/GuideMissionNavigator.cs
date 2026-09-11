using UnityEngine;

/// <summary>가이드 미션 "이동" 버튼의 목적지 실행. 잠금·준비 실패는 조용히 무시한다 — 가이드는 부가 기능이다.
/// 도착 화면에 안내를 붙이지 않는다: 카드 소지가 전제인 안내는 소지 여부에 따라 성립하지 않는다.</summary>
internal static class GuideMissionNavigator
{
    internal static bool CanGo(MissionDefinition _definition)
        => GuideMissionTrack.RouteOf(_definition).Kind != GuideMissionTrack.ERouteKind.None;

    internal static void Go(MissionDefinition _definition)
    {
        GuideMissionTrack.GuideRoute t_route = GuideMissionTrack.RouteOf(_definition);
        switch (t_route.Kind)
        {
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

        t_shell.Select(t_tab, false);
        int t_slot = DeckSaveManager.SelectedSlot;
        if (DeckSaveManager.IsSlotValid(t_slot)) t_tab.OpenEditor(t_slot);
        else t_tab.OpenNewDeckEditor();
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
        t_shell.Select(t_tab, false);
    }

    static void GoCardGrowth(MissionDefinition _definition)
    {
        int t_card = GuideMissionTrack.PickGrowthCard(_definition);
        if (t_card <= 0) return;
        CardDetailOverlayView.Open(t_card);
    }
}
