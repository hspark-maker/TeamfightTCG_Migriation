using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class PassPanel
{
    [Header("프리미엄 혜택 안내")]
    [SerializeField] Button premiumButton;
    [SerializeField] TMP_Text premiumBonusText;
    [SerializeField] TMP_Text premiumTitleText;

    [Header("최고 레벨 이후 반복 보상")]
    [SerializeField] GameObject repeatRoot;
    [SerializeField] TMP_Text repeatRuleText;
    [SerializeField] TMP_Text repeatProgressText;
    [SerializeField] Image repeatFill;
    [SerializeField] Image repeatRewardIcon;
    [SerializeField] TMP_Text repeatRewardAmountText;
    [SerializeField] TMP_Text repeatStatusText;
    [SerializeField] Button repeatClaimButton;
    [SerializeField] TMP_Text repeatClaimText;

    float m_nextExtrasRefresh;

    void BindExtraButtons()
    {
        if (this.premiumButton != null)
        {
            this.premiumButton.onClick.RemoveAllListeners();
            this.premiumButton.onClick.AddListener(this.ShowPremiumDetails);
        }
        if (this.repeatClaimButton != null)
        {
            this.repeatClaimButton.onClick.RemoveAllListeners();
            this.repeatClaimButton.onClick.AddListener(this.HandleRepeatClaim);
        }
    }

    void RefreshExtras()
    {
        long t_bonus = PassManager.PremiumExpAt(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (this.premiumBonusText != null)
            this.premiumBonusText.text = PassManager.PremiumUnlocked ? "유료 트랙 보상 활성화"
                : t_bonus > 0 ? $"구매 혜택 +{t_bonus:N0} EXP >" : "프리미엄 혜택 >";
        if (this.premiumTitleText != null)
            this.premiumTitleText.text = PassManager.PremiumUnlocked ? "프리미엄 활성화" : "프리미엄 · 준비 중";
        if (this.premiumButton != null) this.premiumButton.interactable = t_bonus > 0 && !PassManager.PremiumUnlocked;

        bool t_hasRepeat = PassManager.HasSeason && PassManager.HasRepeatReward;
        if (this.repeatRoot != null) this.repeatRoot.SetActive(t_hasRepeat);
        if (!t_hasRepeat) return;

        long t_required = PassManager.Repeat.RequiredExp;
        long t_available = PassManager.RepeatAvailableClaims;
        bool t_unlocked = PassManager.Exp >= PassManager.MaxRequiredExp;
        if (this.repeatRuleText != null) this.repeatRuleText.text = $"{t_required:N0} EXP마다 · 시즌 종료까지";
        if (this.repeatProgressText != null)
            this.repeatProgressText.text = $"{PassManager.RepeatProgressExp:N0} / {t_required:N0} EXP";
        if (this.repeatFill != null)
        {
            float t_fill = (float)PassManager.RepeatProgressExp / t_required;
            this.repeatFill.rectTransform.anchorMax = new Vector2(t_fill, 1f);
            this.repeatFill.gameObject.SetActive(t_fill > 0f);
        }
        ClaimRewardGain t_reward = PassManager.Repeat.Reward.Find(t_gain => t_gain != null && t_gain.Amount > 0);
        if (this.repeatRewardAmountText != null) this.repeatRewardAmountText.text = t_reward.Amount.ToString("N0");
        if (this.repeatRewardIcon != null && CurrencyCode.TryParse(t_reward.Currency, out ECurrencyType t_currency))
        {
            this.repeatRewardIcon.sprite = CurrencyLook.IconOf(t_currency);
            this.repeatRewardIcon.enabled = this.repeatRewardIcon.sprite != null;
        }
        if (this.repeatStatusText != null)
            this.repeatStatusText.text = !PassManager.IsSeasonOpen ? "시즌 종료"
                : !t_unlocked ? "최고 레벨 달성 후 열립니다"
                : t_available > 0 ? $"{t_available:N0}회 수령 가능" : "계속 플레이하고 보상을 받아요";
        if (this.repeatClaimButton != null)
            this.repeatClaimButton.interactable = !this.m_claiming && PassManager.CanClaimRepeat;
        if (this.repeatClaimText != null)
            this.repeatClaimText.text = t_available > 0 ? $"{t_available:N0}회 받기" : "받기";
    }

    void ShowPremiumDetails()
    {
        long t_bonus = PassManager.PremiumExpAt(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (t_bonus <= 0) return;
        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = $"프리미엄 구매 혜택\n<color=#FFD76A>지금 +{t_bonus:N0} EXP</color>\n"
                + "<size=65%>유료 트랙 보상 추가 획득\n"
                + $"기본 {PassManager.PremiumBaseExp:N0} → 최대 {PassManager.PremiumBaseExp * 4:N0} EXP\n"
                + "시즌 종료에 가까울수록 더 많이!\n구매 기능은 준비 중입니다.</size>",
            yesText = "확인",
            noText = "닫기"
        });
    }

    void HandleRepeatClaim() => this.ClaimAsync(new List<ClaimTarget>(), true).Forget();

    static bool HasUnclaimedLevel()
    {
        foreach (PassLevelDefinition t_level in PassManager.Levels)
            if (PassManager.CanClaim(t_level) || PassManager.CanClaimPremium(t_level)) return true;
        return false;
    }
}
