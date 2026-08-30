using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;

// 로비 PlayBtn 의 매칭. 실제 상대를 SEARCH_SECONDS 동안 찾고, 그 안에 아무도 안 오면 AI 로 내려간다.
//
// DeckConfig.SetMultiplayer(true) 는 씬 전 동기화까지 끝났을 때만 켠다 — 중간에 끊긴 판이
// 멀티 플래그만 남기면 다음 싱글 전투가 서버 제출 경로를 타고 보상이 통째로 사라진다.
public sealed class PhotonRankedMatchmaker : IMatchmaker
{
    const float SEARCH_SECONDS   = 20f;
    const int   REQUIRED_PLAYERS = 2;

    // 상대의 닉네임·티어를 받아올 와이어가 아직 없다. 지어낸 이름을 붙이면 실제 사람에게
    // 가짜 신원을 씌우는 것이라, 그 자리를 비워 둔 것이 드러나는 값을 쓴다.
    const string OPPONENT_PLACEHOLDER_NAME = "상대";

    enum EOutcome
    {
        Matched,      // 실 상대 확정 — 씬 전 동기화까지 끝났다
        NoOpponent,   // 아무도 안 왔다(또는 실 매칭 자체가 불가능하다) — AI 로 내려간다
        Failed,       // 상대는 만났는데 동기화가 깨졌다 — 양쪽이 각자 정리하고 로비로
        Canceled,     // 유저가 접었다
    }

    readonly IMatchmaker m_aiFallback;

    public PhotonRankedMatchmaker(IMatchmaker _aiFallback)
    {
        m_aiFallback = _aiFallback;
    }

    public async UniTask<MatchOpponent?> FindOpponentAsync(CancellationToken _ct)
    {
        (EOutcome outcome, MatchOpponent opponent) t_real = await MatchRealOpponentAsync(_ct);

        if (t_real.outcome == EOutcome.Matched) return t_real.opponent;

        // AI 로 내려가는 것은 "상대가 없었다" 하나뿐이다. 취소는 유저가 로비로 가겠다는 뜻이고,
        // 실패는 상대가 이미 같은 방에 있던 판이라 AI 전투로 이어붙이면 그쪽 화면과 어긋난다.
        if (t_real.outcome != EOutcome.NoOpponent) return null;

        return await m_aiFallback.FindOpponentAsync(_ct);
    }

    async UniTask<(EOutcome, MatchOpponent)> MatchRealOpponentAsync(CancellationToken _ct)
    {
        NetworkSession t_session = NetworkSession.Instance;

        // 세션이 없는 씬(테스트·단독 실행)에서는 실 매칭 자체가 성립하지 않는다. 막지 않고 AI 로 넘긴다.
        if (t_session == null)
        {
            Debug.LogWarning("[Matchmaking] NetworkSession 이 없어 실 매칭을 건너뛴다 — AI 로 진행한다.");
            return (EOutcome.NoOpponent, default);
        }

        if (_ct.IsCancellationRequested) return (EOutcome.Canceled, default);

        if (!await t_session.JoinRandomRoom())
        {
            Debug.LogWarning("[Matchmaking] 방 참가에 실패했다 — AI 로 진행한다.");
            await t_session.Disconnect();
            return (EOutcome.NoOpponent, default);
        }

        bool t_paired = await WaitForOpponentAsync(t_session, _ct);

        if (_ct.IsCancellationRequested)
        {
            await t_session.Disconnect();
            return (EOutcome.Canceled, default);
        }

        if (!t_paired)
        {
            await t_session.Disconnect();
            return (EOutcome.NoOpponent, default);
        }

        // 씬 전 동기화가 서버 시드·덱 잠금까지 끝낸다. 그 안의 제출 경로가 멀티로 동작하려면 여기서 켜져 있어야 한다.
        DeckConfig.SetMultiplayer(true);

        EPreBattleSyncResult t_sync = await PreBattleMatchSync.RunAsync(_ct);
        if (t_sync == EPreBattleSyncResult.Success)
        {
            // 덱은 비워 보낸다 — 상대 덱은 배틀 씬이 PreBattleMatchHandoff 에서 읽는다(MatchOpponent.IsValid 규약).
            return (EOutcome.Matched,
                    new MatchOpponent(MatchProfile.OfOpponent(OPPONENT_PLACEHOLDER_NAME, null), null));
        }

        bool t_canceled = t_sync == EPreBattleSyncResult.Canceled;

        // 끊기 전에 사유를 알린다 — Disconnect 만 하면 상대 화면에 "연결 끊김"으로만 남는다.
        NetworkGameController.Instance?.SendMatchAbort(
            t_canceled ? EMatchEndReason.OpponentLeftDuringInit : EMatchEndReason.InitError);

        DeckConfig.ResetMode();
        PreBattleMatchHandoff.Clear();
        await t_session.Disconnect();
        return (t_canceled ? EOutcome.Canceled : EOutcome.Failed, default);
    }

    /// <summary>정원이 찰 때까지 최대 <see cref="SEARCH_SECONDS"/> 대기. 취소는 즉시 깨운다 —
    /// 토큰을 Delay 에 걸면 예외로 새어 IMatchmaker 계약(취소=null)을 깨므로 TCS 쪽에 등록한다.</summary>
    static async UniTask<bool> WaitForOpponentAsync(NetworkSession _session, CancellationToken _ct)
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
                // DelayType.Realtime: 매칭 중에도 씬 로드 정지가 끼면 게임 시간 델타가 한 번에 몰린다.
                await UniTask.WhenAny(
                    t_tcs.Task,
                    UniTask.Delay(TimeSpan.FromSeconds(SEARCH_SECONDS), DelayType.Realtime));
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
