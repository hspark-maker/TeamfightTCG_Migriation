using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

internal interface IPreBattleNetworkReceiver
{
    int LocalOwnerIndex { get; }
    void OnInitialDeckReceived(MatchGrowthOpponent _opponent, int[] _cardIds, CardGrowth[] _growth);
    void OnServerSeedCapabilityReceived(byte[] _pairingNonce);
    void OnContentMismatch(string _detail);
    void OnMatchAbort(EMatchEndReason _reason);
    void OnProtocolError(string _detail);
}

public sealed class PreBattleMatchData
{
    public string MatchId;
    public string SeedHex;
    public ulong Seed;
    public int RulesetVersion;
    public int LocalOwnerIndex;
    public int[] LocalCardIds;
    public CardGrowth[] LocalGrowth;
    public int[] OpponentCardIds;
    public CardGrowth[] OpponentGrowth;
}

public static class PreBattleMatchHandoff
{
    static PreBattleMatchData s_data;

    public static bool HasValue => s_data != null;

    internal static void Set(PreBattleMatchData _data) => s_data = _data;

    public static bool TryConsume(out PreBattleMatchData _data)
    {
        _data = s_data;
        s_data = null;
        return _data != null;
    }

    public static void Clear() => s_data = null;
}

public enum EPreBattleSyncResult
{
    Success,
    Failed,
    Canceled,
}

public static class PreBattleMatchSync
{
    sealed class Receiver : IPreBattleNetworkReceiver
    {
        readonly UniTaskCompletionSource capabilityTcs = new UniTaskCompletionSource();
        readonly UniTaskCompletionSource deckTcs = new UniTaskCompletionSource();
        readonly UniTaskCompletionSource abortTcs = new UniTaskCompletionSource();

        public int LocalOwnerIndex { get; }
        public byte[] OpponentPairingNonce { get; private set; }
        public int[] OpponentCardIds { get; private set; }
        public CardGrowth[] OpponentGrowth { get; private set; }
        public EMatchEndReason AbortReason { get; private set; } = EMatchEndReason.InitError;
        public string Error { get; private set; }
        public bool Failed => Error != null;

        public Receiver(int _localOwnerIndex) => LocalOwnerIndex = _localOwnerIndex;

        public void OnInitialDeckReceived(MatchGrowthOpponent _opponent, int[] _cardIds, CardGrowth[] _growth)
        {
            if (Failed || OpponentCardIds != null) return;
            if (_opponent.OwnerIndex == LocalOwnerIndex || _cardIds == null || _growth == null ||
                _cardIds.Length != DeckSaveManager.DECK_SIZE || _cardIds.Length != _growth.Length)
            {
                Fail(EMatchEndReason.Desync, "상대 덱 페이로드가 유효하지 않습니다.");
                return;
            }
            for (int i = 0; i < _cardIds.Length; i++)
            {
                bool t_known = CardCatalog.Contains(_cardIds[i]);
                string t_error = t_known ? null : "카드 카탈로그에 없음";
                bool t_validGrowth = t_known && MatchGrowthValidation.IsValid(
                    _cardIds[i], _growth[i], out t_error);
                if (!t_validGrowth ||
                    (i > 0 && _cardIds[i - 1] >= _cardIds[i]))
                {
                    Fail(EMatchEndReason.Desync,
                        $"상대 덱 스냅샷 검증 실패(index={i}, id={_cardIds[i]}): {t_error}");
                    return;
                }
            }
            OpponentCardIds = (int[])_cardIds.Clone();
            OpponentGrowth = (CardGrowth[])_growth.Clone();
            deckTcs.TrySetResult();
        }

        public void OnServerSeedCapabilityReceived(byte[] _pairingNonce)
        {
            if (Failed || _pairingNonce == null || _pairingNonce.Length != 16)
            {
                Fail(EMatchEndReason.Desync, "서버 시드 capability nonce가 유효하지 않습니다.");
                return;
            }
            OpponentPairingNonce = (byte[])_pairingNonce.Clone();
            capabilityTcs.TrySetResult();
        }

        public void OnContentMismatch(string _detail) => Fail(EMatchEndReason.Desync, _detail);
        public void OnMatchAbort(EMatchEndReason _reason) => Fail(_reason, "상대가 매치 준비를 중단했습니다.");
        public void OnProtocolError(string _detail) => Fail(EMatchEndReason.Desync, _detail);

        public async UniTask<bool> WaitForPeerAsync(CancellationToken _ct)
        {
            int t_completed = await UniTask.WhenAny(
                UniTask.WhenAll(capabilityTcs.Task, deckTcs.Task),
                abortTcs.Task,
                UniTask.Delay(Timeout.InfiniteTimeSpan, cancellationToken: _ct));
            return t_completed == 0 && !Failed;
        }

        void Fail(EMatchEndReason _reason, string _error)
        {
            if (Failed) return;
            AbortReason = _reason;
            Error = _error ?? "매치 준비 프로토콜 오류";
            abortTcs.TrySetResult();
        }
    }

    public static async UniTask<EPreBattleSyncResult> RunAsync(CancellationToken _ct)
    {
        PreBattleMatchHandoff.Clear();
        NetworkSession t_session = NetworkSession.Instance;
        NetworkGameController t_network = NetworkGameController.Instance;
        if (t_session?.Runner == null || !t_session.Runner.IsRunning || t_network == null)
            return EPreBattleSyncResult.Failed;

        int t_ownerIndex = t_session.Runner.IsSharedModeMasterClient ? 0 : 1;
        var t_receiver = new Receiver(t_ownerIndex);
        using var t_deadline = CancellationTokenSource.CreateLinkedTokenSource(_ct);
        t_deadline.CancelAfter(TimeSpan.FromSeconds(NetTimeouts.PreBattleSyncSec));
        CancellationToken t_token = t_deadline.Token;

        void OnPlayerLeft(Fusion.PlayerRef _) => t_deadline.Cancel();
        void OnConnectionFailed(string _) => t_deadline.Cancel();
        t_session.OnPlayerLeftRoom += OnPlayerLeft;
        t_session.OnConnectionFailed += OnConnectionFailed;
        MatchRandom.Reset();
        NetworkGameController.SetPreBattleReceiver(t_receiver);

        try
        {
            (bool t_deckOk, int[] t_cardIds, CardGrowth[] t_growth) =
                await TryGetCanonicalLocalDeckAsync(t_token);
            if (!t_deckOk)
            {
                t_network.SendMatchAbort(EMatchEndReason.InitError);
                return EPreBattleSyncResult.Failed;
            }

            byte[] t_pairingNonce = NewPairingNonce();
            t_network.SendServerSeedCapability(t_pairingNonce);
            if (!t_network.SendInitialDeck(t_cardIds, t_growth, t_ownerIndex))
            {
                t_network.SendMatchAbort(EMatchEndReason.InitError);
                return EPreBattleSyncResult.Failed;
            }

            if (!await t_receiver.WaitForPeerAsync(t_token))
            {
                if (t_receiver.Failed) Debug.LogError($"[PreBattleSync] {t_receiver.Error}");
                if (!_ct.IsCancellationRequested && !t_token.IsCancellationRequested)
                    t_network.SendMatchAbort(t_receiver.AbortReason);
                return _ct.IsCancellationRequested ? EPreBattleSyncResult.Canceled : EPreBattleSyncResult.Failed;
            }

            string t_pairingKey = BuildPairingKey(
                t_session.PairingKey, t_pairingNonce, t_receiver.OpponentPairingNonce);
            if (string.IsNullOrEmpty(t_pairingKey))
            {
                t_network.SendMatchAbort(EMatchEndReason.InitError);
                return EPreBattleSyncResult.Failed;
            }

            (ServerMatchSeedStatus status, ServerMatchSeed match) t_seedResult =
                await ServerMatchSeedSubmission.TryAcquireAsync(
                    ContentProfileConfig.Active.CloudEnvId,
                    t_pairingKey,
                    SpecSource.BattleFingerprint.ToLowerInvariant(),
                    t_token);
            if (t_seedResult.status != ServerMatchSeedStatus.Paired || t_seedResult.match == null)
            {
                if (!t_token.IsCancellationRequested)
                    t_network.SendMatchAbort(EMatchEndReason.InitError);
                return t_token.IsCancellationRequested ? EPreBattleSyncResult.Canceled : EPreBattleSyncResult.Failed;
            }

            MatchRandom.Seed(t_seedResult.match.Seed);
            DeckLockResult t_lockResult = await DeckLockSubmission.TryLockAsync(
                ContentProfileConfig.Active.CloudEnvId,
                t_seedResult.match.MatchId,
                "server",
                t_seedResult.match.SeedHex,
                t_seedResult.match.RulesetVersion,
                null,
                null,
                SpecSource.BattleFingerprint.ToLowerInvariant(),
                t_cardIds,
                t_growth,
                t_token);
            if (t_lockResult != DeckLockResult.Approved)
            {
                EMatchEndReason t_reason = t_lockResult == DeckLockResult.Rejected
                    ? EMatchEndReason.Desync
                    : EMatchEndReason.InitError;
                t_network.SendMatchAbort(t_reason);
                return t_token.IsCancellationRequested ? EPreBattleSyncResult.Canceled : EPreBattleSyncResult.Failed;
            }

            PreBattleMatchHandoff.Set(new PreBattleMatchData
            {
                MatchId = t_seedResult.match.MatchId,
                SeedHex = t_seedResult.match.SeedHex,
                Seed = t_seedResult.match.Seed,
                RulesetVersion = t_seedResult.match.RulesetVersion,
                LocalOwnerIndex = t_ownerIndex,
                LocalCardIds = t_cardIds,
                LocalGrowth = t_growth,
                OpponentCardIds = t_receiver.OpponentCardIds,
                OpponentGrowth = t_receiver.OpponentGrowth,
            });
            Debug.Log($"[PreBattleSync] 준비 완료 matchId={t_seedResult.match.MatchId}, owner={t_ownerIndex}");
            return EPreBattleSyncResult.Success;
        }
        catch (OperationCanceledException)
        {
            return _ct.IsCancellationRequested ? EPreBattleSyncResult.Canceled : EPreBattleSyncResult.Failed;
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[PreBattleSync] 준비 실패: {t_exception}");
            return EPreBattleSyncResult.Failed;
        }
        finally
        {
            NetworkGameController.ClearPreBattleReceiver(t_receiver);
            t_session.OnPlayerLeftRoom -= OnPlayerLeft;
            t_session.OnConnectionFailed -= OnConnectionFailed;
        }
    }

    static async UniTask<(bool ok, int[] cardIds, CardGrowth[] growth)> TryGetCanonicalLocalDeckAsync(
        CancellationToken _ct)
    {
        int[] t_cardIds = DeckConfig.PlayerDeck?.ToArray() ?? Array.Empty<int>();
        if (t_cardIds.Length != DeckSaveManager.DECK_SIZE || MatchGrowthSource.Current == null)
            return (false, null, null);

        CardGrowth[] t_growth;
        try
        {
            t_growth = await MatchGrowthSource.Current.ResolveMyGrowth(t_cardIds, _ct);
        }
        catch (Exception t_exception)
        {
            Debug.LogError($"[PreBattleSync] 내 성장 스냅샷 조회 실패: {t_exception.Message}");
            return (false, null, null);
        }
        if (t_growth == null || t_growth.Length != t_cardIds.Length) return (false, null, null);
        Array.Sort(t_cardIds, t_growth);
        for (int i = 0; i < t_cardIds.Length; i++)
        {
            if ((i > 0 && t_cardIds[i - 1] == t_cardIds[i]) ||
                !MatchGrowthValidation.IsValid(t_cardIds[i], t_growth[i], out string _))
                return (false, null, null);
        }
        return (true, t_cardIds, t_growth);
    }

    static byte[] NewPairingNonce()
    {
        byte[] t_nonce = new byte[16];
        using (RandomNumberGenerator t_rng = RandomNumberGenerator.Create()) t_rng.GetBytes(t_nonce);
        return t_nonce;
    }

    static string BuildPairingKey(string _roomName, byte[] _mine, byte[] _opponent)
    {
        if (string.IsNullOrEmpty(_roomName) || _mine == null || _opponent == null) return null;
        byte[] t_first = _mine;
        byte[] t_second = _opponent;
        for (int i = 0; i < t_first.Length; i++)
        {
            if (t_first[i] == t_second[i]) continue;
            if (t_first[i] > t_second[i]) { t_first = _opponent; t_second = _mine; }
            break;
        }
        byte[] t_room = Encoding.UTF8.GetBytes(_roomName);
        byte[] t_input = new byte[t_room.Length + t_first.Length + t_second.Length];
        Buffer.BlockCopy(t_room, 0, t_input, 0, t_room.Length);
        Buffer.BlockCopy(t_first, 0, t_input, t_room.Length, t_first.Length);
        Buffer.BlockCopy(t_second, 0, t_input, t_room.Length + t_first.Length, t_second.Length);
        using (SHA256 t_sha = SHA256.Create()) return MatchResultSubmission.Hex(t_sha.ComputeHash(t_input));
    }
}
