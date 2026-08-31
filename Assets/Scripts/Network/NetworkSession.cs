using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

public class NetworkSession : MonoBehaviour, INetworkRunnerCallbacks
{
    const string ProtocolSuffix = "-p3";
    const string RankedLobbyName = "RankedMatchP3";
    const string TierProperty = "t";
    const string NicknameProperty = "n";
    const string AvatarProperty = "a";
    const string FrameProperty = "f";
    public static NetworkSession Instance { get; private set; }

    public string BattleSceneName = "BattleScene";

    public NetworkRunner Runner { get; private set; }
    public string PairingKey => this.Runner != null && this.Runner.SessionInfo.IsValid
        ? this.Runner.SessionInfo.Name
        : null;

    GameObject sceneManagerGo;
    readonly List<SessionInfo> rankedSessions = new List<SessionInfo>();

    public IReadOnlyList<SessionInfo> RankedSessions => this.rankedSessions;
    public bool HasRankedSessionList { get; private set; }

    /// <summary>마지막으로 세운 랭크 방 이름. 내려도 서버 목록에는 잠시 남기 때문에
    /// 매처가 자기 유령 방을 후보로 집지 않으려면 이 이름을 알아야 한다.</summary>
    public string CurrentSessionName { get; private set; }

    public event Action            OnConnected;
    public event Action<PlayerRef> OnPlayerJoinedRoom;
    public event Action<PlayerRef> OnPlayerLeftRoom;
    public event Action<string>    OnConnectionFailed;
    public event Action            OnRankedSessionListChanged;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (GetComponent<NetworkGameController>() == null)
            gameObject.AddComponent<NetworkGameController>();
    }

    // 앱 종료·에디터 Play 정지에는 await 창이 없다 — 러너 종료를 킥만 하고 나간다.
    // 통보 없이 프로세스가 사라지면 Photon 세션이 타임아웃까지 살아 있어,
    // 다시 켰을 때 유령 플레이어가 낀 방에 들어가거나 정원이 차서 못 들어간다.
    void OnApplicationQuit() => ShutdownRunnerImmediate();

    void OnDestroy()
    {
        if (Instance != this) return;

        ShutdownRunnerImmediate();
        Instance = null;
    }

    void ShutdownRunnerImmediate()
    {
        NetworkRunner t_runner = this.Runner;
        this.Runner = null;
        if (t_runner == null) return;

        ShutdownAsync(t_runner).Forget();
    }

    static async UniTaskVoid ShutdownAsync(NetworkRunner _runner)
    {
        try { await _runner.Shutdown(); }
        catch (Exception t_exception) { Debug.LogWarning($"[Net] 러너 종료 실패: {t_exception.Message}"); }
    }

#if UNITY_EDITOR
    // 러너가 살아 있는 채로 어셈블리 리로드에 들어가면 소켓 스레드가 남아 "Reloading Domain"이 멈춘다.
    [UnityEditor.InitializeOnLoadMethod]
    static void InstallEditorTeardown()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ShutdownForEditor;
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ShutdownForEditor;
    }

    // 여기서는 ShutdownRunnerImmediate만으로 부족하다 — 그쪽은 종료를 Forget으로 킥만 하는데,
    // 어셈블리 리로드 콜백 뒤에는 플레이어 루프가 더 돌지 않아 그 continuation이 영영 실행되지 않는다.
    // 러너가 소켓 스레드를 든 채로 남으면 Unity가 "Reloading Domain"에서 그 스레드를 기다리며 멈춘다
    // (매칭 중 Play를 끄면 재현된다 — 그때는 러너가 반드시 살아 있다).
    // 그래서 종료를 킥한 뒤 러너 오브젝트를 즉시 파괴해 네이티브 자원을 그 자리에서 놓게 한다.
    static void ShutdownForEditor()
    {
        NetworkRunner t_runner = Instance?.Runner;
        Instance?.ShutdownRunnerImmediate();
        if (t_runner == null) return;

        try
        {
            if (t_runner.gameObject != null) UnityEngine.Object.DestroyImmediate(t_runner.gameObject);
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[Net] 에디터 러너 파괴 실패: {t_exception.Message}");
        }
    }
#endif

    public async UniTask<bool> JoinOrCreateRoom(string _roomName)
    {
        await ShutdownRunner();
        DestroySceneManager();
        NetworkGameController.Instance?.ResetMatchState();

        NetworkSceneManagerDefault t_sceneManager = CreateSceneManager();
        CreateRunner();

        StartGameResult t_result = await this.Runner.StartGame(BuildStartGameArgs(_roomName, t_sceneManager));
        return t_result.Ok;
    }

    public async UniTask<bool> JoinRandomRoom()
    {
        await ShutdownRunner();
        DestroySceneManager();
        NetworkGameController.Instance?.ResetMatchState();

        NetworkSceneManagerDefault t_sceneManager = CreateSceneManager();
        CreateRunner();

        var t_args = new StartGameArgs
        {
            GameMode     = GameMode.Shared,
            SessionName  = null,
            PlayerCount  = 2,
            CustomLobbyName  = "RandomMatchP3",
            SceneManager = t_sceneManager,
        };

        StartGameResult t_result = await this.Runner.StartGame(t_args);
        return t_result.Ok;
    }

    public async UniTask<bool> JoinRankedLobby()
    {
        await ShutdownRunner();
        DestroySceneManager();
        NetworkGameController.Instance?.ResetMatchState();
        this.rankedSessions.Clear();
        this.HasRankedSessionList = false;

        CreateSceneManager();
        CreateRunner();
        var t_result = await this.Runner.JoinSessionLobby(SessionLobby.Custom, RankedLobbyName);
        return t_result.Ok;
    }

    public UniTask<bool> JoinRankedRoom(string _sessionName)
        => StartRankedRoom(_sessionName, default, false);

    public UniTask<bool> CreateRankedRoom(MatchmakingProfile _profile)
        => StartRankedRoom($"ranked-{Guid.NewGuid():N}{ProtocolSuffix}", _profile, true);

    async UniTask<bool> StartRankedRoom(string _sessionName, MatchmakingProfile _profile, bool _create)
    {
        if (this.Runner == null || string.IsNullOrEmpty(_sessionName)) return false;

        var t_args = new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = _sessionName,
            PlayerCount = 2,
            CustomLobbyName = RankedLobbyName,
            SceneManager = this.sceneManagerGo != null
                ? this.sceneManagerGo.GetComponent<NetworkSceneManagerDefault>()
                : null,
            IsOpen = true,
            IsVisible = true,
            EnableClientSessionCreation = _create,
        };
        if (_create)
        {
            t_args.SessionProperties = new Dictionary<string, SessionProperty>
            {
                [TierProperty] = _profile.TierIndex,
                [NicknameProperty] = _profile.Nickname,
                [AvatarProperty] = _profile.AvatarId,
                [FrameProperty] = _profile.FrameId,
            };
        }

        StartGameResult t_result = await this.Runner.StartGame(t_args);
        if (t_result.Ok && _create) this.CurrentSessionName = _sessionName;
        return t_result.Ok;
    }

    public static bool TryGetRankedProfile(SessionInfo _session, out MatchmakingProfile _profile)
    {
        _profile = default;
        if (_session.Properties == null
            || !_session.Properties.TryGetValue(TierProperty, out SessionProperty t_tier)
            || !_session.Properties.TryGetValue(NicknameProperty, out SessionProperty t_name)
            || !t_tier.IsInt || !t_name.IsString)
            return false;

        string t_avatar = _session.Properties.TryGetValue(AvatarProperty, out SessionProperty t_avatarProp)
                          && t_avatarProp.IsString ? (string)t_avatarProp : string.Empty;
        string t_frame = _session.Properties.TryGetValue(FrameProperty, out SessionProperty t_frameProp)
                         && t_frameProp.IsString ? (string)t_frameProp : string.Empty;
        _profile = new MatchmakingProfile((string)t_name, (int)t_tier, t_avatar, t_frame);
        return _profile.IsValid;
    }

    public async UniTask Disconnect()
    {
        var t_target = this.Runner;
        if (t_target != null)
            await t_target.Shutdown();
        // JoinOrCreateRoom이 새 Runner를 먼저 할당했으면 덮어쓰지 않음
        if (this.Runner == t_target)
            this.Runner = null;
        this.rankedSessions.Clear();
        this.HasRankedSessionList = false;
    }

    async UniTask ShutdownRunner()
    {
        if (this.Runner == null) return;
        await this.Runner.Shutdown();
        this.Runner = null;
    }

    void CreateRunner()
    {
        this.Runner = new GameObject("NetworkRunner").AddComponent<NetworkRunner>();
        this.Runner.AddCallbacks(this);
    }

    NetworkSceneManagerDefault CreateSceneManager()
    {
        this.sceneManagerGo = new GameObject("NetworkSceneManager");
        UnityEngine.Object.DontDestroyOnLoad(this.sceneManagerGo);
        return this.sceneManagerGo.AddComponent<NetworkSceneManagerDefault>();
    }

    void DestroySceneManager()
    {
        if (this.sceneManagerGo == null) return;
        Destroy(this.sceneManagerGo);
        this.sceneManagerGo = null;
    }

    static StartGameArgs BuildStartGameArgs(string _roomName, NetworkSceneManagerDefault _sceneManager)
    {
        return new StartGameArgs
        {
            GameMode     = GameMode.Shared,
            SessionName  = ProtocolRoomName(_roomName),
            PlayerCount  = 2,
            CustomLobbyName  = "CodeMatch",
            SceneManager = _sceneManager,
        };
    }

    static string ProtocolRoomName(string _roomName)
        => string.IsNullOrEmpty(_roomName) || _roomName.EndsWith(ProtocolSuffix, StringComparison.Ordinal)
            ? _roomName
            : _roomName + ProtocolSuffix;

    // ── INetworkRunnerCallbacks ───────────────────────────────────────────

    public void OnConnectedToServer(NetworkRunner _r)                          => OnConnected?.Invoke();
    public void OnPlayerJoined(NetworkRunner _r, PlayerRef _p)                 => OnPlayerJoinedRoom?.Invoke(_p);
    public void OnPlayerLeft(NetworkRunner _r, PlayerRef _p)                   => OnPlayerLeftRoom?.Invoke(_p);
    public void OnDisconnectedFromServer(NetworkRunner _r, NetDisconnectReason _reason) => OnConnectionFailed?.Invoke(_reason.ToString());
    public void OnConnectFailed(NetworkRunner _r, NetAddress _addr, NetConnectFailedReason _reason) => OnConnectionFailed?.Invoke(_reason.ToString());

    public void OnConnectRequest(NetworkRunner _r, NetworkRunnerCallbackArgs.ConnectRequest _req, byte[] _token) { }
    public void OnCustomAuthenticationResponse(NetworkRunner _r, Dictionary<string, object> _data) { }
    public void OnHostMigration(NetworkRunner _r, HostMigrationToken _token) { }
    public void OnInput(NetworkRunner _r, NetworkInput _input) { }
    public void OnInputMissing(NetworkRunner _r, PlayerRef _p, NetworkInput _input) { }
    public void OnObjectEnterAOI(NetworkRunner _r, NetworkObject _o, PlayerRef _p) { }
    public void OnObjectExitAOI(NetworkRunner _r, NetworkObject _o, PlayerRef _p) { }
    public void OnReliableDataProgress(NetworkRunner _r, PlayerRef _p, ReliableKey _k, float _progress) { }
    public void OnReliableDataReceived(NetworkRunner _r, PlayerRef _p, ReliableKey _k, ReadOnlySpan<byte> _data)
        => NetworkGameController.Instance?.HandleMessage(_p, _data.ToArray());
    public void OnSceneLoadDone(NetworkRunner _r) { }
    public void OnSceneLoadStart(NetworkRunner _r) { }
    public void OnSessionListUpdated(NetworkRunner _r, List<SessionInfo> _list)
    {
        if (_r != this.Runner) return;
        this.rankedSessions.Clear();
        if (_list != null) this.rankedSessions.AddRange(_list);
        this.HasRankedSessionList = true;
        this.OnRankedSessionListChanged?.Invoke();
    }
    public void OnShutdown(NetworkRunner _r, ShutdownReason _reason) { }
#pragma warning disable CS0618 
    // SimulationMessagePtr는 Fusion에서 obsolete지만 INetworkRunnerCallbacks 구현상 시그니처 유지 필수
    public void OnUserSimulationMessage(NetworkRunner _r, SimulationMessagePtr _msg) { }
#pragma warning restore CS0618
}
