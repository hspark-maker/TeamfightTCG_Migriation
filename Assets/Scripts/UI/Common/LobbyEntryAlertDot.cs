using System;
using UnityEngine;

// 로비 진입 버튼의 "여기 볼 게 있다" 알림 점.
// 대상만 고르고 판정 근거는 각 도메인의 집계 프로퍼티 하나뿐 — UI가 상태 규칙을 복제하지 않는다.
// 연출(등장 팝·상시 맥동·퇴장)은 AlertDotView가 전부 쥔다.
public class LobbyEntryAlertDot : AlertDotView
{
    [Tooltip("이 점이 무엇을 가리키는가. 대상마다 판정 근거와 구독할 통지가 함께 갈린다.")]
    [SerializeField] EAlertDotTarget target;

    // 구독한 시점의 대상. 켜져 있는 동안 인스펙터로 target을 돌리면 해제가 엉뚱한 통지를 찾아간다.
    EAlertDotTarget m_boundTarget;

    protected override bool ShouldShow
    {
        get
        {
            switch (this.target)
            {
                case EAlertDotTarget.RankReward:
                    return RankRewardManager.HasAnyClaimable;

                case EAlertDotTarget.Adventure:
                    return AdventureProgress.HasAnyClaimable
                           && OutgameFeatureLock.IsUnlocked(EOutgameFeature.Adventure);

                case EAlertDotTarget.Mission:
                    return OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission)
                           && MissionManager.HasAnyRegularClaimable;
                case EAlertDotTarget.GuideMission:
                    return OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission)
                           && MissionManager.HasAnyClaimable("guide");
                case EAlertDotTarget.Pass:
                    return PassManager.HasAnyClaimable;
                case EAlertDotTarget.Achievement:
                    return OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission)
                           && AchievementManager.HasAnyClaimable;

                default:
                    return false;
            }
        }
    }

    protected override void Subscribe(Action _handler)
    {
        this.m_boundTarget = this.target;

        switch (this.m_boundTarget)
        {
            case EAlertDotTarget.Mission:
            case EAlertDotTarget.GuideMission:
                MissionManager.OnChanged += _handler;
                OutgameFeatureLock.OnChanged += _handler;
                break;
            case EAlertDotTarget.Pass:
                PassManager.OnChanged += _handler;
                break;
            case EAlertDotTarget.Achievement:
                AchievementManager.OnChanged += _handler;
                OutgameFeatureLock.OnChanged += _handler;
                break;
            // 수령 자격은 랭크 티어를 즉시 읽어 판정한다 — 티어가 오른 순간을 랭크 통지로만 잡을 수 있다.
            case EAlertDotTarget.RankReward:
                RankRewardManager.OnChanged += _handler;
                RankManager.OnChanged += _handler;
                break;

            case EAlertDotTarget.Adventure:
                AdventureProgress.OnChanged += _handler;
                RankManager.OnChanged += _handler;
                OutgameFeatureLock.OnChanged += _handler;
                break;
        }
    }

    protected override void Unsubscribe(Action _handler)
    {
        switch (this.m_boundTarget)
        {
            case EAlertDotTarget.Mission:
            case EAlertDotTarget.GuideMission:
                MissionManager.OnChanged -= _handler;
                OutgameFeatureLock.OnChanged -= _handler;
                break;
            case EAlertDotTarget.Pass:
                PassManager.OnChanged -= _handler;
                break;
            case EAlertDotTarget.Achievement:
                AchievementManager.OnChanged -= _handler;
                OutgameFeatureLock.OnChanged -= _handler;
                break;
            case EAlertDotTarget.RankReward:
                RankRewardManager.OnChanged -= _handler;
                RankManager.OnChanged -= _handler;
                break;

            case EAlertDotTarget.Adventure:
                AdventureProgress.OnChanged -= _handler;
                RankManager.OnChanged -= _handler;
                OutgameFeatureLock.OnChanged -= _handler;
                break;
        }
    }
}

// 0번을 바꾸면 이미 저작된 프리팹이 조용히 다른 대상으로 갈아탄다 — 값 재배치 금지.
public enum EAlertDotTarget
{
    RankReward = 0,
    KeywordGrowth = 1, // 폐기 — 기존 프리팹의 직렬화 값 보존.
    Adventure,
    Mission = 3,
    GuideMission = 4,
    Pass = 5,
    Achievement = 6,
}
