using System;
using System.Text;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase.Firestore;
using UnityEngine;

// 아웃게임 세이브의 클라우드 창구. 원격 문서 채택과 업로드를 모두 여기서 한다.
// 채택은 부트당 1회뿐이다 — 세션 중 재-pull 경로는 만들지 않는다(매니저들이 이미 슬롯을 캐싱했다).
static class PlayerSaveCloud
{
    const int UPLOAD_DEBOUNCE_MS = 1000;
    const int READ_ATTEMPT_COUNT = 3;
    const int READ_BACKOFF_MS = 500;
    const int DOCUMENT_WARNING_BYTES = 256 * 1024;
    const int DOCUMENT_MAX_BYTES = 300000;
    const int BANNER_FAILURE_THRESHOLD = 3;

    static FirebaseContext s_context;
    static string s_envId = string.Empty;
    static string s_activeUserId = string.Empty;
    static string s_uploadedSnapshot = string.Empty;
    static UniTaskCompletionSource s_uploadCompletion;
    static int s_dirtySerial;
    static int s_uploadedSerial;
    static int s_pendingVersion;
    static int s_generation;
    static bool s_initialized;
    static bool s_uploading;
    static bool s_gateComplete;
    static bool s_uploadApproved;

    internal static EPlayerSaveCloudState State { get; private set; } = EPlayerSaveCloudState.Disabled;
    internal static string LastError { get; private set; } = string.Empty;

    // 폴링이 아니라 이벤트인 이유: 배너가 필요로 하는 것은 값이 아니라 엣지다
    // — 실패 3회째 전이, 성공 시 리셋, Blocked 모달의 정확히 1회 오픈.
    internal static event Action OnStateChanged;

    // 카운터가 배너가 아니라 여기 있는 이유: 인증이 끊긴 업로드는 요청을 띄우지도 못하고 Offline이 된다
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

        DataSaveManager.SetImmediateUploadHandler(RequestImmediateUpload);
        DataSaveManager.OnSaved += MarkDirty;
        FirebaseAuthService.Instance.OnStateChanged += HandleAuthStateChanged;

        LoadAsync(s_generation).Forget();
    }

    /// <summary>메모리 세이브가 바뀌었다 — 업로드를 디바운스 예약한다.</summary>
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

    /// <summary>복귀 시 재시도. 못 올린 변경분만 다시 태운다 — 재-pull은 하지 않는다.</summary>
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
        s_uploading = false;
        s_gateComplete = false;
        s_uploadApproved = false;
        s_activeUserId = string.Empty;
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

    // 채택 경로의 예외를 전부 감싼다 — 여기서 예외가 새면 게이트가 열리지 않아
    // InitializationInstaller가 로딩 화면에서 타임아웃까지 돈다.
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
        string t_userId;
        try
        {
            t_userId = await AuthenticateAsync(_generation);
        }
        catch (Exception t_exception)
        {
            if (_generation != s_generation) return;
            Fail($"Firebase authentication failed ({t_exception.GetBaseException().Message}).");
            return;
        }

        if (_generation != s_generation) return;
        if (string.IsNullOrEmpty(t_userId))
        {
            Fail("Firebase authentication is unavailable.");
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
            Fail($"Remote save read failed ({t_exception.GetBaseException().Message}).");
            return;
        }

        if (_generation != s_generation) return;
        if (t_document == null)
        {
            Fail("Remote save read was cancelled.");
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
            // 읽기는 됐는데 내용이 깨진 것이다 — 빈 세이브로 이어 가면 사람이 고치던 문서를 다음 업로드가 덮는다.
            Fail($"Remote save could not be converted ({t_exception.GetBaseException().Message}).");
            return;
        }

        if (t_remote == null)
        {
            Fail("Remote save converted to nothing.");
            return;
        }

        AdoptRemote(t_userId, t_remote, t_revision);
    }

    static void AdoptRemote(string _userId, UserSaveData _data, long _revision)
    {
        IsFreshAccount = false;
        Revision = _revision;
        DataSaveManager.AdoptRemote(_data);
        s_uploadedSnapshot = DataSaveManager.CreateSnapshot();
        s_uploadedSerial = s_dirtySerial;
        CompleteAdoption(_userId);
        Debug.Log($"[PlayerSaveCloud] Adopted the remote save. env={s_envId}, revision={_revision}");
    }

    static void AdoptFreshAccount(string _userId)
    {
        IsFreshAccount = true;
        Revision = 0;
        DataSaveManager.AdoptRemote(new UserSaveData());
        MarkUploadPending();
        CompleteAdoption(_userId);
        Debug.Log($"[PlayerSaveCloud] No remote save found. Starting a fresh account. env={s_envId}");
    }

    static void MarkUploadPending()
    {
        s_uploadedSnapshot = string.Empty;
        s_uploadedSerial = s_dirtySerial - 1;
    }

    static void CompleteAdoption(string _userId)
    {
        s_activeUserId = string.IsNullOrEmpty(_userId) ? string.Empty : _userId;
        s_uploadApproved = true;
        s_gateComplete = true;
        SetState(EPlayerSaveCloudState.Ready);
    }

    // 부트 게이트를 못 연 채 끝났다 — 로딩 화면이 복구 화면으로 넘어간다.
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
    // 로컬 복구선이 없으므로 여기서부터의 진행분은 재시작과 함께 버려진다 — 그래서 표면이 재시작을 강제해야 한다.
    static void BlockSession(string _message)
    {
        LastError = _message;
        s_uploadApproved = false;
        SetState(EPlayerSaveCloudState.Blocked);
        Debug.LogError(
            $"[PlayerSaveCloud] {_message} Cloud uploads are stopped for this session; " +
            "progress made from here is discarded. Restart is required.");
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

    // 권한·인증 실패는 다시 시도해도 같은 답이라 즉시 실패로 보낸다.
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

    // Firebase Auth 콜백의 스레드가 보장되지 않는다 — GameInitialization을 만지기 전에 메인으로 올린다.
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

        // 채택 전에 오는 통지는 부트 중의 최초 로그인이다 — s_activeUserId는 채택이 세운다.
        if (string.IsNullOrEmpty(s_activeUserId)) return;

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
