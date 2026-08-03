using UnityEngine;

// 아웃게임 디버그 조작의 단일 창구. 인스펙터 ContextMenu(OwnershipDebugTool)와 런타임 오버레이(OutgameDebugOverlay)가
// 같은 동작을 공유하도록 여기에 모은다 — 두 입구가 각자 구현하면 한쪽만 고쳐지는 이중 진실원이 된다.
public static class OutgameDebugActions
{
    // 카탈로그 전량 지급. 덱 편성은 소유 카드만 허용하므로 인게임 덱 연동 테스트의 출발점이다.
    // 실제 지급은 OwnershipManager가 소유 — 인게임 해금 버튼(UnlockAllCardsButton)과 같은 창구를 쓴다.
    public static void UnlockAllCards()
    {
        int t_added = OwnershipManager.GrantEntireCatalog();
        Debug.Log($"[OutgameDebug] 전체 해금 — 신규 {t_added}장 / 소유 {OwnershipManager.OwnedCount}장");
    }

    public static void RevokeAllCards()
    {
        int t_removed = OwnershipManager.RevokeAll();
        Debug.Log($"[OutgameDebug] 전체 회수 — {t_removed}장 제거 / 소유 {OwnershipManager.OwnedCount}장");
    }

    public static void LogOwnership()
    {
        Debug.Log($"[OutgameDebug] 소유 {OwnershipManager.OwnedCount}장: {string.Join(", ", OwnershipManager.OwnedKeys)}");
    }

    // 튜토리얼을 완료로 낙인. 진행 중이면 게이트 딤이 덱 탭을 막으므로 해금만으로는 덱을 만들 수 없다.
    // 진행도만 닫으면 이미 떠 있는 게이트가 화면에 남으므로 브리지의 CloseGate와 같은 조치를 함께 한다.
    public static void SkipTutorial()
    {
        OutgameTutorialProgress.Complete();
        TriggeredTutorialRunner.Abort();   // 트리거 런의 메모리 좌표가 남으면 게이트를 곧바로 다시 세운다
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();

        Debug.Log("[OutgameDebug] 튜토리얼 완료 처리 — 게이트 해제");
    }

    // 진행도만 초기화(소유는 유지). 마이그레이션 낙인은 남으므로 소유가 있어도 다시 완료 처리되지 않는다.
    public static void ResetTutorial()
    {
        OutgameTutorialProgress.ResetForDebug();
        TriggeredTutorialRunner.Abort();
        Debug.Log($"[OutgameDebug] 튜토리얼 진행도 리셋 — {OutgameTutorialProgress.ChapterIndex}-{OutgameTutorialProgress.StepIndex} / completed {OutgameTutorialProgress.IsCompleted}");
    }

    // 트리거 튜토리얼(탭 첫 진입 등)만 되돌린다 — 온보딩 진행도·소유·재화는 그대로.
    public static void ResetTriggeredTutorials()
    {
        TriggeredTutorialRunner.Abort();
        OutgameTutorialProgress.ClearTriggersForDebug();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();

        Debug.Log("[OutgameDebug] 트리거 튜토리얼 낙인 초기화 — 탭에 다시 들어가면 재생됩니다");
    }

    // N편 처음으로 되감기(소유·재화는 유지 — 앞 편에서 받은 카드는 그대로 남는다).
    // 러너는 씬 진입 시점에 좌표를 읽으므로 적용을 보려면 씬을 다시 로드해야 한다.
    public static void RestartTutorialFromChapter(int _chapterIndex)
    {
        // 저작된 편 밖으로 보내면 다음 씬 진입이 곧장 완료로 닫아버린다 — 되감기 의도와 정반대라 클램프한다.
        int t_last    = OutgameTutorialRunner.ChapterCount - 1;
        int t_chapter = t_last < 0 ? 0 : Mathf.Clamp(_chapterIndex, 0, t_last);

        OutgameTutorialProgress.JumpForDebug(t_chapter, 0);
        TriggeredTutorialRunner.Abort();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();

        Debug.Log($"[OutgameDebug] 튜토리얼 {t_chapter + 1}편 처음으로 — 씬 재진입 시 적용 (저작된 총 {OutgameTutorialRunner.ChapterCount}편)");
    }

    // 첫실행 재현 원샷: 소유까지 비워 스텝 0의 자동 진행을 원상태로 돌린다.
    public static void ResetTutorialFromScratch()
    {
        RevokeAllCards();
        ResetTutorial();
    }
}
