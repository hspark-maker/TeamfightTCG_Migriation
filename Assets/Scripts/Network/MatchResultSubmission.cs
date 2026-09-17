using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
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

        /// <summary>셔플 후 초기 보드 순서(owner0, owner1). 서버가 시드로 산출할 수 없어 실어 보낸다 —
        /// 신뢰는 양쪽 제출이 같은지로만 세운다.</summary>
        public int[] boardOrder0;
        public int[] boardOrder1;

        /// <summary>무승부(양쪽 동시 전멸). true면 won 은 양쪽 다 false 이고, 서버는 승자 대조를
        /// 건너뛴 뒤 골드만 지급하고 랭크는 건드리지 않는다.</summary>
        public bool draw;

        /// <summary>전투가 끝난 시점의 보드 해시. <see cref="finalStateHash"/>(= 마지막으로 양쪽이
        /// **합의한** 해시)와 다르다 — 마지막 턴은 상대와 교환할 기회가 없어 합의 목록에 없다.
        /// 서버 재시뮬 대조는 이 값과 해야 한다. 합의 해시는 두 클라가 같은 지점까지 같았는지 보는 용도다.</summary>
        public string endStateHash;
        public int attempts;
        public string adventureNodeId;

        [NonSerialized] public bool confirmed;
        [NonSerialized] public bool rejected;
        [NonSerialized] public bool collected;
        [NonSerialized] public bool adventureWon;
        [NonSerialized] public bool experienceCollected;
        [NonSerialized] public bool payoutCollected;
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
    static CancellationTokenSource s_retryCancellation;
    static PendingSubmission s_battleSubmission;

    internal static string BattleMatchId => s_battleSubmission?.matchId;

    internal static void Initialize(string _envId)
    {
        s_generation++;
        CancelRetry();
        s_sending = false;
        s_battleSubmission = null;
        s_envId = _envId;
        LoadPending();
    }

    internal static void Shutdown()
    {
        s_generation++;
        CancelRetry();
        SavePending();
        s_envId = null;
        s_battleSubmission = null;
    }

    internal static void DiscardPending()
    {
        s_generation++;
        CancelRetry();
        s_sending = false;
        s_pending.Clear();
        s_battleSubmission = null;
        SavePending();
    }

    internal static bool TryEnqueue(bool _won, int _myRemaining, int _opponentRemaining, long _rankPointsBefore,
        bool _draw = false, ulong _endStateHash = 0)
    {
        MultiplayerTurnRunner t_turn = MultiplayerTurnRunner.Instance;
        NetworkGameController t_net = NetworkGameController.Instance;
        bool t_multiplayer = DeckConfig.IsMultiplayer;
        bool t_hasMultiplayerIdentity = t_turn != null && !string.IsNullOrEmpty(t_turn.MatchId) &&
            t_turn.SeedSource == "server" && t_net != null &&
            !string.IsNullOrEmpty(t_net.LocalDeckHash) && !string.IsNullOrEmpty(t_net.OpponentDeckHash);
        bool t_hasSoloIdentity = SoloMatchHandoff.TryGetResultIdentity(
            out string t_soloMatchId, out string t_soloDeckHash, out string t_soloOpponentDeckHash);
        if (string.IsNullOrEmpty(s_envId) || (t_multiplayer ? !t_hasMultiplayerIdentity : !t_hasSoloIdentity))
        {
            Debug.LogError("[MatchResult] The submission evidence was not complete, so server confirmation could not start.");
            return false;
        }

        string t_matchId = t_multiplayer ? t_turn.MatchId : t_soloMatchId;
        for (int i = 0; i < s_pending.Count; i++)
            if (s_pending[i].matchId == t_matchId)
            {
                s_battleSubmission = s_pending[i];
                return true;
            }

        s_battleSubmission = new PendingSubmission
        {
            env = s_envId,
            matchId = t_matchId,
            seedSource = t_multiplayer ? t_turn.SeedSource : "server",
            // 서버 시드에는 nonce 가 없다. 서버도 seedSource=="server" 면 빈 문자열로 정규화한다.
            myNonce = string.Empty,
            opponentNonce = string.Empty,
            myDeckHash = t_multiplayer ? t_net.LocalDeckHash : t_soloDeckHash,
            opponentDeckHash = t_multiplayer ? t_net.OpponentDeckHash : t_soloOpponentDeckHash,
            finalStateHash = (t_multiplayer ? t_net.FinalStateHash : _endStateHash).ToString("x16"),
            stateHashChain = (t_multiplayer ? t_net.StateHashChain : 0UL).ToString("x16"),
            stateHashChainPrev = (t_multiplayer ? t_net.StateHashChainPrev : 0UL).ToString("x16"),
            stateHashChainLength = t_multiplayer ? t_net.StateHashChainLength : 0,
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
            boardOrder0 = BattleBoardOrder.For(0),
            boardOrder1 = BattleBoardOrder.For(1),
            draw = _draw,
            endStateHash = _endStateHash.ToString("x16"),
            adventureNodeId = AdventureRun.IsActive ? AdventureRun.NodeId : null,
        };
        s_pending.Add(s_battleSubmission);
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

    /// <summary>이번 전투의 판정과 지급 ack를 기다린다. pending·통신 지연은 실패로 처리하지 않는다.</summary>
    internal static async UniTask WaitForBattleResultAsync()
    {
        PendingSubmission t_item = s_battleSubmission;
        if (t_item == null) return;
        int t_generation = s_generation;
        try
        {
            while (t_generation == s_generation && !GameInitialization.IsTerminated &&
                   !t_item.rejected && !t_item.collected)
            {
                RetryPending();
                if (t_item.confirmed && string.IsNullOrEmpty(t_item.adventureNodeId)) await PayoutInbox.FlushAsync();
                // 배속·일시 정지와 무관하게 확인한다. 전송 중이면 기존 요청을 기다리므로 중복 제출하지 않는다.
                await UniTask.Delay(TimeSpan.FromSeconds(2), DelayType.Realtime);
            }
        }
        finally
        {
            if (ReferenceEquals(s_battleSubmission, t_item)) s_battleSubmission = null;
        }
    }

    internal static void NotifyPayoutCollected(string _matchId)
    {
        if (s_battleSubmission?.matchId != _matchId) return;
        s_battleSubmission.payoutCollected = true;
        s_battleSubmission.collected = s_battleSubmission.experienceCollected;
    }

    static async UniTask SendPending()
    {
        if (s_sending || s_pending.Count == 0 || string.IsNullOrEmpty(s_envId)) return;
        CancelRetry();
        s_sending = true;
        int t_generation = s_generation;
        try
        {
            // 콜러블은 로그인 토큰이 붙어야만 통과한다(서버가 uid 없으면 unauthenticated).
            // 초기화의 로그인은 대기 없이 시작되므로 여기서 완료를 확인한다 — 미완료면 큐를 유지한 채
            // 물러나고 아래 finally가 재시도를 건다. PlayerSaveCloud·BattleContentSync와 같은 관문이다.
            if (!await EnsureSignedIn())
            {
                if (t_generation != s_generation) return;
                ChargeAttempt();
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
                    if (TryHandleResponse(t_response.Data, t_item, out bool t_complete) && t_complete)
                    {
                        if (t_item.confirmed && !t_item.experienceCollected)
                        {
                            // 완료 큐를 내리기 전에 XP도 회수한다. 앱 종료·응답 유실은 같은 matchId로 복구한다.
                            t_item.experienceCollected = await AccountExperienceCommands.ClaimBattleAsync(t_item.env, t_item.matchId);
                            if (t_generation != s_generation) return;
                            if (!t_item.experienceCollected)
                            {
                                t_item.attempts = Math.Min(t_item.attempts, MaxAttempts);
                                continue;
                            }
                            if (t_item.payoutCollected) t_item.collected = true;
                        }
                        if (t_item.confirmed && !string.IsNullOrEmpty(t_item.adventureNodeId))
                        {
                            // 모험은 랭크·골드 payout이 없다. 승리 낙인 응답까지 받아야 복귀 보상을 열 수 있다.
                            t_drop = await CompleteAdventureAsync(t_item, t_generation);
                            if (t_generation != s_generation) return;
                        }
                        else
                        {
                            t_drop = true;
                            if (t_item.confirmed) PayoutInbox.RetryPending();
                        }
                    }
                }
                catch (Exception t_exception)
                {
                    if (t_generation != s_generation) return;
                    // 영구 거절은 재시도해도 같은 답이 온다. 큐에 남기면 같은 실패를 영원히 반복한다.
                    if (IsPermanentRejection(t_exception, out FunctionsErrorCode t_code))
                    {
                        Debug.LogError($"[MatchResult] The server permanently rejected the submission (match={t_item.matchId}, " +
                                       $"code={t_code}, uid={FirebaseAuthService.Instance.UserId}): " +
                                       $"{t_exception.GetBaseException().Message}");
                        // 제출 거절은 매치 무효 확정과 다르다. 다른 제출로 정산될 수도 있어 미지급을 단정하지 않는다.
                        MatchResultFailurePopup.Show();
                        t_item.rejected = true;
                        t_drop = true;
                    }
                    else
                    {
                        Debug.LogWarning($"[MatchResult] Submission deferred (match={t_item.matchId}, " +
                                         $"attempt {t_item.attempts}/{MaxAttempts}): {t_exception.GetBaseException().Message}");
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
                if (s_pending.Count > 0) ScheduleRetry(t_generation);
            }
        }
    }

    /// <summary>재시도가 무의미한 서버 판정. Unauthenticated는 로그인이 붙으면 통과하므로 여기 넣지 않는다.
    /// FailedPrecondition은 이 제출 자체가 서버 상태와 어긋났다는 뜻이라(랭크 기준선 불일치·구 프로토콜·정산 계산 실패)
    /// 몇 번을 더 보내도 같은 답이 온다 — 서버가 <b>일시적</b> 사정(스펙 표 적재 실패 등)으로 거절할 때는
    /// Unavailable로 내리기로 계약했다(functions/src/commands/submitMatchResult.ts).</summary>
    internal static bool IsPermanentRejection(Exception _exception, out FunctionsErrorCode _code)
    {
        _code = FunctionsErrorCode.None;
        if (_exception.GetBaseException() is not FunctionsException t_functions) return false;
        _code = t_functions.ErrorCode;
        return _code == FunctionsErrorCode.InvalidArgument ||
               _code == FunctionsErrorCode.AlreadyExists ||
               _code == FunctionsErrorCode.PermissionDenied ||
               _code == FunctionsErrorCode.FailedPrecondition;
    }

    static async UniTask<bool> CompleteAdventureAsync(PendingSubmission _item, int _generation)
    {
        if (_item.adventureWon &&
            !await AdventureWinCommand.ReportWinAsync(_item.adventureNodeId, _item.matchId)) return false;
        if (_generation != s_generation) return false;
        if (ReferenceEquals(s_battleSubmission, _item))
            AdventureResultHandoff.Set(_item.adventureNodeId, _item.adventureWon);
        _item.collected = true;
        MissionCommands.RefreshAsync().Forget();
        return true;
    }

    /// <summary>전송 루프 앞에서 물러나는 경로의 재시도 지수를 올린다. 여기서 큐를 버리지는 않는다 —
    /// 물러난 이유(로그인 미완료 등)는 다음 시도에 풀릴 수 있어 영구 거절과 성격이 다르다.
    /// <see cref="MaxAttempts"/>는 재시도 간격의 상한일 뿐 포기 지점이 아니다.
    /// 물러난 사유는 판정한 자리(<see cref="EnsureSignedIn"/>)가 로그로 남긴다.</summary>
    static void ChargeAttempt()
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

        Debug.LogWarning($"[MatchResult] Not signed in yet, so the submission is deferred (state={t_auth.State}, error={t_auth.LastError}).");
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

    static void CancelRetry()
    {
        CancellationTokenSource t_retry = s_retryCancellation;
        s_retryCancellation = null;
        // The waiting task owns disposal, including cancellation observed on the next player loop.
        t_retry?.Cancel();
    }

    static void ScheduleRetry(int _generation)
    {
        CancelRetry();
        var t_retry = new CancellationTokenSource();
        s_retryCancellation = t_retry;
        RetryAfterDelay(_generation, NextDelaySeconds(), t_retry).Forget();
    }

    static async UniTaskVoid RetryAfterDelay(int _generation, int _delaySeconds,
        CancellationTokenSource _retry)
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(_delaySeconds), cancellationToken: _retry.Token);
            if (_retry.IsCancellationRequested || !ReferenceEquals(s_retryCancellation, _retry)) return;
        }
        catch (OperationCanceledException) when (_retry.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(s_retryCancellation, _retry)) s_retryCancellation = null;
            _retry.Dispose();
        }
        if (_generation == s_generation) RetryPending();
    }

    static Dictionary<string, object> ToPayload(PendingSubmission _item) => new Dictionary<string, object>
    {
        ["env"] = _item.env,
        ["matchId"] = _item.matchId,
        ["seedSource"] = string.IsNullOrEmpty(_item.seedSource) ? "server" : _item.seedSource,
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
        ["boardOrder"] = new List<object> { ToObjectList(_item.boardOrder0), ToObjectList(_item.boardOrder1) },
        ["draw"] = _item.draw,
        ["endStateHash"] = _item.endStateHash ?? "",
    };

    static bool TryHandleResponse(object _raw, PendingSubmission _item, out bool _complete)
    {
        _complete = false;
        if (!TryMap(_raw, out IDictionary t_root) || !TryString(t_root, "status", out string t_status)) return false;
        if (t_status == "pending") return true;
        if (t_status == "flagged")
        {
            _complete = true;
            _item.rejected = true;
            TryString(t_root, "reason", out string t_reason);
            Debug.LogError($"[MatchResult] The server voided the match (match={_item.matchId}, reason={t_reason}).");
            // 무효는 payout 문서를 아예 만들지 않는다 — PayoutInbox가 나중에 메워 줄 것도 없다.
            MatchResultFailurePopup.Show(_voided: true);
            return true;
        }
        if (t_status != "confirmed") return false;
        if (!string.IsNullOrEmpty(_item.adventureNodeId))
        {
            if (!TryString(t_root, "adventureNodeId", out string t_nodeId) || t_nodeId != _item.adventureNodeId ||
                !t_root.Contains("won") || t_root["won"] is not bool t_won ||
                !t_root.Contains("draw") || t_root["draw"] is not bool t_draw) return false;
            _item.adventureWon = t_won && !t_draw;
        }
        _item.confirmed = true;
        MissionCommands.Invalidate();

        // confirmed 트랜잭션이 양쪽 payout을 함께 만들었고 별도 inbox가 적용·ack한다.
        // 제출 큐를 내린 뒤 PayoutInbox가 서버 원장을 로컬 세이브에 반영한다.
        _complete = true;
        Debug.Log(
            $"[MatchResult] Server cross-check matched; starting payout collection (match={_item.matchId}, " +
            $"saveUploadsThisSession={PlayerSaveCloud.UploadCountThisSession}).");
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

    // Firebase callable 페이로드는 List<object> 만 안전하게 넘어간다. int[] 를 그대로 넣으면
    // 플랫폼에 따라 직렬화가 갈린다.
    static List<object> ToObjectList(int[] _values)
    {
        var t_list = new List<object>(_values?.Length ?? 0);
        if (_values == null) return t_list;
        for (int i = 0; i < _values.Length; i++) t_list.Add(_values[i]);
        return t_list;
    }
}

sealed class MatchResultFirebaseModule : IFirebaseModule
{
    public void Initialize(in FirebaseContext _context)
    {
        MatchResultSubmission.Initialize(_context.EnvId);
        PayoutInbox.Initialize(_context.EnvId);
        // 입장 flush에 기대지 않고, 앱 재시작으로 복원한 미제출 결과도 회수한다.
        MatchResultSubmission.RetryPending();
    }

    public void RetryPending()
    {
        MatchResultSubmission.RetryPending();
        PayoutInbox.RefreshOnResume();
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
