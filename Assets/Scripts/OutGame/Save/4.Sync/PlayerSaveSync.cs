using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

static class PlayerSaveSync
{
    const string OWNER_UID_KEY = "firebase.playerSave.ownerUid";
    const int UPLOAD_DEBOUNCE_MS = 3000;

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

    internal static ESaveUploadState State { get; private set; } = ESaveUploadState.Disabled;
    internal static ESavePullState PullState { get; private set; } = ESavePullState.Disabled;
    internal static ESaveReconcileDecision LastDecision { get; private set; } = ESaveReconcileDecision.None;
    // 게이트 상태의 주인은 BootGate다. 이 창구는 기존 호출부를 위해 남긴 위임이다.
    internal static bool IsGateComplete => BootGate.IsComplete;

    internal static void MarkGateComplete()
    {
        BootGate.MarkComplete();
    }

    internal static void Initialize(string _profileId, bool _uploadAllowed)
    {
        // Cloud 진실원에서는 이 동기화 기계가 꺼진다. 게이트는 GameManager 가 직접 열므로 여기서는 아무 신호도 만들지 않는다 —
        // Shutdown() 을 타면 이미 열린 게이트를 도로 닫아 로딩이 끝나지 않는다.
        if (SaveSourceMode.Current == ESaveSourceMode.Cloud) return;

        Shutdown();

        s_initialized = true;
        s_uploadAllowed = _uploadAllowed;
        s_remoteWriteApproved = false;
        BootGate.Reset();
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
        BootGate.Reset();
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
        s_profileId = string.Empty;
        s_pendingPayload = string.Empty;
        s_lastKnownRevision = -1;
        s_lastKnownRemoteHash = string.Empty;
        s_uploadedHashes.Clear();
        BootGate.Reset();
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
            BootGate.Reset();
            s_lastKnownRevision = -1;
            s_lastKnownRemoteHash = string.Empty;
            if (t_switchedAccount)
            {
                BootGate.MarkComplete();
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
            Task t_authCompleted = await Task.WhenAny(t_authTask, Task.Delay(PlayerSaveDocument.PULL_TIMEOUT_MS));
            if (t_authCompleted != t_authTask)
            {
                PlayerSaveDocument.ObserveLateTaskAsync(t_authTask).Forget();
                if (_generation != s_generation) return;

                PullState = ESavePullState.TimedOut;
                BootGate.MarkComplete();
                Debug.LogWarning("[PlayerSaveSync] Authentication timed out. Local play continues; cloud writes remain blocked.");
                return;
            }
            await t_authTask;
            if (_generation != s_generation) return;

            if (!FirebaseAuthService.Instance.IsCurrentUserActive)
            {
                PullState = ESavePullState.Failed;
                BootGate.MarkComplete();
                Debug.LogWarning("[PlayerSaveSync][Reconcile] Authentication unavailable. Cloud writes remain blocked.");
                return;
            }

            string t_userId = FirebaseAuthService.Instance.UserId;
            string t_profileId = s_profileId;
            string t_ownerUid = PlayerPrefs.GetString(OWNER_UID_KEY, string.Empty);
            if (await DataSaveManager.HasLocalSaveAsync() &&
                !string.IsNullOrEmpty(t_ownerUid) &&
                t_ownerUid != t_userId)
            {
                BootGate.MarkComplete();
                GameManager.MarkRecoveryRequired();
                Debug.LogError("[PlayerSaveSync] Local save belongs to a different Firebase UID.");
                return;
            }
            string t_localPayload = DataSaveManager.CreateSnapshot();
            bool t_hasLocalSave = await DataSaveManager.HasLocalSaveAsync();
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
            BootGate.MarkComplete();
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
        Task<DocumentSnapshot> t_readTask = PlayerSaveDocument.Document(_userId, _profileId).GetSnapshotAsync(Source.Server);
        Task t_completedTask = await Task.WhenAny(t_readTask, Task.Delay(PlayerSaveDocument.PULL_TIMEOUT_MS));
        if (t_completedTask != t_readTask)
        {
            ObserveLateReadAsync(t_readTask).Forget();
            if (!SessionMatches(_generation, _userId, _profileId)) return;

            PullState = ESavePullState.TimedOut;
            LastDecision = ESaveReconcileDecision.None;
            BootGate.MarkComplete();
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
                _generation, _userId, _profileId, _localPayload, PlayerSaveDocument.HashOf(_localPayload),
                string.Empty, string.Empty, 0, UserSaveData.VERSION);
            return;
        }

        PullState = ESavePullState.Validating;
        if (!t_snapshot.TryGetValue(PlayerSaveDocument.FIELD_SCHEMA_VERSION, out long t_declaredSchemaVersion))
        {
            PullState = ESavePullState.Classified;
            LastDecision = ESaveReconcileDecision.InvalidRemote;
            BootGate.MarkComplete();
            Debug.LogWarning($"[PlayerSaveSync][Reconcile] Invalid remote save: schemaVersion missing. profile={_profileId}");
            return;
        }

        if (t_declaredSchemaVersion > UserSaveData.VERSION)
        {
            PullState = ESavePullState.Classified;
            LastDecision = ESaveReconcileDecision.FutureSchema;
            BootGate.MarkComplete();
            GameManager.MarkUpdateRequired();
            Debug.LogWarning(
                $"[PlayerSaveSync][Reconcile] Remote schema v{t_declaredSchemaVersion} is newer than client v{UserSaveData.VERSION}. " +
                $"profile={_profileId}");
            return;
        }

        if (!PlayerSaveDocument.TryReadRemote(t_snapshot, out long t_schemaVersion, out long t_revision,
                out string t_remotePayload, out string t_remoteFullHash, out string t_error))
        {
            PullState = ESavePullState.Classified;
            LastDecision = ESaveReconcileDecision.InvalidRemote;
            BootGate.MarkComplete();
            Debug.LogWarning($"[PlayerSaveSync][Reconcile] Invalid remote save: {t_error}. profile={_profileId}");
            return;
        }

        s_lastKnownRevision = t_revision;
        s_lastKnownRemoteHash = t_remoteFullHash;
        string t_localFullHash = PlayerSaveDocument.HashOf(_localPayload);
        PlayerSaveSyncMetadata t_base = await PlayerSaveSyncMetadataStore.LoadAsync(_userId, _profileId);
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
        string t_localHash = ShortHash(PlayerSaveDocument.HashOf(_localPayload));
        string t_remoteHash = string.IsNullOrEmpty(_remotePayload)
            ? "none"
            : ShortHash(PlayerSaveDocument.HashOf(_remotePayload));
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
                await CompleteSynchronizedAsync(_userId, _profileId, _localFullHash, _remoteRevision);
                return;

            case ESaveReconcileDecision.RemoteMissing:
                if (!s_uploadAllowed)
                {
                    BootGate.MarkComplete();
                    return;
                }
                await PushInitialOrLocalAsync(
                    _generation, _userId, _profileId, _localPayload, _localFullHash, 0, string.Empty);
                return;

            case ESaveReconcileDecision.LocalAhead:
                if (_remoteSchemaVersion < UserSaveData.VERSION)
                {
                    await SaveConflictOrRequireRecoveryAsync(
                        _userId, _profileId, _remotePayload, _remoteFullHash,
                        _remoteRevision, _remoteSchemaVersion);
                    return;
                }
                if (!s_uploadAllowed)
                {
                    BootGate.MarkComplete();
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
                    BootGate.MarkComplete();
                    Debug.LogWarning("[PlayerSaveSync] Remote apply deferred until the next boot because save caches are active.");
                    return;
                }

                string t_backupKey =
                    $"cloud_import_backup_{_userId}_{_profileId}_{_remoteRevision}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                SaveApplyReport t_applied = await DataSaveManager.ApplyRemoteAsync(_remotePayload, t_backupKey);
                if (!t_applied.Success)
                {
                    Debug.LogError($"[PlayerSaveSync] Remote apply failed: {t_applied.Error}");
                    GameManager.MarkRecoveryRequired();
                    MarkGateComplete();
                    return;
                }

                s_uploadAllowed = true;
                s_pendingPayload = DataSaveManager.CreateSnapshot();
                await CompleteSynchronizedAsync(_userId, _profileId, _remoteFullHash, _remoteRevision);
                return;
            }

            case ESaveReconcileDecision.Diverged:
            case ESaveReconcileDecision.NoBaseConflict:
                await SaveConflictOrRequireRecoveryAsync(
                    _userId, _profileId, _remotePayload, _remoteFullHash,
                    _remoteRevision, _remoteSchemaVersion);
                return;

            default:
                BootGate.MarkComplete();
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
            long t_newRevision = await PlayerSaveDocument.PushTransactionAsync(
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
            await CompleteSynchronizedAsync(_userId, _profileId, _fullHash, t_newRevision);
        }
        catch (Exception t_exception)
        {
            if (!SessionMatches(_generation, _userId, _profileId)) return;
            s_remoteWriteApproved = false;
            BootGate.MarkComplete();
            PullState = ESavePullState.Failed;
            LastDecision = ESaveReconcileDecision.None;
            State = ESaveUploadState.Failed;
            Debug.LogWarning($"[PlayerSaveSync] Transactional push blocked: {t_exception.GetBaseException().Message}");
        }
    }

    static async UniTask CompleteSynchronizedAsync(
        string _userId,
        string _profileId,
        string _fullHash,
        long _revision)
    {
        s_lastKnownRevision = _revision;
        s_lastKnownRemoteHash = _fullHash;
        if (!string.IsNullOrEmpty(s_pendingPayload) && PlayerSaveDocument.HashOf(s_pendingPayload) == _fullHash)
        {
            s_uploadedHashes[HashKey(_userId, _profileId)] = PlayerSaveDocument.WireHashOf(_fullHash);
            s_pendingPayload = string.Empty;
        }
        bool t_metadataSaved = await PlayerSaveSyncMetadataStore.SaveConfirmedAsync(
            _userId, _profileId, _fullHash, _revision);
        if (t_metadataSaved)
        {
            PlayerPrefs.SetString(OWNER_UID_KEY, _userId);
            PlayerPrefs.Save();
        }
        s_remoteWriteApproved = s_uploadAllowed && t_metadataSaved;
        BootGate.MarkComplete();
        State = string.IsNullOrEmpty(s_pendingPayload)
            ? ESaveUploadState.Idle
            : ESaveUploadState.Pending;
    }

    static async UniTask SaveConflictOrRequireRecoveryAsync(
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
        SaveApplyReport t_saved = await DataSaveManager.SaveRemoteConflictAsync(t_key, JsonUtility.ToJson(t_conflict));
        if (!t_saved.Success)
        {
            Debug.LogError($"[PlayerSaveSync] Conflict backup failed: {t_saved.Error}");
            GameManager.MarkRecoveryRequired();
            return;
        }

        s_remoteWriteApproved = false;
        BootGate.MarkComplete();
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
        if (t_payloadBytes > PlayerSaveDocument.PAYLOAD_MAX_BYTES)
        {
            State = ESaveUploadState.Failed;
            Debug.LogError($"[PlayerSaveSync] Payload too large: {t_payloadBytes} bytes.");
            return;
        }

        if (t_payloadBytes > PlayerSaveDocument.PAYLOAD_WARNING_BYTES)
            Debug.LogWarning($"[PlayerSaveSync] Payload is {t_payloadBytes} bytes.");

        string t_fullHash = PlayerSaveDocument.HashOf(t_payload);
        string t_hash = PlayerSaveDocument.WireHashOf(t_fullHash);
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
            long t_newRevision = await PlayerSaveDocument.PushTransactionAsync(
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
            bool t_metadataSaved = await PlayerSaveSyncMetadataStore.SaveConfirmedAsync(
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

    static string HashKey(string _userId, string _profileId)
    {
        return $"firebase.playerSave.hash.{_userId}.{_profileId}";
    }
}
