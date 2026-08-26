using System;
using UnityEngine;

/// <summary>
/// 전투 결과를 보상 재화로 환산
/// </summary>
public static class RewardService
{
    static BattleReward _s;
    static bool s_configured;
    static bool s_warnedDefault;

    public static bool IsConfigured => s_configured;

    public static BattleReward Config
    {
        get
        {
            if (_s != null) return _s;
            WarnDefaultConfig();
            return _s = ScriptableObject.CreateInstance<BattleReward>();
        }
    }

    /// <summary>부트스트랩에서 실제 애셋 주입(선택). null이면 기본 유지.</summary>
    public static void SetConfig(BattleReward _config)
    {
        if (_config == null) return;
        _s = _config;
        s_configured = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        _s = null;
        s_configured = false;
        s_warnedDefault = false;
    }

    static void WarnDefaultConfig()
    {
        if (s_warnedDefault) return;
        s_warnedDefault = true;
        Debug.LogWarning("[RewardService] BattleReward가 주입되지 않아 기본값으로 동작합니다.");
    }

    /// <summary>
    /// 전투 결과를 보상으로 환산(순수 함수). 승리는 남은 카드 수 × 장당 골드에 winFloor 하한,
    /// 패배는 남은 카드와 무관하게 loseGold 고정.
    /// </summary>
    public static CurrencyGain CalculateReward(bool _won, int _remainingCards)
    {
        var t_config = Config;

        // 패배가 남은 카드를 보지 않는 것이 규칙의 핵심이다 — 보면 첫 턴 항복(6장 생존)이 압승과 같은 액수가 된다.
        long t_amount = _won
            ? Math.Max((long)_remainingCards * t_config.goldPerCard, t_config.winFloor)
            : t_config.loseGold;

        return new CurrencyGain(t_config.rewardType, t_amount);
    }

    /// <summary>
    /// 전투 종료 시점에 보상을 직접 지급한다. 환산 → Earn → 즉시 Save(영속화) 순으로 처리하고
    /// 지급분을 반환한다. 반환값은 F-20 보상 팝업이 그대로 소비할 수 있다.
    /// </summary>
    public static CurrencyGain GrantBattleReward(bool _won, int _remainingCards)
    {
        CurrencyGain t_reward = CalculateReward(_won, _remainingCards);

        CurrencyManager.Earn(t_reward.Type, t_reward.Amount);
        // Earn은 flush하지 않으므로 지급 직후 즉시 영속화(앱 강제 종료에도 보상 유실 방지).
        CurrencyManager.Save();

        return t_reward;
    }
}
