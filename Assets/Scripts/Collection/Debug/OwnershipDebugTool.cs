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
}
