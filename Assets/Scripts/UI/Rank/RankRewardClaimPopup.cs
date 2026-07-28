using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 랭크 보상 수령 팝업(ClaimPopup 노드에 부착). 표시와 확인 콜백만 담당하고 지급은 패널이 매니저에 위임한다.
// 씬에 직접 저작되므로 PooledUIBase가 아니라 SetActive 토글로 동작한다.
public class RankRewardClaimPopup : MonoBehaviour
{
    [Tooltip("켜고 끌 대상. 미배선이면 자기 gameObject를 토글한다.")]
    [SerializeField] GameObject root;

    [SerializeField] TMP_Text titleText;  // 티어 표시명(선택)
    [SerializeField] TMP_Text amountText; // 보상 금액("x100")
    [SerializeField] Button claimButton;  // [획득]

    // 확인 콜백. 중복 클릭 방지를 위해 한 번 쓰면 비운다.
    Action m_onConfirm;

    public void Show(RankRewardInfo _info, Action _onConfirm)
    {
        this.m_onConfirm = _onConfirm;

        if (this.titleText != null) this.titleText.text = _info.DisplayName;
        if (this.amountText != null) this.amountText.text = $"x{_info.RewardGold:N0}";

        if (this.claimButton != null)
        {
            this.claimButton.onClick.RemoveAllListeners(); // 재표시마다 중복 등록 방지
            this.claimButton.onClick.AddListener(this.OnClaimClicked);
            this.claimButton.interactable = true;
        }

        this.SetVisible(true);
    }

    public void Hide()
    {
        this.m_onConfirm = null;
        this.SetVisible(false);
    }

    void OnClaimClicked()
    {
        // 콜백을 먼저 비워 연타로 두 번 지급되는 경로를 막는다(매니저 가드와 이중 방어).
        var t_callback = this.m_onConfirm;
        this.m_onConfirm = null;

        if (this.claimButton != null) this.claimButton.interactable = false;

        t_callback?.Invoke();
    }

    void SetVisible(bool _visible)
    {
        var t_target = this.root != null ? this.root : this.gameObject;
        t_target.SetActive(_visible);
    }
}
