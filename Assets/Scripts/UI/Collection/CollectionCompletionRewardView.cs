using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 도감 전체 완성 1회성 보상(F-20) 푸터 뷰. 푸터 오브젝트에 부착.
// 수령 가능(모든 행 완성 & 미수령)일 때만 root를 노출하고 버튼을 활성화한다.
// 소유변경(완성 여부 변동)·수확/완성수령(OnChanged) 양쪽에서 갱신이 필요하므로 둘 다 구독한다.
public class CollectionCompletionRewardView : MonoBehaviour
{
    [SerializeField] GameObject root;      // 수령 가능할 때만 활성(전체 완성 배너)
    [SerializeField] Button claimButton;   // 수령 버튼
    [SerializeField] TMP_Text rewardText;  // 보상 표기(선택)

    void OnEnable()
    {
        if (claimButton != null)
        {
            claimButton.onClick.RemoveAllListeners();
            claimButton.onClick.AddListener(OnClaimClicked);
        }

        CollectionProductionManager.OnChanged += Refresh;
        OwnershipManager.OnOwnershipChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        CollectionProductionManager.OnChanged -= Refresh;
        OwnershipManager.OnOwnershipChanged -= Refresh;
    }

    // 완성 보상 스냅샷으로 갱신. 수령 가능할 때만 root 노출·버튼 활성.
    void Refresh()
    {
        var t_info = CollectionProductionManager.GetCompletionRewardInfo();

        if (root != null) root.SetActive(t_info.CanClaim);
        if (claimButton != null) claimButton.interactable = t_info.CanClaim;
        if (rewardText != null) rewardText.text = $"{t_info.RewardAmount:N0}";
    }

    // 수령 클릭 → 매니저에 위임. 성공 시 OnChanged가 Refresh를 유발해 root가 닫힌다.
    void OnClaimClicked()
    {
        CollectionProductionManager.ClaimCompletionReward();
    }
}
