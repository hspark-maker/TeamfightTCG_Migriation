using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 서버가 확정해 둔 매치 지급을 회수하는 창구.
// 잔액 크레딧은 서버 ack 가 한다 — 클라는 랭크 전이와 결과 화면 전달만 로컬에서 맞춘다.
static class PayoutInbox
{
    const string AppliedKey = "firebase.payout.applied.v1";
    const string CommandName = "claimPayout";
    const double EmptyRefreshCooldownSeconds = 60;
    // functions/src/commands/claimPayout.ts의 list limit과 같다.
    const int PayoutPageSize = 20;

    [Serializable]
    sealed class AppliedStore
    {
        public List<string> matchIds = new List<string>();
    }

    static readonly HashSet<string> s_applied = new HashSet<string>();
    static string s_envId;
    static UniTaskCompletionSource s_inFlight;
    static bool s_refreshRequested;
    static double s_nextPassiveRefreshAt;
    static int s_retryVersion;
    static int s_generation;

    internal static void Initialize(string _envId)
    {
        s_generation++;
        s_inFlight = null;
        s_refreshRequested = false;
        s_nextPassiveRefreshAt = 0;
        s_retryVersion++;
        s_envId = _envId;
        LoadApplied();
        RetryPending();
    }

    internal static void Shutdown()
    {
        s_generation++;
        s_inFlight = null;
        s_refreshRequested = false;
        s_nextPassiveRefreshAt = 0;
        s_retryVersion++;
        SaveApplied();
        s_envId = null;
    }

    internal static void RetryPending()
    {
        if (string.IsNullOrEmpty(s_envId) || GameInitialization.IsTerminated) return;
        // 정산 알림은 조회 중이어도 남긴다. 현재 조회가 그 지급을 못 봤다면 다음 조회가 회수한다.
        s_refreshRequested = true;
        if (s_inFlight != null) return;
        ClaimAsync().Forget();
    }

    internal static void RefreshOnResume()
    {
        if (s_inFlight != null) return;
        // 빈 목록을 최근 확인한 단순 복귀만 생략한다. 정산 알림·미완료 ack·실패 복구는 제한하지 않는다.
        if (!s_refreshRequested && s_applied.Count == 0 &&
            Time.realtimeSinceStartupAsDouble < s_nextPassiveRefreshAt) return;
        RetryPending();
    }

    internal static UniTask FlushAsync()
    {
        // pause·quit·전투 입장의 전체 flush는 알려진 미처리 지급만 처리한다. 빈 함을 새로 조회하지 않는다.
        if (s_inFlight == null && (s_refreshRequested || s_applied.Count > 0)) RetryPending();
        return s_inFlight?.Task ?? UniTask.CompletedTask;
    }

    static async UniTask ClaimAsync()
    {
        if (s_inFlight != null || string.IsNullOrEmpty(s_envId)) return;
        var t_gate = new UniTaskCompletionSource();
        s_inFlight = t_gate;
        int t_generation = s_generation;
        int t_retryVersion = ++s_retryVersion;
        bool t_retry = false;
        try
        {
            while (s_refreshRequested && t_generation == s_generation && !GameInitialization.IsTerminated)
            {
                s_refreshRequested = false;
                await ClaimOnceAsync(t_generation);
            }
        }
        catch (Exception t_exception)
        {
            if (t_generation == s_generation && !GameInitialization.IsTerminated)
            {
                s_refreshRequested = true;
                t_retry = true;
                Debug.LogWarning($"[Payout] Deferring collection of the server-confirmed payout: {t_exception.GetBaseException().Message}");
            }
        }
        finally
        {
            if (t_generation == s_generation)
            {
                s_inFlight = null;
                if (t_retry) RetryAfterDelay(t_generation, t_retryVersion).Forget();
            }
            t_gate.TrySetResult();
        }
    }

    static async UniTask ClaimOnceAsync(int _generation)
    {
        await UniTask.WaitUntil(() =>
            (SaveDependentManagersStep.IsInstalled && RankManager.IsConfigured) || GameInitialization.IsTerminated ||
            _generation != s_generation);
        if (GameInitialization.IsTerminated || _generation != s_generation) return;
        bool t_signedIn = await MatchResultSubmission.EnsureSignedIn();
        if (_generation != s_generation) return;
        if (!t_signedIn) throw new InvalidOperationException("Payout collection requires sign-in.");

        // list 는 아무 문서도 쓰지 않는다 — 업로드 봉인도 응답 채택도 걸지 않는다.
        PayoutListResult t_list = await ServerSaveCommands.InvokeReadOnlyAsync<PayoutListResult>(
            CommandName,
            new { env = s_envId, action = "list" });
        if (_generation != s_generation) return;
        if (t_list?.Payouts == null) throw new InvalidOperationException("Payout list response is missing.");

        List<PayoutEntry> t_payouts = t_list.Payouts;
        if (t_payouts.Count > 0) MissionCommands.Invalidate();
        s_nextPassiveRefreshAt = t_payouts.Count == 0
            ? Time.realtimeSinceStartupAsDouble + EmptyRefreshCooldownSeconds
            : 0;
        t_payouts.Sort((a, b) => a.RankSequence != b.RankSequence
            ? a.RankSequence.CompareTo(b.RankSequence)
            : a.SettledAtMs.CompareTo(b.SettledAtMs));

        var t_ackIds = new List<string>();
        var t_gains = new Dictionary<string, CurrencyGain>();
        foreach (PayoutEntry t_payout in t_payouts)
        {
            if (!TryReadGain(t_payout, out CurrencyGain t_gain)) continue;

            if (!s_applied.Contains(t_payout.MatchId))
            {
                // 잔액은 건드리지 않는다 — 크레딧의 진실원은 아래 ack 응답의 지갑이다.
                RankProgressResult t_progress = t_payout.RankProgress;
                RankApplyResult t_rank = RankManager.ApplyServerPayout(
                    t_payout.Rank.Before,
                    t_payout.Rank.After,
                    t_progress?.SeasonId,
                    t_progress?.BestTierIndex ?? -1,
                    null);
                DataSaveManager.SaveImmediate();
                RankResultHandoff.Set(t_rank);
                s_applied.Add(t_payout.MatchId);
                SaveApplied();
            }

            t_gains[t_payout.MatchId] = t_gain;
            t_ackIds.Add(t_payout.MatchId);
        }

        if (t_ackIds.Count == 0) return;

        // ack 는 지갑을 쓴다 — 채택 창구를 타야 응답의 wallet 이 잔액에 반영된다(세이브는 쓰지 않아 revision 이 없다).
        PayoutAckResult t_ack = await ServerSaveCommands.InvokeAsync<PayoutAckResult>(
            CommandName,
            new { env = s_envId, action = "ack", matchIds = t_ackIds });
        if (_generation != s_generation) return;
        if (t_ack?.Acked == null) throw new InvalidOperationException("Payout ack response is missing.");

        foreach (string t_matchId in t_ack.Acked)
        {
            s_applied.Remove(t_matchId);

            // 표시량은 크레딧이 확정된 뒤에 싣는다 — ack 전에 실으면 결과 화면이 "+N" 을 띄우는 동안 지갑은 그대로다.
            // 여러 건이 한 번에 acked 되면 그만큼 누적된다(핸드오프가 합산 홀더다).
            if (t_gains.TryGetValue(t_matchId, out CurrencyGain t_credited)) BattleRewardHandoff.Set(t_credited);
        }
        SaveApplied();
        // 전투 정산은 미션 카운터도 갱신하지만 지급 응답에는 그 상태가 없다.
        // 미션 화면을 열기 전에도 변경 알림·컷인이 도착하도록 정산 후 한 번 조회한다.
        MissionCommands.RefreshAsync().Forget();
        // 종료·입장마다 다시 조회하지 않으므로 쌓인 지급은 이번 회수에서 비운다.
        // 실제 ack가 없으면 재조회하지 않아 손상된 항목만 있는 페이지를 무한 반복하지 않는다.
        if (t_payouts.Count >= PayoutPageSize && t_ack.Acked.Count > 0) s_refreshRequested = true;
    }

    static async UniTaskVoid RetryAfterDelay(int _generation, int _retryVersion)
    {
        await UniTask.Delay(TimeSpan.FromSeconds(30), DelayType.Realtime);
        if (_generation == s_generation && _retryVersion == s_retryVersion) RetryPending();
    }

    // 한 줄이 깨져 있어도 나머지는 회수한다 — 낙인(ack)도 걸지 않아 서버 쪽에 그대로 남는다.
    static bool TryReadGain(PayoutEntry _payout, out CurrencyGain _gain)
    {
        _gain = CurrencyGain.None;
        if (_payout == null || string.IsNullOrEmpty(_payout.MatchId)) return false;

        if (_payout.Currency == null || _payout.Rank == null)
        {
            Debug.LogError($"[Payout] A payout line is missing a field, so applying it is deferred (match={_payout.MatchId}).");
            return false;
        }

        if (!CurrencyCode.TryParse(_payout.Currency.Currency, out ECurrencyType t_type))
        {
            Debug.LogError($"[Payout] Unknown currency, so applying it is deferred (match={_payout.MatchId}, currency={_payout.Currency.Currency}).");
            return false;
        }

        _gain = new CurrencyGain(t_type, _payout.Currency.Amount);
        return true;
    }

    static void LoadApplied()
    {
        s_applied.Clear();
        string t_json = LocalPrefs.GetString(AppliedKey, string.Empty);
        if (string.IsNullOrEmpty(t_json)) return;
        AppliedStore t_store = JsonUtility.FromJson<AppliedStore>(t_json);
        if (t_store?.matchIds == null) return;
        foreach (string t_matchId in t_store.matchIds)
            if (!string.IsNullOrEmpty(t_matchId)) s_applied.Add(t_matchId);
    }

    static void SaveApplied()
    {
        if (s_applied.Count == 0) LocalPrefs.DeleteKey(AppliedKey);
        else LocalPrefs.SetString(AppliedKey,
            JsonUtility.ToJson(new AppliedStore { matchIds = new List<string>(s_applied) }));
        LocalPrefs.Save();
    }
}
