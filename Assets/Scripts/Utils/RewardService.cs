using System;
using UnityEngine;

/// <summary>
/// 전투 결과를 보상 재화로 환산
/// </summary>
public static class RewardService
{
    // 전투 보상 계수는 Reward 표(ownerType=Battle)가 소유한다 — 앨범·토너먼트·랭크와 같은 자리다.
    // 표가 비면 매판 0을 지급하게 되므로 부팅이 RewardSpec.TryValidateRequired로 먼저 막는다.
    static bool s_warnedMissing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => s_warnedMissing = false;

    // 계수 한 칸. 표에 없으면 0을 돌려주되 세션당 1회 소리를 낸다(부팅 검사를 통과했다면 여기 오지 않는다).
    static long CoefficientOf(string _ownerId)
    {
        if (RewardSpec.TryGetSingle(ERewardOwnerType.Battle, _ownerId, out AlbumRewardDef t_def))
            return t_def.amount;

        if (!s_warnedMissing)
        {
            s_warnedMissing = true;
            Debug.LogError($"[RewardService] Reward 표에 Battle/{_ownerId} 행이 없어 전투 보상이 0으로 계산됩니다.");
        }
        return 0;
    }

    // 지급 재화는 승리 계수 행이 정한다(패배는 자기 행의 재화를 쓴다).
    static ECurrencyType CurrencyOf(string _ownerId)
        => RewardSpec.TryGetSingle(ERewardOwnerType.Battle, _ownerId, out AlbumRewardDef t_def)
            ? t_def.currency
            : ECurrencyType.Gold;

    /// <summary>
    /// 전투 결과를 보상으로 환산(순수 함수). 승리는 남은 카드 수 × 장당 골드에 winFloor 하한,
    /// 패배는 남은 카드와 무관하게 loseGold 고정.
    /// </summary>
    public static CurrencyGain CalculateReward(bool _won, int _remainingCards)
    {
        // 패배가 남은 카드를 보지 않는 것이 규칙의 핵심이다 — 보면 첫 턴 항복(6장 생존)이 압승과 같은 액수가 된다.
        if (!_won)
            return new CurrencyGain(CurrencyOf(RewardSpec.BattleLoseFlat), CoefficientOf(RewardSpec.BattleLoseFlat));

        long t_amount = Math.Max(
            (long)_remainingCards * CoefficientOf(RewardSpec.BattleWinPerCard),
            CoefficientOf(RewardSpec.BattleWinFloor));

        return new CurrencyGain(CurrencyOf(RewardSpec.BattleWinPerCard), t_amount);
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
