using System;
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

    static readonly ReliableKey MSG_KEY = ReliableKey.FromInts(0x4255, 0, 0, 0);

    enum MsgType : byte
    {
        Attack      = 1,
        CardSpawn   = 2,
        AnimReady   = 3,
        InitialDeck = 4,
        TurnEnd     = 5,
        SeedCommit  = 6,   // commit-reveal: SHA256(nonce) 32바이트
        SeedReveal  = 7,   // commit-reveal: nonce 8바이트
    }

    UniTaskCompletionSource opponentReadyTcs;
    bool opponentReadyReceived;
    bool waitingForOpponentReady;

    void Awake()
    {
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
        if (_data.Count == 0) return;
        byte[] t_buf    = _data.Array;
        int    t_offset = _data.Offset;

        switch ((MsgType)t_buf[t_offset])
        {
            case MsgType.Attack:
            {
                int t_atk = ReadInt(t_buf, t_offset + 1);
                int t_def = ReadInt(t_buf, t_offset + 5);
                bool t_cunningSwap = _data.Count >= 13 && ReadInt(t_buf, t_offset + 9) != 0;
                MultiplayerTurnRunner.Instance?.OnAttackReceived(_sender, t_atk, t_def, t_cunningSwap);
                break;
            }
            case MsgType.CardSpawn:
            {
                int t_slot   = ReadInt(t_buf, t_offset + 1);
                int t_cardId = ReadInt(t_buf, t_offset + 5);
                int t_owner  = ReadInt(t_buf, t_offset + 9);
                MultiplayerTurnRunner.Instance?.OnCardSpawnReceived(t_slot, t_cardId, t_owner);
                break;
            }
            case MsgType.AnimReady:
                OnOpponentReadyReceived();
                break;

            case MsgType.InitialDeck:
            {
                int   t_ownerIdx = ReadInt(t_buf, t_offset + 1);
                int   t_count    = ReadInt(t_buf, t_offset + 5);
                int[] t_ids      = new int[t_count];
                for (int i = 0; i < t_count; i++)
                    t_ids[i] = ReadInt(t_buf, t_offset + 9 + i * 4);
                MultiplayerTurnRunner.Instance?.OnInitialDeckReceived(t_ids, t_ownerIdx);
                break;
            }
            case MsgType.TurnEnd:
                MultiplayerTurnRunner.Instance?.OnTurnEndReceived(_sender);
                break;

            case MsgType.SeedCommit:
            {
                byte[] t_hash = new byte[32];
                Array.Copy(t_buf, t_offset + 1, t_hash, 0, 32);
                MultiplayerTurnRunner.Instance?.OnSeedCommitReceived(t_hash);
                break;
            }
            case MsgType.SeedReveal:
            {
                byte[] t_nonce = new byte[8];
                Array.Copy(t_buf, t_offset + 1, t_nonce, 0, 8);
                MultiplayerTurnRunner.Instance?.OnSeedRevealReceived(t_nonce);
                break;
            }
        }
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

    public void SendInitialDeck(int[] _cardIds, int _ownerIndex)
    {
        int    t_count = _cardIds?.Length ?? 0;
        byte[] t_msg   = new byte[9 + t_count * 4];
        t_msg[0] = (byte)MsgType.InitialDeck;
        WriteInt(t_msg, 1, _ownerIndex);
        WriteInt(t_msg, 5, t_count);
        for (int i = 0; i < t_count; i++)
            WriteInt(t_msg, 9 + i * 4, _cardIds[i]);
        SendToOpponents(t_msg);
    }

    public void SendTurnEnd()
        => SendToOpponents(new byte[] { (byte)MsgType.TurnEnd });

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

    public async UniTask WaitForOpponentReady()
    {
        SendToOpponents(new byte[] { (byte)MsgType.AnimReady });

        if (this.opponentReadyReceived)
        {
            this.opponentReadyReceived = false;
            return;
        }

        this.waitingForOpponentReady = true;
        this.opponentReadyTcs = new UniTaskCompletionSource();
        await this.opponentReadyTcs.Task;
        this.waitingForOpponentReady = false;
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

    public void ForceOpponentReady()
    {
        if (this.waitingForOpponentReady && this.opponentReadyTcs != null)
        {
            this.waitingForOpponentReady = false;
            UniTaskCompletionSource t_tcs = this.opponentReadyTcs;
            this.opponentReadyTcs = null;
            t_tcs.TrySetResult();
        }
        else
        {
            this.opponentReadyReceived = true;
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
