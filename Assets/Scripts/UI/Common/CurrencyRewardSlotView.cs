using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 재화 보상 칸 하나(아이콘 + 수량) — MonoBehaviour가 아니라 뷰가 필드로 소유한다.
// 도감 보상 요약과 랭크 보상 행·팝업이 같은 칸을 쓴다.
[System.Serializable]
public class CurrencyRewardSlotView
{
    [SerializeField] GameObject root;
    [SerializeField] Image icon;
    [SerializeField] TMP_Text amountLabel;

    public void Bind(Sprite _icon, long _amount)
    {
        if (root != null) root.SetActive(true);
        if (icon != null && _icon != null) icon.sprite = _icon;   // null이면 목업 스프라이트 보존
        if (amountLabel != null) amountLabel.text = _amount.ToString("N0");
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
    }
}
