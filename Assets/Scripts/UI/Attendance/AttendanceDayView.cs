using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>출석 보상 칸. 보상과 날짜 상태는 서버 응답을 받아 표시한다.</summary>
public sealed class AttendanceDayView : MonoBehaviour
{
    [SerializeField] TMP_Text dayLabel;
    [SerializeField] TMP_Text stateLabel;
    [SerializeField] GameObject todayBorder;
    [SerializeField] CanvasGroup rewardGroup;
    [SerializeField] CurrencyRewardSlotView[] rewardSlots;

    public void Bind(int _day, IReadOnlyList<RewardLine> _rewards,
        bool _claimed, bool _today, bool _claimable)
    {
        if (this.dayLabel != null) this.dayLabel.text = $"{_day}일차";
        if (this.stateLabel != null)
            this.stateLabel.text = _claimed ? "수령 완료" : _today ? "오늘" : "대기";
        // 색은 프리팹 저작을 유지하고, 상태는 오늘 테두리와 수령 완료 알파로 표현한다.
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
