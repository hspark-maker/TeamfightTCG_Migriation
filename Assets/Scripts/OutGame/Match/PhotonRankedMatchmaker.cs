using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;

public sealed class PhotonRankedMatchmaker : IMatchmaker
{
    const int REQUIRED_PLAYERS = 2;
    const float PROFILE_EXCHANGE_SECONDS = 2f;
    // 한 사이클에 시도할 방 개수. 스냅샷이 낡아 실패하는 게 정상이라 하나로는 부족하고,
    // 너무 많으면 로비 갱신이 낡은 채로 헛조인만 반복한다.
    const int MAX_JOIN_ATTEMPTS = 3;
    // 조인 뒤 상대가 실제로 보일 때까지의 유예. 원격 플레이어 목록 동기가 이보다 늦으면
    // 성립한 매칭도 버린다 — 1초는 모바일 회선에서 짧다.
    const float JOIN_CONFIRM_SECONDS = 2.5f;
    const string OPPONENT_PLACEHOLDER_NAME = "상대";

    enum EOutcome
    {
        Matched,
        NoOpponent,
        Failed,
        Canceled,
    }

    readonly IMatchmaker m_aiFallback;

    public PhotonRankedMatchmaker(IMatchmaker _aiFallback)
    {
        this.m_aiFallback = _aiFallback;
    }

    public async UniTask<MatchOpponent?> FindOpponentAsync(CancellationToken _ct)
    {
        (EOutcome outcome, MatchOpponent opponent) t_real = await MatchRealOpponentAsync(_ct);
        if (t_real.outcome == EOutcome.Matched) return t_real.opponent;
        if (t_real.outcome != EOutcome.NoOpponent) return null;
        return await this.m_aiFallback.FindOpponentAsync(_ct);
    }

    async UniTask<(EOutcome, MatchOpponent)> MatchRealOpponentAsync(CancellationToken _ct)
    {
        NetworkSession t_session = NetworkSession.Instance;
        if (t_session == null)
        {
            Debug.LogWarning("[Matchmaking] NetworkSession이 없어 실매칭을 건너뛰고 AI로 진행한다.");
            return (EOutcome.NoOpponent, default);
        }
        if (_ct.IsCancellationRequested) return (EOutcome.Canceled, default);

        // 티어는 매칭 축이라 서버 값으로 시작한다 — 로컬 캐시는 초기화 이후 갱신되지 않는다.
        MatchmakingProfile t_localProfile = await MatchmakingProfile.FromServerAsync();
        if (_ct.IsCancellationRequested) return (EOutcome.Canceled, default);
        (EOutcome outcome, MatchmakingProfile? knownOpponent) t_search =
            await SearchRankedOpponentAsync(t_session, t_localProfile, _ct);
        if (t_search.outcome != EOutcome.Matched)
            return (t_search.outcome, default);

        MatchmakingProfile t_opponentProfile = await ExchangeProfilesAsync(
            t_localProfile, t_search.knownOpponent, _ct);

        // 상대 티어는 서버 티켓으로만 인정한다 — 메시지·세션 프로퍼티에 실려 온 숫자는 자기신고다.
        t_opponentProfile = await MatchmakingProfile.VerifiedAsync(t_opponentProfile);
        if (_ct.IsCancellationRequested)
        {
            await t_session.Disconnect();
            return (EOutcome.Canceled, default);
        }

        DeckConfig.SetMultiplayer(true);
        EPreBattleSyncResult t_sync = await PreBattleMatchSync.RunAsync(_ct);
        if (t_sync == EPreBattleSyncResult.Success)
        {
            // 티어가 없어도(티켓 검증 실패) 닉네임은 살린다 — 등급만 비면 되지 이름까지 "상대"로 지울 이유가 없다.
            MatchProfile t_profile = !string.IsNullOrEmpty(t_opponentProfile.Nickname)
                ? MatchProfile.OfOpponent(t_opponentProfile)
                : MatchProfile.OfOpponent(OPPONENT_PLACEHOLDER_NAME, null);
            return (EOutcome.Matched, new MatchOpponent(t_profile, null));
        }

        bool t_canceled = t_sync == EPreBattleSyncResult.Canceled;
        NetworkGameController.Instance?.SendMatchAbort(
            t_canceled ? EMatchEndReason.OpponentLeftDuringInit : EMatchEndReason.InitError);
        DeckConfig.ResetMode();
        PreBattleMatchHandoff.Clear();
        await t_session.Disconnect();
        return (t_canceled ? EOutcome.Canceled : EOutcome.Failed, default);
    }

    static async UniTask<(EOutcome, MatchmakingProfile?)> SearchRankedOpponentAsync(
        NetworkSession _session, MatchmakingProfile _localProfile, CancellationToken _ct)
    {
        float t_startedAt = Time.realtimeSinceStartup;
        string t_myLastRoom = null;   // 내가 만들고 내린 방. 목록에 잠시 남아 스스로를 후보로 집는 걸 막는다.

        while (!_ct.IsCancellationRequested)
        {
            float t_elapsed = Time.realtimeSinceStartup - t_startedAt;
            if (t_elapsed >= MatchmakingPolicy.SearchSeconds)
                return (EOutcome.NoOpponent, null);

            if (!await _session.JoinRankedLobby())
            {
                Debug.LogWarning("[Matchmaking] Photon 랭크 로비 참가에 실패했다 — AI로 진행한다.");
                await _session.Disconnect();
                return (EOutcome.NoOpponent, null);
            }

            // 목록을 못 받은 채로 "후보 없음"이라 판정하면 양쪽이 방만 만들다 끝난다 — 한 번 더 기다린다.
            // 그래도 안 오면 방은 세운다. 안 세우면 상대가 나를 찾을 방법 자체가 없다.
            // (여기서 로비를 다시 붙지 않는 게 중요하다 — 재접속하면 오던 목록이 매번 버려진다.)
            if (!await WaitForSessionListAsync(_session, _ct)
                && !await WaitForSessionListAsync(_session, _ct))
            {
                if (_ct.IsCancellationRequested) break;
                Debug.LogWarning("[Matchmaking] 로비 목록을 못 받았다 — 후보 탐색 없이 방을 세운다.");
            }

            t_elapsed = Time.realtimeSinceStartup - t_startedAt;
            int t_window = MatchmakingPolicy.TierWindow(t_elapsed);
            List<(SessionInfo session, MatchmakingProfile profile)> t_candidates =
                CollectCandidates(_session, _localProfile.TierIndex, t_window, t_myLastRoom);

            if (t_candidates.Count == 0)
            {
                // 지터는 양쪽이 같은 박자로 "만들고 부수기"를 반복하지 않게 어긋내는 장치다.
                // 그동안 목록이 갱신되면 그 갱신본으로 다시 판정한다.
                float t_jitter = UnityEngine.Random.Range(0.15f, 0.75f);
                bool t_canceled = await UniTask.Delay(TimeSpan.FromSeconds(t_jitter), DelayType.Realtime,
                                                       cancellationToken: _ct).SuppressCancellationThrow();
                if (t_canceled) return (EOutcome.Canceled, null);

                t_elapsed = Time.realtimeSinceStartup - t_startedAt;
                t_window = MatchmakingPolicy.TierWindow(t_elapsed);
                t_candidates = CollectCandidates(_session, _localProfile.TierIndex, t_window, t_myLastRoom);
            }

            // 후보를 가까운 순으로 훑는다. 조인 실패(그새 찼음)는 정상 경로라 다음 후보로 넘어간다.
            bool t_joined = false;
            for (int i = 0; i < t_candidates.Count && i < MAX_JOIN_ATTEMPTS; i++)
            {
                if (_ct.IsCancellationRequested) return (EOutcome.Canceled, null);
                if (Time.realtimeSinceStartup - t_startedAt >= MatchmakingPolicy.SearchSeconds) break;

                if (!await _session.JoinRankedRoom(t_candidates[i].session.Name)) continue;
                if (await WaitForOpponentAsync(_session, JOIN_CONFIRM_SECONDS, _ct))
                    return (EOutcome.Matched, t_candidates[i].profile);

                t_joined = true;   // 방에는 들어갔다 — 러너가 로비를 떠났으니 다시 붙어야 한다
                await _session.Disconnect();
                break;
            }
            if (t_joined) continue;
            if (t_candidates.Count > 0) continue;   // 전부 조인 실패 — 로비부터 다시

            // 후보가 없다: 내 방을 세우고 상대가 들어오길 기다린다(찾는 쪽이 범위를 넓혀 나를 찾는다).
            if (!await _session.CreateRankedRoom(_localProfile))
            {
                await _session.Disconnect();
                continue;
            }
            t_myLastRoom = _session.CurrentSessionName;

            t_elapsed = Time.realtimeSinceStartup - t_startedAt;
            float t_wait = Mathf.Min(MatchmakingPolicy.SecondsUntilNextStage(t_elapsed),
                                     MatchmakingPolicy.SearchSeconds - t_elapsed);
            bool t_paired = t_wait > 0f && await WaitForOpponentAsync(_session, t_wait, _ct);
            if (t_paired) return (EOutcome.Matched, null);

            // 내리기 직전에 한 번 더 본다 — 상대의 조인이 날아오는 중이면 빈 방에 착지시키는 꼴이 된다.
            if (CountActivePlayers(_session) >= REQUIRED_PLAYERS) return (EOutcome.Matched, null);

            await _session.Disconnect();
        }

        await _session.Disconnect();
        return (EOutcome.Canceled, null);
    }

    /// <summary>허용 범위 안의 방을 **가까운 순으로 전부** 모은다. 하나만 고르면 안 되는 이유가 둘이다 —
    /// 목록은 스냅샷이라 조인 실패(그새 찼음)가 정상 경로이고, 최선 하나만 노리면 그때마다 로비부터 다시 돈다.
    /// 같은 거리끼리는 섞는다. 순서를 고정하면 여러 클라가 같은 방으로 몰려 전원 실패한다.
    /// 내가 직전에 만든 방은 서버 목록에 잠시 남으므로 제외한다(이미 죽은 방이라 조인이 반드시 실패한다).</summary>
    static List<(SessionInfo session, MatchmakingProfile profile)> CollectCandidates(
        NetworkSession _session, int _myTier, int _window, string _excludeName)
    {
        var t_found = new List<(SessionInfo session, MatchmakingProfile profile, int distance, int shuffle)>();

        IReadOnlyList<SessionInfo> t_sessions = _session.RankedSessions;
        for (int i = 0; i < t_sessions.Count; i++)
        {
            SessionInfo t_candidate = t_sessions[i];
            if (!t_candidate.IsOpen || !t_candidate.IsVisible
                || t_candidate.PlayerCount >= t_candidate.MaxPlayers)
                continue;
            if (!string.IsNullOrEmpty(_excludeName)
                && string.Equals(t_candidate.Name, _excludeName, StringComparison.Ordinal))
                continue;
            if (!NetworkSession.TryGetRankedProfile(t_candidate, out MatchmakingProfile t_profile))
                continue;

            int t_distance = Math.Abs(t_profile.TierIndex - _myTier);
            if (t_distance > _window) continue;
            t_found.Add((t_candidate, t_profile, t_distance, UnityEngine.Random.Range(0, int.MaxValue)));
        }

        t_found.Sort((a, b) => a.distance != b.distance
                             ? a.distance.CompareTo(b.distance)
                             : a.shuffle.CompareTo(b.shuffle));

        var t_result = new List<(SessionInfo, MatchmakingProfile)>(t_found.Count);
        foreach (var t_entry in t_found) t_result.Add((t_entry.session, t_entry.profile));
        return t_result;
    }

    /// <summary>로비 목록을 한 번이라도 받았는지. **"아직 못 받음"과 "후보 0건"은 다른 사건이다** —
    /// 둘을 같게 다루면 목록이 늦는 회선에서 양쪽이 매 사이클 방만 만들고 서로를 영영 못 본다.</summary>
    static async UniTask<bool> WaitForSessionListAsync(NetworkSession _session, CancellationToken _ct)
    {
        if (_session.HasRankedSessionList) return true;
        var t_tcs = new UniTaskCompletionSource();
        Action t_onUpdated = () => t_tcs.TrySetResult();
        _session.OnRankedSessionListChanged += t_onUpdated;
        try
        {
            using (_ct.Register(() => t_tcs.TrySetResult()))
            {
                await UniTask.WhenAny(t_tcs.Task,
                    UniTask.Delay(TimeSpan.FromSeconds(1f), DelayType.Realtime));
            }
        }
        finally
        {
            _session.OnRankedSessionListChanged -= t_onUpdated;
        }
        return _session.HasRankedSessionList;
    }

    static async UniTask<MatchmakingProfile> ExchangeProfilesAsync(
        MatchmakingProfile _local, MatchmakingProfile? _known, CancellationToken _ct)
    {
        NetworkGameController t_controller = NetworkGameController.Instance;
        if (t_controller == null) return _known ?? default;

        t_controller.SendMatchmakingProfile(_local);
        float t_deadline = Time.realtimeSinceStartup + PROFILE_EXCHANGE_SECONDS;
        while (!_ct.IsCancellationRequested && Time.realtimeSinceStartup < t_deadline)
        {
            if (t_controller.TryGetMatchmakingProfile(out MatchmakingProfile t_profile))
                return t_profile;
            bool t_canceled = await UniTask.Delay(50, DelayType.Realtime,
                                                  cancellationToken: _ct).SuppressCancellationThrow();
            if (t_canceled) break;
        }
        return _known ?? default;
    }

    static async UniTask<bool> WaitForOpponentAsync(NetworkSession _session, float _seconds, CancellationToken _ct)
    {
        if (CountActivePlayers(_session) >= REQUIRED_PLAYERS) return true;

        var t_tcs = new UniTaskCompletionSource();
        bool t_enough = false;
        Action<PlayerRef> t_onJoined = _ =>
        {
            if (CountActivePlayers(_session) < REQUIRED_PLAYERS) return;
            t_enough = true;
            t_tcs.TrySetResult();
        };
        Action<string> t_onFailed = _reason =>
        {
            Debug.LogWarning($"[Matchmaking] 매칭 대기 중 연결이 끊겼다: {_reason}");
            t_tcs.TrySetResult();
        };

        _session.OnPlayerJoinedRoom += t_onJoined;
        _session.OnConnectionFailed += t_onFailed;
        try
        {
            using (_ct.Register(() => t_tcs.TrySetResult()))
            {
                await UniTask.WhenAny(t_tcs.Task,
                    UniTask.Delay(TimeSpan.FromSeconds(_seconds), DelayType.Realtime));
            }
        }
        finally
        {
            _session.OnPlayerJoinedRoom -= t_onJoined;
            _session.OnConnectionFailed -= t_onFailed;
        }
        return t_enough && !_ct.IsCancellationRequested;
    }

    static int CountActivePlayers(NetworkSession _session)
    {
        if (_session.Runner == null) return 0;
        int t_count = 0;
        foreach (PlayerRef _ in _session.Runner.ActivePlayers) t_count++;
        return t_count;
    }
}
