using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>멀티플레이 테스트 전용 단독 초기화. 로비 UI·세이브·초기화(InitializationRunner) 없이
/// 덱을 정하고 방에 붙어 전투 씬까지 넘긴다. 정식 경로는 MultiplayerLobbyPanel이며,
/// 이 스크립트는 그 흐름(덱 확정 → JoinRoom → 2인 → SetMultiplayer → Runner.LoadScene)을 그대로 따른다.</summary>
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

    string status = "대기";
    bool connecting;
    bool switchingAccount;
    int playerCount;
    List<int> resolvedDeck;
    string resolvedAccountId = string.Empty;

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

        // 멀티는 IMatchGrowthSource가 확정한 성장 스냅샷을 요구한다 — 설치기 없는 씬에서는 여기서 세운다.
        GrowthStandaloneInitializer.Ensure();

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
        SetStatus($"덱 준비 완료(슬롯 0): {string.Join(", ", this.resolvedDeck)} — [접속]을 눌러라.");
    }

    void OnDestroy()
    {
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

        // 슬롯 0 이 정본이다 — 서버 lockDeck 이 이 덱의 소유·성장을 세이브 문서와 대조한다.
        if (DeckSaveManager.IsSlotValid(0))
        {
            _deck = new List<int>(DeckSaveManager.Load(0));
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
        if (t_runner == null || !t_runner.IsSharedModeMasterClient) return;

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
    }

    // ── 디버그 화면 ───────────────────────────────────────────────────────
    // 테스트 전용이라 UI 프리팹을 만들지 않고 OnGUI로 끝낸다.

    // 기준 높이. 이보다 큰 화면에서는 같은 비율로 키운다 — 고해상도에서 IMGUI 기본 크기는 읽기 어렵다.
    const float GUI_REFERENCE_HEIGHT = 720f;

    void OnGUI()
    {
        const float WIDTH = 420f;

        Matrix4x4 t_previousMatrix = GUI.matrix;
        float t_scale = Mathf.Max(1f, Screen.height / GUI_REFERENCE_HEIGHT);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(t_scale, t_scale, 1f));

        GUILayout.BeginArea(new Rect(20f, 20f, WIDTH, 460f), GUI.skin.box);

        GUILayout.Label("멀티플레이 테스트");
        GUILayout.Label($"방: {(this.useRandomMatch ? "(랜덤 매칭)" : this.roomName)}");
        GUILayout.Label($"덱: {(this.resolvedDeck == null ? "-" : string.Join(", ", this.resolvedDeck))}");
        GUILayout.Label($"상태: {this.status}");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        FirebaseAuthService t_auth = FirebaseAuthService.Instance;
        string t_uid = string.IsNullOrEmpty(t_auth.UserId)
            ? "-"
            : t_auth.UserId.Substring(0, Mathf.Min(8, t_auth.UserId.Length));
        GUILayout.Label($"Firebase: {t_auth.State} / UID {t_uid}");
        GUILayout.Label($"테스트 계정 id: {(string.IsNullOrEmpty(this.resolvedAccountId) ? "-(기기 기본 계정)" : this.resolvedAccountId)}");
        GUILayout.Label($"클라우드 세이브: {PlayerSaveCloud.State} (rev {PlayerSaveCloud.Revision})");
#endif

        GUILayout.Space(8f);
        GUI.enabled = !this.connecting && !this.switchingAccount;
        if (GUILayout.Button("접속", GUILayout.Height(32f))) Connect();
        GUI.enabled = true;
        if (GUILayout.Button("연결 해제", GUILayout.Height(28f))) Disconnect();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        GUI.enabled = !this.switchingAccount;
        if (GUILayout.Button("새 테스트 계정 발급", GUILayout.Height(32f))) SwitchToNewTestAccountAsync().Forget();
        GUI.enabled = true;
#endif

        GUILayout.EndArea();
        GUI.matrix = t_previousMatrix;
    }
}
