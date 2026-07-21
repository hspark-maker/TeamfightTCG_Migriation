using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;

/// <summary>
/// 멀티플레이어 턴 진행자.
/// NetworkGameController RPC 수신 → 로컬 배틀 로직 실행.
/// </summary>
public class MultiplayerTurnRunner : MonoBehaviour
{
    public static MultiplayerTurnRunner Instance { get; private set; }

    [SerializeField] CardRegistry    cardRegistry;
    [SerializeField] BattleField     playerField;
    [SerializeField] BattleField     enemyField;
    [SerializeField] BattleFieldView playerFieldView;
    [SerializeField] BattleFieldView enemyFieldView;

    /// <summary>내 ownerIndex (0 or 1). Spawned() 이후 설정. -1 = 미설정.</summary>
    public int MyOwnerIndex { get; set; } = -1;

    UniTaskCompletionSource<(int attackerSlot, int defenderSlot, bool cunningSwap)> attackTcs;
    UniTaskCompletionSource initSyncTcs;
    bool enemyDeckReceived;
    bool waitingForAttackRpc;

    // 시드 commit-reveal 상태
    UniTaskCompletionSource seedCommitTcs;
    UniTaskCompletionSource seedRevealTcs;
    bool   seedCommitReceived;
    bool   seedRevealReceived;
    byte[] opponentCommit;
    byte[] opponentNonce;

    // 상대 카드 스폰 버퍼 — RPC 순서 보장으로 WaitForOpponentReady 이후 전부 수신됨
    readonly Queue<(int attackerSlot, int defenderSlot, bool cunningSwap)> attackBuffer = new Queue<(int, int, bool)>();
    readonly Queue<CardInstance> enemySpawnBuffer = new Queue<CardInstance>();

    void Awake()
    {
        InitializeRuntimeState();
    }

    void InitializeRuntimeState()
    {
        Instance = this;
        ResetDeckSyncState();
        ResetAttackSyncState();
        ResetSeedSyncState();
        this.enemySpawnBuffer.Clear();
        this.cardRegistry?.Initialize();
        TrySetOwnerIndexFromRunner();
    }

    public bool TrySetOwnerIndexFromRunner()
    {
        NetworkRunner t_runner = NetworkSession.Instance?.Runner;
        if (t_runner == null || !t_runner.IsRunning) return false;

        // Master(방 생성자) = 0(선공), Non-master = 1(후공)
        MyOwnerIndex = t_runner.IsSharedModeMasterClient ? 0 : 1;
        TurnState.LocalOwnerIndex = MyOwnerIndex;
        return true;
    }

    // ── RPC 수신 콜백 ──────────────────────────────────────────────────────

    public void OnAttackReceived(PlayerRef _sender, int _attackerSlot, int _defenderSlot, bool _cunningSwap)
    {
        var t_attack = (_attackerSlot, _defenderSlot, _cunningSwap);
        if (this.waitingForAttackRpc && this.attackTcs != null)
        {
            this.waitingForAttackRpc = false;
            UniTaskCompletionSource<(int attackerSlot, int defenderSlot, bool cunningSwap)> t_tcs = this.attackTcs;
            this.attackTcs = null;
            t_tcs.TrySetResult(t_attack);
            return;
        }

        this.attackBuffer.Enqueue(t_attack);
    }

    public void OnCardSpawnReceived(int _slot, int _cardId, int _ownerIndex)
    {
        // 상대방 카드만 처리 (내 카드는 이미 로컬에서 채움)
        if (_ownerIndex == this.MyOwnerIndex) return;

        CardData t_data = this.cardRegistry?.GetData(_cardId);
        CardInstance t_card = this.enemyField?.PlaceCardDirectly(_slot, t_data);
        if (t_card != null) this.enemySpawnBuffer.Enqueue(t_card);
    }

    public void OnTurnEndReceived(PlayerRef _sender) { }

    public void OnInitialDeckReceived(int[] _cardIds, int _ownerIndex)
    {
        this.enemyField?.InitializeFromRemote(_cardIds, _ownerIndex, this.cardRegistry);
        this.enemyDeckReceived = true;
        this.initSyncTcs?.TrySetResult();
    }

    public void OnSeedCommitReceived(byte[] _hash)
    {
        this.opponentCommit = _hash;
        if (this.seedCommitTcs != null)
        {
            UniTaskCompletionSource t_tcs = this.seedCommitTcs;
            this.seedCommitTcs = null;
            t_tcs.TrySetResult();
        }
        else this.seedCommitReceived = true;
    }

    public void OnSeedRevealReceived(byte[] _nonce)
    {
        this.opponentNonce = _nonce;
        if (this.seedRevealTcs != null)
        {
            UniTaskCompletionSource t_tcs = this.seedRevealTcs;
            this.seedRevealTcs = null;
            t_tcs.TrySetResult();
        }
        else this.seedRevealReceived = true;
    }

    // ── 초기화 동기화 ─────────────────────────────────────────────────────

    /// <summary>
    /// 배틀 시작 시 호출. 내 덱 broadcast + 상대 덱 수신 대기.
    /// GameInitializer 또는 TurnRunner.StartBattle 전에 await.
    /// </summary>
    public async UniTask SyncInitialDecks()
    {
        int[] t_myIds = this.playerField?.GetShuffledIds(this.cardRegistry);
        NetworkGameController.Instance?.SendInitialDeck(t_myIds ?? System.Array.Empty<int>(), this.MyOwnerIndex);

        if (!this.enemyDeckReceived)
            await this.initSyncTcs.Task;

        ResetDeckSyncState();

        // 덱 동기화 이후 결정론 RNG 시드 합의 (commit-reveal)
        await SyncMatchSeed();

        this.enemyFieldView?.Refresh();
        this.playerFieldView?.Refresh();
    }

    void ResetDeckSyncState()
    {
        this.enemyDeckReceived = false;
        this.initSyncTcs = new UniTaskCompletionSource();
    }

    /// <summary>
    /// commit-reveal로 양쪽이 조작 못 하는 공유 시드 합의.
    /// 1) H(nonce) 교환(commit) 2) nonce 교환(reveal) 3) 검증 4) seed = 내nonce XOR 상대nonce.
    /// </summary>
    async UniTask SyncMatchSeed()
    {
        byte[] t_myNonce  = MatchRandom.NewNonce();
        byte[] t_myCommit = MatchRandom.Hash(t_myNonce);

        NetworkGameController.Instance?.SendSeedCommit(t_myCommit);
        await WaitOpponentCommit();

        // 상대 commit 확보 후에야 내 nonce 공개
        NetworkGameController.Instance?.SendSeedReveal(t_myNonce);
        await WaitOpponentReveal();

        if (!MatchRandom.VerifyCommit(this.opponentNonce, this.opponentCommit))
            Debug.LogError("[MatchSeed] commit-reveal 검증 실패 — 시드 조작 의심");

        ulong t_seed = MatchRandom.ReadU64(t_myNonce) ^ MatchRandom.ReadU64(this.opponentNonce);
        MatchRandom.Seed(t_seed);
    }

    UniTask WaitOpponentCommit()
    {
        if (this.seedCommitReceived) { this.seedCommitReceived = false; return UniTask.CompletedTask; }
        this.seedCommitTcs = new UniTaskCompletionSource();
        return this.seedCommitTcs.Task;
    }

    UniTask WaitOpponentReveal()
    {
        if (this.seedRevealReceived) { this.seedRevealReceived = false; return UniTask.CompletedTask; }
        this.seedRevealTcs = new UniTaskCompletionSource();
        return this.seedRevealTcs.Task;
    }

    void ResetSeedSyncState()
    {
        this.seedCommitTcs      = null;
        this.seedRevealTcs      = null;
        this.seedCommitReceived = false;
        this.seedRevealReceived = false;
        this.opponentCommit     = null;
        this.opponentNonce      = null;
    }

    // ── 대기 API ───────────────────────────────────────────────────────────

    /// <summary>상대 공격 RPC 올 때까지 대기.</summary>
    public UniTask<(int attackerSlot, int defenderSlot, bool cunningSwap)> WaitForOpponentAttack()
    {
        if (this.attackBuffer.Count > 0)
            return UniTask.FromResult(this.attackBuffer.Dequeue());

        this.attackTcs = new UniTaskCompletionSource<(int, int, bool)>();
        this.waitingForAttackRpc = true;
        return this.attackTcs.Task;
    }

    void ResetAttackSyncState()
    {
        this.waitingForAttackRpc = false;
        this.attackTcs = null;
        this.attackBuffer.Clear();
    }

    public void ForceOpponentAttackResolve()
    {
        var t_dummy = (0, 0, false);
        if (this.waitingForAttackRpc && this.attackTcs != null)
        {
            this.waitingForAttackRpc = false;
            UniTaskCompletionSource<(int attackerSlot, int defenderSlot, bool cunningSwap)> t_tcs = this.attackTcs;
            this.attackTcs = null;
            t_tcs.TrySetResult(t_dummy);
        }
        else
        {
            this.attackBuffer.Enqueue(t_dummy);
        }
    }

    /// <summary>WaitForOpponentReady 이후 호출 → 수신된 상대 스폰 카드 반환 및 버퍼 비움.</summary>
    public List<CardInstance> FlushEnemySpawns()
    {
        var t_result = new List<CardInstance>();
        while (this.enemySpawnBuffer.Count > 0)
            t_result.Add(this.enemySpawnBuffer.Dequeue());
        return t_result;
    }

    // ── 브로드캐스트 헬퍼 ─────────────────────────────────────────────────

    public void BroadcastAttack(int _attackerSlot, int _defenderSlot, bool _cunningSwap = false)
        => NetworkGameController.Instance?.SendAttack(_attackerSlot, _defenderSlot, _cunningSwap);

    /// <summary>내 FillEmptySlots 결과를 상대에게 브로드캐스트.</summary>
    public void BroadcastMySpawns(List<CardInstance> _placed)
    {
        if (_placed == null) return;
        foreach (CardInstance t_card in _placed)
        {
            int t_id = this.cardRegistry?.GetId(t_card?.data) ?? -1;
            NetworkGameController.Instance?.SendCardSpawn(t_card.slotIndex, t_id, this.MyOwnerIndex);
        }
    }
}
