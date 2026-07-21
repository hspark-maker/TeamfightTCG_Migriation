using Cysharp.Threading.Tasks;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RandomMatchPanel : MonoBehaviour
{
    [SerializeField] TMP_Text statusText;
    [SerializeField] MainMenuManager mainMenuManager;

    const int REQUIRED_PLAYERS = 2;

    void OnEnable()
    {
        StartMatchAsync().Forget();
    }

    void OnDisable()
    {
        if (NetworkSession.Instance == null) return;
        NetworkSession.Instance.OnPlayerJoinedRoom -= HandlePlayerJoined;
        NetworkSession.Instance.OnPlayerLeftRoom   -= HandlePlayerLeft;
        NetworkSession.Instance.OnConnectionFailed -= HandleConnectionFailed;
    }

    async UniTaskVoid StartMatchAsync()
    {
        SetStatus("상대 탐색 중...");

        if (NetworkSession.Instance == null) { SetStatus("NetworkSession 없음."); return; }

        NetworkSession.Instance.OnPlayerJoinedRoom -= HandlePlayerJoined;
        NetworkSession.Instance.OnPlayerLeftRoom   -= HandlePlayerLeft;
        NetworkSession.Instance.OnConnectionFailed -= HandleConnectionFailed;
        NetworkSession.Instance.OnPlayerJoinedRoom += HandlePlayerJoined;
        NetworkSession.Instance.OnPlayerLeftRoom   += HandlePlayerLeft;
        NetworkSession.Instance.OnConnectionFailed += HandleConnectionFailed;

        bool t_ok = await NetworkSession.Instance.JoinRandomRoom();
        if (!t_ok) { SetStatus("매칭 실패. 다시 시도하세요."); return; }

        SetStatus($"대기 중... ({CountActivePlayers()}/{REQUIRED_PLAYERS})");
    }

    public void OnCancelPressed()
    {
        NetworkSession.Instance?.Disconnect().Forget();
        gameObject.SetActive(false);
        mainMenuManager.OnBackPressed();
    }

    void HandlePlayerJoined(PlayerRef _player)
    {
        int t_count = CountActivePlayers();
        SetStatus($"대기 중... ({t_count}/{REQUIRED_PLAYERS})");

        if (t_count >= REQUIRED_PLAYERS)
        {
            DeckConfig.SetMultiplayer(true);
            StartBattle();
        }
    }

    void HandlePlayerLeft(PlayerRef _player)
        => SetStatus($"대기 중... ({CountActivePlayers()}/{REQUIRED_PLAYERS})");

    void HandleConnectionFailed(string _reason)
        => SetStatus($"연결 끊김: {_reason}");

    void StartBattle() => StartBattleAsync().Forget();

    async UniTaskVoid StartBattleAsync()
    {
        SceneTransitionVideo.Instance?.PlayOverlay();

        NetworkRunner t_runner = NetworkSession.Instance?.Runner;
        if (t_runner == null || !t_runner.IsSharedModeMasterClient) return;

        string t_sceneName = NetworkSession.Instance.BattleSceneName;
        int t_buildIndex = SceneUtility.GetBuildIndexByScenePath($"Assets/Scenes/{t_sceneName}.unity");
        if (t_buildIndex < 0) t_buildIndex = SceneUtility.GetBuildIndexByScenePath(t_sceneName);

        t_runner.LoadScene(SceneRef.FromIndex(t_buildIndex));
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
}
