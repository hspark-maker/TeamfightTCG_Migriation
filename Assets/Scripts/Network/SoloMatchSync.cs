using System.Collections.Generic;
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
/// <para>시드와 서버 확정 보드 순서가 함께 실린다. 일반 AI전은 그 순서를 무셔플로 배치해
/// 클라가 재시뮬의 시작 보드를 고를 수 없게 한다. 수명은 <c>TurnRunner.Cleanup</c> 이 끊는다.</para></summary>
public static class SoloMatchHandoff
{
    static string s_matchId;
    static string s_seedHex;
    static ulong s_seed;
    static int s_rulesetVersion;
    static int[] s_playerBoardOrder;
    static int[] s_enemyBoardOrder;
    static bool s_hasValue;
    static bool s_seedConsumed;
    static bool s_boardOrdersConsumed;

    public static bool HasValue => s_hasValue;
    public static string MatchId => s_matchId;

    internal static void Set(
        string _matchId, string _seedHex, ulong _seed, int _rulesetVersion,
        IReadOnlyList<int> _playerBoardOrder, IReadOnlyList<int> _enemyBoardOrder)
    {
        s_matchId = _matchId;
        s_seedHex = _seedHex;
        s_seed = _seed;
        s_rulesetVersion = _rulesetVersion;
        s_playerBoardOrder = Copy(_playerBoardOrder);
        s_enemyBoardOrder = Copy(_enemyBoardOrder);
        s_hasValue = true;
        s_seedConsumed = false;
        s_boardOrdersConsumed = false;
    }

    internal static bool TryGetLockIdentity(
        out string _matchId, out string _seedHex, out ulong _seed, out int _rulesetVersion)
    {
        _matchId = s_matchId;
        _seedHex = s_seedHex;
        _seed = s_seed;
        _rulesetVersion = s_rulesetVersion;
        return s_hasValue;
    }

    /// <summary>필드 초기화가 서버 시드를 한 번만 적용한다.</summary>
    public static bool TryConsumeSeed(out ulong _seed)
    {
        _seed = s_seed;
        bool t_had = s_hasValue && !s_seedConsumed;
        s_seedConsumed = true;
        s_seed = 0;
        return t_had;
    }

    /// <summary>서버가 봉인한 슬롯→대기열 순서를 한 번만 꺼낸다.</summary>
    public static bool TryConsumeBoardOrders(out int[] _player, out int[] _enemy)
    {
        _player = Copy(s_playerBoardOrder);
        _enemy = Copy(s_enemyBoardOrder);
        bool t_had = s_hasValue && !s_boardOrdersConsumed &&
                     _player.Length == DeckSaveManager.DECK_SIZE &&
                     _enemy.Length == DeckSaveManager.DECK_SIZE;
        s_boardOrdersConsumed = true;
        return t_had;
    }

    public static void Clear()
    {
        s_matchId = null;
        s_seedHex = null;
        s_seed = 0;
        s_rulesetVersion = 0;
        s_playerBoardOrder = null;
        s_enemyBoardOrder = null;
        s_hasValue = false;
        s_seedConsumed = false;
        s_boardOrdersConsumed = false;
    }

    static int[] Copy(IReadOnlyList<int> _source)
    {
        if (_source == null) return System.Array.Empty<int>();
        var t_copy = new int[_source.Count];
        for (int i = 0; i < _source.Count; i++) t_copy[i] = _source[i];
        return t_copy;
    }
}

/// <summary>AI 대전의 씬 전 서버 검증. <c>findAiMatch</c>가 연 매치를 대인전과 <b>같은 lockDeck</b>으로
/// 잠근다 — 덱 검증 규칙을 두 벌로 만들지 않기 위해서다.
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

        if (!SoloMatchHandoff.TryGetLockIdentity(
                out string t_matchId, out string t_seedHex, out ulong _, out int t_rulesetVersion))
        {
            Debug.LogError("[SoloMatchSync] findAiMatch가 발급한 매치 신원이 없다.");
            return ESoloMatchSyncResult.Failed;
        }

        string t_env = ContentProfileConfig.Active.CloudEnvId;
        string t_fingerprint = SpecSource.BattleFingerprint.ToLowerInvariant();

        DeckLockResult t_lock = await DeckLockSubmission.TryLockAsync(
            t_env,
            t_matchId,
            "server",
            t_seedHex,
            t_rulesetVersion,
            0,
            t_fingerprint,
            t_cardIds,
            t_growth,
            _ct);
        if (_ct.IsCancellationRequested) return ESoloMatchSyncResult.Canceled;
        if (t_lock != DeckLockResult.Approved)
        {
            Debug.LogError($"[SoloMatchSync] 덱 검증 실패({t_lock}) matchId={t_matchId}");
            return ESoloMatchSyncResult.Failed;
        }

        Debug.Log($"[SoloMatchSync] 덱 검증 통과 matchId={t_matchId} seed={t_seedHex}");
        return ESoloMatchSyncResult.Success;
    }
}
