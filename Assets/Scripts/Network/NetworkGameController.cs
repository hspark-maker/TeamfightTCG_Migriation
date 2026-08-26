using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

/// <summary>
/// 배틀씬 메시지 허브. NetworkObject 불필요 — ReliableData API로 통신.
/// MonoBehaviour이므로 Spawned() 없이 Awake()에서 Instance 설정됨.
/// </summary>
public class NetworkGameController : MonoBehaviour
{
    public static NetworkGameController Instance { get; private set; }

    // Firebase 연동층이 Fusion PlayerRef를 계정의 안정 UserId로 해석하는 자리.
    // 미주입(로컬/오프라인) 시 빈 값이며, 성장 스냅샷 자체는 그대로 교환한다.
    static System.Func<PlayerRef, string> stablePlayerIdProvider;

    static readonly ReliableKey MSG_KEY = ReliableKey.FromInts(0x4255, 0, 0, 0);

    enum MsgType : byte
    {
        Attack      = 1,
        CardSpawn   = 2,
        AnimReady   = 3,
        InitialDeck = 4,
        MatchAbort  = 5,
        SeedCommit  = 6,   // commit-reveal: SHA256(nonce) 32바이트
        SeedReveal  = 7,   // commit-reveal: nonce 8바이트
        MulliganChoice = 8,
    }

    UniTaskCompletionSource opponentReadyTcs;
    bool opponentReadyReceived;
    bool opponentReadyForced;
    bool waitingForOpponentReady;
    UniTaskCompletionSource<int> opponentMulliganTcs;
    bool opponentMulliganReceived;
    bool opponentMulliganForced;
    bool waitingForOpponentMulligan;
    int opponentMulliganChoice = -1;
    CancellationToken destroyCt;

    void Awake()
    {
        this.destroyCt = this.GetCancellationTokenOnDestroy();
        InitializeInstance();
        ResolveLocalOwnerIndex();
    }

    void InitializeInstance()
    {
        Instance = this;
    }

    void ResolveLocalOwnerIndex()
    {
        if (MultiplayerTurnRunner.Instance == null) return;
        if (MultiplayerTurnRunner.Instance.MyOwnerIndex >= 0) return;
        MultiplayerTurnRunner.Instance.TrySetOwnerIndexFromRunner();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── 수신 디스패치 (NetworkSession.OnReliableDataReceived → 여기 호출) ───

    public void HandleMessage(PlayerRef _sender, ArraySegment<byte> _data)
    {
        // AI 인수 뒤에는 돌아온 상대 상태를 현재 판에 합치지 않는다. 재접속은 인수 전 유예 창에서만 처리한다.
        if (DeckConfig.AiTakeover) return;

        try
        {
            byte[] t_buf = _data.Array;
            if (t_buf == null || _data.Count < 1 || _data.Offset < 0
                              || _data.Offset > t_buf.Length - _data.Count)
            {
                RejectMessage("잘못된 ArraySegment");
                return;
            }

            int t_offset = _data.Offset;
            MsgType t_type = (MsgType)t_buf[t_offset];
            switch (t_type)
            {
                case MsgType.Attack:
                {
                    if (!RequireLength(_data, 13, t_type)) return;
                    int t_atk = ReadInt(t_buf, t_offset + 1);
                    int t_def = ReadInt(t_buf, t_offset + 5);
                    if (!IsValidSlot(t_atk) || !IsValidSlot(t_def))
                    {
                        RejectMessage($"Attack 슬롯 범위 오류(atk={t_atk}, def={t_def})");
                        return;
                    }
                    bool t_cunningSwap = ReadInt(t_buf, t_offset + 9) != 0;
                    MultiplayerTurnRunner.Instance?.OnAttackReceived(_sender, t_atk, t_def, t_cunningSwap);
                    break;
                }
                case MsgType.CardSpawn:
                {
                    if (!RequireLength(_data, 13, t_type)) return;
                    int t_slot   = ReadInt(t_buf, t_offset + 1);
                    int t_cardId = ReadInt(t_buf, t_offset + 5);
                    int t_owner  = ReadInt(t_buf, t_offset + 9);
                    if (!IsValidSlot(t_slot) || !IsRemoteOwner(t_owner))
                    {
                        RejectMessage($"CardSpawn 범위 오류(slot={t_slot}, owner={t_owner})");
                        return;
                    }
                    MultiplayerTurnRunner.Instance?.OnCardSpawnReceived(t_slot, t_cardId, t_owner);
                    break;
                }
                case MsgType.AnimReady:
                    if (!RequireLength(_data, 1, t_type)) return;
                    OnOpponentReadyReceived();
                    break;

                case MsgType.InitialDeck:
                {
                    if (_data.Count < 9)
                    {
                        RejectMessage($"InitialDeck 길이 부족({_data.Count})");
                        return;
                    }
                    int t_ownerIdx = ReadInt(t_buf, t_offset + 1);
                    int t_count = ReadInt(t_buf, t_offset + 5);
                    if (!IsRemoteOwner(t_ownerIdx) || t_count < 0 || t_count > DeckSaveManager.DECK_SIZE)
                    {
                        RejectMessage($"InitialDeck 범위 오류(owner={t_ownerIdx}, count={t_count})");
                        return;
                    }
                    if (!RequireLength(_data, 9 + t_count * 24, t_type)) return;
                    if (t_count < DeckSaveManager.DECK_SIZE)
                        Debug.LogError($"[Net] InitialDeck이 기준 장수보다 적다: {t_count}/{DeckSaveManager.DECK_SIZE}");

                    int[] t_ids = new int[t_count];
                    CardGrowth[] t_growth = new CardGrowth[t_count];
                    for (int i = 0; i < t_count; i++)
                    {
                        int t_entry = t_offset + 9 + i * 24;
                        t_ids[i] = ReadInt(t_buf, t_entry);
                        int t_level = ReadInt(t_buf, t_entry + 4);
                        int t_hpBonus = ReadInt(t_buf, t_entry + 8);
                        int t_evolution = ReadInt(t_buf, t_entry + 12);
                        CardKeyword t_keywords = (CardKeyword)ReadInt(t_buf, t_entry + 16);
                        int t_synergyRaw = ReadInt(t_buf, t_entry + 20);
                        if (t_synergyRaw != 0 && t_synergyRaw != 1)
                        {
                            RejectMessage($"InitialDeck 시너지 해금 값 오류(index={i}, value={t_synergyRaw})");
                            return;
                        }
                        t_growth[i] = new CardGrowth(t_level, t_hpBonus, t_evolution,
                                                     t_keywords, t_synergyRaw == 1);
                    }
                    var t_opponent = new MatchGrowthOpponent(
                        t_ownerIdx, _sender.ToString(), ResolveStablePlayerId(_sender));
                    MultiplayerTurnRunner.Instance?.OnInitialDeckReceived(t_opponent, t_ids, t_growth);
                    break;
                }
                case MsgType.MatchAbort:
                {
                    if (!RequireLength(_data, 2, t_type)) return;
                    EMatchEndReason t_reason = (EMatchEndReason)t_buf[t_offset + 1];
                    if (!t_reason.IsVoid())
                    {
                        RejectMessage($"MatchAbort 사유 오류({t_buf[t_offset + 1]})");
                        return;
                    }
                    TurnRunner.Instance?.HandleMatchAbort(t_reason);
                    break;
                }
                case MsgType.SeedCommit:
                {
                    if (!RequireLength(_data, 33, t_type)) return;
                    byte[] t_hash = new byte[32];
                    Array.Copy(t_buf, t_offset + 1, t_hash, 0, 32);
                    MultiplayerTurnRunner.Instance?.OnSeedCommitReceived(t_hash);
                    break;
                }
                case MsgType.SeedReveal:
                {
                    if (!RequireLength(_data, 9, t_type)) return;
                    byte[] t_nonce = new byte[8];
                    Array.Copy(t_buf, t_offset + 1, t_nonce, 0, 8);
                    MultiplayerTurnRunner.Instance?.OnSeedRevealReceived(t_nonce);
                    break;
                }
                case MsgType.MulliganChoice:
                {
                    if (!RequireLength(_data, 5, t_type)) return;
                    int t_slot = ReadInt(t_buf, t_offset + 1);
                    if (t_slot < -1 || t_slot >= BattleField.SLOT_COUNT)
                    {
                        RejectMessage($"MulliganChoice 슬롯 범위 오류(slot={t_slot})");
                        return;
                    }
                    OnOpponentMulliganChoiceReceived(t_slot);
                    break;
                }
                default:
                    RejectMessage($"알 수 없는 메시지 타입({t_buf[t_offset]})");
                    break;
            }
        }
        catch (Exception t_e)
        {
            RejectMessage($"메시지 처리 예외: {t_e}");
        }
    }

    public static void SetStablePlayerIdProvider(System.Func<PlayerRef, string> _provider)
        => stablePlayerIdProvider = _provider;

    static string ResolveStablePlayerId(PlayerRef _player)
    {
        try
        {
            return stablePlayerIdProvider?.Invoke(_player) ?? string.Empty;
        }
        catch (System.Exception t_e)
        {
            Debug.LogError($"[MatchGrowth] 상대 안정 ID 해석 실패: {t_e}");
            return string.Empty;
        }
    }

    bool RequireLength(ArraySegment<byte> _data, int _expected, MsgType _type)
    {
        if (_data.Count == _expected) return true;
        RejectMessage($"{_type} 길이 오류({_data.Count}, expected={_expected})");
        return false;
    }

    void RejectMessage(string _reason)
    {
        Debug.LogError($"[Net] 수신 패킷 거부 — {_reason}");
        TurnRunner.Instance?.AbortMatch(EMatchEndReason.Desync);
    }

    static bool IsValidSlot(int _slot) => _slot >= 0 && _slot < BattleField.SLOT_COUNT;
    static bool IsValidOwner(int _owner) => _owner == 0 || _owner == 1;

    static bool IsRemoteOwner(int _owner)
    {
        MultiplayerTurnRunner t_runner = MultiplayerTurnRunner.Instance;
        return IsValidOwner(_owner)
            && t_runner != null
            && IsValidOwner(t_runner.MyOwnerIndex)
            && _owner != t_runner.MyOwnerIndex;
    }

    // ── 공개 API ────────────────────────────────────────────────────────────

    public void SendAttack(int _attackerSlot, int _defenderSlot, bool _cunningSwap = false)
    {
        byte[] t_msg = new byte[13];
        t_msg[0] = (byte)MsgType.Attack;
        WriteInt(t_msg, 1, _attackerSlot);
        WriteInt(t_msg, 5, _defenderSlot);
        WriteInt(t_msg, 9, _cunningSwap ? 1 : 0);
        SendToOpponents(t_msg);
    }

    public void SendCardSpawn(int _slot, int _cardId, int _ownerIndex)
    {
        byte[] t_msg = new byte[13];
        t_msg[0] = (byte)MsgType.CardSpawn;
        WriteInt(t_msg, 1, _slot);
        WriteInt(t_msg, 5, _cardId);
        WriteInt(t_msg, 9, _ownerIndex);
        SendToOpponents(t_msg);
    }

    public bool SendInitialDeck(int[] _cardIds, CardGrowth[] _growth, int _ownerIndex)
    {
        int    t_count = _cardIds?.Length ?? 0;
        if (_growth == null || _growth.Length != t_count)
        {
            Debug.LogError($"[Net] InitialDeck 송신 배열 길이 불일치(ids={t_count}, growth={_growth?.Length ?? -1})");
            return false;
        }

        byte[] t_msg   = new byte[9 + t_count * 24];
        t_msg[0] = (byte)MsgType.InitialDeck;
        WriteInt(t_msg, 1, _ownerIndex);
        WriteInt(t_msg, 5, t_count);
        for (int i = 0; i < t_count; i++)
        {
            int t_entry = 9 + i * 24;
            WriteInt(t_msg, t_entry, _cardIds[i]);
            WriteInt(t_msg, t_entry + 4, _growth[i].Level);
            WriteInt(t_msg, t_entry + 8, _growth[i].HpBonus);
            WriteInt(t_msg, t_entry + 12, _growth[i].EvolutionStage);
            WriteInt(t_msg, t_entry + 16, (int)_growth[i].UnlockedKeywords);
            WriteInt(t_msg, t_entry + 20, _growth[i].SynergyUnlocked ? 1 : 0);
        }
        SendToOpponents(t_msg);
        return true;
    }

    public void SendSeedCommit(byte[] _hash)
    {
        byte[] t_msg = new byte[1 + _hash.Length];
        t_msg[0] = (byte)MsgType.SeedCommit;
        Array.Copy(_hash, 0, t_msg, 1, _hash.Length);
        SendToOpponents(t_msg);
    }

    public void SendSeedReveal(byte[] _nonce)
    {
        byte[] t_msg = new byte[1 + _nonce.Length];
        t_msg[0] = (byte)MsgType.SeedReveal;
        Array.Copy(_nonce, 0, t_msg, 1, _nonce.Length);
        SendToOpponents(t_msg);
    }

    public void SendMatchAbort(EMatchEndReason _reason)
    {
        SendToOpponents(new[] { (byte)MsgType.MatchAbort, (byte)_reason });
    }

    public void SendMulliganChoice(int _slot)
    {
        byte[] t_msg = new byte[5];
        t_msg[0] = (byte)MsgType.MulliganChoice;
        WriteInt(t_msg, 1, _slot);
        SendToOpponents(t_msg);
    }

    public async UniTask<(bool received, int slot)> WaitForOpponentMulliganChoice()
    {
        if (this.opponentMulliganForced)
        {
            this.opponentMulliganForced = false;
            return (false, -1);
        }

        if (this.opponentMulliganReceived)
        {
            this.opponentMulliganReceived = false;
            return (true, this.opponentMulliganChoice);
        }

        this.waitingForOpponentMulligan = true;
        var t_tcs = new UniTaskCompletionSource<int>();
        this.opponentMulliganTcs = t_tcs;

        int t_completed = await UniTask.WhenAny(WaitForMulliganSignal(t_tcs.Task), WaitForMulliganDeadline());
        bool t_received = t_completed == 0
                       && !this.opponentMulliganForced
                       && !this.destroyCt.IsCancellationRequested;
        if (t_completed == 1 && !this.destroyCt.IsCancellationRequested)
            Debug.LogError($"[Net] 상대 멀리건 대기가 {NetTimeouts.MulliganWaitSec}초를 넘겼다.");

        if (ReferenceEquals(this.opponentMulliganTcs, t_tcs))
        {
            this.waitingForOpponentMulligan = false;
            this.opponentMulliganTcs = null;
        }
        this.opponentMulliganForced = false;
        if (!t_received) return (false, -1);

        return (true, await t_tcs.Task);
    }

    public async UniTask<bool> WaitForOpponentReady()
    {
        if (this.opponentReadyForced)
        {
            this.opponentReadyForced = false;
            return false;
        }

        SendToOpponents(new byte[] { (byte)MsgType.AnimReady });

        if (this.opponentReadyReceived)
        {
            this.opponentReadyReceived = false;
            return true;
        }

        this.waitingForOpponentReady = true;
        UniTaskCompletionSource t_tcs = new UniTaskCompletionSource();
        this.opponentReadyTcs = t_tcs;

        int t_completed = await UniTask.WhenAny(t_tcs.Task, WaitForReadyDeadline());
        bool t_succeeded = t_completed == 0
                        && !this.opponentReadyForced
                        && !this.destroyCt.IsCancellationRequested;
        if (t_completed == 1 && !this.destroyCt.IsCancellationRequested)
            Debug.LogError($"[Net] AnimReady 대기가 {NetTimeouts.AnimHandshakeSec}초를 넘겼다.");

        if (ReferenceEquals(this.opponentReadyTcs, t_tcs))
        {
            this.waitingForOpponentReady = false;
            this.opponentReadyTcs = null;
        }
        this.opponentReadyForced = false;
        return t_succeeded;
    }

    async UniTask WaitForReadyDeadline()
    {
        await UniTask.Delay(TimeSpan.FromSeconds(NetTimeouts.AnimHandshakeSec),
                            ignoreTimeScale: true,
                            cancellationToken: this.destroyCt)
                     .SuppressCancellationThrow();
    }

    async UniTask WaitForMulliganSignal(UniTask<int> _wait)
    {
        await _wait;
    }

    async UniTask WaitForMulliganDeadline()
    {
        await UniTask.Delay(TimeSpan.FromSeconds(NetTimeouts.MulliganWaitSec),
                            ignoreTimeScale: true,
                            cancellationToken: this.destroyCt)
                     .SuppressCancellationThrow();
    }

    // ── 내부 ────────────────────────────────────────────────────────────────

    void OnOpponentReadyReceived()
    {
        if (this.waitingForOpponentReady && this.opponentReadyTcs != null)
        {
            this.waitingForOpponentReady = false;
            UniTaskCompletionSource t_tcs = this.opponentReadyTcs;
            this.opponentReadyTcs = null;
            t_tcs.TrySetResult();
            return;
        }

        this.opponentReadyReceived = true;
    }

    void OnOpponentMulliganChoiceReceived(int _slot)
    {
        if (this.waitingForOpponentMulligan && this.opponentMulliganTcs != null)
        {
            this.waitingForOpponentMulligan = false;
            UniTaskCompletionSource<int> t_tcs = this.opponentMulliganTcs;
            this.opponentMulliganTcs = null;
            t_tcs.TrySetResult(_slot);
            return;
        }

        this.opponentMulliganChoice = _slot;
        this.opponentMulliganReceived = true;
    }

    public void ForceOpponentReady()
    {
        this.opponentReadyForced = true;
        this.opponentReadyReceived = false;
        if (this.waitingForOpponentReady && this.opponentReadyTcs != null)
        {
            this.waitingForOpponentReady = false;
            UniTaskCompletionSource t_tcs = this.opponentReadyTcs;
            this.opponentReadyTcs = null;
            t_tcs.TrySetResult();
        }
    }

    public void ForceOpponentMulliganChoice()
    {
        this.opponentMulliganForced = true;
        this.opponentMulliganReceived = false;
        if (this.waitingForOpponentMulligan && this.opponentMulliganTcs != null)
        {
            this.waitingForOpponentMulligan = false;
            UniTaskCompletionSource<int> t_tcs = this.opponentMulliganTcs;
            this.opponentMulliganTcs = null;
            t_tcs.TrySetResult(-1);
        }
    }

    void SendToOpponents(byte[] _data)
    {
        NetworkRunner t_runner = NetworkSession.Instance?.Runner;
        if (t_runner == null) return;
        foreach (PlayerRef t_p in t_runner.ActivePlayers)
        {
            if (t_p == t_runner.LocalPlayer) continue;
            t_runner.SendReliableDataToPlayer(t_p, MSG_KEY, _data);
        }
    }

    static void WriteInt(byte[] _buf, int _offset, int _value)
    {
        _buf[_offset]     = (byte)(_value >> 24);
        _buf[_offset + 1] = (byte)(_value >> 16);
        _buf[_offset + 2] = (byte)(_value >> 8);
        _buf[_offset + 3] = (byte)_value;
    }

    static int ReadInt(byte[] _buf, int _offset)
        => (_buf[_offset] << 24) | (_buf[_offset + 1] << 16) | (_buf[_offset + 2] << 8) | _buf[_offset + 3];
}
