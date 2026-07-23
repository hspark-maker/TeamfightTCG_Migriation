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

    // 초기화 단계(SyncInitialDecks) 상대 이탈/연결실패 감지 플래그.
    // StartBattle 이전 구간이라 TurnRunner.HandlePlayerLeft가 아직 미구독 → 여기서 3 TCS를 강제 해제.
    bool opponentLeftDuringInit;

    // 상대 카드 스폰 버퍼 — RPC 순서 보장으로 WaitForOpponentReady 이후 전부 수신됨
    readonly Queue<(int attackerSlot, int defenderSlot, bool cunningSwap)> attackBuffer = new Queue<(int, int, bool)>();
    readonly Queue<CardInstance> enemySpawnBuffer = new Queue<CardInstance>();

    void Awake()
    {
        InitializeRuntimeState();
    }

    void OnDestroy()
    {
        // 파괴 시에도 초기화 구독이 남지 않도록 대칭 해제(예외/조기 파괴 안전장치).
        UnsubscribeInitAbort();
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
    /// 반환 false = 초기화 중 상대 이탈/연결실패(정상 완료 아님) → 호출부가 이탈 처리.
    /// </summary>
    public async UniTask<bool> SyncInitialDecks()
    {
        SubscribeInitAbort();
        try
        {
            int[] t_myIds = this.playerField?.GetShuffledIds(this.cardRegistry);
            NetworkGameController.Instance?.SendInitialDeck(t_myIds ?? System.Array.Empty<int>(), this.MyOwnerIndex);

            if (!this.enemyDeckReceived)
                await this.initSyncTcs.Task;
            if (this.opponentLeftDuringInit) return false;

            ResetDeckSyncState();

            // 덱 동기화 이후 결정론 RNG 시드 합의 (commit-reveal)
            if (!await SyncMatchSeed()) return false;

            // 시너지: 양 필드 덱 확정 후 각각 1회 적용.
            // 대칭성: 내 playerField(내 덱) / enemyField(상대 덱 원격 재구성) 각각을 그 덱으로 Resolve →
            // 상대 클라도 동일 두 덱으로 동일 산출 → 결과 일치, 전투 중 재계산 없음.
            // 오프닝 배치는 Placed만 발화하고 시너지 Entered는 미발화 — 등장 트리거(돌보미/흐름)는 런타임 등장(FillEmptySlots/Swap/PlaceDirect)에서만.
            this.playerField?.ApplyDeckSynergy();
            this.enemyField?.ApplyDeckSynergy();

            this.enemyFieldView?.Refresh();
            this.playerFieldView?.Refresh();
            return true;
        }
        finally
        {
            UnsubscribeInitAbort();
        }
    }

    void ResetDeckSyncState()
    {
        this.enemyDeckReceived = false;
        this.initSyncTcs = new UniTaskCompletionSource();
    }

    /// <summary>
    /// commit-reveal로 양쪽이 조작 못 하는 공유 시드 합의.
    /// 1) H(nonce) 교환(commit) 2) nonce 교환(reveal) 3) 검증 4) seed = 내nonce XOR 상대nonce.
    /// 반환 false = 대기 중 상대 이탈(시드 미확정, opponentNonce 접근 금지).
    /// </summary>
    async UniTask<bool> SyncMatchSeed()
    {
        byte[] t_myNonce  = MatchRandom.NewNonce();
        byte[] t_myCommit = MatchRandom.Hash(t_myNonce);

        NetworkGameController.Instance?.SendSeedCommit(t_myCommit);
        await WaitOpponentCommit();
        if (this.opponentLeftDuringInit) return false;

        // 상대 commit 확보 후에야 내 nonce 공개
        NetworkGameController.Instance?.SendSeedReveal(t_myNonce);
        await WaitOpponentReveal();
        if (this.opponentLeftDuringInit) return false;

        if (!MatchRandom.VerifyCommit(this.opponentNonce, this.opponentCommit))
            Debug.LogError("[MatchSeed] commit-reveal 검증 실패 — 시드 조작 의심");

        ulong t_seed = MatchRandom.ReadU64(t_myNonce) ^ MatchRandom.ReadU64(this.opponentNonce);
        MatchRandom.Seed(t_seed);
        return true;
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

    // ── 초기화 단계 이탈 감지 ─────────────────────────────────────────────
    // StartBattle 이전(덱교환·시드 commit-reveal) 구간에서 상대 이탈 시 3 TCS가
    // 아무도 해제 안 해 StartBattleAsync가 영구 정지되는 것을 방지.

    void SubscribeInitAbort()
    {
        this.opponentLeftDuringInit = false;
        if (NetworkSession.Instance == null) return;
        NetworkSession.Instance.OnPlayerLeftRoom   += OnInitPlayerLeft;
        NetworkSession.Instance.OnConnectionFailed += OnInitConnectionFailed;
    }

    void UnsubscribeInitAbort()
    {
        if (NetworkSession.Instance == null) return;
        NetworkSession.Instance.OnPlayerLeftRoom   -= OnInitPlayerLeft;
        NetworkSession.Instance.OnConnectionFailed -= OnInitConnectionFailed;
    }

    void OnInitPlayerLeft(PlayerRef _p)        => HandleInitAbort();
    void OnInitConnectionFailed(string _reason) => HandleInitAbort();

    /// <summary>초기화 중 상대 이탈: 3 TCS를 멱등 해제해 각 await를 즉시 깨움.</summary>
    void HandleInitAbort()
    {
        this.opponentLeftDuringInit = true;
        this.initSyncTcs?.TrySetResult();
        this.seedCommitTcs?.TrySetResult();
        this.seedRevealTcs?.TrySetResult();
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
