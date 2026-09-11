using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>가이드 미션 "이동" 버튼의 목적지 실행. 잠금·준비 실패는 조용히 무시한다 — 가이드는 부가 기능이다.
/// 도착 코치마크의 유일한 발화처다(자연 진입에는 붙지 않는다).</summary>
internal static class GuideMissionNavigator
{
    // 탭 슬라이드(0.35초)가 끝난 뒤 발화해야 게이트가 슬라이드에 딸려 오지 않는다(SynergyIntroduction.OpenDeck 과 같은 대기).
    const int TAB_ARRIVE_DELAY_MS = 500;

    internal static bool CanGo(MissionDefinition _definition)
        => GuideMissionTrack.RouteOf(_definition).Kind != GuideMissionTrack.ERouteKind.None;

    internal static void Go(MissionDefinition _definition)
    {
        GuideMissionTrack.GuideRoute t_route = GuideMissionTrack.RouteOf(_definition);
        switch (t_route.Kind)
        {
            case GuideMissionTrack.ERouteKind.DeckEditor: GoDeckEditor(); break;
            case GuideMissionTrack.ERouteKind.AdventureNode: GoAdventure(t_route.NodeId); break;
            case GuideMissionTrack.ERouteKind.CollectionEnhance: GoCollectionAsync().Forget(); break;
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

    static async UniTaskVoid GoCollectionAsync()
    {
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.LobbyCollectionTab)) return;
        LobbyTabController t_shell = FindShell();
        AlbumTabController t_tab = t_shell != null ? t_shell.GetComponentInChildren<AlbumTabController>(true) : null;
        if (t_tab == null) return;

        t_shell.Select(t_tab, false);
        await UniTask.Delay(TAB_ARRIVE_DELAY_MS, ignoreTimeScale: true);
        if (t_shell == null || t_shell.CurrentPanel != t_tab) return;
        GuidanceCoordinator.TryFire(EOutgameTutorialTrigger.GuideCaretakerEnhanceArrived);
    }

    static void GoCardGrowth(MissionDefinition _definition)
    {
        int t_card = GuideMissionTrack.PickGrowthCard(_definition);
        if (t_card <= 0) return;

        // 강화 버튼 앵커는 Open 안에서 동기로 등록된다 — 같은 스택에서 발화해도 브리지가 찾는다(키워드 강화 패널 선례).
        CardDetailOverlayView.Open(t_card);
        if (CardDetailOverlayView.IsOpen) GuidanceCoordinator.TryFire(EOutgameTutorialTrigger.GuideAceEnhanceArrived);
    }
}
