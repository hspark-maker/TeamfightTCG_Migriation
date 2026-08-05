using UnityEngine;

// 아웃게임 디버그 조작의 단일 창구 (인스펙터 ContextMenu·런타임 오버레이 공용)
public static class OutgameDebugActions
{
    // 디버그 지급 단위
    public const long DEBUG_GOLD_AMOUNT    = 1000;
    public const long DEBUG_DIAMOND_AMOUNT = 1000;

    public static void GrantGold() => GrantCurrency(ECurrencyType.Gold, DEBUG_GOLD_AMOUNT);

    public static void GrantDiamond() => GrantCurrency(ECurrencyType.Diamond, DEBUG_DIAMOND_AMOUNT);

    // 재화 즉시 지급 + 즉시 영속
    public static void GrantCurrency(ECurrencyType _type, long _amount)
    {
        CurrencyManager.Earn(_type, _amount);
        CurrencyManager.Save();

        Debug.Log($"[OutgameDebug] {_type} +{_amount} — 잔액 {CurrencyManager.GetBalance(_type)}");
    }

    // 강화 레벨·진화 단계 초기화 (소유·재화는 유지)
    public static void ResetCardGrowth()
    {
        CardGrowthManager.DebugResetAll();

        Debug.Log("[OutgameDebug] 카드 성장 초기화 — 전 카드 Lv0 · 미진화");
    }

    // 카탈로그 전량 지급
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

    // 튜토리얼 완료 낙인 + 떠 있는 게이트 해제
    public static void SkipTutorial()
    {
        OutgameTutorialProgress.Complete();
        TriggeredTutorialRunner.Abort();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();

        Debug.Log("[OutgameDebug] 튜토리얼 완료 처리 — 게이트 해제");
    }

    // 튜토리얼 진행도만 초기화 (소유는 유지)
    public static void ResetTutorial()
    {
        OutgameTutorialProgress.ResetForDebug();
        TriggeredTutorialRunner.Abort();
        Debug.Log($"[OutgameDebug] 튜토리얼 진행도 리셋 — {OutgameTutorialProgress.ChapterIndex}-{OutgameTutorialProgress.StepIndex} / completed {OutgameTutorialProgress.IsCompleted}");
    }

    // 트리거 튜토리얼(탭 첫 진입 등) 낙인만 초기화
    public static void ResetTriggeredTutorials()
    {
        TriggeredTutorialRunner.Abort();
        OutgameTutorialProgress.ClearTriggersForDebug();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();

        Debug.Log("[OutgameDebug] 트리거 튜토리얼 낙인 초기화 — 탭에 다시 들어가면 재생됩니다");
    }

    // 튜토리얼 N편 처음으로 되감기 (소유·재화 유지, 씬 재진입 시 적용)
    public static void RestartTutorialFromChapter(int _chapterIndex)
    {
        int t_last    = OutgameTutorialRunner.ChapterCount - 1;
        int t_chapter = t_last < 0 ? 0 : Mathf.Clamp(_chapterIndex, 0, t_last);

        OutgameTutorialProgress.JumpForDebug(t_chapter, 0);
        TriggeredTutorialRunner.Abort();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();

        Debug.Log($"[OutgameDebug] 튜토리얼 {t_chapter + 1}편 처음으로 — 씬 재진입 시 적용 (저작된 총 {OutgameTutorialRunner.ChapterCount}편)");
    }

    // 잠긴 기능 전체 해금 토글 (튜토리얼 딤은 별개 축이라 걷히지 않는다)
    public static void ToggleFeatureLock()
    {
        OutgameFeatureLock.ForceUnlockAllForDebug = !OutgameFeatureLock.ForceUnlockAllForDebug;

        Debug.Log($"[OutgameDebug] 기능 잠금 {(OutgameFeatureLock.ForceUnlockAllForDebug ? "무시(전체 해금)" : "정상 적용")}");
    }

    // 첫실행 재현 원샷 — 소유까지 비우고 튜토리얼 리셋
    public static void ResetTutorialFromScratch()
    {
        RevokeAllCards();
        ResetTutorial();
    }
}
