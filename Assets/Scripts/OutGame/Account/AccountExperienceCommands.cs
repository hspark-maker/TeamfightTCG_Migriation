using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

internal sealed class ClaimBattleExperienceResult : ServerCommandResult
{
    [JsonProperty("matchId")] public string MatchId { get; set; }
    [JsonProperty("granted")] public List<ClaimRewardGain> Granted { get; set; }
    [JsonProperty("cards")] public List<OpenPackCard> Cards { get; set; }
    [JsonProperty("packs")] public List<ClaimRewardPack> Packs { get; set; }
}

internal static class AccountExperienceCommands
{
    /// <summary>실패하면 제출 큐를 유지한다. 승패와 지급량은 서버의 확정 매치에서만 읽는다.</summary>
    internal static async UniTask<bool> ClaimBattleAsync(string _env, string _matchId)
    {
        try
        {
            ClaimBattleExperienceResult t_result = await ServerSaveCommands.InvokeAsync<ClaimBattleExperienceResult>(
                "claimBattleExperience", new { env = _env, matchId = _matchId });
            if (t_result.MatchId != _matchId)
                throw new InvalidOperationException("Battle experience response has a different matchId.");
            AccountRewardHandoff.Enqueue(_matchId, t_result.Granted, t_result.Cards, t_result.Packs, t_result.AccountExperience, t_result.Cosmetics, t_result.Titles);
            return true;
        }
        catch (ServerCommandRejectedException)
        {
            // 소유권 없는/만료된 매치는 영구 실패다. 제출 큐의 기존 거절 처리로 로딩을 종료한다.
            throw;
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[AccountExperience] Battle experience collection deferred (match={_matchId}): " +
                             t_exception.GetBaseException().Message);
            return false;
        }
    }
}
