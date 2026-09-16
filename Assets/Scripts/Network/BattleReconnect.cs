using System;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

// Keeps the live battle and its continuations while replacing only the Photon connection.
// Initial peers exchange random keys before battle. Rejoining peers must answer a fresh
// challenge with the original key; a changed PlayerRef never changes the battle owner.
internal sealed class BattleReconnect : MonoBehaviour
{
    const byte Bootstrap = 224, Challenge = 225, Proof = 226, Data = 227, Status = 228;
    const float PulseSeconds = 0.5f;
    static readonly ReliableKey Key = ReliableKey.FromInts(0x4256, 4, 0, 0);
    NetworkSession session;
    ReliableBattleChannel channel = new ReliableBattleChannel();
    byte[] localKey, remoteKey, challenge, remoteChallenge;
    PlayerRef peer;
    bool authenticated, active, stopped, reconnecting, localLost, opponentLeft, joining, battleStarted, failing;
    float lastHeard, nextPulse, deadline, nextJoin;
    int generation, checkpoint, remoteCheckpoint = -1;
    ulong checkpointHash, remoteHash;
    bool atCheckpoint;
    string room;

    internal bool Active => active;
    internal bool IsRecovering => active && reconnecting;
    internal float Remaining => Mathf.Max(0f, deadline - Time.realtimeSinceStartup);

    void Awake()
    {
        session = GetComponent<NetworkSession>();
        ResetTransport();
    }

    internal void ResetTransport()
    {
        EndBattle();
        channel = new ReliableBattleChannel();
        localKey = RandomBytes(32);
        remoteKey = null;
        challenge = remoteChallenge = null;
        authenticated = false;
        stopped = false;
        battleStarted = false;
        peer = PlayerRef.None;
        checkpoint = 0;
        remoteCheckpoint = -1;
        checkpointHash = remoteHash = 0;
        atCheckpoint = false;
        lastHeard = Time.realtimeSinceStartup;
        nextPulse = 0;
    }

    internal void BeginBattle()
    {
        if (stopped || active) return;
        room = session.PairingKey;
        if (string.IsNullOrEmpty(room) || remoteKey == null || !authenticated)
        {
            TurnRunner.Instance?.AbortMatch(EMatchEndReason.InitError);
            return;
        }
        active = true;
        battleStarted = true;
        lastHeard = Time.realtimeSinceStartup;
        // The room remains joinable by its exact name, but is no longer a matchmaking candidate.
        session.Runner.SessionInfo.IsVisible = false;
    }

    internal void EndBattle()
    {
        ++generation;
        active = reconnecting = localLost = opponentLeft = joining = false;
        TurnState.ReconnectPaused = false;
        ServerWaitOverlay.Release(this);
    }

    internal void Stop()
    {
        EndBattle();
        stopped = true;
    }

    void OnDestroy() => Stop();

    internal void PeerLeft(PlayerRef player)
    {
        if (player != peer) return;
        authenticated = false;
        opponentLeft = true;
        BeginRecovery();
    }

    internal void ConnectionLost()
    {
        if (!active) return;
        localLost = true;
        authenticated = false;
        BeginRecovery();
    }

    internal void PeerJoined(PlayerRef player)
    {
        if (session.Runner == null || player == session.Runner.LocalPlayer) return;
        if (active && !authenticated)
        {
            BeginRecovery();
            SendRaw(player, Pack(Challenge, challenge));
        }
    }

    void BeginRecovery()
    {
        if (!active || stopped || TurnState.BattleEnded || DeckConfig.AiTakeover || reconnecting) return;
        reconnecting = true;
        authenticated = false;
        TurnState.ReconnectPaused = true;
        deadline = Time.realtimeSinceStartup + NetTimeouts.OpponentDropGraceSec;
        challenge = RandomBytes(16);
        nextPulse = nextJoin = 0;
        ServerWaitOverlay.Hold(this);
        Debug.Log("[Reconnect] Waiting for the original opponent and matching battle checkpoint.");
    }

    internal void Send(byte[] payload)
    {
        if (stopped || (session.Runner == null && !active)) return;
        try
        {
            byte[] frame = channel.Enqueue(payload);
            if (authenticated) SendRaw(peer, Pack(Data, frame));
            else Pulse();
        }
        catch (Exception error) { Fail(error.Message); }
    }

    internal void Receive(PlayerRef sender, byte[] bytes)
    {
        if (stopped || bytes == null || bytes.Length < 1 || session.Runner == null
            || sender == session.Runner.LocalPlayer) return;
        try
        {
            switch (bytes[0])
            {
                case Bootstrap:
                    if (battleStarted || bytes.Length != 33) return;
                    var key = new byte[32];
                    Array.Copy(bytes, 1, key, 0, 32);
                    if (remoteKey != null && (!Equal(remoteKey, key) || sender != peer)) return;
                    bool first = remoteKey == null;
                    remoteKey = key;
                    peer = sender;
                    authenticated = true;
                    lastHeard = Time.realtimeSinceStartup;
                    if (first) SendRaw(peer, Pack(Bootstrap, localKey));
                    FlushPending();
                    break;
                case Challenge:
                    if (!active || bytes.Length != 17 || remoteKey == null) return;
                    if (authenticated && sender != peer) return;
                    byte[] nonce = new byte[16];
                    Array.Copy(bytes, 1, nonce, 0, 16);
                    if (remoteChallenge == null || !Equal(remoteChallenge, nonce)) BeginRecovery();
                    remoteChallenge = nonce;
                    SendRaw(sender, Pack(Proof, Sign(localKey, nonce)));
                    if (!authenticated) SendRaw(sender, Pack(Challenge, challenge));
                    break;
                case Proof:
                    if (!active || !reconnecting || bytes.Length != 33 || challenge == null || remoteKey == null) return;
                    byte[] proof = new byte[32];
                    Array.Copy(bytes, 1, proof, 0, 32);
                    if (!Equal(proof, Sign(remoteKey, challenge))) return;
                    if (authenticated && sender != peer) return;
                    authenticated = true;
                    peer = sender;
                    lastHeard = Time.realtimeSinceStartup;
                    FlushPending();
                    SendStatus();
                    break;
                case Data:
                    if (!authenticated || sender != peer || bytes.Length < 9) return;
                    lastHeard = Time.realtimeSinceStartup;
                    var frame = new byte[bytes.Length - 1];
                    Array.Copy(bytes, 1, frame, 0, frame.Length);
                    channel.Receive(frame, payload => NetworkGameController.Instance?.HandleMessage(sender, payload));
                    if (frame[0] != 0 || frame[1] != 0 || frame[2] != 0 || frame[3] != 0)
                        SendRaw(peer, Pack(Data, channel.AckFrame()));
                    break;
                case Status:
                    if (!active || !authenticated || sender != peer || bytes.Length != 38) return;
                    lastHeard = Time.realtimeSinceStartup;
                    int remoteSent = ReadInt(bytes, 1), remoteReceived = ReadInt(bytes, 5);
                    int boundary = ReadInt(bytes, 9);
                    ulong hash = ReadULong(bytes, 13);
                    bool ready = bytes[21] == 1;
                    byte[] echoedChallenge = new byte[16];
                    Array.Copy(bytes, 22, echoedChallenge, 0, 16);
                    // Recovery confirmation belongs to this challenge, not to a delayed status from the old connection.
                    if (challenge != null && !Equal(challenge, echoedChallenge)) return;
                    if (atCheckpoint && ready && boundary == checkpoint && hash != checkpointHash)
                    {
                        Fail("Battle checkpoint mismatch.");
                        return;
                    }
                    if (reconnecting && atCheckpoint && ready && boundary == checkpoint && hash == checkpointHash
                        && remoteSent == channel.Received && remoteReceived == channel.Sent && !channel.HasPending)
                    {
                        reconnecting = localLost = opponentLeft = false;
                        TurnState.ReconnectPaused = false;
                        ServerWaitOverlay.Release(this);
                        Debug.Log("[Reconnect] Original peer verified; battle resumed.");
                    }
                    break;
            }
        }
        catch (Exception error) { Fail(error.Message); }
    }

    // Checkpoints are reliable gameplay messages, so they cannot overtake missing attacks/spawns.
    internal void ReceiveCheckpoint(int number, ulong hash)
    {
        if (stopped || number < checkpoint || number > checkpoint + 1) return;
        remoteCheckpoint = number;
        remoteHash = hash;
    }

    internal void AttackStarted() => atCheckpoint = false;

    internal async UniTask CheckpointAsync(BattleFieldState first, BattleFieldState second, CancellationToken ct)
    {
        if (!active || DeckConfig.AiTakeover || TurnState.BattleEnded) return;
        int version = generation;
        ++checkpoint;
        checkpointHash = BattleStateHash.Compute(first, second);
        atCheckpoint = true;
        NetworkGameController.Instance.SendCheckpoint(checkpoint, checkpointHash);
        float elapsed = 0f;
        while (active && version == generation && !TurnState.BattleEnded && !DeckConfig.AiTakeover)
        {
            if (remoteCheckpoint == checkpoint)
            {
                if (remoteHash != checkpointHash) Fail("Battle checkpoint mismatch.");
                return;
            }
            await UniTask.Yield(ct);
            if (!IsRecovering) elapsed += Time.unscaledDeltaTime;
            if (elapsed >= NetTimeouts.AnimHandshakeSec) { BeginRecovery(); elapsed = 0f; }
        }
    }

    internal async UniTask WaitForInputAsync(CancellationToken ct)
    {
        while (IsRecovering && !TurnState.BattleEnded) await UniTask.Yield(ct);
    }

    void Update()
    {
        if (stopped) return;
        if (active && (TurnState.BattleEnded || DeckConfig.AiTakeover)) EndBattle();
        if (active && (session.Runner == null || !session.Runner.IsRunning)) ConnectionLost();
        float now = Time.realtimeSinceStartup;
        if (active && now - lastHeard > NetTimeouts.ReconnectHeartbeatSec) BeginRecovery();
        if (IsRecovering && now >= deadline)
        {
            bool allowAi = opponentLeft && !localLost && session.Runner != null && session.Runner.IsRunning;
            EndBattle();
            if (allowAi) TurnRunner.Instance?.HandleReconnectExpired();
            else TurnRunner.Instance?.AbortMatch(EMatchEndReason.Timeout);
            return;
        }
        if (IsRecovering && localLost && !joining && now >= nextJoin)
            RejoinAsync(generation).Forget();
        if (now >= nextPulse)
        {
            nextPulse = now + PulseSeconds;
            Pulse();
        }
    }

    async UniTaskVoid RejoinAsync(int version)
    {
        joining = true;
        try
        {
            bool ok = await session.RejoinExistingRoom(room, () => active && reconnecting && version == generation);
            if (version != generation) return;
            // Remain gated until mutual authentication and checkpoint recovery, even if Photon joined successfully.
            if (ok) localLost = false;
        }
        catch (Exception error) { Debug.LogWarning("[Reconnect] Rejoin failed: " + error.Message); }
        finally
        {
            if (version == generation)
            {
                joining = false;
                nextJoin = Time.realtimeSinceStartup + 2f;
            }
        }
    }

    void Pulse()
    {
        if (IsRecovering) ServerWaitOverlay.SetStatus(this, $"재연결 중… {Mathf.CeilToInt(Remaining)}초");
        var runner = session.Runner;
        if (runner == null || !runner.IsRunning) return;
        if (!battleStarted)
        {
            foreach (var player in runner.ActivePlayers)
                if (player != runner.LocalPlayer) SendRaw(player, Pack(Bootstrap, localKey));
        }
        if (IsRecovering && !authenticated)
        {
            foreach (var player in runner.ActivePlayers)
                if (player != runner.LocalPlayer) SendRaw(player, Pack(Challenge, challenge));
        }
        if (authenticated)
        {
            FlushPending();
            SendRaw(peer, Pack(Data, channel.AckFrame()));
            if (active) SendStatus();
        }
    }

    void FlushPending()
    {
        foreach (byte[] frame in channel.PendingFrames()) SendRaw(peer, Pack(Data, frame));
    }

    void SendStatus()
    {
        byte[] bytes = new byte[38];
        bytes[0] = Status;
        WriteInt(bytes, 1, channel.Sent);
        WriteInt(bytes, 5, channel.Received);
        WriteInt(bytes, 9, checkpoint);
        WriteULong(bytes, 13, checkpointHash);
        bytes[21] = (byte)(atCheckpoint && remoteCheckpoint == checkpoint && remoteHash == checkpointHash ? 1 : 0);
        if (remoteChallenge != null) Array.Copy(remoteChallenge, 0, bytes, 22, 16);
        SendRaw(peer, bytes);
    }

    void SendRaw(PlayerRef player, byte[] bytes)
    {
        var runner = session.Runner;
        if (bytes == null || runner == null || !runner.IsRunning) return;
        try { runner.SendReliableDataToPlayer(player, Key, bytes); }
        catch (Exception error)
        {
            Debug.LogWarning("[Reconnect] Send interrupted: " + error.Message);
            ConnectionLost();
        }
    }

    void Fail(string detail)
    {
        if (failing) return;
        failing = true;
        Debug.LogError("[Reconnect] " + detail);
        EndBattle();
        try { NetworkGameController.Instance?.RejectTransportMessage(detail); }
        finally { failing = false; }
    }

    static byte[] RandomBytes(int count) { var bytes = new byte[count]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return bytes; }
    static byte[] Sign(byte[] key, byte[] nonce) { using (var hmac = new HMACSHA256(key)) return hmac.ComputeHash(nonce); }
    static bool Equal(byte[] a, byte[] b) { if (a.Length != b.Length) return false; int diff = 0; for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i]; return diff == 0; }
    static byte[] Pack(byte kind, byte[] body) { if (body == null) return null; var bytes = new byte[body.Length + 1]; bytes[0] = kind; Array.Copy(body, 0, bytes, 1, body.Length); return bytes; }
    static int ReadInt(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];
    static ulong ReadULong(byte[] b, int at) { ulong value = 0; for (int i = 0; i < 8; i++) value = (value << 8) | b[at + i]; return value; }
    static void WriteInt(byte[] b, int at, int value) { for (int i = 0; i < 4; i++) b[at + i] = (byte)(value >> (24 - i * 8)); }
    static void WriteULong(byte[] b, int at, ulong value) { for (int i = 0; i < 8; i++) b[at + i] = (byte)(value >> (56 - i * 8)); }
}
