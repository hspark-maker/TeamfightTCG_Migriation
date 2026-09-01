using Cysharp.Threading.Tasks;
using UnityEngine;

// 전투 보상 지급을 서버에 묻는 단일 창구.
// 액수의 진실원은 서버 claimBattleReward 다 — RewardService.CalculateReward 는 결과 팝업이 먼저 보여줄
// 예상액일 뿐이고, 둘이 엇갈렸을 때 이기는 쪽은 언제나 서버다.
internal static class BattleRewardCommand
{
    const string COMMAND_NAME = "claimBattleReward";

    /// <summary>싱글 전투 한 판의 보상 지급을 요청한다. 성공하면 <b>서버가 실제로 지급한 한 줄</b>이 돌아오고,
    /// 실패·거절은 전부 <see cref="CurrencyGain.None"/> 이다.
    ///
    /// <para><paramref name="_matchId"/> 는 findAiMatch 가 봉인한 매치 신원이다. 서버는 이것으로 지급 자격을
    /// 한 판당 한 번으로 끊으므로 빠뜨리면 거절된다 — 부르는 쪽이 매치 없이 오지 않게 막아야 한다.</para></summary>
    // 부르는 쪽이 전투 종료 흐름이라 예외를 밖으로 내보내지 않는다 — 지급 실패가 결과 화면을 끊으면 안 된다.
    internal static async UniTask<CurrencyGain> ClaimAsync(bool _won, int _remaining, string _matchId)
    {
        try
        {
            var t_result = await ServerSaveCommands.InvokeAsync<BattleRewardResult>(
                COMMAND_NAME,
                new
                {
                    env = ContentProfileConfig.Active.CloudEnvId,
                    won = _won,
                    remaining = _remaining,
                    matchId = _matchId,
                });

            return ToGain(t_result);
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            // 표가 비었거나(RewardUnavailable) · 매치 자격이 없거나(MatchUnverified) · 이미 받아 간
            // 판이다(AlreadyClaimed). 어느 쪽이든 세션은 멀쩡하고 이번 판의 지급만 없다.
            Debug.LogWarning($"[BattleRewardCommand] 전투 보상을 서버가 거절했다 — {t_rejected.Message}");
            return CurrencyGain.None;
        }
        catch (ServerAdoptionException t_adoption)
        {
            // 세션은 이미 접혔고 팝업은 CloudSyncStatusWatcher 담당이다 — 여기서 표면을 두 번 칠하지 않는다.
            Debug.LogWarning($"[BattleRewardCommand] 응답 채택이 세션을 접었다 — {t_adoption.Message}");
            return CurrencyGain.None;
        }
        catch (System.Exception t_exception)
        {
            Debug.LogError($"[BattleRewardCommand] {COMMAND_NAME} 실패 — {t_exception.GetBaseException().Message}");
            return CurrencyGain.None;
        }
    }

    static CurrencyGain ToGain(BattleRewardResult _result)
    {
        ClaimRewardGain t_granted = _result?.Granted;
        if (t_granted == null || t_granted.Amount <= 0) return CurrencyGain.None;

        if (!CurrencyCode.TryParse(t_granted.Currency, out ECurrencyType t_type))
        {
            Debug.LogError($"[BattleRewardCommand] 알 수 없는 재화 '{t_granted.Currency}' — 잔액은 이미 서버가 갈아끼웠고 연출만 건너뛴다.");
            return CurrencyGain.None;
        }

        return new CurrencyGain(t_type, t_granted.Amount);
    }
}
