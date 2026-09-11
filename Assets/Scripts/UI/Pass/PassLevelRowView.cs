using System;
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
    [SerializeField] GameObject dualRewardRoot;
    [SerializeField] Image dualRewardIcon;
    [SerializeField] TMP_Text dualRewardAmountText;
    [SerializeField] Image secondRewardIcon;
    [SerializeField] TMP_Text secondRewardAmountText;
    [SerializeField] Image premiumRewardIcon;
    [SerializeField] TMP_Text premiumRewardAmountText;
    [SerializeField] GameObject premiumDualRewardRoot;
    [SerializeField] Image premiumDualRewardIcon;
    [SerializeField] TMP_Text premiumDualRewardAmountText;
    [SerializeField] Image premiumSecondRewardIcon;
    [SerializeField] TMP_Text premiumSecondRewardAmountText;
    [SerializeField] Button premiumClaimButton;
    [SerializeField] TMP_Text premiumClaimLabel;
    [SerializeField] GameObject premiumClaimAlertDot;
    [SerializeField] GameObject premiumLockedOverlay;
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
    Action<int> m_onPremiumClaim;
    long? m_nextRequiredExp;
    Sprite m_authoredRewardIcon;
    bool m_hasBound;

    internal void Bind(PassLevelDefinition _definition, long? _nextRequiredExp, Action<int> _onClaim, Action<int> _onPremiumClaim = null)
    {
        this.m_onClaim = _onClaim;
        this.m_onPremiumClaim = _onPremiumClaim;
        bool t_sameDefinition = this.m_hasBound && ReferenceEquals(this.m_definition, _definition);
        this.m_nextRequiredExp = _nextRequiredExp;
        // 다음 레벨 문턱은 목록 변경 때 따로 갱신하고, 정의가 같으면 진행 상태만 그린다.
        if (t_sameDefinition)
        {
            this.Refresh();
            return;
        }
        if (!this.m_hasBound && this.rewardIcon != null) this.m_authoredRewardIcon = this.rewardIcon.sprite;
        this.m_hasBound = true;
        this.m_definition = _definition;

        if (this.claimButton != null)
        {
            this.claimButton.onClick.RemoveAllListeners();
            this.claimButton.onClick.AddListener(this.HandleClaim);
        }

        if (this.premiumClaimButton != null)
        {
            this.premiumClaimButton.onClick.RemoveAllListeners();
            this.premiumClaimButton.onClick.AddListener(this.HandlePremiumClaim);
        }

        if (this.levelText != null) this.levelText.text = (_definition?.Level ?? 0).ToString();
        if (this.lockedLevelText != null) this.lockedLevelText.text = (_definition?.Level ?? 0).ToString();
        if (this.requiredText != null) this.requiredText.text = $"{_definition?.RequiredExp ?? 0} EXP";
        if (this.rewardText != null) this.rewardText.gameObject.SetActive(false);
        this.RefreshRewardIcon();
        this.RefreshPremiumRewardIcon();

        this.Refresh();
    }

    /// <summary>도달·수령 상태만 다시 그린다.</summary>
    internal void Refresh()
    {
        if (this.m_definition == null) return;

        this.RefreshPremium();
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
        bool t_dual = this.dualRewardRoot != null && this.TryRewardAt(1, out _, out _);
        if (this.dualRewardRoot != null) this.dualRewardRoot.SetActive(t_dual);
        this.ApplyRewardSlot(this.rewardIcon, this.rewardAmountText, 0);
        if (t_dual)
        {
            if (this.rewardIcon != null) this.rewardIcon.gameObject.SetActive(false);
            if (this.rewardAmountText != null) this.rewardAmountText.gameObject.SetActive(false);
            this.ApplyRewardSlot(this.dualRewardIcon, this.dualRewardAmountText, 0);
            this.ApplyRewardSlot(this.secondRewardIcon, this.secondRewardAmountText, 1);
        }
    }

    void ApplyRewardSlot(Image _icon, TMP_Text _amountText, int _index, bool _premium = false)
    {
        bool t_found = this.TryRewardAt(_index, out string t_currency, out long t_amount, _premium);
        if (_icon != null)
        {
            Sprite t_sprite = CurrencyCode.TryParse(t_currency, out ECurrencyType t_type)
                ? CurrencyLook.IconOf(t_type) : null;
            _icon.sprite = t_sprite != null ? t_sprite : this.m_authoredRewardIcon;
            _icon.gameObject.SetActive(t_found);
        }
        if (_amountText != null)
        {
            _amountText.gameObject.SetActive(t_found);
            _amountText.text = t_found ? t_amount.ToString("N0") : string.Empty;
        }
    }

    bool TryRewardAt(int _index, out string _currency, out long _amount, bool _premium = false)
    {
        _currency = null;
        _amount = 0;
        var t_rewards = _premium ? this.m_definition?.PremiumReward : this.m_definition?.Reward;
        var t_items = _premium ? this.m_definition?.PremiumItems : this.m_definition?.Items;
        if (t_rewards != null)
            foreach (ClaimRewardGain t_gain in t_rewards)
                if (t_gain != null && t_gain.Amount > 0 && _index-- == 0)
                {
                    _currency = t_gain.Currency;
                    _amount = t_gain.Amount;
                    return true;
                }
        if (t_items != null)
            foreach (ClaimRewardItem t_item in t_items)
                if (t_item != null && t_item.Amount > 0 && _index-- == 0)
                {
                    _amount = t_item.Amount;
                    return true;
                }
        return false;
    }

    void RefreshPremiumRewardIcon()
    {
        bool t_dual = this.premiumDualRewardRoot != null && this.TryRewardAt(1, out _, out _, true);
        if (this.premiumDualRewardRoot != null) this.premiumDualRewardRoot.SetActive(t_dual);
        this.ApplyRewardSlot(this.premiumRewardIcon, this.premiumRewardAmountText, 0, true);
        if (t_dual)
        {
            if (this.premiumRewardIcon != null) this.premiumRewardIcon.gameObject.SetActive(false);
            if (this.premiumRewardAmountText != null) this.premiumRewardAmountText.gameObject.SetActive(false);
            this.ApplyRewardSlot(this.premiumDualRewardIcon, this.premiumDualRewardAmountText, 0, true);
            this.ApplyRewardSlot(this.premiumSecondRewardIcon, this.premiumSecondRewardAmountText, 1, true);
        }
    }

    void RefreshPremium()
    {
        bool t_hasReward = this.TryRewardAt(0, out _, out _, true);
        bool t_unlocked = PassManager.PremiumUnlocked;
        bool t_reached = this.m_definition != null && PassManager.Exp >= this.m_definition.RequiredExp;
        bool t_claimed = this.m_definition != null && PassManager.IsPremiumClaimed(this.m_definition.Level);
        bool t_canClaim = t_hasReward && PassManager.CanClaimPremium(this.m_definition);
        if (this.premiumClaimButton != null)
        {
            this.premiumClaimButton.gameObject.SetActive(t_hasReward);
            this.premiumClaimButton.interactable = t_canClaim;
        }
        if (this.premiumClaimAlertDot != null) this.premiumClaimAlertDot.SetActive(t_canClaim);
        if (this.premiumLockedOverlay != null)
            this.premiumLockedOverlay.SetActive(t_hasReward && (!t_unlocked || !t_reached));
        if (this.premiumClaimLabel != null)
            this.premiumClaimLabel.text = !t_unlocked ? "프리미엄 필요" : t_claimed ? "수령 완료" : t_reached ? "수령" : "잠김";
    }

    void HandlePremiumClaim()
    {
        if (this.m_definition == null || !PassManager.CanClaimPremium(this.m_definition)) return;
        this.m_onPremiumClaim?.Invoke(this.m_definition.Level);
    }

    void HandleClaim()
    {
        if (this.m_definition == null) return;
        if (!PassManager.CanClaim(this.m_definition)) return;

        this.m_onClaim?.Invoke(this.m_definition.Level);
    }

}
