using System;
using System.Collections.Generic;
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
            FindAiMatchResult t_result = await t_request;
            if (t_result?.Deck == null || t_result.Deck.Count != DeckSaveManager.DECK_SIZE)
                throw new InvalidOperationException("Server returned an invalid AI deck.");

            MatchProfile t_profile = MatchProfile.OfOpponent(
                this.m_pool != null ? this.m_pool.PickName() : OpponentProfilePool.FALLBACK_NAME,
                this.m_pool != null ? this.m_pool.PickAvatar() : null);
            return new MatchOpponent(t_profile, t_result.Deck, t_result.CardLevel);
        }
        catch (Exception t_exception)
        {
            if (_ct.IsCancellationRequested) return null;
            Debug.LogError($"[ServerMatchmaker] AI 상대 확정 실패: {t_exception.GetBaseException().Message}");
            NetworkFailurePopup.Show("AI 상대를 준비하지 못했습니다.");
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

        return await ServerSaveCommands.InvokeReadOnlyAsync<FindAiMatchResult>(
            CommandName, new { env = t_env });
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

internal sealed class FindAiMatchResult
{
    [JsonProperty("deck")] public List<int> Deck { get; set; }
    [JsonProperty("cardLevel")] public int CardLevel { get; set; }
}
