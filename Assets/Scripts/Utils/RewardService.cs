using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 전투 결과를 보상 재화로 환산(표시용 예상액)하고, 실제 지급은 서버에 맡기는 창구
/// </summary>
public static class RewardService
{
    // 전투 보상 계수는 Reward 표(ownerType=Battle)가 소유한다 — 앨범·모험·랭크와 같은 자리다.
    // 표가 비면 매판 0을 지급하게 되므로 초기화가 RewardSpec.TryValidateRequired로 먼저 막는다.
    static bool s_warnedMissing;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => s_warnedMissing = false;

    // 계수 한 칸. 표에 없으면 0을 돌려주되 세션당 1회 소리를 낸다(초기화 검사를 통과했다면 여기 오지 않는다).
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
    /// 전투 종료 시점에 서버 지급을 띄운다(응답을 기다리지 않는다). 서버가 확정한 액수가 도착하면
    /// 그때 로비 획득 연출용 캐리어가 선다 — 실패하면 캐리어를 세우지 않는다.
    ///
    /// <para><paramref name="_matchId"/> 는 findAiMatch 가 봉인한 매치 신원이다. 서버가 그 문서를 낙인해
    /// 한 판당 지급을 한 번으로 끊으므로, 매치를 열지 않은 전투(튜토리얼·디버그)는 지급 대상이 아니다.</para>
    /// </summary>
    public static void GrantBattleRewardAsync(bool _won, int _remainingCards, string _matchId)
    {
        if (string.IsNullOrEmpty(_matchId))
        {
            // 여기서 그냥 부르면 서버가 MatchUnverified 로 접는다 — 왕복만 버리고 결과는 같다.
            // 튜토리얼 전투가 이 갈래다(시나리오 덱이라 lockDeck을 통과할 수 없어 매치를 열지 않는다).
            Debug.Log("[RewardService] 서버 매치가 없는 전투라 보상 지급을 건너뛴다.");
            return;
        }

        ClaimBattleRewardAsync(_won, _remainingCards, _matchId).Forget();
    }

    // static이라 전투 씬이 언로드돼도 이 호출은 계속 산다(취소 토큰을 붙이지 않는 이유).
    // 여기서 재시도하지 않는다 — 응답만 잃은 갈래는 ServerSaveCommands가 같은 txId로 한 번 다시 태우고,
    // 그 밖의 실패를 새 호출로 다시 걸면 서버는 그것을 이미 낙인된 매치로 보고 AlreadyClaimed로 접는다.
    static async UniTaskVoid ClaimBattleRewardAsync(bool _won, int _remainingCards, string _matchId)
    {
        CurrencyGain t_granted = await BattleRewardCommand.ClaimAsync(_won, _remainingCards, _matchId);

        // 들어오지도 않은 재화의 획득 연출을 로비에서 돌리지 않는다.
        if (!t_granted.HasAmount) return;

        BattleRewardHandoff.Set(t_granted);
    }
}
