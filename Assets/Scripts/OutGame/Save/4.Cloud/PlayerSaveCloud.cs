using System;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

// 아웃게임 세이브의 클라우드 창구. 초기화 채택(원격 → 캐시 폴백)과 업로드를 모두 여기서 한다.
// 채택은 초기화당 1회뿐이다 — 세션 중 재-pull 경로는 만들지 않는다(매니저들이 이미 슬롯을 캐싱했다).
static class PlayerSaveCloud
{
    const string OWNER_UID_KEY = "firebase.playerSave.ownerUid";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    const string TEST_DISABLED_KEY = "firebase.playerSave.testDisabled";
#endif
    const int UPLOAD_DEBOUNCE_MS = 1000;
    const int READ_ATTEMPT_COUNT = 3;
    const int READ_BACKOFF_MS = 500;
    const int DOCUMENT_WARNING_BYTES = 256 * 1024;
    const int DOCUMENT_MAX_BYTES = 300000;
    const int BANNER_FAILURE_THRESHOLD = 3;

    static FirebaseContext s_context;
    static string s_envId = string.Empty;
    static string s_activeUserId = string.Empty;
    static string s_ownerUid = string.Empty;
    static string s_uploadedSnapshot = string.Empty;
    static UniTaskCompletionSource s_uploadCompletion;
    static int s_dirtySerial;
    static int s_uploadedSerial;
    static int s_pendingVersion;
    static int s_generation;
    static bool s_initialized;
    static bool s_hasCacheAtBoot;
    static bool s_uploading;
    static bool s_gateComplete;
    static bool s_uploadApproved;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static bool s_disabledForTestAccountSession;
#endif

    internal static EPlayerSaveCloudState State { get; private set; } = EPlayerSaveCloudState.Disabled;
    internal static string LastError { get; private set; } = string.Empty;

    // 폴링이 아니라 이벤트인 이유: 배너가 필요로 하는 것은 값이 아니라 엣지다
    // — 실패 3회째 전이, 성공 시 리셋, Blocked 모달의 정확히 1회 오픈.
    internal static event Action OnStateChanged;

    // 카운터가 배너가 아니라 여기 있는 이유: 오프라인 부트는 업로드를 한 번도 시도하지 않고 Offline이 된다
    // — "시도했으나 실패"와 "애초에 못 올림"을 가릴 수 있는 건 UploadAsync 내부뿐이다.
    internal static int ConsecutiveUploadFailures { get; private set; }

    // 임계값을 UI로 새게 두지 않는다.
    internal static bool ShouldShowSyncBanner => ConsecutiveUploadFailures >= BANNER_FAILURE_THRESHOLD;

    // 부트 게이트 해제 — 채택이 끝났거나 진입 불가 판정이 났다.
    internal static bool IsGateComplete => s_gateComplete;

    // 원격 문서가 없어 이번 세션이 첫 문서를 만든다. 스타터 지급의 유일한 근거다.
    internal static bool IsFreshAccount { get; private set; }

    internal static long Revision { get; private set; }

    internal static void Initialize(in FirebaseContext _context)
    {
        if (!_context.IsValid) throw new ArgumentException("FirebaseContext is not initialized.", nameof(_context));
        Shutdown();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (IsTestAccountSessionDisabled())
        {
            s_hasCacheAtBoot = DataSaveManager.TryLoadCache(out UserSaveData t_cacheData, out long t_cacheRevision);
            if (s_hasCacheAtBoot)
            {
                Revision = t_cacheRevision;
                DataSaveManager.AdoptRemote(t_cacheData, t_cacheRevision);
            }

            State = EPlayerSaveCloudState.Disabled;
            s_gateComplete = true;
            return;
        }
#endif

        s_context = _context;
        s_envId = _context.EnvId;
        s_initialized = true;
        s_dirtySerial = 0;
        s_uploadedSerial = 0;
        s_uploadedSnapshot = string.Empty;
        IsFreshAccount = false;
        Revision = 0;
        LastError = string.Empty;
        SetUploadFailures(0);
        SetState(EPlayerSaveCloudState.Loading);

        PlayerSaveDocument.CacheDeviceInfo();
        s_ownerUid = PlayerPrefs.GetString(OWNER_UID_KEY, string.Empty);

        DataSaveManager.SetImmediateUploadHandler(RequestImmediateUpload);
        DataSaveManager.OnSaved += MarkDirty;
        FirebaseAuthService.Instance.OnStateChanged += HandleAuthStateChanged;

        LoadAsync(s_generation).Forget();
    }

    /// <summary>로컬 저장이 일어났다 — 업로드를 디바운스 예약한다.</summary>
    internal static void MarkDirty()
    {
        if (!s_initialized) return;

        s_dirtySerial++;
        if (!s_uploadApproved) return;
        ScheduleUpload();
    }

    /// <summary>디바운스를 건너뛰고 지금 업로드한다(결과를 기다리지 않는다).</summary>
    internal static void RequestImmediateUpload()
    {
        if (!s_initialized || !s_uploadApproved) return;

        s_pendingVersion++;
        UploadAsync(s_generation).Forget();
    }

    /// <summary>대기 중인 업로드를 즉시 태우고 끝날 때까지 기다린다. 내부 재시도는 없다 —
    /// OS가 준 백그라운드 창을 넘기면 재시도가 무의미하기 때문이다.</summary>
    internal static async UniTask FlushAsync()
    {
        if (!s_initialized || !s_uploadApproved) return;

        s_pendingVersion++;

        // 진행 중인 업로드가 있으면 그것부터 끝나야 이번 변경분을 태울 수 있다.
        UniTaskCompletionSource t_inFlight = s_uploadCompletion;
        if (t_inFlight != null) await t_inFlight.Task;

        await UploadAsync(s_generation);
    }

    /// <summary>복귀 시 재시도. 오프라인 세션이라도 업로드만 다시 시도한다 — 재-pull은 하지 않는다.</summary>
    internal static void RetryPending()
    {
        if (!s_initialized || !s_uploadApproved) return;
        if (s_dirtySerial == s_uploadedSerial) return;

        FirebaseAuthService.Instance.InitializeAsync().Forget();
        ScheduleUpload();
    }

    internal static void Shutdown()
    {
        DataSaveManager.OnSaved -= MarkDirty;
        FirebaseAuthService.Instance.OnStateChanged -= HandleAuthStateChanged;

        s_generation++;
        s_pendingVersion++;
        s_initialized = false;
        s_hasCacheAtBoot = false;
        s_uploading = false;
        s_gateComplete = false;
        s_uploadApproved = false;
        s_activeUserId = string.Empty;
        s_ownerUid = string.Empty;
        s_envId = string.Empty;
        s_context = default;
        IsFreshAccount = false;
        Revision = 0;
        SetUploadFailures(0);
        SetState(EPlayerSaveCloudState.Disabled);

        UniTaskCompletionSource t_completion = s_uploadCompletion;
        s_uploadCompletion = null;
        t_completion?.TrySetResult();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        // 구독 해제는 여기서만 한다 — Shutdown은 재시도 경로에서도 불리는데, 그때 구독자가 사라지면 배너가 눈을 감는다.
        OnStateChanged = null;
        Shutdown();
        s_dirtySerial = 0;
        s_uploadedSerial = 0;
        s_uploadedSnapshot = string.Empty;
        LastError = string.Empty;
        ConsecutiveUploadFailures = 0;
    }

    // State 대입의 유일한 창구. 메인 스레드 전용이라 락을 걸지 않는다.
    static void SetState(EPlayerSaveCloudState _state)
    {
        if (State == _state) return;

        State = _state;
        RaiseChanged();
    }

    // 상태가 그대로여도 임계값을 넘는 순간은 구독자가 알아야 한다(Offline이 유지된 채 2회 → 3회).
    static void SetUploadFailures(int _count)
    {
        if (ConsecutiveUploadFailures == _count) return;

        ConsecutiveUploadFailures = _count;
        RaiseChanged();
    }

    static void RaiseChanged()
    {
        OnStateChanged?.Invoke();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    internal static bool IsTestAccountModeActive => IsTestAccountSessionDisabled();

    static bool IsTestAccountSessionDisabled()
    {
        if (ContentProfileConfig.Active.RunMode != EContentRunMode.Test) return false;
        return s_disabledForTestAccountSession || PlayerPrefs.GetInt(TEST_DISABLED_KEY, 0) == 1;
    }

    internal static void DisableForTestAccountSession()
    {
        if (ContentProfileConfig.Active.RunMode != EContentRunMode.Test)
            throw new InvalidOperationException("Test save cloud can only be disabled in Test run mode.");
        s_disabledForTestAccountSession = true;
        PlayerPrefs.SetInt(TEST_DISABLED_KEY, 1);
        PlayerPrefs.DeleteKey(OWNER_UID_KEY);
        PlayerPrefs.Save();
        s_ownerUid = string.Empty;
    }

    internal static void ClearTestAccountSession()
    {
        s_disabledForTestAccountSession = false;
        PlayerPrefs.DeleteKey(TEST_DISABLED_KEY);
        PlayerPrefs.DeleteKey(OWNER_UID_KEY);
        PlayerPrefs.Save();
        s_ownerUid = string.Empty;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetTestRuntimeState() => s_disabledForTestAccountSession = false;
#endif

    // 채택 경로의 파일 IO·PlayerPrefs까지 전부 감싼다 — 여기서 예외가 새면 게이트가 열리지 않아
    // 부트 초기화(InitializationRunner)가 로딩 화면에서 타임아웃까지 돈다.
    static async UniTaskVoid LoadAsync(int _generation)
    {
        try
        {
            await LoadCoreAsync(_generation);
        }
        catch (Exception t_exception)
        {
            if (_generation != s_generation) return;
            Fail($"Save adoption failed unexpectedly ({t_exception.GetBaseException().Message}).");
        }
    }

    static async UniTask LoadCoreAsync(int _generation)
    {
        // 캐시는 원격보다 먼저 읽는다 — 못 올린 잔여 변경분이 남아 있는지 원격과 대조해야 하기 때문이다.
        s_hasCacheAtBoot = DataSaveManager.TryLoadCache(out UserSaveData t_cacheData, out long t_cacheRevision);

        string t_userId;
        try
        {
            t_userId = await AuthenticateAsync(_generation);
        }
        catch (Exception t_exception)
        {
            if (_generation != s_generation) return;
            FallbackToCache(
                $"authentication failed ({t_exception.GetBaseException().Message})",
                t_cacheData,
                t_cacheRevision);
            return;
        }

        if (_generation != s_generation) return;
        if (string.IsNullOrEmpty(t_userId))
        {
            FallbackToCache("authentication unavailable", t_cacheData, t_cacheRevision);
            return;
        }

        if (IsCacheOwnedByOther(t_userId))
        {
            Fail("Local cache belongs to a different Firebase UID.");
            return;
        }

        DocumentSnapshot t_document;
        try
        {
            t_document = await ReadWithRetryAsync(_generation, t_userId);
        }
        catch (Exception t_exception)
        {
            if (_generation != s_generation) return;
            FallbackToCache(
                $"remote read failed ({t_exception.GetBaseException().Message})",
                t_cacheData,
                t_cacheRevision);
            return;
        }

        if (_generation != s_generation) return;
        if (t_document == null)
        {
            FallbackToCache("remote read was cancelled", t_cacheData, t_cacheRevision);
            return;
        }

        if (!t_document.Exists)
        {
            AdoptFreshAccount(t_userId);
            return;
        }

        if (!PlayerSaveDocument.TryReadMeta(t_document, out long t_schemaVersion, out long t_revision))
        {
            Fail("Remote save metadata is missing or has a broken type.");
            return;
        }

        if (t_schemaVersion > UserSaveData.VERSION)
        {
            s_gateComplete = true;
            LastError = $"Remote schema v{t_schemaVersion} is newer than client v{UserSaveData.VERSION}.";
            SetState(EPlayerSaveCloudState.Failed);
            GameInitialization.MarkUpdateRequired();
            Debug.LogWarning($"[PlayerSaveCloud] {LastError}");
            return;
        }

        if (t_schemaVersion < UserSaveData.VERSION)
        {
            Fail($"Remote schema v{t_schemaVersion} is older than client v{UserSaveData.VERSION}.");
            return;
        }

        if (t_revision < 1)
        {
            Fail($"Remote revision {t_revision} is invalid.");
            return;
        }

        UserSaveData t_remote;
        try
        {
            t_remote = t_document.ConvertTo<UserSaveData>();
        }
        catch (Exception t_exception)
        {
            // 읽기는 됐는데 내용이 깨진 것이다 — 캐시로 되돌리면 사람이 고치던 문서를 덮는다. 폴백 금지.
            Fail($"Remote save could not be converted ({t_exception.GetBaseException().Message}).");
            return;
        }

        if (t_remote == null)
        {
            Fail("Remote save converted to nothing.");
            return;
        }

        // 같은 revision 위에서 저장했는데 못 올린 변경분이 캐시에 남아 있다 — 원격으로 덮으면 영구 소실이다.
        // revision이 다르면 다른 기기가 그 뒤에 썼다는 뜻이라 원격이 이긴다.
        if (t_cacheData != null &&
            t_cacheRevision == t_revision &&
            DataSaveManager.SnapshotOf(t_cacheData) != DataSaveManager.SnapshotOf(t_remote))
        {
            AdoptUnsyncedCache(t_userId, t_cacheData, t_cacheRevision);
            return;
        }

        AdoptRemote(t_userId, t_remote, t_revision);
    }

    static void AdoptRemote(string _userId, UserSaveData _data, long _revision)
    {
        IsFreshAccount = false;
        Revision = _revision;
        DataSaveManager.AdoptRemote(_data, _revision);
        s_uploadedSnapshot = DataSaveManager.CreateSnapshot();
        s_uploadedSerial = s_dirtySerial;
        CompleteAdoption(_userId, EPlayerSaveCloudState.Ready);
        Debug.Log($"[PlayerSaveCloud] Adopted the remote save. env={s_envId}, revision={_revision}");
    }

    static void AdoptUnsyncedCache(string _userId, UserSaveData _data, long _revision)
    {
        IsFreshAccount = false;
        Revision = _revision;
        DataSaveManager.AdoptRemote(_data, _revision);
        MarkUploadPending();
        CompleteAdoption(_userId, EPlayerSaveCloudState.Ready);
        RequestImmediateUpload();
        Debug.LogWarning(
            "[PlayerSaveCloud] Local cache holds changes that never reached the cloud. " +
            $"Adopted the cache and re-uploading. env={s_envId}, revision={_revision}");
    }

    static void AdoptFreshAccount(string _userId)
    {
        IsFreshAccount = true;
        Revision = 0;
        DataSaveManager.AdoptRemote(new UserSaveData(), 0);
        MarkUploadPending();
        CompleteAdoption(_userId, EPlayerSaveCloudState.Ready);
        Debug.Log($"[PlayerSaveCloud] No remote save found. Starting a fresh account. env={s_envId}");
    }

    static void FallbackToCache(string _reason, UserSaveData _cacheData, long _cacheRevision)
    {
        if (_cacheData == null)
        {
            Fail($"Remote save is unreachable and there is no usable local cache — {_reason}.");
            return;
        }

        // 캐시가 있다는 건 계정이 이미 있다는 뜻이다. 원격을 못 읽었다는 이유로 신규 판정이 나면 스타터가 재지급된다.
        IsFreshAccount = false;
        Revision = _cacheRevision;
        DataSaveManager.AdoptRemote(_cacheData, _cacheRevision);

        // 이 캐시 자체가 아직 원격에 못 올라간 진행분일 수 있다 — 올릴 것이 있는 상태로 세워야 복귀 재시도가 돈다.
        MarkUploadPending();
        LastError = _reason;
        CompleteAdoption(s_activeUserId, EPlayerSaveCloudState.Offline);
        Debug.LogWarning(
            $"[PlayerSaveCloud] Offline boot — adopted the local cache. reason={_reason}, revision={_cacheRevision}. " +
            "Saves from this session are uploaded when the connection returns.");
    }

    static void MarkUploadPending()
    {
        s_uploadedSnapshot = string.Empty;
        s_uploadedSerial = s_dirtySerial - 1;
    }

    static void CompleteAdoption(string _userId, EPlayerSaveCloudState _state)
    {
        s_activeUserId = string.IsNullOrEmpty(_userId) ? string.Empty : _userId;
        if (!string.IsNullOrEmpty(s_activeUserId))
        {
            s_ownerUid = s_activeUserId;
            PlayerPrefs.SetString(OWNER_UID_KEY, s_activeUserId);
            PlayerPrefs.Save();
        }

        s_uploadApproved = true;
        s_gateComplete = true;
        SetState(_state);
    }

    // 캐시가 없으면 소유권도 없다 — 재설치로 PlayerPrefs만 복원된 기기가 여기 걸리면 안 된다.
    static bool IsCacheOwnedByOther(string _userId)
    {
        return s_hasCacheAtBoot &&
               !string.IsNullOrEmpty(s_ownerUid) &&
               s_ownerUid != _userId;
    }

    // 초기화 게이트를 못 연 채 끝났다 — 로딩 화면이 복구 화면으로 넘어간다.
    static void Fail(string _message)
    {
        LastError = _message;
        s_uploadApproved = false;
        s_gateComplete = true;
        GameInitialization.MarkRecoveryRequired();
        SetState(EPlayerSaveCloudState.Failed);
        Debug.LogError($"[PlayerSaveCloud] {_message}");
    }

    // 게이트를 통과한 뒤라 MarkRecoveryRequired는 화면을 바꾸지 못한 채 IsReady만 떨어뜨렸다 — 그래서 부르지 않는다.
    // 유저 표면은 Blocked를 보고 뜨는 재시작 모달(CloudSyncStatusWatcher)이 맡는다.
    // 클라우드 업로드만 끊고 로컬 캐시 기록은 살려 둔다 — 진행분이 다음 부트에 복구될 유일한 통로다.
    static void BlockSession(string _message)
    {
        LastError = _message;
        s_uploadApproved = false;
        SetState(EPlayerSaveCloudState.Blocked);
        Debug.LogError(
            $"[PlayerSaveCloud] {_message} Cloud uploads are stopped for this session; " +
            "the local cache keeps recording and the next boot recovers it. Restart is required.");
    }

    static async UniTask<string> AuthenticateAsync(int _generation)
    {
        // 이 파일의 대기는 전부 DelayType.Realtime이다 — ignoreTimeScale(=UnscaledDeltaTime)은 Time.unscaledDeltaTime을
        // 프레임마다 더하는데, BeforeSceneLoad에서 시작한 이 타임아웃의 첫 프레임 델타에 씬 로드 정지 구간이 통째로
        // 실려 5초 예산이 1프레임 만에 소진된다(실측 705ms). 네트워크 시간은 실시간으로만 재야 한다.
        int t_winner = await UniTask.WhenAny(
            FirebaseAuthService.Instance.InitializeAsync(),
            UniTask.Delay(FirebaseTimeouts.AuthAndReadMilliseconds, DelayType.Realtime));
        if (t_winner != 0) throw new TimeoutException("Firebase authentication timed out.");
        if (_generation != s_generation) return string.Empty;

        return FirebaseAuthService.Instance.IsCurrentUserActive
            ? FirebaseAuthService.Instance.UserId
            : string.Empty;
    }

    static async UniTask<DocumentSnapshot> ReadWithRetryAsync(int _generation, string _userId)
    {
        Exception t_lastException = null;

        for (int t_attempt = 0; t_attempt < READ_ATTEMPT_COUNT; t_attempt++)
        {
            if (t_attempt > 0)
            {
                await UniTask.Delay(READ_BACKOFF_MS, DelayType.Realtime);
                if (_generation != s_generation) return null;
            }

            try
            {
                Task<DocumentSnapshot> t_readTask = Document(_userId).GetSnapshotAsync(Source.Server);
                (bool t_hasResult, DocumentSnapshot t_document) = await UniTask.WhenAny(
                    t_readTask.AsUniTask(),
                    UniTask.Delay(FirebaseTimeouts.AuthAndReadMilliseconds, DelayType.Realtime));
                if (!t_hasResult)
                {
                    t_lastException = new TimeoutException("Firestore read timed out.");
                    continue;
                }

                return t_document;
            }
            catch (Exception t_exception)
            {
                t_lastException = t_exception;
                if (!IsRetryableRead(t_exception)) break;
            }
        }

        throw t_lastException ?? new InvalidOperationException("Firestore read failed.");
    }

    // 권한·인증 실패는 다시 시도해도 같은 답이라 즉시 캐시 폴백으로 보낸다.
    static bool IsRetryableRead(Exception _exception)
    {
        if (_exception.GetBaseException() is FirestoreException t_firestoreException)
        {
            return t_firestoreException.ErrorCode != FirestoreError.PermissionDenied &&
                   t_firestoreException.ErrorCode != FirestoreError.Unauthenticated;
        }

        return true;
    }

    static void ScheduleUpload()
    {
        int t_version = ++s_pendingVersion;
        DebounceUploadAsync(t_version, s_generation).Forget();
    }

    static async UniTaskVoid DebounceUploadAsync(int _version, int _generation)
    {
        await UniTask.Delay(UPLOAD_DEBOUNCE_MS, DelayType.Realtime);
        if (_generation != s_generation || _version != s_pendingVersion) return;

        await UploadAsync(_generation);
    }

    static async UniTask UploadAsync(int _generation)
    {
        if (!s_initialized || !s_uploadApproved || s_uploading) return;
        if (_generation != s_generation) return;

        // 마지막 업로드 이후 저장이 한 번도 없었으면 시작조차 하지 않는다.
        if (s_dirtySerial == s_uploadedSerial) return;

        int t_serial = s_dirtySerial;
        string t_snapshot = DataSaveManager.CreateSnapshot();

        // 내용이 직전 업로드와 같으면 revision을 올리지 않는다.
        if (t_snapshot == s_uploadedSnapshot)
        {
            s_uploadedSerial = t_serial;
            return;
        }

        int t_bytes = Encoding.UTF8.GetByteCount(t_snapshot);
        if (t_bytes > DOCUMENT_MAX_BYTES)
        {
            LastError = $"Save document is too large: {t_bytes} bytes.";
            SetState(EPlayerSaveCloudState.Failed);
            Debug.LogError($"[PlayerSaveCloud] {LastError}");
            return;
        }

        if (t_bytes > DOCUMENT_WARNING_BYTES)
            Debug.LogWarning($"[PlayerSaveCloud] Save document is {t_bytes} bytes.");

        if (!FirebaseAuthService.Instance.IsCurrentUserActive ||
            string.IsNullOrEmpty(FirebaseAuthService.Instance.UserId))
        {
            LastError = "Upload postponed because Firebase authentication is unavailable.";
            SetUploadFailures(ConsecutiveUploadFailures + 1);   // 시도는 했고 착지를 못 했다
            SetState(EPlayerSaveCloudState.Offline);
            return;
        }

        string t_userId = FirebaseAuthService.Instance.UserId;
        s_uploading = true;
        s_uploadCompletion = new UniTaskCompletionSource();
        SetState(EPlayerSaveCloudState.Uploading);
        bool t_uploaded = false;

        try
        {
            long t_newRevision = await PushAsync(t_userId, Revision);
            if (_generation != s_generation) return;

            Revision = t_newRevision;
            s_uploadedSnapshot = t_snapshot;
            s_uploadedSerial = t_serial;
            LastError = string.Empty;
            SetUploadFailures(0);
            SetState(EPlayerSaveCloudState.Ready);
            DataSaveManager.MarkUploadedRevision(t_newRevision);
            t_uploaded = true;
        }
        catch (Exception t_exception)
        {
            if (_generation != s_generation) return;

            if (t_exception.GetBaseException() is RevisionConflictException t_conflict)
            {
                // 다른 기기가 먼저 썼다. 재시작하면 원격을 다시 채택하므로 이 세션은 여기서 접는다.
                BlockSession($"Upload rejected: {t_conflict.Message}");
                return;
            }

            LastError = t_exception.GetBaseException().Message;
            SetUploadFailures(ConsecutiveUploadFailures + 1);
            SetState(EPlayerSaveCloudState.Offline);
            Debug.LogWarning($"[PlayerSaveCloud] Upload failed: {LastError}");
        }
        finally
        {
            if (_generation == s_generation) s_uploading = false;

            UniTaskCompletionSource t_completion = s_uploadCompletion;
            s_uploadCompletion = null;
            t_completion?.TrySetResult();
        }

        if (t_uploaded && _generation == s_generation && s_dirtySerial != s_uploadedSerial)
            ScheduleUpload();
    }

    static async UniTask<long> PushAsync(string _userId, long _expectedRevision)
    {
        DocumentReference t_document = Document(_userId);
        UserSaveData t_data = DataSaveManager.Data;

        Task<long> t_transactionTask = Firestore().RunTransactionAsync<long>(async t_transaction =>
        {
            DocumentSnapshot t_snapshot = await t_transaction.GetSnapshotAsync(t_document);
            long t_currentRevision = 0;
            if (t_snapshot.Exists)
            {
                if (!PlayerSaveDocument.TryReadMeta(t_snapshot, out long t_schemaVersion, out t_currentRevision) ||
                    t_schemaVersion != UserSaveData.VERSION)
                    throw new RevisionConflictException("Remote save metadata is not writable by this client.");
            }

            if (t_currentRevision != _expectedRevision)
                throw new RevisionConflictException(
                    $"Remote revision {t_currentRevision} does not match the expected {_expectedRevision}.");

            long t_nextRevision = t_currentRevision + 1;

            // Overwrite여야 삭제가 전파된다. MergeAll은 중첩 맵을 재귀 병합해 지운 항목이 원격에 영원히 남는다.
            t_transaction.Set(
                t_document,
                PlayerSaveDocument.ToFieldMap(t_data, t_nextRevision),
                SetOptions.Overwrite);
            return t_nextRevision;
        });

        (bool t_hasResult, long t_revision) = await UniTask.WhenAny(
            t_transactionTask.AsUniTask(),
            UniTask.Delay(FirebaseTimeouts.TransactionMilliseconds, DelayType.Realtime));
        if (!t_hasResult) throw new TimeoutException("Firestore save transaction timed out.");

        return t_revision;
    }

    // Firebase Auth 콜백의 스레드가 보장되지 않는다 — PlayerPrefs·GameInitialization을 만지기 전에 메인으로 올린다.
    static void HandleAuthStateChanged()
    {
        if (!s_initialized) return;
        HandleAuthStateChangedAsync(s_generation).Forget();
    }

    static async UniTaskVoid HandleAuthStateChangedAsync(int _generation)
    {
        await UniTask.SwitchToMainThread();
        if (!s_initialized || _generation != s_generation) return;

        string t_userId = FirebaseAuthService.Instance.IsCurrentUserActive
            ? FirebaseAuthService.Instance.UserId
            : string.Empty;
        if (string.IsNullOrEmpty(t_userId) || t_userId == s_activeUserId) return;

        // 오프라인 폴백으로 진입한 세션이 뒤늦게 로그인된 경우 — 캐시 주인이 같을 때만 이어 붙인다.
        if (string.IsNullOrEmpty(s_activeUserId))
        {
            if (IsCacheOwnedByOther(t_userId))
            {
                BlockSession("Signed in as a different Firebase UID than the local cache owner.");
                return;
            }

            s_activeUserId = t_userId;
            s_ownerUid = t_userId;
            PlayerPrefs.SetString(OWNER_UID_KEY, t_userId);
            PlayerPrefs.Save();
            return;
        }

        BlockSession("Firebase account changed during the session.");
    }

    static FirebaseFirestore Firestore()
    {
        return s_context.GetFirestore();
    }

    static DocumentReference Document(string _userId)
    {
        return Firestore().Document(PlayerSaveFirestorePaths.Current(s_envId, _userId));
    }

    sealed class RevisionConflictException : Exception
    {
        internal RevisionConflictException(string _message) : base(_message) { }
    }
}
