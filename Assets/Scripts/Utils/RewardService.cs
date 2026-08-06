using System;
using UnityEngine;

/// <summary>
/// 전투 결과를 보상 재화로 환산
/// </summary>
public static class RewardService
{
    static BattleReward _s;

    public static BattleReward Config
        => _s != null ? _s : (_s = ScriptableObject.CreateInstance<BattleReward>());

    /// <summary>부트스트랩에서 실제 애셋 주입(선택). null이면 기본 유지.</summary>
    public static void SetConfig(BattleReward _config)
    {
        if (_config != null) _s = _config;
    }

    /// <summary>
    /// 전투 결과를 보상으로 환산(순수 함수). 남은 카드 수 × 장당 골드,
    /// minGold를 하한으로 적용(승패 무관 동일 공식).
    /// </summary>
    public static CurrencyGain CalculateReward(int remainingCards)
    {
        var t_config = Config;

        long t_amount = Math.Max((long)remainingCards * t_config.goldPerCard, t_config.minGold);

        return new CurrencyGain(t_config.rewardType, t_amount);
    }

    /// <summary>
    /// 전투 종료 시점에 보상을 직접 지급한다. 환산 → Earn → 즉시 Save(영속화) 순으로 처리하고
    /// 지급분을 반환한다. 반환값은 F-20 보상 팝업이 그대로 소비할 수 있다.
    /// </summary>
    public static CurrencyGain GrantBattleReward(int remainingCards)
    {
        CurrencyGain t_reward = CalculateReward(remainingCards);

        CurrencyManager.Earn(t_reward.Type, t_reward.Amount);
        // Earn은 flush하지 않으므로 지급 직후 즉시 영속화(앱 강제 종료에도 보상 유실 방지).
        CurrencyManager.Save();

        return t_reward;
    }
}
