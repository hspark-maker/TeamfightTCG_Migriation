using System;

// 랭크 보상 진입 버튼의 "수령 가능" 알림 점.
// 판정 근거는 RankRewardManager.HasAnyClaimable 하나뿐 — UI가 상태 규칙을 복제하지 않는다.
// 연출(등장 팝·상시 맥동·퇴장)은 AlertDotView가 전부 쥔다.
public class RankRewardAlertDot : AlertDotView
{
    protected override bool ShouldShow => RankRewardManager.HasAnyClaimable;

    protected override void Subscribe(Action _handler) => RankRewardManager.OnChanged += _handler;

    protected override void Unsubscribe(Action _handler) => RankRewardManager.OnChanged -= _handler;
}
