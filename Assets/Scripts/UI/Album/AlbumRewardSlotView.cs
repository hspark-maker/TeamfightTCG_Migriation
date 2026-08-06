using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 전체 보상 요약의 보상 칸 하나(RewardSlot_xx) — MonoBehaviour가 아니라 뷰가 필드로 소유한다
[System.Serializable]
public class AlbumRewardSlotView
{
    [SerializeField] GameObject root;
    [SerializeField] Image icon;
    [SerializeField] TMP_Text amountLabel;

    public void Bind(in AlbumRewardDef _reward)
    {
        if (root != null) root.SetActive(true);
        if (icon != null && _reward.icon != null) icon.sprite = _reward.icon;   // null이면 목업 스프라이트 보존
        if (amountLabel != null) amountLabel.text = _reward.amount.ToString("N0");
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
    }
}
