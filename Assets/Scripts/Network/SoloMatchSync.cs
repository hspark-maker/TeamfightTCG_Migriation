using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

public enum ESoloMatchSyncResult
{
    Success,
    Failed,
    Canceled,
}

/// <summary>서버가 승인한 AI 대전 1판의 신원. 로비에서 세우고 배틀 씬이 읽는다.
///
/// <para>시드가 여기 실린다 — 싱글 셔플이 <see cref="ShufflePolicy.Match"/>(MatchRandom 소비)이므로
/// 이 값 하나로 보드까지 서버에서 재현된다. 수명은 <c>TurnRunner.Cleanup</c> 이 끊는다.</para></summary>
public static class SoloMatchHandoff
{
    static string s_matchId;
    static ulong s_seed;
    static bool s_hasValue;

    public static bool HasValue => s_hasValue;
    public static string MatchId => s_matchId;

    internal static void Set(string _matchId, ulong _seed)
    {
        s_matchId = _matchId;
        s_seed = _seed;
        s_hasValue = true;
    }

    /// <summary>시드를 꺼내며 비운다 — 남겨 두면 다음 판이 검증을 건너뛴 경우
    /// 직전 판과 같은 보드로 시작한다.</summary>
    public static bool TryConsumeSeed(out ulong _seed)
    {
        _seed = s_seed;
        bool t_had = s_hasValue;
        s_hasValue = false;
        s_seed = 0;
        return t_had;
    }

    public static void Clear()
    {
        s_matchId = null;
        s_seed = 0;
        s_hasValue = false;
    }
}

/// <summary>AI 대전의 씬 전 서버 검증. 대인전(<see cref="PreBattleMatchSync"/>)과 <b>같은 callable</b>
/// (createMatch → lockDeck)을 정원 1인 모드로 태운다 — 덱 검증 규칙을 두 벌로 만들지 않기 위해서다.
///
/// <para>여기서 승인이 나야 배틀 씬으로 넘어간다. 서버는 이 단계에서 소유·레벨·진화·키워드 성장을
/// 세이브와 대조해 재계산하므로, 위조한 덱은 전투가 시작되기 전에 걸린다.</para></summary>
public static class SoloMatchSync
{
    public static async UniTask<ESoloMatchSyncResult> RunAsync(CancellationToken _ct)
    {
        (bool t_deckOk, int[] t_cardIds, CardGrowth[] t_growth) =
            await PreBattleMatchSync.TryGetCanonicalLocalDeckAsync(_ct);
        if (_ct.IsCancellationRequested) return ESoloMatchSyncResult.Canceled;
        if (!t_deckOk)
        {
            Debug.LogError("[SoloMatchSync] 출전 덱 스냅샷을 만들지 못해 서버 검증을 시작하지 못했다.");
            return ESoloMatchSyncResult.Failed;
        }

        string t_env = ContentProfileConfig.Active.CloudEnvId;
        string t_fingerprint = SpecSource.BattleFingerprint.ToLowerInvariant();

        (ServerMatchSeedStatus status, ServerMatchSeed match) t_seed =
            await ServerMatchSeedSubmission.TryAcquireAsync(
                t_env, NewPairingKey(), t_fingerprint, 0, _ct, "solo");
        if (_ct.IsCancellationRequested) return ESoloMatchSyncResult.Canceled;
        if (t_seed.status != ServerMatchSeedStatus.Paired || t_seed.match == null)
        {
            Debug.LogError($"[SoloMatchSync] 매치를 열지 못했다({t_seed.status}).");
            return ESoloMatchSyncResult.Failed;
        }

        DeckLockResult t_lock = await DeckLockSubmission.TryLockAsync(
            t_env,
            t_seed.match.MatchId,
            "server",
            t_seed.match.SeedHex,
            t_seed.match.RulesetVersion,
            0,
            t_fingerprint,
            t_cardIds,
            t_growth,
            _ct);
        if (_ct.IsCancellationRequested) return ESoloMatchSyncResult.Canceled;
        if (t_lock != DeckLockResult.Approved)
        {
            Debug.LogError($"[SoloMatchSync] 덱 검증 실패({t_lock}) matchId={t_seed.match.MatchId}");
            return ESoloMatchSyncResult.Failed;
        }

        // 서버 시드를 전투 RNG 로 넘긴다 — 덱 셔플부터 스플래시 대상까지 이 값에서 파생된다.
        SoloMatchHandoff.Set(t_seed.match.MatchId, t_seed.match.Seed);

        Debug.Log($"[SoloMatchSync] 덱 검증 통과 matchId={t_seed.match.MatchId} seed={t_seed.match.SeedHex}");
        return ESoloMatchSyncResult.Success;
    }

    // 판마다 새 매치여야 한다 — 키를 재사용하면 createMatch 가 이미 잠긴 문서를 보고 거절한다.
    // 서버 정규식(A-Za-z0-9_-)에 맞춰 hex 로 만든다.
    static string NewPairingKey()
    {
        byte[] t_bytes = new byte[16];
        using (RandomNumberGenerator t_rng = RandomNumberGenerator.Create()) t_rng.GetBytes(t_bytes);
        return "solo-" + MatchResultSubmission.Hex(t_bytes);
    }
}
