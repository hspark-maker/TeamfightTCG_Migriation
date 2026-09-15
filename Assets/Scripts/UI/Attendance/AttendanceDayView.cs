using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>출석 보상 칸. 보상과 날짜 상태는 서버 응답을 받아 표시한다.</summary>
public sealed class AttendanceDayView : MonoBehaviour
{
    [SerializeField] TMP_Text dayLabel;
    [SerializeField] TMP_Text stateLabel;
    [SerializeField] Image background;
    [SerializeField] Image stateBackground;
    [SerializeField] GameObject todayBorder;
    [SerializeField] CanvasGroup rewardGroup;
    [SerializeField] CurrencyRewardSlotView[] rewardSlots;
    [SerializeField] Color normalColor = new Color(1f, 0.98f, 0.92f);
    [SerializeField] Color todayColor = new Color(1f, 0.94f, 0.71f);
    [SerializeField] Color claimedColor = new Color(0.85f, 0.83f, 0.77f);

    public void Bind(int _day, IReadOnlyList<RewardLine> _rewards,
        bool _claimed, bool _today, bool _claimable)
    {
        if (this.dayLabel != null) this.dayLabel.text = $"{_day}일차";
        if (this.stateLabel != null)
        {
            this.stateLabel.text = _claimed ? "수령 완료" : _today ? "오늘" : "대기";
            this.stateLabel.color = _claimed
                ? new Color(0.28f, 0.40f, 0.22f)
                : new Color(0.24f, 0.09f, 0f);
        }
        if (this.background != null)
            this.background.color = _claimed ? this.claimedColor : _today ? this.todayColor : this.normalColor;
        if (this.stateBackground != null)
            this.stateBackground.color = _claimed
                ? new Color(0.79f, 0.88f, 0.69f)
                : _today && _claimable ? new Color(1f, 0.79f, 0.32f) : new Color(0.90f, 0.87f, 0.80f);
        if (this.todayBorder != null) this.todayBorder.SetActive(_today && !_claimed);
        if (this.rewardGroup != null) this.rewardGroup.alpha = _claimed ? 0.48f : 1f;

        if (this.rewardSlots == null) return;
        int t_count = _rewards?.Count ?? 0;
        for (int i = 0; i < this.rewardSlots.Length; i++)
        {
            if (i < t_count) this.rewardSlots[i]?.Bind(_rewards[i]);
            else this.rewardSlots[i]?.Hide();
        }
    }
}
