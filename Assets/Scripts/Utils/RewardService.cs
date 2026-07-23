using System;
using UnityEngine;

/// <summary>
/// 전투 결과를 골드로 환산
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
    /// 전투 결과를 골드로 환산(순수 함수). 남은 카드 × 장당 골드 + 승/패 보너스를
    /// [minGold, maxGold]로 클램프. 승패는 보너스 정액에만 영향(공식은 승패 무관 동일).
    /// </summary>
    public static long CalculateGold(bool won, int remainingCards)
    {
        var t_config = Config;

        long t_gold = Math.Min((long)remainingCards * t_config.goldPerCard, t_config.minGold);

        return t_gold;
    }

    /// <summary>
    /// 전투 종료 시점에 보상을 직접 지급한다. 환산 → Earn → 즉시 Save(영속화) 순으로 처리하고
    /// 지급액을 반환한다. 반환값은 F-20 보상 팝업이 그대로 소비할 수 있다.
    /// </summary>
    public static long GrantBattleReward(bool won, int remainingCards)
    {
        long gold = CalculateGold(won, remainingCards);

        CurrencyManager.Earn(ECurrencyType.Gold, gold);
        // Earn은 flush하지 않으므로 지급 직후 즉시 영속화(앱 강제 종료에도 보상 유실 방지).
        CurrencyManager.Save();

        return gold;
    }
}
