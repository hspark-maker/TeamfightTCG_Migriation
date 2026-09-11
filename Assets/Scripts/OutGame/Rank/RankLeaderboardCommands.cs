using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>랭킹 화면 재진입의 조회를 공유한다. 캐시는 현재 계정·랭크 기준으로 30초만 유지한다.</summary>
internal static class RankLeaderboardCommands
{
    const double CacheSeconds = 30;
    static int s_session;
    static Request s_inFlight;
    static Request s_cachedRequest;
    static RankLeaderboardResult s_cached;
    static double s_validUntil;

    sealed class Request
    {
        internal readonly string Env = ContentProfileConfig.Active?.CloudEnvId;
        internal readonly string UserId = FirebaseAuthService.Instance.UserId;
        internal readonly string SeasonId = RankManager.SeasonId;
        internal readonly long Points = RankManager.Points;
        internal readonly int Session = s_session;
        internal readonly UniTaskCompletionSource<RankLeaderboardResult> Completion = new UniTaskCompletionSource<RankLeaderboardResult>();

        internal bool IsCurrent => Session == s_session && Env == ContentProfileConfig.Active?.CloudEnvId &&
            UserId == FirebaseAuthService.Instance.UserId && SeasonId == RankManager.SeasonId && Points == RankManager.Points;
    }

    internal static UniTask<RankLeaderboardResult> RefreshAsync(bool _force = false)
    {
        if (string.IsNullOrEmpty(ContentProfileConfig.Active?.CloudEnvId) ||
            string.IsNullOrEmpty(FirebaseAuthService.Instance.UserId))
            return UniTask.FromResult<RankLeaderboardResult>(null);

        // 닫힌 화면의 요청도 살아 있다. 다시 열거나 재시도를 눌러도 같은 왕복을 기다린다.
        if (s_inFlight != null && s_inFlight.IsCurrent) return s_inFlight.Completion.Task;
        if (!_force && s_cached != null && s_cachedRequest.IsCurrent &&
            Time.realtimeSinceStartupAsDouble < s_validUntil &&
            s_cached.Season.EndAtMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            return UniTask.FromResult(s_cached);

        var t_request = new Request();
        s_cached = null;
        s_cachedRequest = null;
        s_inFlight = t_request;
        ReadAsync(t_request).Forget();
        return t_request.Completion.Task;
    }

    static async UniTaskVoid ReadAsync(Request _request)
    {
        RankLeaderboardResult t_result = null;
        try
        {
            var t_response = await ServerSaveCommands.InvokeReadOnlyAsync<RankLeaderboardResult>(
                "getRankLeaderboard", new { env = _request.Env });
            if (!_request.IsCurrent || !ReferenceEquals(s_inFlight, _request)) return;
            if (t_response?.Entries == null || t_response.Self == null ||
                string.IsNullOrEmpty(t_response.Season?.SeasonId))
                throw new InvalidOperationException("Ranking response is incomplete.");

            t_result = t_response;
            s_cached = t_response;
            s_cachedRequest = _request;
            s_validUntil = Time.realtimeSinceStartupAsDouble + CacheSeconds;
        }
        catch (Exception t_error)
        {
            if (_request.IsCurrent)
                Debug.LogWarning($"[RankLeaderboard] Query failed: {t_error.GetBaseException().Message}");
        }
        finally
        {
            if (ReferenceEquals(s_inFlight, _request)) s_inFlight = null;
            _request.Completion.TrySetResult(t_result);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void ResetSession()
    {
        unchecked { s_session++; }
        var t_previous = s_inFlight;
        s_inFlight = null;
        s_cached = null;
        s_cachedRequest = null;
        s_validUntil = 0;
        t_previous?.Completion.TrySetResult(null);
    }
}
