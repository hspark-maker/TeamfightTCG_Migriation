using UnityEngine;

// 소유권 디버그 툴. 씬의 아무 GameObject에 붙이고 인스펙터 컴포넌트 우클릭 ContextMenu로 실행.
// 실제 동작은 OutgameDebugActions에 있다 — 런타임 오버레이(OutgameDebugOverlay)와 같은 경로를 쓴다.
public class OwnershipDebugTool : MonoBehaviour
{
    [ContextMenu("전체 해금")]
    void GrantAll() => OutgameDebugActions.UnlockAllCards();

    [ContextMenu("전체 회수")]
    void RevokeAll() => OutgameDebugActions.RevokeAllCards();

    // 라벨의 수치는 OutgameDebugActions의 지급 단위와 맞춘다(ContextMenu 인자는 const 문자열만 받아 보간이 안 된다).
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
