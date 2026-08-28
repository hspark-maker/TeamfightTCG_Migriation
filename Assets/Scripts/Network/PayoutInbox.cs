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

    [Serializable]
    sealed class AppliedStore
    {
        public List<string> matchIds = new List<string>();
    }

    static readonly HashSet<string> s_applied = new HashSet<string>();
    static string s_envId;
    static bool s_sending;
    static int s_generation;

    internal static void Initialize(string _envId)
    {
        s_generation++;
        s_sending = false;
        s_envId = _envId;
        LoadApplied();
        RetryPending();
    }

    internal static void Shutdown()
    {
        s_generation++;
        s_sending = false;
        SaveApplied();
        s_envId = null;
    }

    internal static void RetryPending()
    {
        if (s_sending || string.IsNullOrEmpty(s_envId)) return;
        ClaimAsync().Forget();
    }

    internal static UniTask FlushAsync() => ClaimAsync();

    static async UniTask ClaimAsync()
    {
        if (s_sending || string.IsNullOrEmpty(s_envId)) return;
        s_sending = true;
        int t_generation = s_generation;
        try
        {
            await UniTask.WaitUntil(() =>
                (SaveDependentManagersStep.IsInstalled && RankManager.IsConfigured) || GameInitialization.IsTerminated);
            if (GameInitialization.IsTerminated || t_generation != s_generation) return;
            if (!await MatchResultSubmission.EnsureSignedIn() || t_generation != s_generation) return;

            // list 는 아무 문서도 쓰지 않는다 — 업로드 봉인도 응답 채택도 걸지 않는다.
            PayoutListResult t_list = await ServerSaveCommands.InvokeReadOnlyAsync<PayoutListResult>(
                CommandName,
                new { env = s_envId, action = "list" });
            if (t_generation != s_generation || t_list?.Payouts == null) return;

            List<PayoutEntry> t_payouts = t_list.Payouts;
            t_payouts.Sort((a, b) => a.RankSequence != b.RankSequence
                ? a.RankSequence.CompareTo(b.RankSequence)
                : a.SettledAtMs.CompareTo(b.SettledAtMs));

            var t_ackIds = new List<string>();
            foreach (PayoutEntry t_payout in t_payouts)
            {
                if (!TryReadGain(t_payout, out CurrencyGain t_gain)) continue;

                if (!s_applied.Contains(t_payout.MatchId))
                {
                    // 잔액은 건드리지 않는다 — 크레딧의 진실원은 아래 ack 응답의 지갑이다.
                    RankApplyResult t_rank = RankManager.ApplyServerPayout(t_payout.Rank.Before, t_payout.Rank.After);
                    DataSaveManager.SaveImmediate();
                    BattleRewardHandoff.Set(t_gain);
                    RankResultHandoff.Set(t_rank);
                    s_applied.Add(t_payout.MatchId);
                    SaveApplied();
                }

                t_ackIds.Add(t_payout.MatchId);
            }

            if (t_ackIds.Count == 0) return;

            // ack 는 지갑을 쓴다 — 채택 창구를 타야 응답의 wallet 이 잔액에 반영된다(세이브는 쓰지 않아 revision 이 없다).
            PayoutAckResult t_ack = await ServerSaveCommands.InvokeAsync<PayoutAckResult>(
                CommandName,
                new { env = s_envId, action = "ack", matchIds = t_ackIds });
            if (t_ack?.Acked == null) return;

            foreach (string t_matchId in t_ack.Acked) s_applied.Remove(t_matchId);
            SaveApplied();
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[Payout] 서버 확정 지급 회수를 미룬다: {t_exception.GetBaseException().Message}");
            RetryAfterDelay(t_generation).Forget();
        }
        finally
        {
            if (t_generation == s_generation) s_sending = false;
        }
    }

    static async UniTaskVoid RetryAfterDelay(int _generation)
    {
        await UniTask.Delay(TimeSpan.FromSeconds(30));
        if (_generation == s_generation) RetryPending();
    }

    // 한 줄이 깨져 있어도 나머지는 회수한다 — 낙인(ack)도 걸지 않아 서버 쪽에 그대로 남는다.
    static bool TryReadGain(PayoutEntry _payout, out CurrencyGain _gain)
    {
        _gain = CurrencyGain.None;
        if (_payout == null || string.IsNullOrEmpty(_payout.MatchId)) return false;

        if (_payout.Currency == null || _payout.Rank == null)
        {
            Debug.LogError($"[Payout] 지급 줄에 필드가 빠져 적용을 보류한다(match={_payout.MatchId}).");
            return false;
        }

        if (!CurrencyCode.TryParse(_payout.Currency.Currency, out ECurrencyType t_type))
        {
            Debug.LogError($"[Payout] 알 수 없는 재화라 적용을 보류한다(match={_payout.MatchId}, currency={_payout.Currency.Currency}).");
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
