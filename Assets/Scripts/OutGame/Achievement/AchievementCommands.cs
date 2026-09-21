using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>영구 업적 조회·수령. 계정 변경과 조회/수령 경합으로 이전 상태가 덮이지 않게 한다.</summary>
internal static class AchievementCommands
{
    const double RefreshCacheSeconds = 30;
    static readonly HashSet<string> s_claims = new HashSet<string>();
    static UniTaskCompletionSource<bool> s_refresh;
    static int s_session;
    static int s_claimGeneration;
    static string s_env;
    static string s_userId;
    static double s_validUntil;
    static long s_cachedStateVersion;
    static long s_cachedSaveRevision;

    internal static bool IsInFlight(string _id) => _id != null && s_claims.Contains(_id);

    internal static void ResetSession()
    {
        unchecked { s_session++; s_claimGeneration++; }
        var t_previous = s_refresh;
        s_refresh = null;
        s_claims.Clear();
        s_env = s_userId = null;
        s_validUntil = 0;
        AchievementManager.ResetSession();
        t_previous?.TrySetResult(false);
    }

    internal static UniTask<bool> RefreshAsync(bool _force = false)
    {
        string t_env = ContentProfileConfig.Active?.CloudEnvId;
        string t_userId = FirebaseAuthService.Instance.UserId;
        if (string.IsNullOrEmpty(t_env) || string.IsNullOrEmpty(t_userId)) return UniTask.FromResult(false);
        if (s_env != t_env || s_userId != t_userId)
        {
            ResetSession();
            s_env = t_env;
            s_userId = t_userId;
        }
        if (s_refresh != null) return s_refresh.Task;
        if (s_claims.Count > 0) return UniTask.FromResult(false);
        if (!_force && AchievementManager.IsReady && Time.realtimeSinceStartupAsDouble < s_validUntil &&
            s_cachedStateVersion == AchievementManager.StateVersion &&
            s_cachedSaveRevision == PlayerSaveCloud.Revision && !PlayerSaveCloud.HasPendingUpload)
            return UniTask.FromResult(true);

        var t_gate = new UniTaskCompletionSource<bool>();
        s_refresh = t_gate;
        s_validUntil = 0;
        RefreshCoreAsync(t_gate, t_env, t_userId, s_session).Forget();
        return t_gate.Task;
    }

    static async UniTask RefreshCoreAsync(UniTaskCompletionSource<bool> _gate, string _env, string _uid, int _session)
    {
        bool t_success = false;
        try
        {
            for (int t_attempt = 0; t_attempt < 2; t_attempt++)
            {
                if (s_claims.Count > 0 || _session != s_session) return;
                int t_claimGeneration = s_claimGeneration;
                long t_version = AchievementManager.StateVersion;
                long t_saveRevision = PlayerSaveCloud.Revision;
                bool t_pendingUpload = PlayerSaveCloud.HasPendingUpload;
                var t_result = await ServerSaveCommands.InvokeReadOnlyAsync<AchievementGetResponse>(
                    "getAchievements", new { env = _env });
                if (_session != s_session || _env != ContentProfileConfig.Active?.CloudEnvId ||
                    _uid != FirebaseAuthService.Instance.UserId) return;
                if (t_result?.Achievements == null || t_result.Definitions == null) return;
                if (s_claims.Count > 0 || t_claimGeneration != s_claimGeneration ||
                    t_version != AchievementManager.StateVersion || t_saveRevision != PlayerSaveCloud.Revision)
                    continue;

                if (t_result.Statistics != null) PlayerStatisticsManager.Adopt(t_result.Statistics);
                AchievementManager.Adopt(t_result.Achievements, t_result.Definitions);
                s_cachedStateVersion = AchievementManager.StateVersion;
                s_cachedSaveRevision = t_saveRevision;
                s_validUntil = !t_pendingUpload && !PlayerSaveCloud.HasPendingUpload
                    ? Time.realtimeSinceStartupAsDouble + RefreshCacheSeconds : 0;
                t_success = AchievementManager.IsReady;
                return;
            }
        }
        catch (Exception t_error)
        {
            Debug.LogWarning($"[AchievementCommands] Query failed: {t_error.GetBaseException().Message}");
        }
        finally
        {
            if (ReferenceEquals(s_refresh, _gate)) s_refresh = null;
            _gate.TrySetResult(t_success);
        }
    }

    internal static async UniTask<RewardClaimOutcome> ClaimAsync(string _achievementId)
    {
        if (string.IsNullOrWhiteSpace(_achievementId)) return default;
        string t_id = _achievementId.Trim();
        if (!s_claims.Add(t_id)) return default;
        int t_session = s_session;
        bool t_refreshOnFailure = false;
        unchecked { s_claimGeneration++; }
        s_validUntil = 0;
        AchievementManager.NotifyCommandStateChanged();
        try
        {
            var t_result = await ServerSaveCommands.InvokeAsync<ClaimAchievementResult>(
                "claimAchievement", new { env = ContentProfileConfig.Active.CloudEnvId, achievementId = t_id });
            if (t_session != s_session || t_result == null) return default;
            return new RewardClaimOutcome(AchievementManager.ToGains(t_result.Granted));
        }
        catch (Exception t_error)
        {
            t_refreshOnFailure = true;
            Debug.LogWarning($"[AchievementCommands] Claim {t_id} failed: {t_error.GetBaseException().Message}");
            return default;
        }
        finally
        {
            if (t_session == s_session)
            {
                s_claims.Remove(t_id);
                AchievementManager.NotifyCommandStateChanged();
                // 응답 유실·다른 기기의 선행 수령도 서버 상태로 복구한다.
                // 진행 중 표시를 걷은 뒤 조회해야 조회 자체가 생략되지 않는다.
                if (t_refreshOnFailure) RefreshAsync(_force: true).Forget();
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => ResetSession();
}
