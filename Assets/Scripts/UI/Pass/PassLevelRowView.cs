using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 패스 레벨 한 줄. 레벨·문턱·보상·수령 버튼을 그린다.
///
/// <para>행이 자기 판정을 갖지 않는다 — 도달 여부와 수령 낙인은 <see cref="PassManager"/> 에 묻고,
/// 최종 판정은 서버(claimPassReward)다. 여기 표시는 왕복을 아끼는 낙관 표시다.</para>
/// </summary>
public class PassLevelRowView : MonoBehaviour
{
    [SerializeField] TMP_Text levelText;
    [SerializeField] TMP_Text lockedLevelText;
    [SerializeField] GameObject reachedLevelRoot;
    [SerializeField] GameObject lockedLevelRoot;
    [SerializeField] GameObject lockedOverlay;
    [SerializeField] Image rewardIcon;
    [SerializeField] TMP_Text rewardAmountText;
    [SerializeField] RectTransform levelFill;
    [SerializeField] GameObject connectorRoot;

    [Tooltip("누적 필요 경험치 표시. 비워 두면 그리지 않는다.")]
    [SerializeField] TMP_Text requiredText;

    [Tooltip("보상 문구. 재화 목록을 한 줄로 적는다.")]
    [SerializeField] TMP_Text rewardText;

    [SerializeField] Button claimButton;
    [SerializeField] GameObject claimAlertDot;

    [SerializeField] TMP_Text claimLabel;

    [Tooltip("이미 받은 줄에 켜는 표식. 비워 두면 그리지 않는다.")]
    [SerializeField] GameObject claimedMark;

    [Tooltip("수령 불가일 때 버튼에 씌울 알파.")]
    [Range(0f, 1f)] [SerializeField] float disabledAlpha = 0.5f;

    [SerializeField] CanvasGroup claimGroup;

    PassLevelDefinition m_definition;
    Action<int> m_onClaim;
    long? m_nextRequiredExp;

    // 행이 최대 레벨 수만큼 늘고 OnChanged 마다 전부 다시 그려진다 — 여기서 만든 GC 는 그대로 쌓인다.
    static readonly StringBuilder s_text = new StringBuilder(64);

    internal void Bind(PassLevelDefinition _definition, long? _nextRequiredExp, Action<int> _onClaim)
    {
        this.m_definition = _definition;
        this.m_onClaim = _onClaim;
        this.m_nextRequiredExp = _nextRequiredExp;

        if (this.claimButton != null)
        {
            this.claimButton.onClick.RemoveAllListeners();
            this.claimButton.onClick.AddListener(this.HandleClaim);
        }

        if (this.levelText != null) this.levelText.text = (_definition?.Level ?? 0).ToString();
        if (this.lockedLevelText != null) this.lockedLevelText.text = (_definition?.Level ?? 0).ToString();
        if (this.requiredText != null) this.requiredText.text = $"{_definition?.RequiredExp ?? 0} EXP";
        if (this.rewardText != null) this.rewardText.text = BuildRewardText(_definition);
        this.RefreshRewardIcon();

        this.Refresh();
    }

    /// <summary>도달·수령 상태만 다시 그린다.</summary>
    internal void Refresh()
    {
        if (this.m_definition == null) return;

        bool t_claimed = PassManager.IsClaimed(this.m_definition.Level);
        bool t_reached = PassManager.Exp >= this.m_definition.RequiredExp;
        bool t_canClaim = PassManager.CanClaim(this.m_definition);
        if (this.claimAlertDot != null) this.claimAlertDot.SetActive(t_canClaim);

        if (this.reachedLevelRoot != null) this.reachedLevelRoot.SetActive(t_reached);
        if (this.lockedLevelRoot != null) this.lockedLevelRoot.SetActive(!t_reached);
        if (this.lockedOverlay != null) this.lockedOverlay.SetActive(!t_reached);
        if (this.connectorRoot != null) this.connectorRoot.SetActive(this.m_nextRequiredExp.HasValue);
        if (this.levelFill != null)
        {
            long t_span = (this.m_nextRequiredExp ?? this.m_definition.RequiredExp) - this.m_definition.RequiredExp;
            float t_fill = t_span > 0 ? Mathf.Clamp01((float)(PassManager.Exp - this.m_definition.RequiredExp) / t_span) : 0f;
            this.levelFill.anchorMin = new Vector2(0f, 1f - t_fill);
            this.levelFill.gameObject.SetActive(t_fill > 0f);
        }

        if (this.claimedMark != null) this.claimedMark.SetActive(t_claimed);
        if (this.claimButton != null)
        {
            this.claimButton.gameObject.SetActive(true);
            this.claimButton.interactable = t_canClaim;
        }
        if (this.claimGroup != null) this.claimGroup.alpha = t_canClaim ? 1f : this.disabledAlpha;
        if (this.claimLabel != null)
            this.claimLabel.text = t_claimed ? "수령 완료" : t_reached ? "수령" : "잠김";
    }

    void RefreshRewardIcon()
    {
        if (this.rewardIcon == null) return;
        ClaimRewardGain t_gain = this.m_definition?.Reward?.Find(t_reward => t_reward != null && t_reward.Amount > 0);
        ClaimRewardItem t_item = this.m_definition?.Items?.Find(t_reward => t_reward != null && t_reward.Amount > 0);
        Sprite t_icon = null;
        if (t_gain != null && CurrencyCode.TryParse(t_gain.Currency, out ECurrencyType t_type))
            t_icon = CurrencyLook.IconOf(t_type);
        // 팩·카드 보상은 저작된 선물 아이콘과 서버 보상명으로 표시한다.
        if (t_icon != null) this.rewardIcon.sprite = t_icon;
        this.rewardIcon.gameObject.SetActive(t_gain != null || t_item != null);
        if (this.rewardAmountText != null)
            this.rewardAmountText.text = $"×{t_gain?.Amount ?? t_item?.Amount ?? 0:N0}";
    }

    void HandleClaim()
    {
        if (this.m_definition == null) return;
        if (!PassManager.CanClaim(this.m_definition)) return;

        this.m_onClaim?.Invoke(this.m_definition.Level);
    }

    static string BuildRewardText(PassLevelDefinition _definition)
    {
        List<ClaimRewardGain> t_gains = _definition?.Reward;
        if (t_gains == null) t_gains = new List<ClaimRewardGain>();

        s_text.Clear();
        for (int t_i = 0; t_i < t_gains.Count; t_i++)
        {
            ClaimRewardGain t_gain = t_gains[t_i];
            if (t_gain == null || t_gain.Amount <= 0) continue;
            if (s_text.Length > 0) s_text.Append(", ");
            string t_name = CurrencyCode.TryParse(t_gain.Currency, out ECurrencyType t_type)
                ? CurrencyLook.NameOf(t_type) : t_gain.Currency;
            s_text.Append(t_name).Append(' ').Append(t_gain.Amount.ToString("N0"));
        }
        RewardItemDisplay.Append(s_text, _definition?.Items);
        return s_text.Length == 0 ? "-" : s_text.ToString();
    }
}
