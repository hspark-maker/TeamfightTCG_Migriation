using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>미션 조회·수령 callable의 클라이언트 단일 창구.</summary>
internal static class MissionCommands
{
    const string GET_COMMAND = "getMissions";
    const string CLAIM_COMMAND = "claimMission";
    const double RefreshCacheSeconds = 30;

    static readonly HashSet<string> s_inFlightClaims = new HashSet<string>();
    static int s_claimGeneration;
    static bool s_debugResetInFlight;
    static UniTaskCompletionSource<bool> s_refreshInFlight;
    static int s_refreshGeneration;
    static int s_sessionGeneration;
    static double s_refreshValidUntil;
    static long s_cachedSaveRevision;
    static string s_cachedEnv;
    static string s_cachedUserId;

    internal static void Invalidate()
    {
        unchecked { s_refreshGeneration++; }
        s_refreshValidUntil = 0;
    }

    internal static void ResetSession()
    {
        unchecked { s_sessionGeneration++; s_claimGeneration++; }
        Invalidate();
        s_refreshInFlight?.TrySetResult(false);
        s_refreshInFlight = null;
        s_cachedEnv = s_cachedUserId = null;
        s_inFlightClaims.Clear();
        s_debugResetInFlight = false;
    }

    internal static bool IsInFlight(string _missionId)
        => s_debugResetInFlight || (!string.IsNullOrEmpty(_missionId) && s_inFlightClaims.Contains(_missionId));

    /// <summary>정의와 최신 상태를 조회한다. 실패는 부가 기능 실패이므로 게임 세션을 막지 않는다.</summary>
    internal static UniTask<bool> RefreshAsync(bool _force = false)
    {
        string t_env = ContentProfileConfig.Active?.CloudEnvId;
        string t_userId = FirebaseAuthService.Instance.UserId;
        if (string.IsNullOrEmpty(t_env) || string.IsNullOrEmpty(t_userId)) return UniTask.FromResult(false);
        // 初期化と画面の要求は同じ完了を待つ。forceも進行中の同じ要求を共有する。
        if (s_refreshInFlight != null) return s_refreshInFlight.Task;
        if (s_debugResetInFlight || s_inFlightClaims.Count > 0) return UniTask.FromResult(false);
        long t_now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!_force && MissionManager.IsReady && s_cachedEnv == t_env && s_cachedUserId == t_userId &&
            Time.realtimeSinceStartupAsDouble < s_refreshValidUntil &&
            !PlayerSaveCloud.HasPendingUpload && s_cachedSaveRevision == PlayerSaveCloud.Revision &&
            MissionManager.DailyResetAtMs > t_now && MissionManager.WeeklyResetAtMs > t_now)
            return UniTask.FromResult(true);

        var t_gate = new UniTaskCompletionSource<bool>();
        s_refreshInFlight = t_gate;
        RefreshCoreAsync(t_gate, t_env, t_userId, s_sessionGeneration).Forget();
        return t_gate.Task;
    }

    static async UniTask RefreshCoreAsync(UniTaskCompletionSource<bool> _gate, string _env, string _userId, int _session)
    {
        bool t_success = false;
        try
        {
            // 왕복 중 상태가 바뀌면 한 번만 다시 읽는다. 계속 변경 중이면 다음 요청이 재시도한다.
            for (int t_attempt = 0; t_attempt < 2; t_attempt++)
            {
                if (s_debugResetInFlight || s_inFlightClaims.Count > 0) return;
                t_success = await ReadAndAdoptAsync(_env, _userId, _session);
                if (t_success || _session != s_sessionGeneration) return;
                // 무효화로 버린 응답만 다시 읽는다. 통신 실패는 별도 자동 재시도를 만들지 않는다.
                if (!s_retryInvalidatedRead) return;
            }
        }
        finally
        {
            if (ReferenceEquals(s_refreshInFlight, _gate)) s_refreshInFlight = null;
            _gate.TrySetResult(t_success);
        }
    }

    static bool s_retryInvalidatedRead;

    static async UniTask<bool> ReadAndAdoptAsync(string _env, string _userId, int _session)
    {
        s_retryInvalidatedRead = false;
        // 수령 중 읽은 봉투가 수령 완료 뒤 도착하면 claimed=true를 옛 false로 되돌린다.
        // 시작부터 겹친 조회는 생략하고, 왕복 중 수령이 시작된 경우는 세대 대조로 응답을 버린다.
        if (s_debugResetInFlight || s_inFlightClaims.Count > 0) return false;
        int t_claimGeneration = s_claimGeneration;
        int t_refreshGeneration = s_refreshGeneration;
        long t_stateVersion = MissionManager.StateVersion;
        long t_saveRevision = PlayerSaveCloud.Revision;
        bool t_pendingUpload = PlayerSaveCloud.HasPendingUpload;
        try
        {
            MissionGetResponse t_result = await ServerSaveCommands.InvokeReadOnlyAsync<MissionGetResponse>(
                GET_COMMAND, new { env = _env });
            if (_session != s_sessionGeneration || _env != ContentProfileConfig.Active?.CloudEnvId ||
                _userId != FirebaseAuthService.Instance.UserId) return false;
            if (t_result?.Missions == null)
            {
                Debug.LogWarning("[MissionCommands] getMissions did not return a state.");
                return false;
            }
            if (s_debugResetInFlight || s_inFlightClaims.Count > 0 || t_claimGeneration != s_claimGeneration ||
                t_refreshGeneration != s_refreshGeneration || t_stateVersion != MissionManager.StateVersion ||
                t_saveRevision != PlayerSaveCloud.Revision)
            {
                s_retryInvalidatedRead = true;
                Debug.Log("[MissionCommands] Discarding a mission query response that overlapped with a state change.");
                return false;
            }

            s_cachedEnv = _env;
            s_cachedUserId = _userId;
            s_cachedSaveRevision = t_saveRevision;
            // 업로드 전 조회는 예전 가이드 상태일 수 있다. 다음 화면 진입에서 다시 확인한다.
            s_refreshValidUntil = !t_pendingUpload && !PlayerSaveCloud.HasPendingUpload
                ? Time.realtimeSinceStartupAsDouble + RefreshCacheSeconds : 0;
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
        if (s_debugResetInFlight) return null;
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
            if (t_result != null && t_result.GrantedPassExp > 0L)
                PassCommands.ApplyMissionProgress(t_result.Pass);
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

    /// <summary>서버의 테스트 환경에서 일일 진행도·수령 낙인을 비우고 응답 상태를 채택한다.</summary>
    internal static async UniTask<bool> ResetDailyForDebugAsync()
    {
        if (ContentProfileConfig.Active.CloudEnvId != "test")
        {
            Debug.LogWarning("[MissionCommands] Daily reset is available on the test env only.");
            return false;
        }
        if (s_debugResetInFlight || s_inFlightClaims.Count > 0)
        {
            Debug.LogWarning("[MissionCommands] Wait for the current mission command before resetting.");
            return false;
        }

        s_debugResetInFlight = true;
        unchecked { s_claimGeneration++; }
        MissionManager.NotifyCommandStateChanged();
        try
        {
            // 세이브·지갑을 변경하지 않는 디버그 명령이다. 실패를 전체 저장 세션 차단으로 전파하지 않는다.
            ServerCommandResult t_result = await ServerSaveCommands.InvokeReadOnlyAsync<ServerCommandResult>(
                "devResetDailyMissions", new { env = ContentProfileConfig.Active.CloudEnvId });
            if (t_result?.Missions == null)
                throw new InvalidOperationException("Daily reset did not return a mission state.");
            MissionManager.Adopt(t_result.Missions);
            return true;
        }
        finally
        {
            s_debugResetInFlight = false;
            MissionManager.NotifyCommandStateChanged();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        ResetSession();
    }
}
