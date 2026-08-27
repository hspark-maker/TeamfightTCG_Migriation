using System.Collections.Generic;
using System.Threading;
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

    [SerializeField] BattleField     playerField;
    [SerializeField] BattleField     enemyField;
    [SerializeField] BattleFieldView playerFieldView;
    [SerializeField] BattleFieldView enemyFieldView;

    /// <summary>내 ownerIndex (0 or 1). Spawned() 이후 설정. -1 = 미설정.</summary>
    public int MyOwnerIndex { get; set; } = -1;

    UniTaskCompletionSource<(int attackerSlot, int defenderSlot, bool cunningSwap)> attackTcs;
    UniTaskCompletionSource initSyncTcs;
    bool enemyDeckReceived;
    bool enemyDeckProcessing;
    bool attackWaitForced;
    bool waitingForAttackRpc;
    CancellationToken destroyCt;

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
    bool networkAbortRequested;

    /// <summary>초기화를 계속할 수 없는 상태(이탈이든 상한 초과든). 결과 처리는 둘을 구분한다 —
    /// <see cref="InitTimedOut"/> 참조.</summary>
    bool InitAborted => this.opponentLeftDuringInit || this.InitTimedOut || this.networkAbortRequested;

    // 상대 카드 스폰 버퍼 — RPC 순서 보장으로 WaitForOpponentReady 이후 전부 수신됨
    readonly Queue<(int attackerSlot, int defenderSlot, bool cunningSwap)> attackBuffer = new Queue<(int, int, bool)>();
    readonly Queue<CardInstance> enemySpawnBuffer = new Queue<CardInstance>();
    readonly Dictionary<int, CardGrowth> localGrowthByCardId = new Dictionary<int, CardGrowth>();
    IMatchGrowthSource matchGrowthSource;

    void Awake()
    {
        this.destroyCt = this.GetCancellationTokenOnDestroy();
        InitializeRuntimeState();
    }

    void OnDestroy()
    {
        // 파괴 시에도 초기화 구독이 남지 않도록 대칭 해제(예외/조기 파괴 안전장치).
        UnsubscribeInitAbort();
        this.matchGrowthSource = null;

        // 파괴된 인스턴스를 static에 남겨두면 재매치 때 늦게 도착한 RPC가 죽은 컴포넌트를 건드려
        // MissingReferenceException이 난다. `Instance == this`는 Unity fake-null 비교라
        // 이미 파괴된 참조를 걸러내지 못하므로 참조 동일성으로 판단한다.
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    void InitializeRuntimeState()
    {
        Instance = this;
        ResetDeckSyncState();
        ResetAttackSyncState();
        ResetSeedSyncState();
        this.networkAbortRequested = false;
        this.InitAbortReason = EMatchEndReason.OpponentLeftDuringInit;
        this.enemySpawnBuffer.Clear();
        this.localGrowthByCardId.Clear();
        this.matchGrowthSource = null;
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

    public void SetLocalGrowthProfiles(IReadOnlyList<int> _cards, IReadOnlyList<CardGrowth> _growth)
    {
        this.localGrowthByCardId.Clear();
        if (_cards == null || _growth == null || _cards.Count != _growth.Count) return;
        for (int i = 0; i < _cards.Count; i++)
        {
            int t_cardId = _cards[i];
            if (CardCatalog.Contains(t_cardId)) this.localGrowthByCardId[t_cardId] = _growth[i];
        }
    }

    /// <summary>한 경기의 로컬 조회와 상대 검증이 반드시 같은 공급자를 쓰도록 시작 시점에 고정한다.</summary>
    public void SetMatchGrowthSource(IMatchGrowthSource _source) => this.matchGrowthSource = _source;

    // ── RPC 수신 콜백 ──────────────────────────────────────────────────────

    public void OnAttackReceived(PlayerRef _sender, int _attackerSlot, int _defenderSlot, bool _cunningSwap)
    {
        if (DeckConfig.AiTakeover) return;

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
        if (DeckConfig.AiTakeover) return;

        // 상대방 카드만 처리 (내 카드는 이미 로컬에서 채움)
        if (_ownerIndex == this.MyOwnerIndex)
        {
            Debug.LogError($"[Net] CardSpawn owner가 로컬 소유자로 들어왔다 — owner={_ownerIndex}");
            TurnRunner.Instance?.AbortMatch(EMatchEndReason.Desync);
            return;
        }

        if (this.enemyField?.GetSlot(_slot) != null)
        {
            Debug.LogError($"[Net] CardSpawn 대상 슬롯이 이미 차 있다 — slot={_slot}, owner={_ownerIndex}");
            TurnRunner.Instance?.AbortMatch(EMatchEndReason.Desync);
            return;
        }

        if (!CardCatalog.Contains(_cardId))
        {
            Debug.LogError($"[Net] CardSpawn 미상 카드 ID — id={_cardId}, slot={_slot}, owner={_ownerIndex}");
            TurnRunner.Instance?.AbortMatch(EMatchEndReason.Desync);
            return;
        }

        CardInstance t_card = this.enemyField?.PlaceCardDirectly(_slot, _cardId);
        if (t_card == null)
        {
            Debug.LogError($"[Net] CardSpawn 미러 배치 실패 — id={_cardId}, slot={_slot}, owner={_ownerIndex}");
            TurnRunner.Instance?.AbortMatch(EMatchEndReason.Desync);
            return;
        }
        this.enemySpawnBuffer.Enqueue(t_card);
    }

    public void OnInitialDeckReceived(MatchGrowthOpponent _opponent, int[] _cardIds, CardGrowth[] _growth)
    {
        if (DeckConfig.AiTakeover) return;
        if (this.enemyDeckReceived || this.enemyDeckProcessing)
        {
            AbortInitialDeck(EMatchEndReason.Desync, "InitialDeck 중복 수신");
            return;
        }
        if (_cardIds == null || _growth == null || _cardIds.Length != _growth.Length)
        {
            AbortInitialDeck(EMatchEndReason.Desync,
                $"InitialDeck 배열 길이 불일치(ids={_cardIds?.Length ?? -1}, growth={_growth?.Length ?? -1})");
            return;
        }

        var t_seenIds = new HashSet<int>();
        for (int i = 0; i < _cardIds.Length; i++)
        {
            if (!t_seenIds.Add(_cardIds[i]))
            {
                AbortInitialDeck(EMatchEndReason.Desync,
                    $"InitialDeck 중복 카드 ID — index={i}, id={_cardIds[i]}");
                return;
            }
            if (!CardCatalog.Contains(_cardIds[i]))
            {
                AbortInitialDeck(EMatchEndReason.Desync,
                    $"InitialDeck 미상 카드 ID — index={i}, id={_cardIds[i]}");
                return;
            }
            if (!MatchGrowthValidation.IsValid(_cardIds[i], _growth[i], out string t_error))
            {
                AbortInitialDeck(EMatchEndReason.Desync,
                    $"InitialDeck 성장값 오류 — index={i}, id={_cardIds[i]}, {t_error}");
                return;
            }
        }

        this.enemyDeckProcessing = true;
        VerifyAndInitializeRemoteDeck(_opponent, _cardIds, _growth).Forget();
    }

    async UniTask VerifyAndInitializeRemoteDeck(MatchGrowthOpponent _opponent, int[] _cardIds, CardGrowth[] _growth)
    {
        try
        {
            IMatchGrowthSource t_source = this.matchGrowthSource;
            if (t_source == null)
            {
                AbortInitialDeck(EMatchEndReason.InitError, "매치 성장 공급자 미주입");
                return;
            }

            bool t_verified = await t_source.VerifyOpponentGrowth(
                _opponent, _cardIds, _growth, this.destroyCt);
            if (this.destroyCt.IsCancellationRequested || InitAborted || !this.enemyDeckProcessing) return;
            if (!t_verified)
            {
                AbortInitialDeck(EMatchEndReason.Desync, "상대 성장값이 공급자 정본과 일치하지 않는다");
                return;
            }

            this.enemyField?.InitializeFromRemote(_cardIds, _growth, _opponent.OwnerIndex);
            this.enemyDeckReceived = true;
            this.initSyncTcs?.TrySetResult();
        }
        catch (System.Exception t_e)
        {
            AbortInitialDeck(EMatchEndReason.InitError, $"상대 성장값 검증 예외: {t_e}");
        }
        finally
        {
            this.enemyDeckProcessing = false;
        }
    }

    void AbortInitialDeck(EMatchEndReason _reason, string _message)
    {
        Debug.LogError($"[MatchGrowth] {_message}");
        this.InitAbortReason = _reason;
        this.networkAbortRequested = true;
        TurnRunner.Instance?.AbortMatch(_reason);
        ReleaseInitWaits();
    }

    /// <summary>상대와 콘텐츠(스펙 스냅샷) 버전이 다르다. <b>손상 패킷은 여기로 오지 않는다</b> —
    /// 그쪽은 NetworkGameController가 RejectMessage로 따로 끊는다. 두 원인을 뭉치면
    /// 회선 문제가 "데이터가 다르다"로 보고돼 원인 추적이 막힌다.</summary>
    public void OnContentMismatchReceived(string _detail)
        => AbortInitialDeck(EMatchEndReason.Desync, $"상대와 전투 데이터 스냅샷이 달라 매치를 중단합니다 — {_detail}");

    public void OnSeedCommitReceived(byte[] _hash)
    {
        if (DeckConfig.AiTakeover) return;

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
        if (DeckConfig.AiTakeover) return;

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
            int[] t_myIds = this.playerField?.GetShuffledIds();
            if (t_myIds == null) t_myIds = System.Array.Empty<int>();
            if (!TryBuildGrowthForIds(t_myIds, out CardGrowth[] t_myGrowth))
            {
                AbortInitialDeck(EMatchEndReason.InitError, "셔플된 내 덱의 성장 스냅샷을 찾을 수 없다");
                return false;
            }
            if (NetworkGameController.Instance == null
                || !NetworkGameController.Instance.SendInitialDeck(t_myIds, t_myGrowth, this.MyOwnerIndex))
            {
                AbortInitialDeck(EMatchEndReason.InitError, "InitialDeck 송신 실패");
                return false;
            }

            if (!this.enemyDeckReceived)
                await AwaitInitStep(this.initSyncTcs.Task, "상대 덱 수신");
            if (InitAborted) return false;

            // 같은 경기에서 InitialDeck은 한 번만 허용한다. 완료 플래그를 유지해 지연/중복 패킷을 거부한다.
            this.initSyncTcs = null;

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
        this.enemyDeckProcessing = false;
        this.initSyncTcs = new UniTaskCompletionSource();
    }

    bool TryBuildGrowthForIds(int[] _ids, out CardGrowth[] _growth)
    {
        _growth = new CardGrowth[_ids?.Length ?? 0];
        if (_ids == null) return false;
        for (int i = 0; i < _ids.Length; i++)
        {
            if (!this.localGrowthByCardId.TryGetValue(_ids[i], out _growth[i])) return false;
        }
        return true;
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
        await AwaitInitStep(WaitOpponentCommit(), "시드 commit");
        if (InitAborted) return false;

        // 상대 commit 확보 후에야 내 nonce 공개
        NetworkGameController.Instance?.SendSeedReveal(t_myNonce);
        await AwaitInitStep(WaitOpponentReveal(), "시드 reveal");
        if (InitAborted) return false;

        // 검증 실패 = commit과 다른 nonce가 공개됐다는 뜻이고, 그건 상대가 내 nonce를 보고 나서
        // 원하는 시드가 나오도록 자기 nonce를 역산했다는 뜻이다. 여기서 그 nonce를 그대로 쓰면
        // commit-reveal이 존재만 하고 방어 효과는 0이 된다 — 시드를 확정하지 말고 매치를 중단한다.
        if (!MatchRandom.VerifyCommit(this.opponentNonce, this.opponentCommit))
        {
            AbortInitialDeck(EMatchEndReason.Desync,
                "[MatchSeed] commit-reveal 검증 실패 — 시드 조작 의심. 시드를 확정하지 않고 매치를 중단한다.");
            return false;
        }

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
        this.InitTimedOut  = false;
        this.initDeadline  = Time.realtimeSinceStartup + InitSyncTimeoutSec;
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

    /// <summary>초기화 대기 상한. 이탈·연결실패 콜백은 <see cref="HandleInitAbort"/>가 잡지만,
    /// 콜백이 아예 오지 않는 경우(스테일 러너로 멀티 오진입, 상대가 조용히 멈춤, 패킷 유실)는
    /// 아무도 TCS를 풀어주지 않아 전투가 영원히 시작되지 않는다. 그 마지막 구멍을 막는 벽시계 상한이다.</summary>
    public const float InitSyncTimeoutSec = NetTimeouts.InitSyncSec;

    /// <summary>초기화 전체(덱 교환 + 시드 commit-reveal)에 걸리는 **하나의** 데드라인.
    /// 단계마다 상한을 따로 걸면 최악 대기가 단계 수만큼 곱해져 사용자가 잠긴 화면에 몇 분씩 갇힌다.</summary>
    float initDeadline;

    /// <summary>초기화가 상한 초과로 끝났는가. **상대 이탈과 구분해야 한다** —
    /// 이탈은 부전승(보상 지급)이지만 타임아웃은 내 쪽 문제일 수 있어 보상을 주면 안 된다.
    /// 양쪽이 동시에 타임아웃 나면 둘 다 승리+보상이 되어 랭크·골드가 부풀어 오른다.</summary>
    public bool InitTimedOut { get; private set; }
    public EMatchEndReason InitAbortReason { get; private set; } = EMatchEndReason.OpponentLeftDuringInit;

    /// <summary>초기화 단계 대기 + 공용 데드라인. 초과하면 대기를 깨우고 타임아웃으로 표시한다.</summary>
    async UniTask AwaitInitStep(UniTask _wait, string _what)
    {
        float t_left = this.initDeadline - Time.realtimeSinceStartup;
        if (t_left > 0f)
        {
            int t_timedOut = await UniTask.WhenAny(
                _wait,
                UniTask.Delay(System.TimeSpan.FromSeconds(t_left), ignoreTimeScale: true));
            if (t_timedOut != 1) return;
        }

        Debug.LogError($"[MultiInit] {_what} 대기가 초기화 상한({InitSyncTimeoutSec}초)을 넘겼다. 초기화 중단.");
        this.InitTimedOut = true;
        this.InitAbortReason = EMatchEndReason.Timeout;
        ReleaseInitWaits();
    }

    /// <summary>초기화 중 상대 이탈: 대기를 깨우고 **이탈**로 표시.</summary>
    void HandleInitAbort()
    {
        this.opponentLeftDuringInit = true;
        this.InitAbortReason = EMatchEndReason.OpponentLeftDuringInit;
        ReleaseInitWaits();
    }

    /// <summary>3 TCS를 멱등 해제해 각 await를 즉시 깨운다.
    /// 해제한 TCS는 비운다 — 안 비우면 뒤늦게 도착한 RPC가 이미 완료된 TCS를 풀며
    /// "TCS냐 플래그냐" 분기(OnSeedCommitReceived)와 상태가 어긋난다.</summary>
    void ReleaseInitWaits()
    {
        this.initSyncTcs?.TrySetResult();

        UniTaskCompletionSource t_commit = this.seedCommitTcs;
        this.seedCommitTcs = null;
        t_commit?.TrySetResult();

        UniTaskCompletionSource t_reveal = this.seedRevealTcs;
        this.seedRevealTcs = null;
        t_reveal?.TrySetResult();
    }

    // ── 대기 API ───────────────────────────────────────────────────────────

    /// <summary>상대 공격 RPC 올 때까지 대기.</summary>
    public async UniTask<(bool received, int attackerSlot, int defenderSlot, bool cunningSwap)> WaitForOpponentAttack()
    {
        if (this.attackWaitForced)
        {
            this.attackWaitForced = false;
            return (false, 0, 0, false);
        }

        if (this.attackBuffer.Count > 0)
        {
            var t_buffered = this.attackBuffer.Dequeue();
            return (true, t_buffered.attackerSlot, t_buffered.defenderSlot, t_buffered.cunningSwap);
        }

        UniTaskCompletionSource<(int attackerSlot, int defenderSlot, bool cunningSwap)> t_tcs
            = new UniTaskCompletionSource<(int, int, bool)>();
        this.attackTcs = t_tcs;
        this.waitingForAttackRpc = true;

        int t_completed = await UniTask.WhenAny(WaitForAttackSignal(t_tcs.Task), WaitForAttackDeadline());
        bool t_received = t_completed == 0
                       && !this.attackWaitForced
                       && !this.destroyCt.IsCancellationRequested;
        if (t_completed == 1 && !this.destroyCt.IsCancellationRequested)
            Debug.LogError($"[Net] 상대 공격 대기가 {NetTimeouts.TurnActionSec}초를 넘겼다.");

        if (ReferenceEquals(this.attackTcs, t_tcs))
        {
            this.waitingForAttackRpc = false;
            this.attackTcs = null;
        }
        this.attackWaitForced = false;
        if (!t_received) return (false, 0, 0, false);

        var t_attack = await t_tcs.Task;
        return (true, t_attack.attackerSlot, t_attack.defenderSlot, t_attack.cunningSwap);
    }

    async UniTask WaitForAttackDeadline()
    {
        await UniTask.Delay(System.TimeSpan.FromSeconds(NetTimeouts.TurnActionSec),
                            ignoreTimeScale: true,
                            cancellationToken: this.destroyCt)
                     .SuppressCancellationThrow();
    }

    async UniTask WaitForAttackSignal(UniTask<(int attackerSlot, int defenderSlot, bool cunningSwap)> _wait)
    {
        await _wait;
    }

    void ResetAttackSyncState()
    {
        this.waitingForAttackRpc = false;
        this.attackTcs = null;
        this.attackWaitForced = false;
        this.attackBuffer.Clear();
    }

    public void ForceOpponentAttackResolve()
    {
        this.attackWaitForced = true;
        var t_dummy = (0, 0, false);
        if (this.waitingForAttackRpc && this.attackTcs != null)
        {
            this.waitingForAttackRpc = false;
            UniTaskCompletionSource<(int attackerSlot, int defenderSlot, bool cunningSwap)> t_tcs = this.attackTcs;
            this.attackTcs = null;
            t_tcs.TrySetResult(t_dummy);
        }
    }

    public void AbortNetworkWaits()
    {
        this.networkAbortRequested = true;
        ReleaseInitWaits();
        ForceOpponentAttackResolve();
    }

    /// <summary>상대 이탈 뒤 네트워크 턴을 끝내고 현재 미러를 로컬 AI에 넘긴다.
    /// 이미 배치된 스폰은 유지하고, 재생 대기 중인 RPC/연출 버퍼만 버린다.</summary>
    public void PrepareAiTakeover()
    {
        this.attackBuffer.Clear();
        this.enemySpawnBuffer.Clear();
        ForceOpponentAttackResolve();
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
            int t_id = t_card?.cardId ?? -1;
            NetworkGameController.Instance?.SendCardSpawn(t_card.slotIndex, t_id, this.MyOwnerIndex);
        }
    }
}
