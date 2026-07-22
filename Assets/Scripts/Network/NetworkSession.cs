using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

public class NetworkSession : MonoBehaviour, INetworkRunnerCallbacks
{
    public static NetworkSession Instance { get; private set; }

    public string BattleSceneName = "BattleScene";

    public NetworkRunner Runner { get; private set; }

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
    }

    public async UniTask<bool> JoinOrCreateRoom(string _roomName)
    {
        await ShutdownRunner();
        DestroySceneManager();

        NetworkSceneManagerDefault t_sceneManager = CreateSceneManager();
        CreateRunner();

        StartGameResult t_result = await this.Runner.StartGame(BuildStartGameArgs(_roomName, t_sceneManager));
        return t_result.Ok;
    }

    public async UniTask<bool> JoinRandomRoom()
    {
        await ShutdownRunner();
        DestroySceneManager();

        NetworkSceneManagerDefault t_sceneManager = CreateSceneManager();
        CreateRunner();

        var t_args = new StartGameArgs
        {
            GameMode     = GameMode.Shared,
            SessionName  = null,
            PlayerCount  = 2,
            CustomLobbyName  = "RandomMatch",
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
            SessionName  = _roomName,
            PlayerCount  = 2,
            CustomLobbyName  = "CodeMatch",
            SceneManager = _sceneManager,
        };
    }

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
