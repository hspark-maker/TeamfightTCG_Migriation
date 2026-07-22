using Cysharp.Threading.Tasks;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MultiplayerLobbyPanel : MonoBehaviour
{
    [SerializeField] TMP_InputField roomNameInput;
    [SerializeField] TMP_Text statusText;
    [SerializeField] Button connectButton;
    [SerializeField] Button cancelButton;
    [SerializeField] MainMenuManager mainMenuManager;

    const int REQUIRED_PLAYERS = 2;
    int currentPlayerCount;
    bool connecting;

    void OnEnable()
    {
        this.connecting = false;
        this.currentPlayerCount = 0;
        SetStatus("방 이름을 입력하고 연결하세요.");
        SetConnectButtonInteractable(true);
    }

    void OnDisable()
    {
        if (NetworkSession.Instance != null)
        {
            NetworkSession.Instance.OnPlayerJoinedRoom -= HandlePlayerJoined;
            NetworkSession.Instance.OnPlayerLeftRoom -= HandlePlayerLeft;
            NetworkSession.Instance.OnConnectionFailed -= HandleConnectionFailed;
        }
    }

    public void OnConnectPressed()
    {
        if (this.connecting) return;
        if (!DeckConfig.HasDeck) { SetStatus("덱을 먼저 선택하세요."); return; }

        string t_room = this.roomNameInput.text.Trim();
        if (string.IsNullOrEmpty(t_room)) { SetStatus("방 이름을 입력하세요."); return; }

        ConnectAsync(t_room).Forget();
    }

    public void OnCancelPressed()
    {
        if (NetworkSession.Instance != null)
            NetworkSession.Instance.Disconnect().Forget();
        gameObject.SetActive(false);
        mainMenuManager.OnBackPressed();
    }

    async UniTaskVoid ConnectAsync(string _roomName)
    {
        this.connecting = true;
        SetConnectButtonInteractable(false);
        SetStatus("연결 중...");

        if (NetworkSession.Instance == null)
        {
            SetStatus("NetworkSession 없음. 씬에 추가하세요.");
            this.connecting = false;
            SetConnectButtonInteractable(true);
            return;
        }

        NetworkSession.Instance.OnPlayerJoinedRoom -= HandlePlayerJoined;
        NetworkSession.Instance.OnPlayerLeftRoom -= HandlePlayerLeft;
        NetworkSession.Instance.OnConnectionFailed -= HandleConnectionFailed;
        NetworkSession.Instance.OnPlayerJoinedRoom += HandlePlayerJoined;
        NetworkSession.Instance.OnPlayerLeftRoom += HandlePlayerLeft;
        NetworkSession.Instance.OnConnectionFailed += HandleConnectionFailed;

        bool t_ok = await NetworkSession.Instance.JoinOrCreateRoom(_roomName);
        if (!t_ok)
        {
            SetStatus("연결 실패. 다시 시도하세요.");
            this.connecting = false;
            SetConnectButtonInteractable(true);
            return;
        }

        // 연결 성공 — 상대 대기
        this.currentPlayerCount = NetworkSession.Instance.Runner?.ActivePlayers != null
            ? CountActivePlayers()
            : 1;
        UpdateWaitingStatus();
    }

    void HandlePlayerJoined(PlayerRef _player)
    {
        this.currentPlayerCount = CountActivePlayers();
        UpdateWaitingStatus();

        if (this.currentPlayerCount >= REQUIRED_PLAYERS)
        {
            // 모든 클라이언트에서 멀티 플래그 설정
            DeckConfig.SetMultiplayer(true);
            StartBattle();
        }
    }

    void HandlePlayerLeft(PlayerRef _player)
    {
        this.currentPlayerCount = CountActivePlayers();
        UpdateWaitingStatus();
    }

    void HandleConnectionFailed(string _reason)
    {
        SetStatus($"연결 끊김: {_reason}");
        this.connecting = false;
        SetConnectButtonInteractable(true);
    }

    void StartBattle()
    {
        SceneTransitionVideo.Instance?.PlayOverlay();

        NetworkRunner t_runner = NetworkSession.Instance?.Runner;
        if (t_runner == null || !t_runner.IsSharedModeMasterClient) return;

        string t_sceneName = NetworkSession.Instance.BattleSceneName;
        int t_buildIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{t_sceneName}.unity");
        if (t_buildIndex < 0) t_buildIndex = SceneUtility.GetBuildIndexByScenePath(t_sceneName);

        t_runner.LoadScene(SceneRef.FromIndex(t_buildIndex));
    }

    void UpdateWaitingStatus()
    {
        SetStatus($"대기 중... ({this.currentPlayerCount}/{REQUIRED_PLAYERS})");
    }

    int CountActivePlayers()
    {
        int t_count = 0;
        if (NetworkSession.Instance?.Runner == null) return t_count;
        foreach (PlayerRef _ in NetworkSession.Instance.Runner.ActivePlayers)
            t_count++;
        return t_count;
    }

    void SetStatus(string _msg)
    {
        if (this.statusText != null) this.statusText.text = _msg;
    }

    void SetConnectButtonInteractable(bool _value)
    {
        if (this.connectButton != null) this.connectButton.interactable = _value;
    }
}
