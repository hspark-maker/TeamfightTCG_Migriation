using System;
using System.Collections.Generic;
using System.Security.Cryptography;
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
    const int CONTENT_FINGERPRINT_BYTES = 32;

    /// <summary>AnimReady 페이로드 = [0]타입 + [1..4]핸드셰이크 순번(int) + [5..12]상태해시(ulong).
    /// 새 MsgType을 만들지 않고 기존 배리어 메시지에 실어 보낸다 — 메시지 왕복 횟수를 늘리지 않고
    /// "이 배리어 시점의 두 보드가 같은가"를 정확히 짝지어 비교할 수 있는 유일한 지점이기 때문이다.</summary>
    const int ANIM_READY_BYTES = 13;

    public static NetworkGameController Instance { get; private set; }
    static IPreBattleNetworkReceiver preBattleReceiver;

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
        ServerSeedCapability = 9,
        SceneReady = 10,
        Surrender = 11,
    }

    UniTaskCompletionSource opponentReadyTcs;
    readonly HashSet<int> opponentReadySeqs = new HashSet<int>();
    bool opponentReadyForced;
    bool waitingForOpponentReady;
    UniTaskCompletionSource<int> opponentMulliganTcs;
    bool opponentMulliganReceived;
    bool opponentMulliganForced;
    bool waitingForOpponentMulligan;
    int opponentMulliganChoice = -1;
    CancellationToken destroyCt;

    // ── divergence 카나리아 상태 ────────────────────────────────────────────
    // 핸드셰이크 순번은 AnimReady 교환 1회당 1 증가한다. 양 클라가 공격 1회당 정확히 한 번씩
    // 이 배리어를 통과하므로 정상 진행에서는 항상 같은 값이다. 값이 어긋났다는 것 자체가 발견거리라
    // 비교를 생략하고 경고만 남긴다(어긋난 시점끼리 비교하면 무의미한 오탐이 난다).
    int   handshakeSeq;
    ulong stagedStateHash;
    string stagedStateDump;
    int   stagedStateHashSeq;
    ulong remoteStateHash;
    bool  hasRemoteStateHash;
    int   remoteStateHashSeq;
    ulong lastAgreedStateHash;
    ulong stateHashChain = 14695981039346656037UL;
    ulong stateHashChainPrev = 14695981039346656037UL;
    int stateHashChainLength;
    UniTaskCompletionSource sceneReadyTcs;
    bool sceneReadyReceived;
    bool awaitingSceneReady;
    bool hasBufferedInitialDeck;
    MatchGrowthOpponent bufferedOpponent;
    int[] bufferedCardIds;
    CardGrowth[] bufferedGrowth;
    byte[] bufferedPairingNonce;
    EMatchEndReason? bufferedAbortReason;

    public string LocalDeckHash { get; private set; }
    public string OpponentDeckHash { get; private set; }
    public ulong FinalStateHash => this.lastAgreedStateHash;
    public ulong StateHashChain => this.stateHashChain;
    public ulong StateHashChainPrev => this.stateHashChainPrev;
    public int StateHashChainLength => this.stateHashChainLength;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        this.destroyCt = this.GetCancellationTokenOnDestroy();
        InitializeInstance();
        ResolveLocalOwnerIndex();
    }

    void InitializeInstance()
    {
        Instance = this;
        this.LocalDeckHash = string.Empty;
        this.OpponentDeckHash = string.Empty;
        this.lastAgreedStateHash = 0UL;
        this.stateHashChain = 14695981039346656037UL;
        this.stateHashChainPrev = 14695981039346656037UL;
        this.stateHashChainLength = 0;
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
        if (DeckConfig.AiTakeover && preBattleReceiver == null) return;

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
                    if (!RequireLength(_data, ANIM_READY_BYTES, t_type)) return;
                    int t_readySeq = ReadInt(t_buf, t_offset + 1);
                    if (t_readySeq < this.handshakeSeq) break;
                    if ((long)t_readySeq > (long)this.handshakeSeq + 1)
                    {
                        RejectMessage($"AnimReady 순번 오류(current={this.handshakeSeq}, received={t_readySeq})");
                        return;
                    }
                    OnOpponentStateHashReceived(t_readySeq, ReadULong(t_buf, t_offset + 5));
                    OnOpponentReadyReceived(t_readySeq);
                    break;

                case MsgType.InitialDeck:
                {
                    // 길이 오류는 손상 패킷이고 지문 불일치는 콘텐츠 버전 차이다. 둘을 같은 사유로 뭉치면
                    // "상대와 데이터가 다르다"는 안내가 실제로는 회선 문제인 경우에도 나가 원인 추적이 막힌다.
                    if (_data.Count < 9 + CONTENT_FINGERPRINT_BYTES)
                    {
                        RejectMessage($"InitialDeck 길이 부족({_data.Count}) — 손상 패킷");
                        return;
                    }
                    int t_ownerIdx = ReadInt(t_buf, t_offset + 1);
                    int t_count = ReadInt(t_buf, t_offset + 5);
                    if (!IsRemoteOwner(t_ownerIdx) || t_count < 0 || t_count > DeckSaveManager.DECK_SIZE)
                    {
                        RejectMessage($"InitialDeck 범위 오류(owner={t_ownerIdx}, count={t_count})");
                        return;
                    }
                    if (_data.Count != 9 + CONTENT_FINGERPRINT_BYTES + t_count * 24)
                    {
                        // 지문 32바이트만 빠진 길이 = 지문을 안 싣던 구버전 클라 → 콘텐츠 버전 불일치.
                        // 그 외의 길이는 손상 패킷이다.
                        if (_data.Count == 9 + t_count * 24)
                            ReportContentMismatch(
                                $"상대가 전투 데이터 지문을 싣지 않았다(구버전 클라이언트, count={t_count})");
                        else
                            RejectMessage($"InitialDeck 길이 불일치 수신={_data.Count} " +
                                          $"기대={9 + CONTENT_FINGERPRINT_BYTES + t_count * 24} (count={t_count}) — 손상 패킷");
                        return;
                    }
                    byte[] t_remoteFingerprint = new byte[CONTENT_FINGERPRINT_BYTES];
                    Array.Copy(t_buf, t_offset + 9, t_remoteFingerprint, 0, CONTENT_FINGERPRINT_BYTES);
                    if (!ContentFingerprintMatches(t_remoteFingerprint))
                    {
                        ReportContentMismatch(
                            $"전투 데이터 지문 대조 실패 로컬={SpecSource.BattleFingerprint} " +
                            $"상대={FingerprintHex(t_remoteFingerprint)}");
                        return;
                    }
                    Debug.Log($"[Net] 전투 데이터 지문 일치 {SpecSource.BattleFingerprint} (owner={t_ownerIdx}, count={t_count})");
                    if (t_count < DeckSaveManager.DECK_SIZE)
                        Debug.LogError($"[Net] InitialDeck이 기준 장수보다 적다: {t_count}/{DeckSaveManager.DECK_SIZE}");

                    int[] t_ids = new int[t_count];
                    CardGrowth[] t_growth = new CardGrowth[t_count];
                    for (int i = 0; i < t_count; i++)
                    {
                        int t_entry = t_offset + 9 + CONTENT_FINGERPRINT_BYTES + i * 24;
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
                    this.OpponentDeckHash = ComputeDeckHash(t_ids, t_growth);
                    var t_opponent = new MatchGrowthOpponent(
                        t_ownerIdx, _sender.ToString(), ResolveStablePlayerId(_sender));
                    if (preBattleReceiver != null)
                        preBattleReceiver.OnInitialDeckReceived(t_opponent, t_ids, t_growth);
                    else if (MultiplayerTurnRunner.Instance != null)
                        MultiplayerTurnRunner.Instance?.OnInitialDeckReceived(t_opponent, t_ids, t_growth);
                    else
                    {
                        this.hasBufferedInitialDeck = true;
                        this.bufferedOpponent = t_opponent;
                        this.bufferedCardIds = t_ids;
                        this.bufferedGrowth = t_growth;
                    }
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
                    if (preBattleReceiver != null) preBattleReceiver.OnMatchAbort(t_reason);
                    else if (this.awaitingSceneReady)
                    {
                        this.bufferedAbortReason = t_reason;
                        this.sceneReadyTcs?.TrySetResult();
                    }
                    else if (TurnRunner.Instance != null) TurnRunner.Instance.HandleMatchAbort(t_reason);
                    else this.bufferedAbortReason = t_reason;
                    break;
                }
                case MsgType.Surrender:
                {
                    if (!RequireLength(_data, 2, t_type)) return;
                    int t_actorOwner = t_buf[t_offset + 1];
                    if (!IsRemoteOwner(t_actorOwner))
                    {
                        RejectMessage($"Surrender owner 오류({t_actorOwner})");
                        return;
                    }
                    TurnRunner.Instance?.HandleRemoteSurrender(t_actorOwner);
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

                case MsgType.ServerSeedCapability:
                    if (!RequireLength(_data, 17, t_type)) return;
                    byte[] t_pairingNonce = new byte[16];
                    Array.Copy(t_buf, t_offset + 1, t_pairingNonce, 0, t_pairingNonce.Length);
                    if (preBattleReceiver != null)
                        preBattleReceiver.OnServerSeedCapabilityReceived(t_pairingNonce);
                    else if (MultiplayerTurnRunner.Instance != null)
                        MultiplayerTurnRunner.Instance?.OnServerSeedCapabilityReceived(t_pairingNonce);
                    else
                        this.bufferedPairingNonce = t_pairingNonce;
                    break;
                case MsgType.SceneReady:
                    if (!RequireLength(_data, 1, t_type)) return;
                    OnSceneReadyReceived();
                    break;
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

    internal static void SetPreBattleReceiver(IPreBattleNetworkReceiver _receiver)
    {
        preBattleReceiver = _receiver;
        Instance?.ReplayBufferedPreBattleMessages();
    }

    internal static void ClearPreBattleReceiver(IPreBattleNetworkReceiver _expected)
    {
        if (ReferenceEquals(preBattleReceiver, _expected)) preBattleReceiver = null;
    }

    void ReplayBufferedPreBattleMessages()
    {
        IPreBattleNetworkReceiver t_receiver = preBattleReceiver;
        if (t_receiver == null) return;
        if (this.bufferedPairingNonce != null)
        {
            byte[] t_nonce = this.bufferedPairingNonce;
            this.bufferedPairingNonce = null;
            t_receiver.OnServerSeedCapabilityReceived(t_nonce);
        }
        if (this.hasBufferedInitialDeck)
        {
            this.hasBufferedInitialDeck = false;
            int[] t_ids = this.bufferedCardIds;
            CardGrowth[] t_growth = this.bufferedGrowth;
            this.bufferedCardIds = null;
            this.bufferedGrowth = null;
            t_receiver.OnInitialDeckReceived(this.bufferedOpponent, t_ids, t_growth);
        }
        if (this.bufferedAbortReason.HasValue)
        {
            EMatchEndReason t_reason = this.bufferedAbortReason.Value;
            this.bufferedAbortReason = null;
            t_receiver.OnMatchAbort(t_reason);
        }
    }

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
        if (preBattleReceiver != null) preBattleReceiver.OnProtocolError(_reason);
        else TurnRunner.Instance?.AbortMatch(EMatchEndReason.Desync);
    }

    static void ReportContentMismatch(string _detail)
    {
        if (preBattleReceiver != null) preBattleReceiver.OnContentMismatch(_detail);
        else MultiplayerTurnRunner.Instance?.OnContentMismatchReceived(_detail);
    }

    static bool IsValidSlot(int _slot) => _slot >= 0 && _slot < BattleField.SLOT_COUNT;
    static bool IsValidOwner(int _owner) => _owner == 0 || _owner == 1;

    static bool IsRemoteOwner(int _owner)
    {
        if (preBattleReceiver != null)
            return IsValidOwner(_owner) && IsValidOwner(preBattleReceiver.LocalOwnerIndex) &&
                   _owner != preBattleReceiver.LocalOwnerIndex;
        MultiplayerTurnRunner t_runner = MultiplayerTurnRunner.Instance;
        if (t_runner == null)
        {
            NetworkRunner t_networkRunner = NetworkSession.Instance?.Runner;
            if (t_networkRunner == null || !t_networkRunner.IsRunning) return false;
            int t_localOwner = t_networkRunner.IsSharedModeMasterClient ? 0 : 1;
            return IsValidOwner(_owner) && _owner != t_localOwner;
        }
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

        if (!TryContentFingerprintBytes(out byte[] t_fingerprint))
        {
            Debug.LogError("[Net] InitialDeck 송신 차단: 유효한 전투 데이터 지문이 없습니다.");
            return false;
        }
        this.LocalDeckHash = ComputeDeckHash(_cardIds, _growth);
        byte[] t_msg   = new byte[9 + CONTENT_FINGERPRINT_BYTES + t_count * 24];
        t_msg[0] = (byte)MsgType.InitialDeck;
        WriteInt(t_msg, 1, _ownerIndex);
        WriteInt(t_msg, 5, t_count);
        Array.Copy(t_fingerprint, 0, t_msg, 9, CONTENT_FINGERPRINT_BYTES);
        for (int i = 0; i < t_count; i++)
        {
            int t_entry = 9 + CONTENT_FINGERPRINT_BYTES + i * 24;
            WriteInt(t_msg, t_entry, _cardIds[i]);
            WriteInt(t_msg, t_entry + 4, _growth[i].Level);
            WriteInt(t_msg, t_entry + 8, _growth[i].HpBonus);
            WriteInt(t_msg, t_entry + 12, _growth[i].EvolutionStage);
            WriteInt(t_msg, t_entry + 16, (int)_growth[i].UnlockedKeywords);
            WriteInt(t_msg, t_entry + 20, _growth[i].SynergyUnlocked ? 1 : 0);
        }
        SendToOpponents(t_msg);
        Debug.Log($"[Net] InitialDeck 송신 지문={SpecSource.BattleFingerprint} (owner={_ownerIndex}, count={t_count})");
        return true;
    }

    static bool TryContentFingerprintBytes(out byte[] _bytes)
    {
        string t_hex = SpecSource.BattleFingerprint;
        if (t_hex == null || t_hex.Length != CONTENT_FINGERPRINT_BYTES * 2)
        {
            _bytes = null;
            return false;
        }
        _bytes = new byte[CONTENT_FINGERPRINT_BYTES];
        try
        {
            for (int i = 0; i < _bytes.Length; i++)
                _bytes[i] = Convert.ToByte(t_hex.Substring(i * 2, 2), 16);
            return true;
        }
        catch (FormatException)
        {
            _bytes = null;
            return false;
        }
    }

    static string FingerprintHex(byte[] _bytes)
    {
        if (_bytes == null) return "(없음)";
        var t_builder = new System.Text.StringBuilder(_bytes.Length * 2);
        foreach (byte t_byte in _bytes) t_builder.Append(t_byte.ToString("x2"));
        return t_builder.ToString();
    }

    /// <summary>덱 스냅샷 해시. 배열 순서를 그대로 직렬화하므로 <b>호출 전에 cardId 오름차순으로
    /// 정규화</b>돼 있어야 한다 — 서버 functions/src/deckValidation.ts의 computeDeckHash와
    /// 바이트 레이아웃(4 + n*24, 빅엔디안)·순서 규약이 같아야 lockDeck이 통과한다.</summary>
    internal static string ComputeDeckHash(int[] _cardIds, CardGrowth[] _growth)
    {
        int t_count = _cardIds?.Length ?? 0;
        byte[] t_bytes = new byte[4 + t_count * 24];
        WriteInt(t_bytes, 0, t_count);
        for (int i = 0; i < t_count; i++)
        {
            int t_entry = 4 + i * 24;
            WriteInt(t_bytes, t_entry, _cardIds[i]);
            WriteInt(t_bytes, t_entry + 4, _growth[i].Level);
            WriteInt(t_bytes, t_entry + 8, _growth[i].HpBonus);
            WriteInt(t_bytes, t_entry + 12, _growth[i].EvolutionStage);
            WriteInt(t_bytes, t_entry + 16, (int)_growth[i].UnlockedKeywords);
            WriteInt(t_bytes, t_entry + 20, _growth[i].SynergyUnlocked ? 1 : 0);
        }
        using (SHA256 t_sha = SHA256.Create())
            return FingerprintHex(t_sha.ComputeHash(t_bytes));
    }

    static bool ContentFingerprintMatches(byte[] _remote)
    {
        if (!TryContentFingerprintBytes(out byte[] t_local)) return false;
        if (_remote == null || _remote.Length != t_local.Length) return false;
        int t_diff = 0;
        for (int i = 0; i < t_local.Length; i++) t_diff |= t_local[i] ^ _remote[i];
        return t_diff == 0;
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

    public void SendServerSeedCapability(byte[] _pairingNonce)
    {
        byte[] t_msg = new byte[1 + _pairingNonce.Length];
        t_msg[0] = (byte)MsgType.ServerSeedCapability;
        Array.Copy(_pairingNonce, 0, t_msg, 1, _pairingNonce.Length);
        SendToOpponents(t_msg);
    }

    public void SendMatchAbort(EMatchEndReason _reason)
    {
        SendToOpponents(new[] { (byte)MsgType.MatchAbort, (byte)_reason });
    }

    public void SendSurrender(int _actorOwner)
    {
        SendToOpponents(new[] { (byte)MsgType.Surrender, checked((byte)_actorOwner) });
    }

    public void ResetMatchState()
    {
        this.LocalDeckHash = string.Empty;
        this.OpponentDeckHash = string.Empty;
        this.handshakeSeq = 0;
        this.stagedStateHash = 0UL;
        this.stagedStateDump = null;
        this.stagedStateHashSeq = 0;
        this.remoteStateHash = 0UL;
        this.hasRemoteStateHash = false;
        this.remoteStateHashSeq = 0;
        this.lastAgreedStateHash = 0UL;
        this.stateHashChain = 14695981039346656037UL;
        this.stateHashChainPrev = 14695981039346656037UL;
        this.stateHashChainLength = 0;
        this.sceneReadyTcs = null;
        this.sceneReadyReceived = false;
        this.awaitingSceneReady = false;
        this.hasBufferedInitialDeck = false;
        this.bufferedCardIds = null;
        this.bufferedGrowth = null;
        this.bufferedPairingNonce = null;
        this.bufferedAbortReason = null;
    }

    public async UniTask<(bool ready, EMatchEndReason failureReason)> SendSceneReadyAndWaitAsync(
        CancellationToken _ct)
    {
        this.awaitingSceneReady = true;
        SendToOpponents(new[] { (byte)MsgType.SceneReady });
        if (TryConsumeBufferedAbort(out EMatchEndReason t_earlyAbort))
        {
            this.awaitingSceneReady = false;
            return (false, t_earlyAbort);
        }
        if (this.sceneReadyReceived)
        {
            this.sceneReadyReceived = false;
            this.awaitingSceneReady = false;
            return (true, default);
        }
        this.sceneReadyTcs = new UniTaskCompletionSource();
        int t_completed = await UniTask.WhenAny(
            this.sceneReadyTcs.Task,
            UniTask.Delay(TimeSpan.FromSeconds(NetTimeouts.InitSyncSec),
                          ignoreTimeScale: true, cancellationToken: _ct));
        this.sceneReadyTcs = null;
        this.awaitingSceneReady = false;
        if (TryConsumeBufferedAbort(out EMatchEndReason t_abort)) return (false, t_abort);
        bool t_ready = t_completed == 0 && !_ct.IsCancellationRequested;
        return (t_ready, t_ready ? default : EMatchEndReason.Timeout);
    }

    bool TryConsumeBufferedAbort(out EMatchEndReason _reason)
    {
        if (!this.bufferedAbortReason.HasValue)
        {
            _reason = default;
            return false;
        }
        _reason = this.bufferedAbortReason.Value;
        this.bufferedAbortReason = null;
        return true;
    }

    void OnSceneReadyReceived()
    {
        if (this.sceneReadyTcs != null)
        {
            UniTaskCompletionSource t_tcs = this.sceneReadyTcs;
            this.sceneReadyTcs = null;
            t_tcs.TrySetResult();
        }
        else this.sceneReadyReceived = true;
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

        int t_seq = this.handshakeSeq;
        SendAnimReady(t_seq);
        TryCompareStateHash();

        if (this.opponentReadySeqs.Remove(t_seq))
        {
            EndHandshake(t_seq);
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
        EndHandshake(t_seq);
        return t_succeeded;
    }

    // ── divergence 카나리아 ────────────────────────────────────────────────

    /// <summary>다음 배리어에서 대조할 보드 지문을 맡긴다. <b>호출 시점이 계약이다</b> —
    /// 현재 배리어 통과 후 양쪽의 <c>FillEmptySlots</c>가 모두 반영된 시점에 불러야 한다.
    /// 그보다 이르면 상대 보충분의 수신 시점에 따라 정상 경기에서도 지문이 어긋날 수 있다.
    ///
    /// <para>계산이 실패해도 전투는 그대로 간다 — 카나리아가 게임을 죽이면 안 된다.
    /// 지문을 못 맡기면 센티널(0)이 나가고 상대는 비교를 생략한다.</para></summary>
    public void StageStateHash(BattleField _a, BattleField _b)
    {
        try
        {
            this.stagedStateHash    = BattleStateHash.Compute(_a, _b);
            this.stagedStateDump    = BattleStateHash.Dump(_a, _b);
            this.stagedStateHashSeq = this.handshakeSeq;
        }
        catch (Exception t_e)
        {
            this.stagedStateHash = 0UL;
            this.stagedStateDump = null;
            Debug.LogWarning($"[Hash] 상태 지문 계산 실패 — 이번 배리어는 대조를 생략한다: {t_e.Message}");
        }
        TryCompareStateHash();
    }

    void SendAnimReady(int _seq)
    {
        byte[] t_msg = new byte[ANIM_READY_BYTES];
        t_msg[0] = (byte)MsgType.AnimReady;
        WriteInt(t_msg, 1, _seq);
        WriteULong(t_msg, 5, this.stagedStateHashSeq == _seq ? this.stagedStateHash : 0UL);
        SendToOpponents(t_msg);
    }

    void OnOpponentStateHashReceived(int _seq, ulong _hash)
    {
        this.remoteStateHashSeq = _seq;
        this.remoteStateHash    = _hash;
        this.hasRemoteStateHash = true;
        TryCompareStateHash();
    }

    /// <summary>양쪽 지문이 같은 순번으로 모였을 때만 대조한다. 일치하면 **무로그** — 매 공격마다
    /// 로그를 남기면 정작 불일치 한 줄이 묻힌다.</summary>
    void TryCompareStateHash()
    {
        if (!this.hasRemoteStateHash) return;
        if (this.stagedStateHash == 0UL || this.stagedStateHashSeq != this.handshakeSeq) return;

        if (this.remoteStateHashSeq != this.stagedStateHashSeq)
        {
            Debug.LogWarning($"[Hash] 핸드셰이크 순번 불일치 local={this.stagedStateHashSeq} " +
                             $"remote={this.remoteStateHashSeq} — 이번 대조는 생략한다.");
            return;
        }
        if (this.remoteStateHash == 0UL) return;   // 상대가 지문을 못 맡겼다(센티널)

        this.hasRemoteStateHash = false;
        AppendStateProof(this.stagedStateHashSeq, this.stagedStateHash);
        if (this.remoteStateHash == this.stagedStateHash)
        {
            this.lastAgreedStateHash = this.stagedStateHash;
            return;
        }

        Debug.LogError($"[Hash] **상태 불일치** seq={this.stagedStateHashSeq} " +
                       $"local=0x{this.stagedStateHash:X16} remote=0x{this.remoteStateHash:X16}\n" +
                       $"  로컬 상태: {this.stagedStateDump}");
    }

    void AppendStateProof(int _seq, ulong _hash)
    {
        unchecked
        {
            this.stateHashChainPrev = this.stateHashChain;
            for (int t_shift = 0; t_shift < 32; t_shift += 8)
            {
                this.stateHashChain ^= (byte)(_seq >> t_shift);
                this.stateHashChain *= 1099511628211UL;
            }
            for (int t_shift = 0; t_shift < 64; t_shift += 8)
            {
                this.stateHashChain ^= (byte)(_hash >> t_shift);
                this.stateHashChain *= 1099511628211UL;
            }
            this.stateHashChainLength++;
        }
    }

    /// <summary>이번 배리어를 닫는다. 다음 배리어의 지문이 이미 도착해 있을 수 있으므로
    /// **이번 순번 이하**의 상대 지문만 버린다 — 앞선 것까지 버리면 다음 대조를 통째로 놓친다.</summary>
    void EndHandshake(int _seq)
    {
        this.stagedStateHash = 0UL;
        this.stagedStateDump = null;
        if (this.hasRemoteStateHash && this.remoteStateHashSeq <= _seq) this.hasRemoteStateHash = false;
        this.opponentReadySeqs.RemoveWhere(t_readySeq => t_readySeq <= _seq);
        this.handshakeSeq = _seq + 1;
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

    void OnOpponentReadyReceived(int _seq)
    {
        if (_seq < this.handshakeSeq) return;

        if (_seq == this.handshakeSeq && this.waitingForOpponentReady && this.opponentReadyTcs != null)
        {
            this.waitingForOpponentReady = false;
            UniTaskCompletionSource t_tcs = this.opponentReadyTcs;
            this.opponentReadyTcs = null;
            t_tcs.TrySetResult();
            return;
        }

        this.opponentReadySeqs.Add(_seq);
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
        this.opponentReadySeqs.Clear();
        // 강제 해제(이탈·AI 인수·무효 종료) 경로에서는 대조하지 않는다 — 짝이 맞지 않는 지문끼리
        // 비교해 "불일치"를 찍으면 진짜 divergence 로그와 구분이 안 된다.
        this.stagedStateHash = 0UL;
        this.stagedStateDump = null;
        this.hasRemoteStateHash = false;
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

    // big-endian. WriteInt/ReadInt와 같은 바이트 순서를 쓴다 — 한 메시지 안에서 순서가 갈리면
    // 플랫폼이 다른 두 클라가 같은 바이트를 다르게 읽는다.
    static void WriteULong(byte[] _buf, int _offset, ulong _value)
    {
        for (int i = 0; i < 8; i++) _buf[_offset + i] = (byte)(_value >> (56 - i * 8));
    }

    static ulong ReadULong(byte[] _buf, int _offset)
    {
        ulong t_value = 0;
        for (int i = 0; i < 8; i++) t_value = (t_value << 8) | _buf[_offset + i];
        return t_value;
    }
}
