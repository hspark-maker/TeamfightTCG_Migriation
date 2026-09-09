using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>미션 조회·수령 callable의 클라이언트 단일 창구.</summary>
internal static class MissionCommands
{
    const string GET_COMMAND = "getMissions";
    const string CLAIM_COMMAND = "claimMission";

    static readonly HashSet<string> s_inFlightClaims = new HashSet<string>();
    static int s_claimGeneration;

    internal static bool IsInFlight(string _missionId)
        => !string.IsNullOrEmpty(_missionId) && s_inFlightClaims.Contains(_missionId);

    /// <summary>정의와 최신 상태를 조회한다. 실패는 부가 기능 실패이므로 게임 세션을 막지 않는다.</summary>
    internal static async UniTask<bool> RefreshAsync()
    {
        // 수령 중 읽은 봉투가 수령 완료 뒤 도착하면 claimed=true를 옛 false로 되돌린다.
        // 시작부터 겹친 조회는 생략하고, 왕복 중 수령이 시작된 경우는 세대 대조로 응답을 버린다.
        if (s_inFlightClaims.Count > 0) return false;
        int t_claimGeneration = s_claimGeneration;
        try
        {
            MissionGetResponse t_result = await ServerSaveCommands.InvokeReadOnlyAsync<MissionGetResponse>(
                GET_COMMAND, new { env = ContentProfileConfig.Active.CloudEnvId });
            if (t_result?.Missions == null)
            {
                Debug.LogWarning("[MissionCommands] getMissions did not return a state.");
                return false;
            }
            if (s_inFlightClaims.Count > 0 || t_claimGeneration != s_claimGeneration)
            {
                Debug.Log("[MissionCommands] Discarding a mission query response that overlapped with a claim.");
                return false;
            }

            MissionManager.Adopt(t_result.Missions, t_result.Definitions);
            return true;
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[MissionCommands] Mission query failed — {t_exception.GetBaseException().Message}");
            return false;
        }
    }

    /// <summary>수령을 서버에 요청한다. 로컬 진행도·낙인은 미리 바꾸지 않고 서버 응답 채택만 따른다.</summary>
    internal static async UniTask<ClaimMissionResult> ClaimAsync(string _missionId)
    {
        if (string.IsNullOrWhiteSpace(_missionId)) return null;
        string t_missionId = _missionId.Trim();
        if (!s_inFlightClaims.Add(t_missionId)) return null;
        unchecked { s_claimGeneration++; }
        MissionManager.NotifyCommandStateChanged();

        try
        {
            ClaimMissionResult t_result = await ServerSaveCommands.InvokeAsync<ClaimMissionResult>(
                CLAIM_COMMAND,
                new { env = ContentProfileConfig.Active.CloudEnvId, missionId = t_missionId });
            if (t_result != null && t_result.GrantedPassExp > 0L) PassCommands.Invalidate();
            Debug.Log($"[MissionCommands] {t_missionId} claimed — {t_result?.Granted?.Count ?? 0} currency line(s), pass exp {t_result?.GrantedPassExp ?? 0}");
            return t_result;
        }
        catch (ServerCommandRejectedException t_rejected)
        {
            Debug.LogWarning($"[MissionCommands] {t_missionId} claim rejected ({t_rejected.Reason}) — {t_rejected.Message}");
            return null;
        }
        catch (ServerAdoptionException t_adoption)
        {
            Debug.LogWarning($"[MissionCommands] Failed to adopt the claim response — {t_adoption.Message}");
            return null;
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[MissionCommands] claimMission failed — {t_exception.GetBaseException().Message}");
            return null;
        }
        finally
        {
            s_inFlightClaims.Remove(t_missionId);
            MissionManager.NotifyCommandStateChanged();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_inFlightClaims.Clear();
        s_claimGeneration = 0;
    }
}
