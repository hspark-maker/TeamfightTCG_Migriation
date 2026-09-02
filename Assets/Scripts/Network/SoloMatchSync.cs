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
    static int s_resultProtocol;
    static string s_localDeckHash;
    static string s_opponentDeckHash;
    static bool s_hasValue;
    static bool s_seedConsumed;
    static bool s_boardOrdersConsumed;

    public static bool HasValue => s_hasValue;
    public static string MatchId => s_matchId;
    /// <summary>이 판의 결과를 <c>submitMatchResult</c> 로 확정하는가. 협상 값만 보지 않고 <b>제출 증거가
    /// 다 모였는지</b>까지 본다 — 증거가 비면 제출이 실패하는데 로컬 랭크·구 지급 경로는 이미 꺼져 있어
    /// 랭크도 보상도 없이 판이 사라진다.</summary>
    public static bool UsesResultSubmission =>
        s_hasValue && s_resultProtocol >= 1 && !string.IsNullOrEmpty(s_matchId) &&
        !string.IsNullOrEmpty(s_localDeckHash) && !string.IsNullOrEmpty(s_opponentDeckHash);

    internal static void Set(
        string _matchId, string _seedHex, ulong _seed, int _rulesetVersion,
        IReadOnlyList<int> _playerBoardOrder, IReadOnlyList<int> _enemyBoardOrder,
        int _resultProtocol, string _opponentDeckHash)
    {
        s_matchId = _matchId;
        s_seedHex = _seedHex;
        s_seed = _seed;
        s_rulesetVersion = _rulesetVersion;
        s_playerBoardOrder = Copy(_playerBoardOrder);
        s_enemyBoardOrder = Copy(_enemyBoardOrder);
        s_resultProtocol = _resultProtocol;
        s_localDeckHash = null;
        s_opponentDeckHash = _opponentDeckHash;
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

    internal static void SetLocalDeckHash(string _matchId, string _deckHash)
    {
        if (!s_hasValue || s_matchId != _matchId) return;
        s_localDeckHash = _deckHash;
    }

    internal static bool TryGetResultIdentity(
        out string _matchId, out string _localDeckHash, out string _opponentDeckHash)
    {
        _matchId = s_matchId;
        _localDeckHash = s_localDeckHash;
        _opponentDeckHash = s_opponentDeckHash;
        return UsesResultSubmission && !string.IsNullOrEmpty(_matchId) &&
               !string.IsNullOrEmpty(_localDeckHash) && !string.IsNullOrEmpty(_opponentDeckHash);
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
        s_resultProtocol = 0;
        s_localDeckHash = null;
        s_opponentDeckHash = null;
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

        // 잠금에 실린 것과 **같은 정규화**로 만든 해시여야 한다 — 편성 순서 그대로 해시하면
        // 오름차순이 아닌 덱은 서버가 solo_player_deck_mismatch 로 접어 보상·랭크가 통째로 날아간다.
        if (!DeckLockSubmission.TryNormalize(t_cardIds, t_growth, out _, out _, out string t_deckHash))
        {
            Debug.LogError("[SoloMatchSync] 덱 해시를 만들지 못해 결과 제출 신원을 세우지 못했다.");
            return ESoloMatchSyncResult.Failed;
        }
        SoloMatchHandoff.SetLocalDeckHash(t_matchId, t_deckHash);

        Debug.Log($"[SoloMatchSync] 덱 검증 통과 matchId={t_matchId} seed={t_seedHex}");
        return ESoloMatchSyncResult.Success;
    }
}
