using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>현재 가이드 미션의 막·제목·진행도·보상을 표시한다. 클릭과 잠금은 LobbyMatchTabPanel이 담당한다.</summary>
public sealed class GuideMissionTrackerView : MonoBehaviour
{
    [Header("문구")]
    [SerializeField] TMP_Text actText;
    [SerializeField] TMP_Text titleText;
    [SerializeField] TMP_Text progressText;

    [Header("보상")]
    [SerializeField] Image rewardIcon;
    [SerializeField] TMP_Text rewardCountText;

    void OnEnable()
    {
        MissionManager.OnChanged += this.Rebind;
        this.Rebind();
    }

    void OnDisable()
    {
        MissionManager.OnChanged -= this.Rebind;
    }

    void Rebind()
    {
        MissionDefinition t_definition = GuideMissionTrack.Current;
        if (t_definition == null) return;

        if (this.actText != null)
            this.actText.text = GuideMissionTrack.TryGetAct(t_definition, out GuideMissionTrack.GuideAct t_act) ? t_act.Label : string.Empty;
        if (this.titleText != null) this.titleText.text = t_definition.Title ?? string.Empty;

        long t_target = t_definition.Target;
        long t_progress = t_target > 0 ? Math.Min(MissionManager.ProgressOf(t_definition), t_target) : MissionManager.ProgressOf(t_definition);
        if (this.progressText != null) this.progressText.text = $"{t_progress} / {t_target}";
        this.ApplyReward(t_definition);
    }

    void ApplyReward(MissionDefinition _definition)
    {
        ClaimRewardGain t_gain = MissionRowView.CurrencyAt(_definition, 0);
        ClaimRewardItem t_item = t_gain == null ? MissionRowView.FirstItem(_definition) : null;

        if (this.rewardIcon != null)
        {
            if (t_gain != null && Enum.TryParse(t_gain.Currency, out ECurrencyType t_type))
            {
                Sprite t_sprite = CurrencyLook.IconOf(t_type);
                if (t_sprite != null) this.rewardIcon.sprite = t_sprite;
            }
            this.rewardIcon.gameObject.SetActive(t_gain != null || t_item != null);
        }
        if (this.rewardCountText != null)
        {
            long t_amount = t_gain != null ? t_gain.Amount : t_item != null ? t_item.Amount : 0;
            this.rewardCountText.text = t_amount > 0 ? t_amount.ToString("N0") : string.Empty;
        }
    }
}
