using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>실시간 상대를 찾지 못했을 때 서버가 확정한 AI 덱으로 매칭한다.</summary>
public sealed class ServerMatchmaker : IMatchmaker
{
    const string CommandName = "findAiMatch";

    readonly OpponentProfilePool m_pool;
    readonly float m_minSeconds;
    readonly float m_maxSeconds;

    public ServerMatchmaker(OpponentProfilePool _pool, float _minSeconds = 2f, float _maxSeconds = 3.5f)
    {
        this.m_pool = _pool;
        this.m_minSeconds = _minSeconds;
        this.m_maxSeconds = _maxSeconds;
    }

    public async UniTask<MatchOpponent?> FindOpponentAsync(CancellationToken _ct)
    {
        // callable 왕복을 매칭 연출과 함께 시작한다. 서버가 먼저 답하면 기존 연출 길이는 유지되고,
        // 왕복이 더 길 때만 그만큼 매칭 화면이 이어진다.
        UniTask<FindAiMatchResult> t_request = RequestAsync();
        float t_wait = UnityEngine.Random.Range(
            Mathf.Min(this.m_minSeconds, this.m_maxSeconds),
            Mathf.Max(this.m_minSeconds, this.m_maxSeconds));
        bool t_canceled = await UniTask.Delay(
            TimeSpan.FromSeconds(t_wait), cancellationToken: _ct).SuppressCancellationThrow();
        if (t_canceled)
        {
            ObserveCanceledRequestAsync(t_request).Forget();
            return null;
        }

        try
        {
            // 취소를 함께 본다 — 그러지 않으면 유저가 취소한 뒤에도 callable 예산(최대 15초 + 재인증 1회)이
            // 끝날 때까지 매칭 화면이 남고, 그 다음 상대를 정상 확정해 취소한 전투가 시작된다.
            FindAiMatchResult t_result = await t_request.AttachExternalCancellation(_ct);
            if (t_result?.Deck == null || t_result.Deck.Count != DeckSaveManager.DECK_SIZE)
                throw new InvalidOperationException("Server returned an invalid AI deck.");
            if (string.IsNullOrEmpty(t_result.MatchId) ||
                !ulong.TryParse(t_result.SeedHex, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out ulong t_seed) ||
                t_result.RulesetVersion <= 0 ||
                !SameCards(t_result.PlayerBoardOrder, DeckConfig.PlayerDeck) ||
                !SameCards(t_result.EnemyBoardOrder, t_result.Deck))
                throw new InvalidOperationException("Server returned an invalid solo match identity.");

            SoloMatchHandoff.Set(
                t_result.MatchId, t_result.SeedHex, t_seed, t_result.RulesetVersion,
                t_result.PlayerBoardOrder, t_result.EnemyBoardOrder,
                t_result.ResultProtocol, ComputeEnemyDeckHash(t_result.Deck, t_result.CardLevel));

            MatchProfile t_profile = MatchProfile.OfOpponent(
                this.m_pool != null ? this.m_pool.PickName() : OpponentProfilePool.FALLBACK_NAME,
                this.m_pool != null ? this.m_pool.PickAvatar() : null);
            return new MatchOpponent(t_profile, t_result.Deck, t_result.CardLevel);
        }
        catch (Exception t_exception)
        {
            if (_ct.IsCancellationRequested) return null;
            Debug.LogError($"[ServerMatchmaker] AI 상대 확정 실패: {t_exception.GetBaseException().Message}");
            ShowFailureNextFrameAsync().Forget();
            return null;
        }
    }

    static async UniTask<FindAiMatchResult> RequestAsync()
    {
        string t_env = ContentProfileConfig.Active != null
            ? ContentProfileConfig.Active.CloudEnvId
            : null;
        if (string.IsNullOrEmpty(t_env))
            throw new InvalidOperationException("Content profile has no cloud environment.");

        return await ServerSaveCommands.InvokeAsync<FindAiMatchResult>(CommandName, new
        {
            env = t_env,
            contentFingerprint = SpecSource.BattleFingerprint.ToLowerInvariant(),
            playerDeck = DeckConfig.PlayerDeck,
            resultProtocol = 1,
        });
    }

    static string ComputeEnemyDeckHash(List<int> _deck, int _cardLevel)
    {
        int[] t_ids = _deck.ToArray();
        Array.Sort(t_ids);
        // 레벨 클램프는 실제 적 카드를 세우는 자리(BattleGrowthBridge.EnemyCardLevel)와 같은 하나를 쓴다 —
        // 여기만 다르게 조이면 해시는 서버와 맞는데 필드는 다른 카드가 서서 재시뮬이 상시 발산한다.
        int t_level = CardGrowthManager.ClampLevel(_cardLevel);
        var t_growth = new CardGrowth[t_ids.Length];
        for (int i = 0; i < t_ids.Length; i++)
            t_growth[i] = CardGrowthManager.GrowthAtLevel(t_ids[i], t_level);
        return NetworkGameController.ComputeDeckHash(t_ids, t_growth);
    }

    static bool SameCards(IReadOnlyList<int> _left, IReadOnlyList<int> _right)
    {
        if (_left == null || _right == null || _left.Count != _right.Count) return false;
        var t_left = new List<int>(_left);
        var t_right = new List<int>(_right);
        t_left.Sort();
        t_right.Sort();
        for (int i = 0; i < t_left.Count; i++)
            if (t_left[i] != t_right[i]) return false;
        return true;
    }

    /// <summary>매칭 화면이 내려간 뒤에 안내를 올린다.</summary>
    // 셸은 이 메서드가 null을 돌려준 뒤 같은 프레임에 스스로 닫는다(MatchmakingShell.RunMatchAsync의 finally).
    // 그 자리에서 바로 띄우면 안내가 아직 떠 있는 매칭 화면에 묻힌다 — 둘은 자기 캔버스가 없어 형제 순서로만 갈린다.
    static async UniTaskVoid ShowFailureNextFrameAsync()
    {
        await UniTask.Yield();
        NetworkFailurePopup.Show("AI 상대를 준비하지 못했습니다.");
    }

    static async UniTaskVoid ObserveCanceledRequestAsync(UniTask<FindAiMatchResult> _request)
    {
        try { await _request; }
        catch (Exception t_exception)
        {
            Debug.LogWarning(
                $"[ServerMatchmaker] 취소 뒤 끝난 AI 상대 요청: {t_exception.GetBaseException().Message}");
        }
    }
}

internal sealed class FindAiMatchResult : ServerCommandResult
{
    [JsonProperty("matchId")] public string MatchId { get; set; }
    [JsonProperty("seedHex")] public string SeedHex { get; set; }
    [JsonProperty("rulesetVersion")] public int RulesetVersion { get; set; }
    [JsonProperty("deck")] public List<int> Deck { get; set; }
    [JsonProperty("cardLevel")] public int CardLevel { get; set; }
    [JsonProperty("playerBoardOrder")] public List<int> PlayerBoardOrder { get; set; }
    [JsonProperty("enemyBoardOrder")] public List<int> EnemyBoardOrder { get; set; }
    [JsonProperty("resultProtocol")] public int ResultProtocol { get; set; }
}
