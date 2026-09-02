using System;
using System.Threading;
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
    bool preparingBattle;

    /// <summary>진행 중인 씬 전 핸드셰이크를 끊는 유일한 수단.
    /// 패널은 비활성화될 뿐 파괴되지 않아 GetCancellationTokenOnDestroy로는 취소되지 않는다 —
    /// 이게 없으면 취소한 유저 화면에서 전투 씬이 로드된다.</summary>
    CancellationTokenSource prepareCts;

    void OnEnable()
    {
        this.connecting = false;
        this.preparingBattle = false;
        this.currentPlayerCount = 0;
        SetStatus("방 이름을 입력하고 연결하세요.");
        SetConnectButtonInteractable(true);
    }

    void OnDisable()
    {
        CancelPreparation();
        if (NetworkSession.Instance != null)
        {
            NetworkSession.Instance.OnPlayerJoinedRoom -= HandlePlayerJoined;
            NetworkSession.Instance.OnPlayerLeftRoom -= HandlePlayerLeft;
            NetworkSession.Instance.OnConnectionFailed -= HandleConnectionFailed;
        }
    }

    void CancelPreparation()
    {
        if (this.prepareCts == null) return;
        CancellationTokenSource t_cts = this.prepareCts;
        this.prepareCts = null;
        t_cts.Cancel();
        t_cts.Dispose();
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
        bool t_wasPreparing = this.preparingBattle;
        CancelPreparation();
        PreBattleMatchHandoff.Clear();
        DeckConfig.ResetMode();
        this.preparingBattle = false;
        // Disconnect 이탈 이벤트만으로도 상대가 복구되기는 하지만 사유가 "연결 끊김"으로만 남는다.
        // 끊기 전에 명시적으로 알려야 상대 화면에 정확한 종료 사유가 뜬다.
        if (t_wasPreparing)
            NetworkGameController.Instance?.SendMatchAbort(EMatchEndReason.OpponentLeftDuringInit);
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
            PrepareBattleAsync().Forget();
        }
    }

    void HandlePlayerLeft(PlayerRef _player)
    {
        if (this.preparingBattle)
        {
            RecoverPreparationFailure("상대 연결이 끊겼습니다. 방에 다시 연결해주세요.");
            return;
        }

        this.currentPlayerCount = CountActivePlayers();
        UpdateWaitingStatus();
    }

    void HandleConnectionFailed(string _reason)
    {
        if (this.preparingBattle)
        {
            RecoverPreparationFailure($"전투 준비 중 연결이 끊겼습니다: {_reason}");
            return;
        }

        SetStatus($"연결 끊김: {_reason}");
        this.connecting = false;
        SetConnectButtonInteractable(true);
    }

    void RecoverPreparationFailure(string _message)
    {
        PreBattleMatchHandoff.Clear();
        DeckConfig.ResetMode();
        this.preparingBattle = false;
        this.connecting = false;
        SetConnectButtonInteractable(true);
        SetStatus(_message);
    }

    void StartBattle()
    {
        SceneTransitionVideo.Instance?.PlayOverlay();

        // 마스터 여부를 보지 않는다 — 두 클라가 각자 연다(BattleSceneEntry 설명 참조).
        NetworkRunner t_runner = NetworkSession.Instance?.Runner;
        if (t_runner == null) return;

        BattleSceneEntry.Load(NetworkSession.Instance.BattleSceneName);
    }

    async UniTaskVoid PrepareBattleAsync()
    {
        if (this.preparingBattle) return;
        this.preparingBattle = true;
        SetStatus("전투 준비 동기화 중...");

        CancelPreparation();
        this.prepareCts = CancellationTokenSource.CreateLinkedTokenSource(
            this.GetCancellationTokenOnDestroy());
        CancellationTokenSource t_cts = this.prepareCts;

        EPreBattleSyncResult t_result;
        bool t_canceled;
        try
        {
            t_result = await PreBattleMatchSync.RunAsync(t_cts.Token);
        }
        finally
        {
            t_canceled = t_cts.IsCancellationRequested;
            if (this.prepareCts == t_cts)
            {
                this.prepareCts = null;
                t_cts.Dispose();
            }
        }
        if (this == null || t_canceled || t_result == EPreBattleSyncResult.Canceled) return;
        if (t_result == EPreBattleSyncResult.Success)
        {
            StartBattle();
            MonitorBattleSceneEntryAsync().Forget();
            return;
        }

        if (!this.preparingBattle) return;
        PreBattleMatchHandoff.Clear();
        DeckConfig.ResetMode();
        if (NetworkSession.Instance != null) await NetworkSession.Instance.Disconnect();
        if (this == null) return;
        this.preparingBattle = false;
        this.connecting = false;
        SetConnectButtonInteractable(true);
        SetStatus("전투 준비에 실패했습니다. 방에 다시 연결해주세요.");
    }

    async UniTaskVoid MonitorBattleSceneEntryAsync()
    {
        try
        {
            await UniTask.Delay(TimeSpan.FromSeconds(NetTimeouts.BattleSceneEntrySec),
                cancellationToken: this.GetCancellationTokenOnDestroy());
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (this == null || !gameObject.activeInHierarchy || !this.preparingBattle) return;
        NetworkGameController.Instance?.SendMatchAbort(EMatchEndReason.Timeout);
        RecoverPreparationFailure("전투 입장 시간이 초과됐습니다. 방에 다시 연결해주세요.");
        NetworkSession.Instance?.Disconnect().Forget();
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
