using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

static class PlayerSaveSync
{
    const string DEVICE_ID_KEY = "firebase.playerSave.deviceId";
    const string OWNER_UID_KEY = "firebase.playerSave.ownerUid";
    const int UPLOAD_DEBOUNCE_MS = 3000;
    const int PULL_TIMEOUT_MS = 5000;
    const int TRANSACTION_TIMEOUT_MS = 10000;
    const int PAYLOAD_WARNING_BYTES = 256 * 1024;
    const int PAYLOAD_MAX_BYTES = 300000;

    static FirebaseFirestore s_firestore;
    static readonly Dictionary<string, string> s_uploadedHashes = new();
    static string s_activeUserId;
    static string s_profileId;
    static string s_pendingPayload;
    static int s_pendingVersion;
    static int s_generation;
    static long s_lastKnownRevision = -1;
    static string s_lastKnownRemoteHash;
    static bool s_initialized;
    static bool s_uploadAllowed;
    static bool s_uploading;
    static bool s_pullInProgress;
    static bool s_remoteWriteApproved;
    static bool s_gateComplete;

    internal static ESaveUploadState State { get; private set; } = ESaveUploadState.Disabled;
    internal static ESavePullState PullState { get; private set; } = ESavePullState.Disabled;
    internal static ESaveReconcileDecision LastDecision { get; private set; } = ESaveReconcileDecision.None;
    internal static bool IsGateComplete => s_gateComplete;

    internal static void MarkGateComplete()
    {
        s_gateComplete = true;
    }

    internal static void Initialize(string _profileId, bool _uploadAllowed)
    {
        Shutdown();

        s_initialized = true;
        s_uploadAllowed = _uploadAllowed;
        s_remoteWriteApproved = false;
        s_gateComplete = false;
        s_profileId = _profileId;
        s_activeUserId = FirebaseAuthService.Instance.IsCurrentUserActive
            ? FirebaseAuthService.Instance.UserId
            : string.Empty;
        State = _uploadAllowed ? ESaveUploadState.Idle : ESaveUploadState.Disabled;

        DataSaveManager.OnSaved += HandleLocalSaved;
        FirebaseAuthService.Instance.OnStateChanged += HandleAuthStateChanged;

        s_pendingPayload = DataSaveManager.CreateSnapshot();
        State = _uploadAllowed ? ESaveUploadState.Pending : ESaveUploadState.Disabled;
        BeginRemoteInspectionAsync(s_generation).Forget();
    }

    internal static void Shutdown()
    {
        DataSaveManager.OnSaved -= HandleLocalSaved;
        FirebaseAuthService.Instance.OnStateChanged -= HandleAuthStateChanged;
        s_generation++;
        s_pendingVersion++;
        s_initialized = false;
        s_uploading = false;
        s_pullInProgress = false;
        s_remoteWriteApproved = false;
        s_gateComplete = false;
        s_lastKnownRevision = -1;
        s_lastKnownRemoteHash = string.Empty;
        s_activeUserId = string.Empty;
    }

    internal static void FlushPending()
    {
        if (!s_initialized || !s_uploadAllowed || string.IsNullOrEmpty(s_pendingPayload)) return;
        UploadPendingAsync().Forget();
    }

    internal static void RetryPending()
    {
        if (!s_initialized || !s_uploadAllowed || string.IsNullOrEmpty(s_pendingPayload)) return;

        FirebaseAuthService.Instance.InitializeAsync().Forget();
        if (!s_pullInProgress && PullState != ESavePullState.Classified)
            BeginRemoteInspectionAsync(s_generation).Forget();
        if (CanUpload())
            ScheduleUpload();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        Shutdown();
        s_firestore = null;
        s_profileId = string.Empty;
        s_pendingPayload = string.Empty;
        s_lastKnownRevision = -1;
        s_lastKnownRemoteHash = string.Empty;
        s_uploadedHashes.Clear();
        s_gateComplete = false;
        State = ESaveUploadState.Disabled;
        PullState = ESavePullState.Disabled;
        LastDecision = ESaveReconcileDecision.None;
    }

    static void HandleLocalSaved(string _payload)
    {
        if (!s_uploadAllowed) return;
        QueueUpload(_payload);
    }

    static void HandleAuthStateChanged()
    {
        string t_activeUserId = FirebaseAuthService.Instance.IsCurrentUserActive
            ? FirebaseAuthService.Instance.UserId
            : string.Empty;
        if (s_activeUserId != t_activeUserId)
        {
            bool t_switchedAccount = !string.IsNullOrEmpty(s_activeUserId) &&
                                     !string.IsNullOrEmpty(t_activeUserId);
            s_activeUserId = t_activeUserId;
            s_generation++;
            s_uploading = false;
            s_pullInProgress = false;
            s_remoteWriteApproved = false;
            s_gateComplete = false;
            s_lastKnownRevision = -1;
            s_lastKnownRemoteHash = string.Empty;
            if (t_switchedAccount)
            {
                s_gateComplete = true;
                GameManager.MarkRecoveryRequired();
                Debug.LogError("[PlayerSaveSync] Firebase account changed during the session. Restart is required.");
                return;
            }
        }

        if (FirebaseAuthService.Instance.IsCurrentUserActive &&
            !s_pullInProgress &&
            PullState != ESavePullState.Classified)
            BeginRemoteInspectionAsync(s_generation).Forget();
    }

    static void QueueUpload(string _payload)
    {
        s_pendingPayload = _payload;
        State = ESaveUploadState.Pending;
        if (!s_pullInProgress &&
            (PullState == ESavePullState.Failed || PullState == ESavePullState.TimedOut))
        {
            BeginRemoteInspectionAsync(s_generation).Forget();
        }
        if (s_remoteWriteApproved)
            ScheduleUpload();
    }

    static void ScheduleUpload()
    {
        int t_version = ++s_pendingVersion;
        DebounceUploadAsync(t_version).Forget();
    }

    static async UniTaskVoid DebounceUploadAsync(int _version)
    {
        await UniTask.Delay(UPLOAD_DEBOUNCE_MS, ignoreTimeScale: true);
        if (!s_initialized || _version != s_pendingVersion) return;
        await UploadPendingAsync();
    }

    static async UniTaskVoid BeginRemoteInspectionAsync(int _generation)
    {
        if (!s_initialized || s_pullInProgress) return;

        s_pullInProgress = true;
        PullState = ESavePullState.WaitingAuth;
        LastDecision = ESaveReconcileDecision.None;

        try
        {
            Task t_authTask = FirebaseAuthService.Instance.InitializeAsync().AsTask();
            Task t_authCompleted = await Task.WhenAny(t_authTask, Task.Delay(PULL_TIMEOUT_MS));
            if (t_authCompleted != t_authTask)
            {
                ObserveLateTaskAsync(t_authTask).Forget();
                if (_generation != s_generation) return;

                PullState = ESavePullState.TimedOut;
                s_gateComplete = true;
                Debug.LogWarning("[PlayerSaveSync] Authentication timed out. Local play continues; cloud writes remain blocked.");
                return;
            }
            await t_authTask;
            if (_generation != s_generation) return;

            if (!FirebaseAuthService.Instance.IsCurrentUserActive)
            {
                PullState = ESavePullState.Failed;
                s_gateComplete = true;
                Debug.LogWarning("[PlayerSaveSync][Reconcile] Authentication unavailable. Cloud writes remain blocked.");
                return;
            }

            string t_userId = FirebaseAuthService.Instance.UserId;
            string t_profileId = s_profileId;
            string t_ownerUid = PlayerPrefs.GetString(OWNER_UID_KEY, string.Empty);
            if (DataSaveManager.HasLocalSave &&
                !string.IsNullOrEmpty(t_ownerUid) &&
                t_ownerUid != t_userId)
            {
                s_gateComplete = true;
                GameManager.MarkRecoveryRequired();
                Debug.LogError("[PlayerSaveSync] Local save belongs to a different Firebase UID.");
                return;
            }
            string t_localPayload = DataSaveManager.CreateSnapshot();
            bool t_hasLocalSave = DataSaveManager.HasLocalSave;
            await InspectRemoteAsync(
                _generation,
                t_userId,
                t_profileId,
                t_localPayload,
                t_hasLocalSave);
        }
        catch (Exception t_exception)
        {
            if (_generation != s_generation) return;
            PullState = ESavePullState.Failed;
            LastDecision = ESaveReconcileDecision.None;
            s_gateComplete = true;
            Debug.LogWarning($"[PlayerSaveSync][Reconcile] Inspection failed: {t_exception.GetBaseException().Message}");
        }
        finally
        {
            if (_generation == s_generation)
                s_pullInProgress = false;
        }
    }

    static async UniTask InspectRemoteAsync(
        int _generation,
        string _userId,
        string _profileId,
        string _localPayload,
        bool _hasLocalSave)
    {
        PullState = ESavePullState.Pulling;
        Task<DocumentSnapshot> t_readTask = Document(_userId, _profileId).GetSnapshotAsync(Source.Server);
        Task t_completedTask = await Task.WhenAny(t_readTask, Task.Delay(PULL_TIMEOUT_MS));
        if (t_completedTask != t_readTask)
        {
            ObserveLateReadAsync(t_readTask).Forget();
            if (!SessionMatches(_generation, _userId, _profileId)) return;

            PullState = ESavePullState.TimedOut;
            LastDecision = ESaveReconcileDecision.None;
            s_gateComplete = true;
            Debug.LogWarning($"[PlayerSaveSync][Reconcile] Remote read timed out. profile={_profileId}");
            return;
        }

        DocumentSnapshot t_snapshot = await t_readTask;
        if (!SessionMatches(_generation, _userId, _profileId))
            return;

        if (!t_snapshot.Exists)
        {
            s_lastKnownRevision = 0;
            s_lastKnownRemoteHash = string.Empty;
            PullState = ESavePullState.RemoteMissing;
            LastDecision = ESaveReconcileDecision.RemoteMissing;
            LogDecision(_profileId, 0, _localPayload, string.Empty, null);
            await ResolveDecisionAsync(
                _generation, _userId, _profileId, _localPayload, HashOf(_localPayload),
                string.Empty, string.Empty, 0, UserSaveData.VERSION);
            return;
        }

        PullState = ESavePullState.Validating;
        if (!t_snapshot.TryGetValue("schemaVersion", out long t_declaredSchemaVersion))
        {
            PullState = ESavePullState.Classified;
            LastDecision = ESaveReconcileDecision.InvalidRemote;
            s_gateComplete = true;
            Debug.LogWarning($"[PlayerSaveSync][Reconcile] Invalid remote save: schemaVersion missing. profile={_profileId}");
            return;
        }

        if (t_declaredSchemaVersion > UserSaveData.VERSION)
        {
            PullState = ESavePullState.Classified;
            LastDecision = ESaveReconcileDecision.FutureSchema;
            s_gateComplete = true;
            GameManager.MarkUpdateRequired();
            Debug.LogWarning(
                $"[PlayerSaveSync][Reconcile] Remote schema v{t_declaredSchemaVersion} is newer than client v{UserSaveData.VERSION}. " +
                $"profile={_profileId}");
            return;
        }

        if (!TryReadRemote(t_snapshot, out long t_schemaVersion, out long t_revision,
                out string t_remotePayload, out string t_remoteFullHash, out string t_error))
        {
            PullState = ESavePullState.Classified;
            LastDecision = ESaveReconcileDecision.InvalidRemote;
            s_gateComplete = true;
            Debug.LogWarning($"[PlayerSaveSync][Reconcile] Invalid remote save: {t_error}. profile={_profileId}");
            return;
        }

        s_lastKnownRevision = t_revision;
        s_lastKnownRemoteHash = t_remoteFullHash;
        string t_localFullHash = HashOf(_localPayload);
        PlayerSaveSyncMetadata t_base = PlayerSaveSyncMetadataStore.Load(_userId, _profileId);
        LastDecision = Classify(
            _hasLocalSave,
            t_schemaVersion,
            t_localFullHash,
            t_remoteFullHash,
            t_base,
            _userId,
            _profileId);
        PullState = ESavePullState.Classified;
        LogDecision(_profileId, t_revision, _localPayload, t_remotePayload, t_base);
        await ResolveDecisionAsync(
            _generation, _userId, _profileId, _localPayload, t_localFullHash,
            t_remotePayload, t_remoteFullHash, t_revision, t_schemaVersion);
    }

    static async UniTaskVoid ObserveLateReadAsync(Task<DocumentSnapshot> _readTask)
    {
        try
        {
            await _readTask;
        }
        catch
        {
            // Timeout already owns the visible result; observe only to avoid an unhandled Task exception.
        }
    }

    static async UniTaskVoid ObserveLateTaskAsync(Task _task)
    {
        try
        {
            await _task;
        }
        catch
        {
            // The visible timeout already owns the failure.
        }
    }

    static bool TryReadRemote(
        DocumentSnapshot _snapshot,
        out long _schemaVersion,
        out long _revision,
        out string _payload,
        out string _fullHash,
        out string _error)
    {
        _schemaVersion = 0;
        _revision = 0;
        _payload = string.Empty;
        _fullHash = string.Empty;
        _error = string.Empty;

        if (!_snapshot.TryGetValue("schemaVersion", out _schemaVersion) ||
            !_snapshot.TryGetValue("revision", out _revision) ||
            !_snapshot.TryGetValue("payload", out _payload) ||
            !_snapshot.TryGetValue("payloadHash", out string t_wireHash))
        {
            _error = "missing or invalid fields";
            return false;
        }

        if (_schemaVersion <= 0 || _revision < 1 || string.IsNullOrEmpty(_payload))
        {
            _error = "invalid schema, revision, or payload";
            return false;
        }

        int t_payloadBytes = Encoding.UTF8.GetByteCount(_payload);
        if (t_payloadBytes > PAYLOAD_MAX_BYTES)
        {
            _error = $"payload too large ({t_payloadBytes} bytes)";
            return false;
        }

        _fullHash = HashOf(_payload);
        if (string.IsNullOrEmpty(t_wireHash) ||
            t_wireHash.Length != 16 ||
            !_fullHash.StartsWith(t_wireHash, StringComparison.OrdinalIgnoreCase))
        {
            _error = "payload hash mismatch";
            return false;
        }

        UserSaveData t_remoteData;
        try
        {
            t_remoteData = JsonUtility.FromJson<UserSaveData>(_payload);
        }
        catch (Exception)
        {
            _error = "payload JSON is invalid";
            return false;
        }

        if (!IsComplete(t_remoteData) || t_remoteData.version != _schemaVersion)
        {
            _error = "payload schema does not match document";
            return false;
        }

        return true;
    }

    static bool IsComplete(UserSaveData _data)
    {
        return _data != null &&
               _data.currency != null &&
               _data.ownership != null &&
               _data.deck != null &&
               _data.tutorial != null &&
               _data.rank != null &&
               _data.cardGrowth != null &&
               _data.keywordGrowth != null &&
               _data.albumReward != null &&
               _data.tournament != null &&
               _data.profile != null;
    }

    static ESaveReconcileDecision Classify(
        bool _hasLocalSave,
        long _remoteSchemaVersion,
        string _localHash,
        string _remoteHash,
        PlayerSaveSyncMetadata _base,
        string _userId,
        string _profileId)
    {
        if (_localHash == _remoteHash)
            return ESaveReconcileDecision.InSync;

        if (_remoteSchemaVersion < UserSaveData.VERSION)
            return ESaveReconcileDecision.NoBaseConflict;

        if (!s_uploadAllowed)
            return ESaveReconcileDecision.RemoteAhead;

        if (!_hasLocalSave)
            return ESaveReconcileDecision.RemoteAhead;

        bool t_baseValid = _base != null &&
                           _base.firebaseUid == _userId &&
                           _base.profileId == _profileId &&
                           _base.schemaVersion == UserSaveData.VERSION &&
                           !string.IsNullOrEmpty(_base.lastSyncedHash);
        if (!t_baseValid)
            return ESaveReconcileDecision.NoBaseConflict;

        bool t_localChanged = _localHash != _base.lastSyncedHash;
        bool t_remoteChanged = _remoteHash != _base.lastSyncedHash;
        if (t_localChanged && !t_remoteChanged)
            return ESaveReconcileDecision.LocalAhead;
        if (!t_localChanged && t_remoteChanged)
            return ESaveReconcileDecision.RemoteAhead;
        if (t_localChanged && t_remoteChanged)
            return ESaveReconcileDecision.Diverged;

        return ESaveReconcileDecision.NoBaseConflict;
    }

    static void LogDecision(
        string _profileId,
        long _remoteRevision,
        string _localPayload,
        string _remotePayload,
        PlayerSaveSyncMetadata _base)
    {
        string t_localHash = ShortHash(HashOf(_localPayload));
        string t_remoteHash = string.IsNullOrEmpty(_remotePayload)
            ? "none"
            : ShortHash(HashOf(_remotePayload));
        string t_baseHash = string.IsNullOrEmpty(_base?.lastSyncedHash)
            ? "none"
            : ShortHash(_base.lastSyncedHash);

        Debug.Log(
            $"[PlayerSaveSync][Reconcile] decision={LastDecision}, profile={_profileId}, revision={_remoteRevision}, " +
            $"local={t_localHash}, remote={t_remoteHash}, base={t_baseHash}");
    }

    static string ShortHash(string _hash)
    {
        return string.IsNullOrEmpty(_hash) ? "none" : _hash.Substring(0, Math.Min(8, _hash.Length));
    }

    static async UniTask ResolveDecisionAsync(
        int _generation,
        string _userId,
        string _profileId,
        string _localPayload,
        string _localFullHash,
        string _remotePayload,
        string _remoteFullHash,
        long _remoteRevision,
        long _remoteSchemaVersion)
    {
        if (!SessionMatches(_generation, _userId, _profileId)) return;

        switch (LastDecision)
        {
            case ESaveReconcileDecision.InSync:
                CompleteSynchronized(_userId, _profileId, _localFullHash, _remoteRevision);
                return;

            case ESaveReconcileDecision.RemoteMissing:
                if (!s_uploadAllowed)
                {
                    s_gateComplete = true;
                    return;
                }
                await PushInitialOrLocalAsync(
                    _generation, _userId, _profileId, _localPayload, _localFullHash, 0, string.Empty);
                return;

            case ESaveReconcileDecision.LocalAhead:
                if (_remoteSchemaVersion < UserSaveData.VERSION)
                {
                    SaveConflictOrRequireRecovery(
                        _userId, _profileId, _remotePayload, _remoteFullHash,
                        _remoteRevision, _remoteSchemaVersion);
                    return;
                }
                if (!s_uploadAllowed)
                {
                    s_gateComplete = true;
                    return;
                }
                await PushInitialOrLocalAsync(
                    _generation, _userId, _profileId, _localPayload, _localFullHash,
                    _remoteRevision, _remoteFullHash);
                return;

            case ESaveReconcileDecision.RemoteAhead:
            {
                if (BootInstaller.IsSaveDependentInstalled)
                {
                    s_remoteWriteApproved = false;
                    s_gateComplete = true;
                    Debug.LogWarning("[PlayerSaveSync] Remote apply deferred until the next boot because save caches are active.");
                    return;
                }

                string t_backupKey =
                    $"cloud_import_backup_{_userId}_{_profileId}_{_remoteRevision}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                if (!DataSaveManager.TryApplyRemote(_remotePayload, t_backupKey, out string t_error))
                {
                    Debug.LogError($"[PlayerSaveSync] Remote apply failed: {t_error}");
                    GameManager.MarkRecoveryRequired();
                    MarkGateComplete();
                    return;
                }

                s_uploadAllowed = true;
                s_pendingPayload = DataSaveManager.CreateSnapshot();
                CompleteSynchronized(_userId, _profileId, _remoteFullHash, _remoteRevision);
                return;
            }

            case ESaveReconcileDecision.Diverged:
            case ESaveReconcileDecision.NoBaseConflict:
                SaveConflictOrRequireRecovery(
                    _userId, _profileId, _remotePayload, _remoteFullHash,
                    _remoteRevision, _remoteSchemaVersion);
                return;

            default:
                s_gateComplete = true;
                return;
        }
    }

    static async UniTask PushInitialOrLocalAsync(
        int _generation,
        string _userId,
        string _profileId,
        string _payload,
        string _fullHash,
        long _expectedRevision,
        string _expectedRemoteHash)
    {
        try
        {
            long t_newRevision = await PushTransactionAsync(
                _userId,
                _profileId,
                _payload,
                _fullHash,
                _expectedRevision,
                _expectedRemoteHash);
            if (!SessionMatches(_generation, _userId, _profileId)) return;

            s_lastKnownRevision = t_newRevision;
            s_lastKnownRemoteHash = _fullHash;
            if (s_pendingPayload == _payload) s_pendingPayload = string.Empty;
            CompleteSynchronized(_userId, _profileId, _fullHash, t_newRevision);
        }
        catch (Exception t_exception)
        {
            if (!SessionMatches(_generation, _userId, _profileId)) return;
            s_remoteWriteApproved = false;
            s_gateComplete = true;
            PullState = ESavePullState.Failed;
            LastDecision = ESaveReconcileDecision.None;
            State = ESaveUploadState.Failed;
            Debug.LogWarning($"[PlayerSaveSync] Transactional push blocked: {t_exception.GetBaseException().Message}");
        }
    }

    static async Task<long> PushTransactionAsync(
        string _userId,
        string _profileId,
        string _payload,
        string _fullHash,
        long _expectedRevision,
        string _expectedRemoteHash)
    {
        DocumentReference t_document = Document(_userId, _profileId);
        string t_wireHash = _fullHash.Substring(0, 16);
        string t_deviceId = DeviceId();
        string t_appVersion = Application.version;
        int t_payloadBytes = Encoding.UTF8.GetByteCount(_payload);
        if (t_payloadBytes > PAYLOAD_MAX_BYTES)
            throw new InvalidOperationException($"Payload too large: {t_payloadBytes} bytes.");

        Task<long> t_transactionTask = Firestore().RunTransactionAsync<long>(async t_transaction =>
        {
            DocumentSnapshot t_snapshot = await t_transaction.GetSnapshotAsync(t_document);
            long t_currentRevision = 0;
            string t_currentWireHash = string.Empty;
            if (t_snapshot.Exists)
            {
                if (!t_snapshot.TryGetValue("schemaVersion", out long t_currentSchemaVersion) ||
                    t_currentSchemaVersion != UserSaveData.VERSION ||
                    !t_snapshot.TryGetValue("revision", out t_currentRevision) ||
                    t_currentRevision < 1 ||
                    !t_snapshot.TryGetValue("payloadHash", out t_currentWireHash))
                    throw new InvalidOperationException("Remote transaction precondition fields are invalid.");

                if (string.Equals(t_currentWireHash, t_wireHash, StringComparison.OrdinalIgnoreCase) &&
                    t_snapshot.TryGetValue("payload", out string t_currentPayload) &&
                    HashOf(t_currentPayload) == _fullHash)
                    return t_currentRevision;
            }

            string t_expectedWireHash = string.IsNullOrEmpty(_expectedRemoteHash)
                ? string.Empty
                : _expectedRemoteHash.Substring(0, 16);
            if (t_currentRevision != _expectedRevision ||
                !string.Equals(t_currentWireHash, t_expectedWireHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Remote save changed after inspection.");

            long t_newRevision = t_currentRevision + 1;
            var t_data = new Dictionary<string, object>
            {
                ["schemaVersion"] = UserSaveData.VERSION,
                ["revision"] = t_newRevision,
                ["payload"] = _payload,
                ["payloadHash"] = t_wireHash,
                ["updatedAt"] = FieldValue.ServerTimestamp,
                ["deviceId"] = t_deviceId,
                ["appVersion"] = t_appVersion
            };
            t_transaction.Set(t_document, t_data, SetOptions.MergeAll);
            return t_newRevision;
        });


        Task t_completedTask = await Task.WhenAny(
            t_transactionTask,
            Task.Delay(TRANSACTION_TIMEOUT_MS));
        if (t_completedTask != t_transactionTask)
        {
            ObserveLateTaskAsync(t_transactionTask).Forget();
            throw new TimeoutException("Firestore save transaction timed out.");
        }

        return await t_transactionTask;
    }

    static void CompleteSynchronized(
        string _userId,
        string _profileId,
        string _fullHash,
        long _revision)
    {
        s_lastKnownRevision = _revision;
        s_lastKnownRemoteHash = _fullHash;
        if (!string.IsNullOrEmpty(s_pendingPayload) && HashOf(s_pendingPayload) == _fullHash)
        {
            s_uploadedHashes[HashKey(_userId, _profileId)] = _fullHash.Substring(0, 16);
            s_pendingPayload = string.Empty;
        }
        bool t_metadataSaved = PlayerSaveSyncMetadataStore.SaveConfirmed(
            _userId, _profileId, _fullHash, _revision);
        if (t_metadataSaved)
        {
            PlayerPrefs.SetString(OWNER_UID_KEY, _userId);
            PlayerPrefs.Save();
        }
        s_remoteWriteApproved = s_uploadAllowed && t_metadataSaved;
        s_gateComplete = true;
        State = string.IsNullOrEmpty(s_pendingPayload)
            ? ESaveUploadState.Idle
            : ESaveUploadState.Pending;
    }

    static void SaveConflictOrRequireRecovery(
        string _userId,
        string _profileId,
        string _payload,
        string _fullHash,
        long _revision,
        long _schemaVersion)
    {
        var t_conflict = new PlayerSaveConflictSnapshot
        {
            firebaseUid = _userId,
            profileId = _profileId,
            payload = _payload,
            payloadHash = _fullHash,
            revision = _revision,
            schemaVersion = _schemaVersion,
            capturedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        string t_key = $"outgame_remote_conflict_{_userId}_{_profileId}_{_revision}_{t_conflict.capturedUnix}";
        if (!DataSaveManager.TrySaveRemoteConflict(t_key, JsonUtility.ToJson(t_conflict), out string t_error))
        {
            Debug.LogError($"[PlayerSaveSync] Conflict backup failed: {t_error}");
            GameManager.MarkRecoveryRequired();
            return;
        }

        s_remoteWriteApproved = false;
        s_gateComplete = true;
        Debug.LogWarning($"[PlayerSaveSync] Conflict preserved locally. decision={LastDecision}, key={t_key}");
    }

    static async UniTask UploadPendingAsync()
    {
        if (!CanUpload() || s_uploading || string.IsNullOrEmpty(s_pendingPayload)) return;

        bool t_succeeded = false;
        int t_generation = s_generation;
        string t_userId = FirebaseAuthService.Instance.UserId;
        string t_profileId = s_profileId;
        string t_payload = s_pendingPayload;
        int t_payloadBytes = Encoding.UTF8.GetByteCount(t_payload);
        if (t_payloadBytes > PAYLOAD_MAX_BYTES)
        {
            State = ESaveUploadState.Failed;
            Debug.LogError($"[PlayerSaveSync] Payload too large: {t_payloadBytes} bytes.");
            return;
        }

        if (t_payloadBytes > PAYLOAD_WARNING_BYTES)
            Debug.LogWarning($"[PlayerSaveSync] Payload is {t_payloadBytes} bytes.");

        string t_fullHash = HashOf(t_payload);
        string t_hash = t_fullHash.Substring(0, 16);
        string t_hashKey = HashKey(t_userId, t_profileId);
        if (s_uploadedHashes.TryGetValue(t_hashKey, out string t_uploadedHash) && t_uploadedHash == t_hash)
        {
            if (s_pendingPayload == t_payload) s_pendingPayload = string.Empty;
            State = ESaveUploadState.Idle;
            return;
        }

        s_uploading = true;
        State = ESaveUploadState.Uploading;

        try
        {
            long t_newRevision = await PushTransactionAsync(
                t_userId,
                t_profileId,
                t_payload,
                t_fullHash,
                s_lastKnownRevision,
                s_lastKnownRemoteHash);
            if (!SessionMatches(t_generation, t_userId, t_profileId)) return;
            t_succeeded = true;

            Debug.Log($"[PlayerSaveSync] Firestore mirror uploaded. profile={t_profileId}, bytes={t_payloadBytes}");

            s_lastKnownRevision = t_newRevision;
            s_lastKnownRemoteHash = t_fullHash;
            s_uploadedHashes[t_hashKey] = t_hash;
            bool t_metadataSaved = PlayerSaveSyncMetadataStore.SaveConfirmed(
                t_userId,
                t_profileId,
                t_fullHash,
                t_newRevision);
            if (!t_metadataSaved)
                s_remoteWriteApproved = false;
            if (s_pendingPayload == t_payload) s_pendingPayload = string.Empty;
            State = string.IsNullOrEmpty(s_pendingPayload)
                ? ESaveUploadState.Idle
                : ESaveUploadState.Pending;
        }
        catch (Exception t_exception)
        {
            if (!SessionMatches(t_generation, t_userId, t_profileId)) return;
            s_remoteWriteApproved = false;
            PullState = ESavePullState.Failed;
            LastDecision = ESaveReconcileDecision.None;
            State = ESaveUploadState.Failed;
            Debug.LogWarning($"[PlayerSaveSync] Upload failed: {t_exception.GetBaseException().Message}");
        }
        finally
        {
            if (SessionMatches(t_generation, t_userId, t_profileId))
                s_uploading = false;
        }

        if (t_succeeded &&
            SessionMatches(t_generation, t_userId, t_profileId) &&
            !string.IsNullOrEmpty(s_pendingPayload))
            ScheduleUpload();
    }

    static bool CanUpload()
    {
        return s_initialized &&
               s_uploadAllowed &&
               s_remoteWriteApproved &&
               FirebaseAuthService.Instance.State == EFirebaseAuthState.SignedIn &&
               FirebaseAuthService.Instance.IsCurrentUserActive &&
               !string.IsNullOrEmpty(FirebaseAuthService.Instance.UserId);
    }

    static bool SessionMatches(int _generation, string _userId, string _profileId)
    {
        return _generation == s_generation &&
               _userId == FirebaseAuthService.Instance.UserId &&
               _profileId == s_profileId &&
               FirebaseAuthService.Instance.IsCurrentUserActive;
    }

    static FirebaseFirestore Firestore()
    {
        if (s_firestore != null) return s_firestore;

        s_firestore = FirebaseFirestore.DefaultInstance;
        s_firestore.Settings.PersistenceEnabled = false;
        return s_firestore;
    }

    static DocumentReference Document(string _userId, string _profileId)
    {
        return Firestore().Collection("users")
            .Document(_userId)
            .Collection("save")
            .Document(_profileId);
    }

    static string DeviceId()
    {
        string t_deviceId = PlayerPrefs.GetString(DEVICE_ID_KEY, string.Empty);
        if (!string.IsNullOrEmpty(t_deviceId)) return t_deviceId;

        t_deviceId = Guid.NewGuid().ToString("N");
        PlayerPrefs.SetString(DEVICE_ID_KEY, t_deviceId);
        PlayerPrefs.Save();
        return t_deviceId;
    }

    static string HashKey(string _userId, string _profileId)
    {
        return $"firebase.playerSave.hash.{_userId}.{_profileId}";
    }

    static string HashOf(string _payload)
    {
        using SHA256 t_sha = SHA256.Create();
        byte[] t_hash = t_sha.ComputeHash(Encoding.UTF8.GetBytes(_payload));
        return BitConverter.ToString(t_hash).Replace("-", string.Empty).ToLowerInvariant();
    }
}
