using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Firebase;
using Firebase.Functions;
using UnityEngine;

static class PayoutInbox
{
    const string AppliedKey = "firebase.payout.applied.v1";
    const string Region = "asia-northeast3";

    [Serializable]
    sealed class AppliedStore
    {
        public List<string> matchIds = new List<string>();
    }

    sealed class Payout
    {
        public string matchId;
        public string currency;
        public long amount;
        public long rankBefore;
        public long rankAfter;
        public long rankSequence;
        public long settledAtMs;
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
            HttpsCallableReference t_callable = FirebaseFunctions.GetInstance(FirebaseApp.DefaultInstance, Region)
                .GetHttpsCallable("claimPayout");
            HttpsCallableResult t_listResponse = await CallAsync(t_callable, new Dictionary<string, object>
            {
                ["env"] = s_envId,
                ["action"] = "list",
            });
            if (t_generation != s_generation || !TryReadPayouts(t_listResponse.Data, out List<Payout> t_payouts)) return;
            t_payouts.Sort((a, b) => a.rankSequence != b.rankSequence
                ? a.rankSequence.CompareTo(b.rankSequence)
                : a.settledAtMs.CompareTo(b.settledAtMs));

            var t_ackIds = new List<string>();
            foreach (Payout t_payout in t_payouts)
            {
                if (!s_applied.Contains(t_payout.matchId))
                {
                    if (!Enum.TryParse(t_payout.currency, true, out ECurrencyType t_currency) ||
                        (int)t_currency < 0 || (int)t_currency >= (int)ECurrencyType.Count)
                    {
                        Debug.LogError($"[Payout] 알 수 없는 재화라 적용을 보류한다(match={t_payout.matchId}, currency={t_payout.currency}).");
                        continue;
                    }

                    var t_gain = new CurrencyGain(t_currency, t_payout.amount);
                    CurrencyManager.Earn(t_gain.Type, t_gain.Amount);
                    CurrencyManager.Save();
                    RankApplyResult t_rank = RankManager.ApplyServerPayout(t_payout.rankBefore, t_payout.rankAfter);
                    DataSaveManager.SaveImmediate();
                    BattleRewardHandoff.Set(t_gain);
                    RankResultHandoff.Set(t_rank);
                    s_applied.Add(t_payout.matchId);
                    SaveApplied();
                }
                t_ackIds.Add(t_payout.matchId);
            }

            if (t_ackIds.Count == 0) return;
            HttpsCallableResult t_ackResponse = await CallAsync(t_callable, new Dictionary<string, object>
            {
                ["env"] = s_envId,
                ["action"] = "ack",
                ["matchIds"] = t_ackIds,
            });
            if (!TryReadAcked(t_ackResponse.Data, out List<string> t_acked)) return;
            foreach (string t_matchId in t_acked) s_applied.Remove(t_matchId);
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

    static bool TryReadPayouts(object _raw, out List<Payout> _payouts)
    {
        _payouts = new List<Payout>();
        if (_raw is not IDictionary t_root || !t_root.Contains("payouts") || t_root["payouts"] is not IList t_items)
            return false;
        foreach (object t_rawItem in t_items)
        {
            if (t_rawItem is not IDictionary t_item ||
                !TryString(t_item, "matchId", out string t_matchId) ||
                !TryMap(t_item, "currency", out IDictionary t_currency) ||
                !TryString(t_currency, "currency", out string t_currencyId) ||
                !TryLong(t_currency, "amount", out long t_amount) ||
                !TryMap(t_item, "rank", out IDictionary t_rank) ||
                !TryLong(t_rank, "before", out long t_before) ||
                !TryLong(t_rank, "after", out long t_after) ||
                !TryLong(t_item, "rankSequence", out long t_rankSequence)) return false;
            TryLong(t_item, "settledAtMs", out long t_settledAtMs);
            _payouts.Add(new Payout
            {
                matchId = t_matchId,
                currency = t_currencyId,
                amount = t_amount,
                rankBefore = t_before,
                rankAfter = t_after,
                rankSequence = t_rankSequence,
                settledAtMs = t_settledAtMs,
            });
        }
        return true;
    }

    static bool TryReadAcked(object _raw, out List<string> _acked)
    {
        _acked = new List<string>();
        if (_raw is not IDictionary t_root || !t_root.Contains("acked") || t_root["acked"] is not IList t_items)
            return false;
        foreach (object t_item in t_items)
            if (t_item is string t_matchId) _acked.Add(t_matchId);
        return true;
    }

    static bool TryMap(IDictionary _map, string _key, out IDictionary _value)
    {
        _value = _map.Contains(_key) ? _map[_key] as IDictionary : null;
        return _value != null;
    }

    static bool TryString(IDictionary _map, string _key, out string _value)
    {
        _value = _map.Contains(_key) ? _map[_key] as string : null;
        return !string.IsNullOrEmpty(_value);
    }

    static bool TryLong(IDictionary _map, string _key, out long _value)
    {
        _value = 0;
        if (!_map.Contains(_key) || _map[_key] == null) return false;
        try { _value = Convert.ToInt64(_map[_key]); return true; }
        catch { return false; }
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

    /// <summary>콜러블 왕복을 Firebase 세션 수명에 묶는다. 안 묶으면 에디터 정리가
    /// Firestore <c>TerminateAsync</c> 에서 이 왕복을 기다리다 못 끝내고,
    /// gRPC 네이티브 스레드가 남아 Unity가 "Reloading Domain"에서 멈춘다.</summary>
    static UniTask<HttpsCallableResult> CallAsync(
        HttpsCallableReference _callable, Dictionary<string, object> _payload)
        => _callable.CallAsync(_payload).AsUniTask().AttachExternalCancellation(FirebaseManager.Lifetime);
}
