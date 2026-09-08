using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>배틀패스 조회·수령 callable 의 클라이언트 단일 창구.</summary>
internal static class PassCommands
{
    const string GET_COMMAND = "getPass";
    const string CLAIM_COMMAND = "claimPassReward";

    static readonly HashSet<int> s_inFlightClaims = new HashSet<int>();

    internal static bool IsInFlight(int _level) => s_inFlightClaims.Contains(_level);

    /// <summary>시즌·곡선·진행도를 한 번에 받는다. 실패는 부가 기능 실패라 세션을 막지 않는다.</summary>
    internal static async UniTask<bool> RefreshAsync()
    {
        // 수령 중 읽은 봉투가 수령 완료 뒤 도착하면 낙인을 옛 값으로 되돌린다(미션과 같은 함정).
        if (s_inFlightClaims.Count > 0) return false;

        try
        {
            PassGetResponse t_result = await ServerSaveCommands.InvokeReadOnlyAsync<PassGetResponse>(
                GET_COMMAND, new { env = ContentProfileConfig.Active.CloudEnvId });
            if (t_result == null)
            {
                Debug.LogWarning("[PassCommands] getPass returned nothing.");
                return false;
            }
            if (s_inFlightClaims.Count > 0) return false;

            PassManager.Adopt(t_result);
            return true;
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[PassCommands] Pass query failed — {t_exception.GetBaseException().Message}");
            return false;
        }
    }

    /// <summary>레벨 하나의 무료 트랙 보상을 수령한다. 진행 상태는 서버 응답 채택만 따른다.</summary>
    internal static async UniTask<ClaimPassRewardResult> ClaimAsync(int _level)
    {
        if (_level <= 0) return null;
        if (!s_inFlightClaims.Add(_level)) return null;
        PassManager.NotifyCommandStateChanged();

        try
        {
            ClaimPassRewardResult t_result = await ServerSaveCommands.InvokeAsync<ClaimPassRewardResult>(
                CLAIM_COMMAND,
                new { env = ContentProfileConfig.Active.CloudEnvId, level = _level });
            PassManager.Adopt(t_result?.Progress);
            Debug.Log($"[PassCommands] Level {_level} claimed — {t_result?.Granted?.Count ?? 0} currency line(s)");
            return t_result;
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            Debug.LogWarning($"[PassCommands] Level {_level} claim rejected ({t_rejected.Reason}) — {t_rejected.Message}");
            return null;
        }
        catch (ServerAdoptionException t_adoption)
        {
            Debug.LogWarning($"[PassCommands] Failed to adopt the claim response — {t_adoption.Message}");
            return null;
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[PassCommands] claimPassReward failed — {t_exception.GetBaseException().Message}");
            return null;
        }
        finally
        {
            s_inFlightClaims.Remove(_level);
            PassManager.NotifyCommandStateChanged();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => s_inFlightClaims.Clear();
}
