using UnityEngine;
using UnityEngine.UI;

// "모든 카드 해금" 버튼 배선용 어댑터. 인스펙터 Button OnClick에 UnlockAll을 연결한다.
// 도감 UI에 의존하지 않아 로비·덱·전투 등 어느 씬의 패널에든 붙는다 — 소유 갱신은 OnOwnershipChanged로 각 화면이 알아서 받는다.
public class UnlockAllCardsButton : MonoBehaviour
{
    // 배선하면 이미 전량 소유일 때 눌리지 않게 잠근다. 비워도 UnlockAll 자체는 동작한다.
    [SerializeField] Button targetButton;

    void Awake()
    {
        // 릴리스 빌드에서는 전량 해금 버튼이 화면에 남으면 안 된다 — 소유는 세이브로 서버까지 올라간다.
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        this.gameObject.SetActive(false);
#endif
    }

    void OnEnable()
    {
        OwnershipManager.OnOwnershipChanged += RefreshInteractable;
        RefreshInteractable();
    }

    void OnDisable()
    {
        OwnershipManager.OnOwnershipChanged -= RefreshInteractable;
    }

    // Button OnClick 진입점.
    public void UnlockAll()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        int t_added = OwnershipManager.GrantEntireCatalog();
        Debug.Log($"[Ownership] 전체 해금 — 신규 {t_added}장 / 소유 {OwnershipManager.OwnedCount}장");
#endif
    }

    void RefreshInteractable()
    {
        if (targetButton == null) return;

        // 카탈로그 미준비(초기화 미경유 씬)면 장수를 알 수 없으므로 잠그지 않는다 — 눌러보면 경고 로그가 원인을 알려준다.
        targetButton.interactable = !CardCatalog.IsReady || OwnershipManager.OwnedCount < CardCatalog.Count;
    }
}
