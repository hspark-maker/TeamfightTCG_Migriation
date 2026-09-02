using System;
using System.Threading;
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
    bool preparingBattle;

    /// <summary>진행 중인 씬 전 핸드셰이크를 끊는 유일한 수단.
    /// 패널은 비활성화될 뿐 파괴되지 않아 GetCancellationTokenOnDestroy로는 취소되지 않는다 —
    /// 이게 없으면 취소한 유저 화면에서 전투 씬이 로드된다.</summary>
    CancellationTokenSource prepareCts;

    void OnEnable()
    {
        this.preparingBattle = false;
        StartMatchAsync().Forget();
    }

    void OnDisable()
    {
        CancelPreparation();
        if (NetworkSession.Instance == null) return;
        NetworkSession.Instance.OnPlayerJoinedRoom -= HandlePlayerJoined;
        NetworkSession.Instance.OnPlayerLeftRoom   -= HandlePlayerLeft;
        NetworkSession.Instance.OnConnectionFailed -= HandleConnectionFailed;
    }

    void CancelPreparation()
    {
        if (this.prepareCts == null) return;
        CancellationTokenSource t_cts = this.prepareCts;
        this.prepareCts = null;
        t_cts.Cancel();
        t_cts.Dispose();
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
        bool t_wasPreparing = this.preparingBattle;
        CancelPreparation();
        PreBattleMatchHandoff.Clear();
        DeckConfig.ResetMode();
        this.preparingBattle = false;
        // Disconnect 이탈 이벤트만으로도 상대가 복구되기는 하지만 사유가 "연결 끊김"으로만 남는다.
        // 끊기 전에 명시적으로 알려야 상대 화면에 정확한 종료 사유가 뜬다.
        if (t_wasPreparing)
            NetworkGameController.Instance?.SendMatchAbort(EMatchEndReason.OpponentLeftDuringInit);
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
            PrepareBattleAsync().Forget();
        }
    }

    void HandlePlayerLeft(PlayerRef _player)
    {
        if (this.preparingBattle)
        {
            RecoverPreparationFailure("상대 연결이 끊겼습니다. 다시 매칭해주세요.");
            return;
        }

        SetStatus($"대기 중... ({CountActivePlayers()}/{REQUIRED_PLAYERS})");
    }

    void HandleConnectionFailed(string _reason)
    {
        if (this.preparingBattle)
        {
            RecoverPreparationFailure($"전투 준비 중 연결이 끊겼습니다: {_reason}");
            return;
        }

        SetStatus($"연결 끊김: {_reason}");
    }

    void RecoverPreparationFailure(string _message)
    {
        PreBattleMatchHandoff.Clear();
        DeckConfig.ResetMode();
        this.preparingBattle = false;
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
        SetStatus("전투 준비에 실패했습니다. 매치 화면으로 돌아가 다시 시도해주세요.");
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
        RecoverPreparationFailure("전투 입장 시간이 초과됐습니다. 다시 매칭해주세요.");
        NetworkSession.Instance?.Disconnect().Forget();
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
