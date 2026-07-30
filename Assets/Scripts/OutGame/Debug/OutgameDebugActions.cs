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
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();

        Debug.Log("[OutgameDebug] 튜토리얼 완료 처리 — 게이트 해제");
    }

    // 진행도만 초기화(소유는 유지). 마이그레이션 낙인은 남으므로 소유가 있어도 다시 완료 처리되지 않는다.
    public static void ResetTutorial()
    {
        OutgameTutorialProgress.ResetForDebug();
        Debug.Log($"[OutgameDebug] 튜토리얼 진행도 리셋 — step {OutgameTutorialProgress.StepIndex} / completed {OutgameTutorialProgress.IsCompleted}");
    }

    // 첫실행 재현 원샷: 소유까지 비워 스텝 0의 자동 진행을 원상태로 돌린다.
    public static void ResetTutorialFromScratch()
    {
        RevokeAllCards();
        ResetTutorial();
    }
}
