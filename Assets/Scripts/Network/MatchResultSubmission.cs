using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using Cysharp.Threading.Tasks;
using Firebase;
using Firebase.Functions;
using UnityEngine;

static class MatchResultSubmission
{
    const string PendingKey = "firebase.matchResult.pending.v2";
    const string Region = "asia-northeast3";

    /// <summary>재시도 지수의 상한. 서버 확정 지급 전에는 큐를 버리지 않는다.</summary>
    const int MaxAttempts = 8;

    /// <summary>재시도 간격(초). 시도 횟수가 쌓일수록 뒤쪽 값을 쓴다.</summary>
    static readonly int[] s_backoffSeconds = { 15, 30, 60, 120, 300 };

    [Serializable]
    sealed class PendingSubmission
    {
        public string env;
        public string matchId;
        public string seedSource;
        public string myNonce;
        public string opponentNonce;
        public string myDeckHash;
        public string opponentDeckHash;
        public string finalStateHash;
        public string stateHashChain;
        public string stateHashChainPrev;
        public int stateHashChainLength;
        public string contentFingerprint;
        public bool won;
        public int myRemaining;
        public int opponentRemaining;
        public long rankPointsBefore;
        public string commandLog;
        public string commandLogHash;
        public int commandCount;
        public bool commandLogTruncated;
        public int commandLogVersion;
        public int attempts;
    }

    [Serializable]
    sealed class PendingStore
    {
        public List<PendingSubmission> items = new List<PendingSubmission>();
    }

    static readonly List<PendingSubmission> s_pending = new List<PendingSubmission>();
    static string s_envId;
    static bool s_sending;
    static int s_generation;

    internal static void Initialize(string _envId)
    {
        s_generation++;
        s_sending = false;
        s_envId = _envId;
        LoadPending();
    }

    internal static void Shutdown()
    {
        s_generation++;
        SavePending();
        s_envId = null;
    }

    internal static void DiscardPending()
    {
        s_pending.Clear();
        SavePending();
    }

    internal static bool TryEnqueue(bool _won, int _myRemaining, int _opponentRemaining, long _rankPointsBefore)
    {
        MultiplayerTurnRunner t_turn = MultiplayerTurnRunner.Instance;
        NetworkGameController t_net = NetworkGameController.Instance;
        bool t_hasSeedIdentity = t_turn != null && !string.IsNullOrEmpty(t_turn.MatchId) &&
            (t_turn.SeedSource == "server" ||
             (t_turn.MyNonce != null && t_turn.OpponentNonce != null));
        if (!t_hasSeedIdentity || t_net == null ||
            string.IsNullOrEmpty(t_net.LocalDeckHash) || string.IsNullOrEmpty(t_net.OpponentDeckHash) ||
            string.IsNullOrEmpty(s_envId))
        {
            Debug.LogError("[MatchResult] 제출 증거가 완성되지 않아 서버 확정을 시작하지 못했다.");
            return false;
        }

        string t_matchId = t_turn.MatchId;
        for (int i = 0; i < s_pending.Count; i++)
            if (s_pending[i].matchId == t_matchId) return true;

        s_pending.Add(new PendingSubmission
        {
            env = s_envId,
            matchId = t_matchId,
            seedSource = t_turn.SeedSource,
            myNonce = t_turn.MyNonce == null ? string.Empty : Hex(t_turn.MyNonce),
            opponentNonce = t_turn.OpponentNonce == null ? string.Empty : Hex(t_turn.OpponentNonce),
            myDeckHash = t_net.LocalDeckHash,
            opponentDeckHash = t_net.OpponentDeckHash,
            finalStateHash = t_net.FinalStateHash.ToString("x16"),
            stateHashChain = t_net.StateHashChain.ToString("x16"),
            stateHashChainPrev = t_net.StateHashChainPrev.ToString("x16"),
            stateHashChainLength = t_net.StateHashChainLength,
            contentFingerprint = SpecSource.BattleFingerprint.ToLowerInvariant(),
            won = _won,
            myRemaining = _myRemaining,
            opponentRemaining = _opponentRemaining,
            rankPointsBefore = _rankPointsBefore,
            commandLog = BattleCommandLog.SerializeBase64(),
            commandLogHash = BattleCommandLog.HashHex(),
            commandCount = BattleCommandLog.Count,
            commandLogTruncated = BattleCommandLog.IsTruncated,
            commandLogVersion = 1,
        });
        SavePending();
        RetryPending();
        return true;
    }

    internal static void RetryPending()
    {
        if (s_sending || s_pending.Count == 0) return;
        SendPending().Forget();
    }

    internal static UniTask FlushAsync() => SendPending();

    static async UniTask SendPending()
    {
        if (s_sending || s_pending.Count == 0) return;
        s_sending = true;
        int t_generation = s_generation;
        try
        {
            // 콜러블은 로그인 토큰이 붙어야만 통과한다(서버가 uid 없으면 unauthenticated).
            // 초기화의 로그인은 대기 없이 시작되므로 여기서 완료를 확인한다 — 미완료면 큐를 유지한 채
            // 물러나고 아래 finally가 재시도를 건다. PlayerSaveCloud·BattleContentSync와 같은 관문이다.
            if (!await EnsureSignedIn())
            {
                ChargeAttemptAndDropExhausted("로그인 미완료");
                return;
            }
            if (t_generation != s_generation) return;

            HttpsCallableReference t_callable = FirebaseFunctions.GetInstance(FirebaseApp.DefaultInstance, Region)
                .GetHttpsCallable("submitMatchResult");
            for (int i = s_pending.Count - 1; i >= 0; i--)
            {
                PendingSubmission t_item = s_pending[i];
                t_item.attempts++;
                bool t_drop = false;
                try
                {
                    HttpsCallableResult t_response = await t_callable.CallAsync(ToPayload(t_item))
                        .AsUniTask()
                        .AttachExternalCancellation(FirebaseManager.Lifetime);
                    if (t_generation != s_generation) return;
                    if (TryHandleResponse(t_response.Data, t_item.matchId, out bool t_complete) && t_complete)
                    {
                        t_drop = true;
                        PayoutInbox.RetryPending();
                    }
                }
                catch (Exception t_exception)
                {
                    // 영구 거절은 재시도해도 같은 답이 온다. 큐에 남기면 같은 실패를 영원히 반복한다.
                    if (IsPermanentRejection(t_exception, out FunctionsErrorCode t_code))
                    {
                        Debug.LogError($"[MatchResult] 서버가 제출을 영구 거절했다(match={t_item.matchId}, " +
                                       $"code={t_code}, uid={FirebaseAuthService.Instance.UserId}): " +
                                       $"{t_exception.GetBaseException().Message}");
                        t_drop = true;
                    }
                    else
                    {
                        Debug.LogWarning($"[MatchResult] 제출 보류(match={t_item.matchId}, " +
                                         $"{t_item.attempts}/{MaxAttempts}회): {t_exception.GetBaseException().Message}");
                    }
                }

                // 멀티 보상·랭크는 서버 payout이 진실원이다. 일시 실패나 pending 제출은 버리면 안 된다.
                if (!t_drop && t_item.attempts >= MaxAttempts)
                    t_item.attempts = MaxAttempts;
                if (t_drop) s_pending.RemoveAt(i);
            }
        }
        finally
        {
            if (t_generation == s_generation)
            {
                s_sending = false;
                SavePending();
                if (s_pending.Count > 0) RetryAfterDelay(t_generation, NextDelaySeconds()).Forget();
            }
        }
    }

    /// <summary>재시도가 무의미한 서버 판정. Unauthenticated는 로그인이 붙으면 통과하므로 여기 넣지 않는다.</summary>
    internal static bool IsPermanentRejection(Exception _exception, out FunctionsErrorCode _code)
    {
        _code = FunctionsErrorCode.None;
        if (_exception.GetBaseException() is not FunctionsException t_functions) return false;
        _code = t_functions.ErrorCode;
        return _code == FunctionsErrorCode.InvalidArgument ||
               _code == FunctionsErrorCode.AlreadyExists ||
               _code == FunctionsErrorCode.PermissionDenied;
    }

    /// <summary>전송 루프 앞에서 물러나는 경로의 재시도 지수를 올린다.</summary>
    static void ChargeAttemptAndDropExhausted(string _reason)
    {
        for (int i = s_pending.Count - 1; i >= 0; i--)
        {
            PendingSubmission t_item = s_pending[i];
            t_item.attempts++;
            if (t_item.attempts > MaxAttempts) t_item.attempts = MaxAttempts;
        }
    }

    internal static async UniTask<bool> EnsureSignedIn()
    {
        FirebaseAuthService t_auth = FirebaseAuthService.Instance;
        if (t_auth.IsCurrentUserActive) return true;

        await t_auth.InitializeAsync();
        if (t_auth.IsCurrentUserActive) return true;

        Debug.LogWarning($"[MatchResult] 로그인 전이라 제출을 미룬다(state={t_auth.State}, error={t_auth.LastError}).");
        return false;
    }

    /// <summary>큐에서 가장 적게 시도한 항목 기준의 다음 재시도 간격.</summary>
    static int NextDelaySeconds()
    {
        int t_minAttempts = int.MaxValue;
        for (int i = 0; i < s_pending.Count; i++)
            if (s_pending[i].attempts < t_minAttempts) t_minAttempts = s_pending[i].attempts;

        int t_index = Mathf.Clamp(t_minAttempts - 1, 0, s_backoffSeconds.Length - 1);
        return s_backoffSeconds[t_index];
    }

    static async UniTaskVoid RetryAfterDelay(int _generation, int _delaySeconds)
    {
        await UniTask.Delay(TimeSpan.FromSeconds(_delaySeconds));
        if (_generation == s_generation) RetryPending();
    }

    static Dictionary<string, object> ToPayload(PendingSubmission _item) => new Dictionary<string, object>
    {
        ["env"] = _item.env,
        ["matchId"] = _item.matchId,
        ["seedSource"] = string.IsNullOrEmpty(_item.seedSource) ? "commit_reveal" : _item.seedSource,
        ["myNonce"] = _item.myNonce,
        ["opponentNonce"] = _item.opponentNonce,
        ["myDeckHash"] = _item.myDeckHash,
        ["opponentDeckHash"] = _item.opponentDeckHash,
        ["finalStateHash"] = _item.finalStateHash,
        ["stateHashChain"] = _item.stateHashChain,
        ["stateHashChainPrev"] = _item.stateHashChainPrev,
        ["stateHashChainLength"] = _item.stateHashChainLength,
        ["contentFingerprint"] = _item.contentFingerprint,
        ["won"] = _item.won,
        ["myRemaining"] = _item.myRemaining,
        ["opponentRemaining"] = _item.opponentRemaining,
        ["rankPointsBefore"] = _item.rankPointsBefore,
        ["commandLog"] = _item.commandLog,
        ["commandLogHash"] = _item.commandLogHash,
        ["commandCount"] = _item.commandCount,
        ["commandLogTruncated"] = _item.commandLogTruncated,
        ["commandLogVersion"] = _item.commandLogVersion,
    };

    static bool TryHandleResponse(object _raw, string _matchId, out bool _complete)
    {
        _complete = false;
        if (!TryMap(_raw, out IDictionary t_root) || !TryString(t_root, "status", out string t_status)) return false;
        if (t_status == "pending") return true;
        if (t_status == "flagged")
        {
            _complete = true;
            TryString(t_root, "reason", out string t_reason);
            Debug.LogError($"[MatchResult] 서버가 매치를 무효 처리했다(match={_matchId}, reason={t_reason}).");
            return true;
        }
        if (t_status != "confirmed") return false;

        // confirmed 트랜잭션이 양쪽 payout을 함께 만들었고 별도 inbox가 적용·ack한다.
        // 제출 큐를 내린 뒤 PayoutInbox가 서버 원장을 로컬 세이브에 반영한다.
        _complete = true;
        Debug.Log($"[MatchResult] 서버 대조 일치, payout 회수를 시작한다(match={_matchId}).");
        return true;
    }

    static bool TryMap(object _value, out IDictionary _map)
    {
        _map = _value as IDictionary;
        return _map != null;
    }

    static bool TryString(IDictionary _map, string _key, out string _value)
    {
        _value = _map.Contains(_key) ? _map[_key] as string : null;
        return _value != null;
    }

    internal static string MatchId(byte[] _myNonce, byte[] _opponentNonce)
    {
        byte[] t_seed = new byte[8];
        for (int i = 0; i < t_seed.Length; i++) t_seed[i] = (byte)(_myNonce[i] ^ _opponentNonce[i]);
        using (SHA256 t_sha = SHA256.Create())
        {
            byte[] t_hash = t_sha.ComputeHash(t_seed);
            var t_builder = new System.Text.StringBuilder(32);
            for (int i = 0; i < 16; i++) t_builder.Append(t_hash[i].ToString("x2"));
            return t_builder.ToString();
        }
    }

    internal static string Hex(byte[] _bytes)
    {
        var t_builder = new System.Text.StringBuilder(_bytes.Length * 2);
        foreach (byte t_byte in _bytes) t_builder.Append(t_byte.ToString("x2"));
        return t_builder.ToString();
    }

    static void LoadPending()
    {
        s_pending.Clear();
        string t_json = LocalPrefs.GetString(PendingKey, string.Empty);
        if (string.IsNullOrEmpty(t_json)) return;
        PendingStore t_store = JsonUtility.FromJson<PendingStore>(t_json);
        if (t_store?.items != null) s_pending.AddRange(t_store.items);
    }

    static void SavePending()
    {
        if (s_pending.Count == 0) LocalPrefs.DeleteKey(PendingKey);
        else LocalPrefs.SetString(PendingKey, JsonUtility.ToJson(new PendingStore { items = new List<PendingSubmission>(s_pending) }));
        LocalPrefs.Save();
    }
}

sealed class MatchResultFirebaseModule : IFirebaseModule
{
    public void Initialize(in FirebaseContext _context)
    {
        MatchResultSubmission.Initialize(_context.EnvId);
        PayoutInbox.Initialize(_context.EnvId);
    }

    public void RetryPending()
    {
        MatchResultSubmission.RetryPending();
        PayoutInbox.RetryPending();
    }

    public async UniTask FlushPendingAsync()
    {
        await MatchResultSubmission.FlushAsync();
        await PayoutInbox.FlushAsync();
    }

    public void Shutdown()
    {
        PayoutInbox.Shutdown();
        MatchResultSubmission.Shutdown();
    }
}
