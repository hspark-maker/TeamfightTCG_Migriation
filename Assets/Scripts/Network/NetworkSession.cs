using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

public class NetworkSession : MonoBehaviour, INetworkRunnerCallbacks
{
    const string ProtocolSuffix = "-p3";
    public static NetworkSession Instance { get; private set; }

    public string BattleSceneName = "BattleScene";

    public NetworkRunner Runner { get; private set; }
    public string PairingKey => this.Runner != null && this.Runner.SessionInfo.IsValid
        ? this.Runner.SessionInfo.Name
        : null;

    GameObject sceneManagerGo;

    public event Action            OnConnected;
    public event Action<PlayerRef> OnPlayerJoinedRoom;
    public event Action<PlayerRef> OnPlayerLeftRoom;
    public event Action<string>    OnConnectionFailed;

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

    static void ShutdownForEditor() => Instance?.ShutdownRunnerImmediate();
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

    public async UniTask Disconnect()
    {
        var t_target = this.Runner;
        if (t_target != null)
            await t_target.Shutdown();
        // JoinOrCreateRoom이 새 Runner를 먼저 할당했으면 덮어쓰지 않음
        if (this.Runner == t_target)
            this.Runner = null;
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
    public void OnSessionListUpdated(NetworkRunner _r, List<SessionInfo> _list) { }
    public void OnShutdown(NetworkRunner _r, ShutdownReason _reason) { }
#pragma warning disable CS0618 
    // SimulationMessagePtr는 Fusion에서 obsolete지만 INetworkRunnerCallbacks 구현상 시그니처 유지 필수
    public void OnUserSimulationMessage(NetworkRunner _r, SimulationMessagePtr _msg) { }
#pragma warning restore CS0618
}
