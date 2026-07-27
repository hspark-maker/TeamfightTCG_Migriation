using UnityEngine;

// 소유권 디버그 툴. 씬의 아무 GameObject에 붙이고 인스펙터 컴포넌트 우클릭 ContextMenu로 실행.
public class OwnershipDebugTool : MonoBehaviour
{
    [ContextMenu("전체 해금")]
    void GrantAll()
    {
        foreach (var t_card in CardCatalog.All)
        {
            OwnershipManager.Grant(CardCatalog.KeyOf(t_card));
        }
        Debug.Log($"[OwnershipDebugTool] 전체 해금 완료 — 소유 {OwnershipManager.OwnedCount}장");
    }

    [ContextMenu("전체 회수")]
    void RevokeAll()
    {
        foreach (var t_key in OwnershipManager.OwnedKeys)
        {
            OwnershipManager.Revoke(t_key);
        }
        Debug.Log($"[OwnershipDebugTool] 전체 회수 완료 — 소유 {OwnershipManager.OwnedCount}장");
    }

    [ContextMenu("소유 현황 로그")]
    void LogOwnership()
    {
        Debug.Log($"[OwnershipDebugTool] 소유 {OwnershipManager.OwnedCount}장: {string.Join(", ", OwnershipManager.OwnedKeys)}");
    }

    // 튜토리얼 진행도만 초기화(소유는 유지). 마이그레이션 낙인은 남으므로 소유가 있어도 다시 완료 처리되지 않는다.
    [ContextMenu("튜토리얼 진행도 리셋")]
    void ResetTutorial()
    {
        OutgameTutorialProgress.ResetForDebug();
        Debug.Log($"[OwnershipDebugTool] 튜토리얼 진행도 리셋 — step {OutgameTutorialProgress.StepIndex} / completed {OutgameTutorialProgress.IsCompleted}");
    }

    // 첫실행 재현 원샷: 소유까지 비워 스텝 0의 자동 구매(중복 없는 스타터팩)를 원상태로 돌린다.
    [ContextMenu("튜토리얼 처음부터 (소유 회수 + 진행도 리셋)")]
    void ResetTutorialFromScratch()
    {
        RevokeAll();
        ResetTutorial();
    }
}
