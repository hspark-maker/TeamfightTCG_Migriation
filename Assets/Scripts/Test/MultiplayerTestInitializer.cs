using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>멀티플레이 테스트 전용 단독 초기화. 로비 UI·세이브·초기화(InitializationRunner) 없이
/// 덱을 정하고 정식 랭크 매칭 또는 방 직행으로 전투 씬까지 넘긴다.
/// 정식 버튼은 PhotonRankedMatchmaker와 서버 덱 검증을 그대로 사용하고, 방 직행은 연결 자체를 진단할 때만 쓴다.</summary>
public class MultiplayerTestInitializer : MonoBehaviour
{
    const int REQUIRED_PLAYERS = 2;

#if UNITY_EDITOR || DEVELOPMENT_BUILD

    static bool s_commandLineSwitchConsumed;

    // 계정 갈아타기는 플레이 세션당 1회다. 씬을 다시 로드해도 다시 갈아타지 않는다.
    static bool s_accountOverrideApplied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetTestRuntimeState()
    {
        s_commandLineSwitchConsumed = false;
        s_accountOverrideApplied = false;
    }
#endif

    [Header("방")]
    [Tooltip("두 클라이언트가 같은 값이어야 같은 방에 붙는다.")]
    [SerializeField] string roomName = "TestRoom";
    [Tooltip("켜면 방 이름 대신 랜덤 매칭(JoinRandomRoom)을 쓴다.")]
    [SerializeField] bool useRandomMatch;

    [Header("덱")]
    [Tooltip("저장된 덱 슬롯 0 이 유효하면 그쪽이 우선한다. 슬롯 0 이 비었을 때만 이 목록을 쓰고, " +
             "그것도 비면 카탈로그 앞에서 6장을 채운다. 서버 lockDeck 이 소유·성장을 세이브와 대조하므로 " +
             "임의 번호로 들어가면 card_not_owned 로 거절된다.")]
    [SerializeField] List<int> deckCardIds = new List<int>();

    [Header("테스트 계정")]
    [Tooltip("클라이언트마다 다른 값을 넣으면 서로 다른 Firebase 계정으로 접속한다(같은 값 = 같은 uid). " +
             "비워두면 이 기기의 기존 계정을 그대로 쓴다. " +
             "실행 인자 -testAccountId=값 과 ParrelSync 클론 인자가 이 필드보다 우선한다.")]
    [SerializeField] string testAccountId = string.Empty;

    [Header("정식 매치메이킹")]
    [SerializeField] OpponentProfilePool profilePool;

    string status = "대기";
    System.IDisposable bootReadyHandle;
    bool connecting;
    bool switchingAccount;
    int playerCount;
    List<int> resolvedDeck;
    string resolvedAccountId = string.Empty;

    public event System.Action<string> OnStatusChanged;
    public event System.Action OnStateChanged;
    public string Status => this.status;
    public IReadOnlyList<int> ResolvedDeck => this.resolvedDeck;
    public bool IsBusy => this.connecting || this.switchingAccount;

    void Start()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!s_commandLineSwitchConsumed && HasCommandLineArgument("-newTestAccount"))
        {
            s_commandLineSwitchConsumed = true;
            s_accountOverrideApplied = true;
            SwitchToNewTestAccountAsync().Forget();
            return;
        }

        this.resolvedAccountId = ResolveTestAccountId();
        if (!s_accountOverrideApplied && !string.IsNullOrEmpty(this.resolvedAccountId))
        {
            s_accountOverrideApplied = true;
            SignInAsTestAccountAsync(this.resolvedAccountId).Forget();
            return;
        }
#endif

        InitializeStandalone();
    }

    void InitializeStandalone()
    {
        if (!CardStandaloneInitializer.Ensure())
        {
            SetStatus("카드 카탈로그 초기화 실패. SynergyRegistry 확인.");
            return;
        }

        // 소유 캐시를 세이브에서 채운다(설치기·ServerSlotRehydrator와 같은 순서: 소유 → 성장).
        // 빼먹으면 s_owned가 빈 채로 남아, 카드 한 장만 지급해도 Save가 세이브의 소유 목록을 그 한 장으로 덮는다.
        OwnershipManager.Init();
        DeckSaveManager.LoadFromSave();

        // 멀티는 IMatchGrowthSource가 확정한 성장 스냅샷을 요구한다 — 설치기 없는 씬에서는 여기서 세운다.
        GrowthStandaloneInitializer.Ensure();

        // 정식 덱 편집 프리팹과 카드 아트는 Addressables 캐시를 통해서만 얻는다.
        if (!CardArtCache.IsComplete) StartCoroutine(CardArtCache.Preload(CardCatalog.AllSpecs));
        if (!UiPrefabCache.IsComplete && !UiPrefabCache.HasFailed) UiPrefabCache.Preload().Forget();

        if (!TryResolveDeck(out this.resolvedDeck, out string t_deckError))
        {
            SetStatus($"덱 구성 실패: {t_deckError}");
            return;
        }

        DeckConfig.Set(this.resolvedDeck);
        if (!DeckConfig.HasDeck)
        {
            SetStatus("덱이 유효하지 않다(카탈로그에 없는 번호이거나 장수 부족).");
            return;
        }

        // 자동 접속하지 않는다 — 두 클라이언트의 진입 시점을 손으로 맞춰야 서버 페어링을 관찰할 수 있다.
        SetStatus($"덱 준비 완료(슬롯 {DeckSaveManager.SelectedSlot}): {string.Join(", ", this.resolvedDeck)}");

        // 이 씬은 Initialize.prefab(정식 기동)을 함께 들고 있고, 그 채택은 여기보다 **나중에** 끝난다 —
        // 위에서 확정한 덱은 채택 전 세이브(=빈 슬롯) 기준이라 카탈로그 폴백일 수 있다.
        // 채택이 끝나면 진짜 저장 덱으로 한 번 더 확정한다. 안 하면 서버 lockDeck 이 대조하는 덱과 갈린다.
        this.bootReadyHandle?.Dispose();
        this.bootReadyHandle = GameInitialization.WhenReady(ReapplySavedDeckAfterBoot);
    }

    void OnDestroy()
    {
        this.bootReadyHandle?.Dispose();
        this.bootReadyHandle = null;

        if (NetworkSession.Instance == null) return;
        NetworkSession.Instance.OnPlayerJoinedRoom -= HandlePlayerJoined;
        NetworkSession.Instance.OnPlayerLeftRoom -= HandlePlayerLeft;
        NetworkSession.Instance.OnConnectionFailed -= HandleConnectionFailed;
    }

    public void Connect()
    {
        if (this.connecting) return;
        if (!DeckConfig.HasDeck) { SetStatus("덱이 없어 접속하지 않는다."); return; }
        ConnectAsync().Forget();
    }

    public void Disconnect()
    {
        if (NetworkSession.Instance != null) NetworkSession.Instance.Disconnect().Forget();
        this.connecting = false;
        this.playerCount = 0;
        SetStatus("연결 해제");
    }

    public bool TryApplySavedDeck(int _slot, out string _message)
    {
        if (_slot < 0 || _slot >= DeckSaveManager.SLOT_COUNT)
        {
            _message = $"슬롯은 0~{DeckSaveManager.SLOT_COUNT - 1} 사이여야 합니다.";
            return false;
        }
        if (!DeckSaveManager.IsSlotValid(_slot))
        {
            _message = $"슬롯 {_slot}에 완성된 덱이 없습니다.";
            return false;
        }

        if (!DeckSaveManager.TrySelectSlot(_slot))
        {
            _message = $"슬롯 {_slot} 저장 후 선택에 실패했습니다.";
            return false;
        }

        this.resolvedDeck = new List<int>(DeckSaveManager.Load(_slot));
        DeckConfig.Set(this.resolvedDeck);
        _message = $"슬롯 {_slot} 출전 덱 확정: {string.Join(", ", this.resolvedDeck)}";
        SetStatus(_message);
        return true;
    }

    // 기동 채택 이후의 재확정. 편집으로 이미 다른 덱을 확정해 뒀으면 그것을 덮지 않는다 —
    // 채택 완료가 유저 조작보다 늦게 올 수 있고, 그때 덮으면 방금 고른 덱이 조용히 되돌아간다.
    void ReapplySavedDeckAfterBoot()
    {
        this.bootReadyHandle = null;
        if (this.connecting || this.switchingAccount) return;

        int t_slot = DeckSaveManager.SelectedSlot;
        if (t_slot < 0 || !DeckSaveManager.IsSlotValid(t_slot)) return;

        List<int> t_saved = new List<int>(DeckSaveManager.Load(t_slot));
        if (this.resolvedDeck != null && SameDeck(this.resolvedDeck, t_saved)) return;

        this.resolvedDeck = t_saved;
        DeckConfig.Set(this.resolvedDeck);
        SetStatus($"초기화 완료 — 저장 덱으로 재확정(슬롯 {t_slot}): {string.Join(", ", this.resolvedDeck)}");
    }

    static bool SameDeck(List<int> _left, List<int> _right)
    {
        if (_left == null || _right == null || _left.Count != _right.Count) return false;
        for (int t_i = 0; t_i < _left.Count; t_i++)
            if (_left[t_i] != _right[t_i]) return false;
        return true;
    }

    public void StartRankedMatchmaking()
    {
        if (this.connecting || this.switchingAccount) return;
        if (!DeckConfig.HasDeck) { SetStatus("덱을 먼저 설정하세요."); return; }
        RankedMatchmakingAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    public bool CanOpenDeckEditor =>
        PlayerSaveCloud.IsGateComplete && CardArtCache.IsComplete && UiPrefabCache.IsComplete &&
        UIPoolManager.Instance != null && DataLibrary.instance != null;

    public bool DeckEditorLoadFailed => CardArtCache.HasFailed || UiPrefabCache.HasFailed;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void SwitchToNewTestAccount()
    {
        if (!this.switchingAccount) SwitchToNewTestAccountAsync().Forget();
    }
#endif

    // ── 덱 확정 ───────────────────────────────────────────────────────────

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static bool HasCommandLineArgument(string _argument)
    {
        string[] t_arguments = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < t_arguments.Length; i++)
            if (string.Equals(t_arguments[i], _argument, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    async UniTaskVoid SwitchToNewTestAccountAsync()
    {
        if (this.switchingAccount) return;
        if (ContentProfileConfig.Active.RunMode != EContentRunMode.Test)
        {
            SetStatus("새 테스트 계정은 Test 런모드에서만 발급할 수 있습니다.");
            return;
        }

        this.switchingAccount = true;
        this.connecting = false;
        SetStatus("새 테스트 계정 발급 중...");

        try
        {
            if (NetworkSession.Instance != null) await NetworkSession.Instance.Disconnect();

            MatchResultSubmission.DiscardPending();
            FirebaseManager.Shutdown();
            if (!await FirebaseAuthService.Instance.SignInAsNewAnonymousAsync())
            {
                SetStatus($"계정 발급 실패(재시작 필요): {FirebaseAuthService.Instance.LastError}");
                this.switchingAccount = false;
                return;
            }

            FirebaseManager.Initialize(ContentProfileConfig.Active.CloudEnvId, ContentProfileConfig.Active.FirebaseEmulators);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
        catch (System.Exception _exception)
        {
            this.switchingAccount = false;
            SetStatus($"계정 전환 실패: {_exception.GetBaseException().Message}");
            Debug.LogException(_exception);
        }
    }

    /// <summary>지정한 id의 계정으로 갈아탄 뒤 평소 초기화(덱 확정 → 자동 접속)를 이어서 한다.
    /// 익명 전환과 달리 씬을 다시 로드하지 않는다 — 로그인이 끝난 시점에 여기서 바로 이어 붙인다.</summary>
    async UniTaskVoid SignInAsTestAccountAsync(string _accountId)
    {
        if (this.switchingAccount) return;
        if (ContentProfileConfig.Active.RunMode != EContentRunMode.Test)
        {
            SetStatus("테스트 계정 로그인은 Test 런모드에서만 가능하다.");
            InitializeStandalone();
            return;
        }

        this.switchingAccount = true;
        this.connecting = false;
        SetStatus($"테스트 계정 '{_accountId}' 로그인 중...");

        try
        {
            if (NetworkSession.Instance != null) await NetworkSession.Instance.Disconnect();

            MatchResultSubmission.DiscardPending();
            FirebaseManager.Shutdown();

            if (!await FirebaseAuthService.Instance.SignInAsTestAccountAsync(_accountId))
            {
                SetStatus($"테스트 계정 로그인 실패: {FirebaseAuthService.Instance.LastError}");
                RestoreFirebase();
                return;
            }

            // 계정만 갈아탄다 — 클라우드 세이브는 켠 채로 둔다.
            // 끄면 원격 세이브 문서가 안 생겨 lockDeck 이 대조할 진실원을 잃는다(= 매치 진입 불가).
            FirebaseManager.Initialize(ContentProfileConfig.Active.CloudEnvId, ContentProfileConfig.Active.FirebaseEmulators);
            SetStatus($"테스트 계정 '{_accountId}' 로그인 완료.");
        }
        catch (System.Exception _exception)
        {
            SetStatus($"테스트 계정 로그인 실패: {_exception.GetBaseException().Message}");
            Debug.LogException(_exception);
            RestoreFirebase();
        }
        finally
        {
            this.switchingAccount = false;
            // 성공이든 실패든 덱은 세워야 한다 — 여기서 빠지면 화면에 상태 문구만 남고 아무것도 못 한다.
            InitializeStandalone();
        }
    }

    // Shutdown 뒤 로그인이 실패하면 Firebase가 죽은 채 남는다 — 되살려야 매치 결과 제출이 버려지지 않는다.
    static void RestoreFirebase()
    {
        if (FirebaseManager.IsInitialized) return;
        try { FirebaseManager.Initialize(ContentProfileConfig.Active.CloudEnvId, ContentProfileConfig.Active.FirebaseEmulators); }
        catch (System.Exception _exception)
        {
            Debug.LogWarning($"[MpTest] Firebase 복구 실패(재시작 필요): {_exception.GetBaseException().Message}");
        }
    }

    // 우선순위: 실행 인자 > ParrelSync 클론 인자 > 인스펙터 필드.
    // ParrelSync 클론은 Assets를 심링크로 공유해 씬의 인스펙터 값이 원본과 같아진다 —
    // 클론마다 다른 계정을 쓰려면 클론별 인자 파일이 있어야 한다.
    // 계정 id와 저장 스코프는 같은 출처여야 한다 — 갈라지면 계정만 다르고 세이브 폴더는 공유되는 상태가 된다.
    string ResolveTestAccountId()
    {
        if (DevAccountScope.IsActive) return DevAccountScope.Id;

        return string.IsNullOrWhiteSpace(this.testAccountId) ? string.Empty : this.testAccountId.Trim();
    }


#endif

    bool TryResolveDeck(out List<int> _deck, out string _error)
    {
        _error = null;

        // 저장된 대표 슬롯을 그대로 쓴다 — 편집에서 선택한 좌표와 다음 매칭의 출전 덱이 같아야 한다.
        int t_selectedSlot = DeckSaveManager.SelectedSlot;
        if (t_selectedSlot >= 0 && DeckSaveManager.IsSlotValid(t_selectedSlot))
        {
            _deck = new List<int>(DeckSaveManager.Load(t_selectedSlot));
            return true;
        }

        if (this.deckCardIds != null && this.deckCardIds.Count > 0)
        {
            if (DeckSaveManager.TryBuildDeck(this.deckCardIds, out _deck)) return true;
            _error = $"인스펙터 덱이 {DeckSaveManager.DECK_SIZE}장을 못 채웠다(중복·미등록 번호 제외 후 부족).";
            return false;
        }

        if (DeckSaveManager.TryBuildDeck(CardCatalog.AllIds, out _deck)) return true;

        _error = $"카탈로그 카드가 {DeckSaveManager.DECK_SIZE}장 미만이다(현재 {CardCatalog.Count}장).";
        return false;
    }

    // ── 접속 ─────────────────────────────────────────────────────────────

    async UniTaskVoid ConnectAsync()
    {
        this.connecting = true;
        SetStatus("연결 중...");

        NetworkSession t_session = EnsureSession();

        t_session.OnPlayerJoinedRoom -= HandlePlayerJoined;
        t_session.OnPlayerLeftRoom -= HandlePlayerLeft;
        t_session.OnConnectionFailed -= HandleConnectionFailed;
        t_session.OnPlayerJoinedRoom += HandlePlayerJoined;
        t_session.OnPlayerLeftRoom += HandlePlayerLeft;
        t_session.OnConnectionFailed += HandleConnectionFailed;

        bool t_ok = this.useRandomMatch
            ? await t_session.JoinRandomRoom()
            : await t_session.JoinOrCreateRoom(this.roomName);

        if (!t_ok)
        {
            this.connecting = false;
            SetStatus("연결 실패. Photon AppId·네트워크 확인.");
            return;
        }

        this.playerCount = CountActivePlayers();
        UpdateWaitingStatus();

        // 내가 늦게 들어와 이미 2인이면 OnPlayerJoined가 안 온다 — 여기서 한 번 더 판정한다.
        if (this.playerCount >= REQUIRED_PLAYERS) BeginBattle();
    }

    /// <summary>씬에 NetworkSession이 없으면 만들어 준다(단독 실행 편의). 이미 있으면 그것을 쓴다.</summary>
    static NetworkSession EnsureSession()
    {
        if (NetworkSession.Instance != null) return NetworkSession.Instance;
        new GameObject("NetworkSession").AddComponent<NetworkSession>();
        return NetworkSession.Instance;
    }

    void HandlePlayerJoined(PlayerRef _player)
    {
        this.playerCount = CountActivePlayers();
        UpdateWaitingStatus();
        if (this.playerCount >= REQUIRED_PLAYERS) BeginBattle();
    }

    void HandlePlayerLeft(PlayerRef _player)
    {
        this.playerCount = CountActivePlayers();
        UpdateWaitingStatus();
    }

    void HandleConnectionFailed(string _reason)
    {
        this.connecting = false;
        SetStatus($"연결 끊김: {_reason}");
    }

    void BeginBattle()
    {
        // 양쪽 클라가 각자 켠다. 씬 전환만 마스터가 건다(Fusion이 나머지를 따라오게 한다).
        DeckConfig.SetMultiplayer(true);
        SetStatus("전투 시작");

        NetworkRunner t_runner = NetworkSession.Instance?.Runner;
        if (t_runner == null)
        {
            // 멀티 플래그만 세운 채 러너가 없으면 아무도 씬을 로드하지 않아 "전투 시작"에서 멈춘다.
            // 정식 경로(LobbyMatchLauncher.LoadBattleSceneOverNetwork)와 같이 싱글로 되돌려 전투는 살린다.
            DeckConfig.ResetMode();
            SetStatus("멀티 진입인데 러너가 없다 — 싱글 경로로 전투를 연다.");
            SceneManager.LoadScene("BattleScene");
            return;
        }
        if (!t_runner.IsSharedModeMasterClient) return;

        string t_sceneName = NetworkSession.Instance.BattleSceneName;
        int t_buildIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{t_sceneName}.unity");
        if (t_buildIndex < 0) t_buildIndex = SceneUtility.GetBuildIndexByScenePath(t_sceneName);
        if (t_buildIndex < 0)
        {
            SetStatus($"'{t_sceneName}'이 Build Settings에 없다.");
            return;
        }

        t_runner.LoadScene(SceneRef.FromIndex(t_buildIndex));
    }

    int CountActivePlayers()
    {
        int t_count = 0;
        if (NetworkSession.Instance?.Runner == null) return t_count;
        foreach (PlayerRef _ in NetworkSession.Instance.Runner.ActivePlayers) t_count++;
        return t_count;
    }

    void UpdateWaitingStatus() => SetStatus($"대기 중... ({this.playerCount}/{REQUIRED_PLAYERS})");

    void SetStatus(string _message)
    {
        this.status = _message;
        Debug.Log($"[MpTest] {_message}");
        OnStatusChanged?.Invoke(_message);
        OnStateChanged?.Invoke();
    }

    async UniTaskVoid RankedMatchmakingAsync(CancellationToken _ct)
    {
        this.connecting = true;
        DeckConfig.SetMultiplayer(false);
        MatchOpponentHandoff.Clear();
        SoloMatchHandoff.Clear();
        DeckConfig.ClearEnemyDeck();
        EnsureSession();
        SetStatus($"정식 매치메이킹 중... (테스트 티어 {RankManager.TierIndex})");

        try
        {
            var t_matchmaker = new PhotonRankedMatchmaker(
                new ServerMatchmaker(this.profilePool), RankManager.TierIndex);
            MatchOpponent? t_matched = await t_matchmaker.FindOpponentAsync(_ct);
            if (_ct.IsCancellationRequested || t_matched == null)
            {
                SetStatus("매치메이킹이 취소되었거나 실패했습니다.");
                return;
            }

            MatchOpponentHandoff.Set(t_matched.Value);
            if (t_matched.Value.IsValid)
                DeckConfig.SetEnemyDeck(t_matched.Value.Deck, t_matched.Value.CardLevel);

            bool t_multiplayer = DeckConfig.IsMultiplayer;
            SetStatus(t_multiplayer ? "실제 상대 매칭 완료. 전투 데이터 확인 중..." : "AI 폴백 완료. 전투 데이터 확인 중...");

            EBattleContentGateResult t_gate = await BattleContentSync.CheckBeforeBattleAsync(t_multiplayer, _ct);
            if (_ct.IsCancellationRequested) return;
            if (t_gate != EBattleContentGateResult.Current && t_gate != EBattleContentGateResult.OfflineAllowed)
            {
                SetStatus($"전투 콘텐츠 확인 실패: {t_gate}");
                return;
            }

            if (t_multiplayer)
            {
                BeginBattle();
                return;
            }

            ESoloMatchSyncResult t_solo = await SoloMatchSync.RunAsync(_ct);
            if (t_solo != ESoloMatchSyncResult.Success)
            {
                SetStatus($"AI 덱 서버 검증 실패: {t_solo}");
                return;
            }

            SetStatus("AI 전투 시작");
            SceneManager.LoadScene("BattleScene");
        }
        catch (System.Exception _exception)
        {
            SetStatus($"매치메이킹 예외: {_exception.GetBaseException().Message}");
            Debug.LogException(_exception);
        }
        finally
        {
            this.connecting = false;
            OnStateChanged?.Invoke();
        }
    }
}
