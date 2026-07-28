using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 랭크 보상 한 행(RankRewardRow 프리팹 루트에 부착).
// 티어 인덱스만 들고 표시값은 매번 RankRewardManager.GetInfo로 다시 받는다(행이 스냅샷을 캐싱하면 수령 후 stale).
public class RankRewardRowView : MonoBehaviour
{
    [SerializeField] Image badgeImage;       // 티어 배지(미저작이면 프리팹 기본 유지)
    [SerializeField] TMP_Text tierNameText;  // 티어 표시명
    [SerializeField] TMP_Text amountText;    // 보상 금액("x100")
    [SerializeField] Button rewardBox;       // 보상 박스 = 수령 요청 버튼

    [Header("상태 노드(선택 — 미배선 시 null 가드)")]
    [SerializeField] GameObject highlight;   // 수령 가능
    [SerializeField] GameObject claimedMark; // 수령 완료(체크)
    [SerializeField] GameObject lockDim;     // 미달성(자물쇠)
    [SerializeField] GameObject chevron;     // 행 사이 장식(마지막 행만 비활성)
    [SerializeField] CanvasGroup canvasGroup;

    [Tooltip("수령 완료 행의 알파. 미달성은 lockDim 노드가 따로 덮으므로 여기서 겹쳐 딤하지 않는다.")]
    [SerializeField] float claimedAlpha = 0.6f;

    // 표시 대상 티어. -1 = 미바인딩(Refresh 무시).
    int m_tierIndex = -1;

    Action<int> m_onClick;

    // 티어 인덱스 배선 + 리스너 1회 등록(재빌드마다 중복 방지). _isLast면 쉐브론을 끈다.
    public void Bind(int _tierIndex, bool _isLast, Action<int> _onClick)
    {
        this.m_tierIndex = _tierIndex;
        this.m_onClick = _onClick;

        if (this.rewardBox != null)
        {
            this.rewardBox.onClick.RemoveAllListeners();
            this.rewardBox.onClick.AddListener(this.OnRewardBoxClicked);
        }

        if (this.chevron != null) this.chevron.SetActive(!_isLast);

        this.Refresh();
    }

    // 상태 표시 갱신. 수령 통지(OnChanged)마다 컨트롤러가 전 행에 호출한다.
    public void Refresh()
    {
        if (this.m_tierIndex < 0) return;

        var t_info = RankRewardManager.GetInfo(this.m_tierIndex);

        // 배지 미저작(null)이면 프리팹에 배선된 기존 스프라이트를 그대로 둔다.
        if (this.badgeImage != null && t_info.Badge != null) this.badgeImage.sprite = t_info.Badge;
        if (this.tierNameText != null) this.tierNameText.text = t_info.DisplayName;
        if (this.amountText != null) this.amountText.text = $"x{t_info.RewardGold:N0}";

        bool t_claimable = t_info.State == ERankRewardState.Claimable;
        bool t_claimed = t_info.State == ERankRewardState.Claimed;

        if (this.rewardBox != null) this.rewardBox.interactable = t_claimable;
        if (this.highlight != null) this.highlight.SetActive(t_claimable);
        if (this.claimedMark != null) this.claimedMark.SetActive(t_claimed);
        if (this.lockDim != null) this.lockDim.SetActive(t_info.State == ERankRewardState.Locked);
        if (this.canvasGroup != null) this.canvasGroup.alpha = t_claimed ? this.claimedAlpha : 1f;
    }

    // 수령 요청은 패널로 올린다(행은 팝업·지급을 모른다).
    void OnRewardBoxClicked()
    {
        if (this.m_tierIndex < 0) return;
        this.m_onClick?.Invoke(this.m_tierIndex);
    }
}
