using UnityEngine;

// 소유권 디버그 툴(인스펙터 우클릭 ContextMenu로 실행)
public class OwnershipDebugTool : MonoBehaviour
{
    [ContextMenu("전체 해금")]
    void GrantAll() => OutgameDebugActions.UnlockAllCards();

    [ContextMenu("전체 회수")]
    void RevokeAll() => OutgameDebugActions.RevokeAllCards();

    [ContextMenu("골드 +1000")]
    void GrantGold() => OutgameDebugActions.GrantGold();

    [ContextMenu("다이아 +1000")]
    void GrantDiamond() => OutgameDebugActions.GrantDiamond();

    [ContextMenu("카드 성장 초기화")]
    void ResetCardGrowth() => OutgameDebugActions.ResetCardGrowth();

    [ContextMenu("소유 현황 로그")]
    void LogOwnership() => OutgameDebugActions.LogOwnership();

    [ContextMenu("튜토리얼 진행도 리셋")]
    void ResetTutorial() => OutgameDebugActions.ResetTutorial();

    [ContextMenu("튜토리얼 완료 처리 (게이트 해제)")]
    void SkipTutorial() => OutgameDebugActions.SkipTutorial();

    [ContextMenu("튜토리얼 처음부터 (소유 회수 + 진행도 리셋)")]
    void ResetTutorialFromScratch() => OutgameDebugActions.ResetTutorialFromScratch();
}
