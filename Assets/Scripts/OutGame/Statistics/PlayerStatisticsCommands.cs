using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>통계 조회의 단일 창구. 중복 조회를 공유하고 다른 계정의 늦은 응답을 버린다.</summary>
internal static class PlayerStatisticsCommands
{
    const double RefreshCacheSeconds = 30;
    static UniTaskCompletionSource<bool> s_refresh;
    static int s_session;
    static string s_env;
    static string s_userId;
    static double s_validUntil;
    static long s_cachedStateVersion;
    static long s_cachedSaveRevision;

    internal static void ResetSession()
    {
        unchecked { s_session++; }
        var t_previous = s_refresh;
        s_refresh = null;
        s_env = s_userId = null;
        s_validUntil = 0;
        PlayerStatisticsManager.ResetSession();
        t_previous?.TrySetResult(false);
    }

    internal static UniTask<bool> RefreshAsync(bool _force = false)
    {
        string t_env = ContentProfileConfig.Active?.CloudEnvId;
        string t_uid = FirebaseAuthService.Instance.UserId;
        if (string.IsNullOrEmpty(t_env) || string.IsNullOrEmpty(t_uid)) return UniTask.FromResult(false);
        if (s_env != t_env || s_userId != t_uid)
        {
            // 첫 조회 전 팩·업적 응답으로 받은 동일 세션 통계는 보존한다.
            if (s_env != null || s_userId != null) ResetSession();
            s_env = t_env;
            s_userId = t_uid;
        }
        if (s_refresh != null) return s_refresh.Task;
        if (!_force && PlayerStatisticsManager.IsReady && Time.realtimeSinceStartupAsDouble < s_validUntil &&
            s_cachedStateVersion == PlayerStatisticsManager.StateVersion &&
            s_cachedSaveRevision == PlayerSaveCloud.Revision && !PlayerSaveCloud.HasPendingUpload)
            return UniTask.FromResult(true);

        var t_gate = new UniTaskCompletionSource<bool>();
        s_refresh = t_gate;
        s_validUntil = 0;
        RefreshCoreAsync(t_gate, t_env, t_uid, s_session).Forget();
        return t_gate.Task;
    }

    static async UniTask RefreshCoreAsync(UniTaskCompletionSource<bool> _gate, string _env, string _uid, int _session)
    {
        bool t_success = false;
        long t_saveRevision = PlayerSaveCloud.Revision;
        bool t_pendingUpload = PlayerSaveCloud.HasPendingUpload;
        try
        {
            var t_result = await ServerSaveCommands.InvokeReadOnlyAsync<PlayerStatisticsGetResponse>(
                "getPlayerStatistics", new { env = _env });
            if (_session != s_session || _env != ContentProfileConfig.Active?.CloudEnvId ||
                _uid != FirebaseAuthService.Instance.UserId) return;
            if (t_result?.Statistics == null) return;

            bool t_adopted = PlayerStatisticsManager.Adopt(t_result.Statistics);
            if (t_result.Achievements != null) AchievementManager.Adopt(t_result.Achievements);
            t_success = PlayerStatisticsManager.IsReady;
            s_cachedStateVersion = PlayerStatisticsManager.StateVersion;
            s_cachedSaveRevision = t_saveRevision;
            s_validUntil = t_adopted && !t_pendingUpload && !PlayerSaveCloud.HasPendingUpload &&
                t_saveRevision == PlayerSaveCloud.Revision
                ? Time.realtimeSinceStartupAsDouble + RefreshCacheSeconds : 0;
        }
        catch (Exception t_error)
        {
            Debug.LogWarning($"[PlayerStatisticsCommands] Query failed: {t_error.GetBaseException().Message}");
        }
        finally
        {
            if (ReferenceEquals(s_refresh, _gate)) s_refresh = null;
            _gate.TrySetResult(t_success);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => ResetSession();
}
