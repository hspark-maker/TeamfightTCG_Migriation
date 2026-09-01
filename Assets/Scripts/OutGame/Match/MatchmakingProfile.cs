using Cysharp.Threading.Tasks;
using UnityEngine;

public readonly struct MatchmakingProfile
{
    public readonly string Nickname;
    public readonly int    TierIndex;
    public readonly string AvatarId;
    public readonly string FrameId;
    /// <summary>서버 서명 티어 티켓. 상대가 verifyMatchTicket 으로 검증한다.
    /// **세션 프로퍼티에는 싣지 않는다** — 로비 목록은 공개라 아무나 주워 쓸 수 있다.</summary>
    public readonly string Ticket;

    public bool IsValid => !string.IsNullOrEmpty(this.Nickname) && this.TierIndex >= 0;

    public MatchmakingProfile(string _nickname, int _tierIndex, string _avatarId, string _frameId,
                              string _ticket = null)
    {
        this.Nickname = _nickname ?? string.Empty;
        this.TierIndex = _tierIndex;
        this.AvatarId = _avatarId ?? string.Empty;
        this.FrameId = _frameId ?? string.Empty;
        this.Ticket = _ticket ?? string.Empty;
    }

    public static MatchmakingProfile LocalPlayer()
        => new MatchmakingProfile(ProfileManager.Nickname, RankManager.TierIndex,
                                  ProfileManager.AvatarId, ProfileManager.FrameId);

    /// <summary>매칭에 쓸 내 프로필. **티어만 서버에서 다시 읽는다**(getRankSnapshot).
    ///
    /// 로컬 <see cref="RankManager"/>는 초기화 때 채택한 값을 세션 내내 들고 있어서
    /// (PlayerSaveCloud가 세션 중 재-pull 경로를 두지 않는다) 승급 직후 매칭이 옛 티어로 걸린다.
    /// 세이브를 손댄 클라도 여기서 걸러진다.
    ///
    /// 막지 못하는 것은 **티어 위장**이다 — 받은 값을 세션 프로퍼티에 싣는 것은 여전히 클라라서,
    /// 조작된 클라는 아무 티어나 신고할 수 있다. 그건 상대 티어를 서버가 보증하는 별도 경로의 몫이다.
    ///
    /// 서버가 답하지 않으면 매칭을 막지 않고 로컬 값으로 진행한다 — 랭크 표시가 한 칸 낡는 것보다
    /// 매칭 자체가 안 되는 쪽이 나쁘다.</summary>
    public static async UniTask<MatchmakingProfile> FromServerAsync()
    {
        MatchmakingProfile t_local = LocalPlayer();

        string t_env = ContentProfileConfig.Active != null ? ContentProfileConfig.Active.CloudEnvId : null;
        if (string.IsNullOrEmpty(t_env)) return t_local;   // 오프라인 프로필 — 서버가 없다

        try
        {
            RankSnapshotResult t_result = await ServerSaveCommands.InvokeReadOnlyAsync<RankSnapshotResult>(
                "getRankSnapshot", new { env = t_env });
            if (t_result == null || !RankManager.TryGetTier(t_result.TierIndex, out _))
            {
                Debug.LogWarning("[Matchmaking] 서버 랭크 스냅샷이 비었거나 티어가 범위를 벗어났다 — 로컬 값으로 진행한다.");
                return t_local;
            }

            if (t_result.TierIndex != t_local.TierIndex)
                Debug.Log($"[Matchmaking] 티어를 서버 값으로 맞춘다: 로컬 {t_local.TierIndex} → 서버 {t_result.TierIndex}");

            return new MatchmakingProfile(t_local.Nickname, t_result.TierIndex,
                                          t_local.AvatarId, t_local.FrameId, t_result.Ticket);
        }
        catch (System.Exception t_exception)
        {
            Debug.LogWarning($"[Matchmaking] 서버 랭크 스냅샷 실패 — 로컬 값으로 진행한다: {t_exception.Message}");
            return t_local;
        }
    }

    /// <summary>상대가 보낸 티켓을 서버에 검증시켜 **티어만** 서버 값으로 갈아 끼운다.
    /// 상대가 메시지에 함께 실어 보낸 티어 숫자는 여기서 버려진다 — 그게 자기신고를 끊는 지점이다.
    ///
    /// 검증이 안 되면(티켓 없음·만료·서명 불일치·서버 실패) 티어를 -1로 눌러 "모르는 상대"로 만든다.
    /// 매칭 자체는 막지 않는다 — 붙는 것과 등급을 표시하는 것은 다른 문제다.</summary>
    public static async UniTask<MatchmakingProfile> VerifiedAsync(MatchmakingProfile _opponent)
    {
        if (!_opponent.IsValid) return _opponent;

        string t_env = ContentProfileConfig.Active != null ? ContentProfileConfig.Active.CloudEnvId : null;
        if (string.IsNullOrEmpty(t_env)) return _opponent;   // 서버 없는 프로필 — 검증할 곳이 없다

        if (string.IsNullOrEmpty(_opponent.Ticket))
        {
            Debug.LogWarning("[Matchmaking] 상대가 티켓을 보내지 않았다 — 랭크 표시를 비운다.");
            return Unranked(_opponent);
        }

        try
        {
            var t_result = await ServerSaveCommands.InvokeReadOnlyAsync<MatchTicketVerifyResult>(
                "verifyMatchTicket", new { env = t_env, ticket = _opponent.Ticket });
            if (t_result == null || !t_result.Valid)
            {
                Debug.LogWarning($"[Matchmaking] 상대 티켓 검증 실패({t_result?.Reason ?? "no-response"}) — 랭크 표시를 비운다.");
                return Unranked(_opponent);
            }

            if (t_result.TierIndex != _opponent.TierIndex)
                Debug.LogWarning($"[Matchmaking] 상대 신고 티어({_opponent.TierIndex})와 서버 티켓({t_result.TierIndex})이 다르다 — 서버 값을 쓴다.");

            return new MatchmakingProfile(_opponent.Nickname, t_result.TierIndex,
                                          _opponent.AvatarId, _opponent.FrameId, _opponent.Ticket);
        }
        catch (System.Exception t_exception)
        {
            Debug.LogWarning($"[Matchmaking] 티켓 검증 호출 실패 — 랭크 표시를 비운다: {t_exception.Message}");
            return Unranked(_opponent);
        }
    }

    static MatchmakingProfile Unranked(MatchmakingProfile _profile)
        => new MatchmakingProfile(_profile.Nickname, -1, _profile.AvatarId, _profile.FrameId, _profile.Ticket);
}
