using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>서버 연결 없이 연속 경기의 네트워크 대기·패킷 수명을 검사한다.</summary>
public static class BattleMatchResetValidation
{
    [MenuItem("Tools/Battle/Validate Match Reset")]
    public static void Run()
    {
        Require(!EditorApplication.isPlaying && NetworkGameController.Instance == null
            && NetworkSession.Instance == null, "Run outside play mode with no active network session.");
        var t_scene = EditorSceneManager.NewPreviewScene();
        NetworkGameController t_network = null;
        try
        {
            var t_root = new GameObject("MatchResetValidation");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(t_root, t_scene);
            t_network = t_root.AddComponent<NetworkGameController>();

            // 싱글 항복은 대기자 없이도 두 강제 해제 표시를 남긴다.
            t_network.ForceOpponentReady();
            t_network.ForceOpponentMulliganChoice();
            t_network.ResetMatchState();
            RequireFreshWaits(t_network, "surrender then rematch");

            // 수신 버퍼도 경기 소유다. 다음 경기의 같은 순번으로 소비하면 안 된다.
            Receive(t_network, Ready(0));
            Receive(t_network, Ready(1));
            Receive(t_network, Choice(0));
            Receive(t_network, new byte[] { 10 });
            t_network.ResetMatchState();
            RequireFreshWaits(t_network, "buffered messages then rematch");
            var t_nextReady = t_network.WaitForOpponentReady();
            Require(t_nextReady.Status == UniTaskStatus.Pending, "previous sequence 1 leaked");
            Receive(t_network, Ready(1));
            Require(t_nextReady.GetAwaiter().GetResult(), "new sequence 1 was rejected");

            // 진행 중 대기는 취소로 끝나야 한다. 성공으로 풀면 이전 판 실행이 재개된다.
            var t_oldReady = t_network.WaitForOpponentReady();
            var t_oldMulligan = t_network.WaitForOpponentMulliganChoice();
            var t_oldScene = t_network.SendSceneReadyAndWaitAsync(CancellationToken.None);
            t_network.ResetMatchState();
            RequireCanceled(t_oldReady, "animation wait");
            RequireCanceled(t_oldMulligan, "mulligan wait");
            RequireCanceled(t_oldScene, "scene wait");
            RequireFreshWaits(t_network, "canceled waits then rematch");
            t_network.ResetMatchState();
            t_network.ResetMatchState();
            RequireFreshWaits(t_network, "repeated cleanup");
            Debug.Log("[BattleMatchResetValidation] PASS: surrender, buffered packets, pending cancellation and repeated cleanup; fresh messages accepted.");
        }
        finally
        {
            if (t_network != null) t_network.ResetMatchState();
            EditorSceneManager.ClosePreviewScene(t_scene);
        }
    }

    static void RequireFreshWaits(NetworkGameController _network, string _case)
    {
        var t_ready = _network.WaitForOpponentReady();
        var t_mulligan = _network.WaitForOpponentMulliganChoice();
        var t_scene = _network.SendSceneReadyAndWaitAsync(CancellationToken.None);
        Require(t_ready.Status == UniTaskStatus.Pending, _case + ": animation did not wait");
        Require(t_mulligan.Status == UniTaskStatus.Pending, _case + ": mulligan did not wait");
        Require(t_scene.Status == UniTaskStatus.Pending, _case + ": scene did not wait");
        Receive(_network, Ready(0));
        Receive(_network, Choice(2));
        Receive(_network, new byte[] { 10 });
        Require(t_ready.GetAwaiter().GetResult(), _case + ": animation failed");
        var t_choice = t_mulligan.GetAwaiter().GetResult();
        Require(t_choice.received && t_choice.slot == 2, _case + ": stale mulligan choice");
        Require(t_scene.GetAwaiter().GetResult().ready, _case + ": scene failed");
    }

    static void RequireCanceled<T>(UniTask<T> _task, string _name)
    {
        Require(_task.Status == UniTaskStatus.Canceled, _name + " survived reset");
        try { _task.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException(_name + " resumed successfully after reset");
    }

    static void Receive(NetworkGameController _network, byte[] _packet)
        => _network.HandleMessage(default, new ArraySegment<byte>(_packet));

    static byte[] Ready(int _sequence)
    {
        var t_packet = new byte[13];
        t_packet[0] = 3;
        Array.Copy(BitConverter.GetBytes(_sequence), 0, t_packet, 1, 4);
        return t_packet;
    }

    static byte[] Choice(int _slot)
    {
        var t_packet = new byte[5];
        t_packet[0] = 8;
        Array.Copy(BitConverter.GetBytes(_slot), 0, t_packet, 1, 4);
        return t_packet;
    }

    static void Require(bool _condition, string _message)
    {
        if (!_condition) throw new InvalidOperationException("[BattleMatchResetValidation] " + _message);
    }
}
