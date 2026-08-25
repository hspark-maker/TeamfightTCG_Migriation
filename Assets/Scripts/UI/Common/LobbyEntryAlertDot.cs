using System;
using UnityEngine;

// 로비 진입 버튼의 "여기 볼 게 있다" 알림 점.
// 대상만 고르고 판정 근거는 각 도메인의 집계 프로퍼티 하나뿐 — UI가 상태 규칙을 복제하지 않는다.
// 연출(등장 팝·상시 맥동·퇴장)은 AlertDotView가 전부 쥔다.
public class LobbyEntryAlertDot : AlertDotView
{
    [Tooltip("이 점이 무엇을 가리키는가. 대상마다 판정 근거와 구독할 통지가 함께 갈린다.")]
    [SerializeField] EAlertDotTarget target;

    // 베이스가 넘긴 갱신 핸들러 보관 — 시그니처가 다른 통지(재화)를 명명 메서드로 중계하기 위해.
    Action m_handler;

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

                // 잠금을 곱한다 — 못 누르는 버튼에 점을 띄우면 갈 수 없는 곳으로 부르는 셈이다.
                case EAlertDotTarget.KeywordGrowth:
                    return KeywordGrowthManager.HasAnyAffordableStep
                           && OutgameFeatureLock.IsUnlocked(EOutgameFeature.KeywordGrowth);

                case EAlertDotTarget.Tournament:
                    return TournamentProgress.HasAnyWaiting
                           && OutgameFeatureLock.IsUnlocked(EOutgameFeature.Tournament);

                default:
                    return false;
            }
        }
    }

    protected override void Subscribe(Action _handler)
    {
        this.m_handler = _handler;
        this.m_boundTarget = this.target;

        switch (this.m_boundTarget)
        {
            // 수령 자격은 랭크 티어를 즉시 읽어 판정한다 — 티어가 오른 순간을 랭크 통지로만 잡을 수 있다.
            case EAlertDotTarget.RankReward:
                RankRewardManager.OnChanged += _handler;
                RankManager.OnChanged += _handler;
                break;

            case EAlertDotTarget.KeywordGrowth:
                KeywordGrowthManager.OnChanged += _handler;
                // 강화는 잔액이 차야 비로소 가능해진다 — 성장 통지만으로는 켜지는 방향을 잡지 못한다.
                CurrencyManager.OnCurrencyChanged += this.HandleCurrencyChanged;
                OutgameFeatureLock.OnChanged += _handler;
                break;

            // 등급이 올라 챕터 잠금이 풀리는 것도 점이 켜지는 사건이다.
            case EAlertDotTarget.Tournament:
                TournamentProgress.OnChanged += _handler;
                RankManager.OnChanged += _handler;
                OutgameFeatureLock.OnChanged += _handler;
                break;
        }
    }

    protected override void Unsubscribe(Action _handler)
    {
        switch (this.m_boundTarget)
        {
            case EAlertDotTarget.RankReward:
                RankRewardManager.OnChanged -= _handler;
                RankManager.OnChanged -= _handler;
                break;

            case EAlertDotTarget.KeywordGrowth:
                KeywordGrowthManager.OnChanged -= _handler;
                CurrencyManager.OnCurrencyChanged -= this.HandleCurrencyChanged;
                OutgameFeatureLock.OnChanged -= _handler;
                break;

            case EAlertDotTarget.Tournament:
                TournamentProgress.OnChanged -= _handler;
                RankManager.OnChanged -= _handler;
                OutgameFeatureLock.OnChanged -= _handler;
                break;
        }

        this.m_handler = null;
    }

    // 람다로 감싸면 구독과 해제가 서로 다른 델리게이트가 돼 -=가 아무것도 지우지 못한다
    // — static 통지가 파괴된 뷰를 계속 붙든다.
    void HandleCurrencyChanged(ECurrencyType _type, long _balance) => this.m_handler?.Invoke();
}

// 0번을 바꾸면 이미 저작된 프리팹이 조용히 다른 대상으로 갈아탄다 — 값 재배치 금지.
public enum EAlertDotTarget
{
    RankReward = 0,
    KeywordGrowth,
    Tournament,
}
