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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    const string TEST_DISABLED_KEY = "firebase.playerSave.testDisabled";
#endif
    const int UPLOAD_DEBOUNCE_MS = 1000;
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
    static int s_serverCommandDepth;
    static int s_suspendBaselineSerial;
    static int s_serverCommandGeneration;
    static bool s_initialized;
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

    // LastError는 서버 원문이라 유저에게 못 보여 준다 — 재시작 모달이 문구를 고르려면 분류된 값이 따로 필요하다.
    internal static ECloudBlockReason BlockReason { get; private set; } = ECloudBlockReason.None;

    // 부트 게이트를 통과했고, 문서를 쓸 수 있는 상태다. Failed/Blocked/Loading에서 서버를 부르면
    // 응답의 revision을 채택할 기준선 자체가 없다.
    internal static bool CanRunServerCommand =>
        s_gateComplete &&
        (State == EPlayerSaveCloudState.Ready ||
         State == EPlayerSaveCloudState.Offline ||
         State == EPlayerSaveCloudState.Uploading);

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
        BlockReason = ECloudBlockReason.None;
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

    /// <summary>서버 호출이 끝날 때까지 업로드를 봉인한다. 진행 중이던 업로드는 끝날 때까지 기다린다.</summary>
    internal static async UniTask SuspendUploadsAsync()
    {
        // 봉인이 await보다 먼저다 — 뒤집으면 기다리는 사이 디바운스 타이머가 새 업로드를 띄우고,
        // 그 업로드가 올린 revision을 서버가 모른 채 응답을 만들어 다음 트랜잭션이 충돌한다.
        s_serverCommandDepth++;
        s_serverCommandGeneration = s_generation;

        UniTaskCompletionSource t_inFlight = s_uploadCompletion;
        if (t_inFlight != null) await t_inFlight.Task;

        s_suspendBaselineSerial = s_dirtySerial;
    }

    /// <summary>서버가 쓴 문서를 채택한다 — 슬롯을 갈아끼우고 revision과 업로드 기준선을 맞춘다.</summary>
    internal static void AdoptServerResult(long _revision, ServerSlotPatch _updatedSlots)
    {
        // 통화 도중 Shutdown → Initialize가 끼면 여기 기준선은 남의 세션 것이다. 채택도 성공 반환도 하지 않는다.
        if (!s_initialized || s_serverCommandGeneration != s_generation)
            throw new ServerAdoptionException("The save session was replaced while the server command was in flight.");

        // 서버 계약: callable 하나당 문서 쓰기는 정확히 1회다. 어긋났다면 우리가 모르는 쓰기가 끼어든 것이라
        // 로컬 세이브를 원격과 맞출 방법이 없다.
        if (_revision != Revision + 1)
        {
            string t_message = $"Server revision {_revision} does not follow the local revision {Revision}.";
            BlockSession(ECloudBlockReason.RemoteAhead, t_message);

            // 예외로 끊지 않으면 세션은 죽었는데 호출한 도메인은 응답을 성공으로 받아 보상 지급을 이어간다.
            throw new ServerAdoptionException(t_message);
        }

        DataSaveManager.AdoptServerSlots(_updatedSlots);
        Revision = _revision;

        // 이 창구가 세운 Offline은 성공 업로드로만 풀렸다 — 호출 뒤 로컬 변경이 없으면 업로드가 예약되지 않아
        // 배너가 세션 끝까지 남았다. 단, 못 올린 변경이 남아 있으면 "동기화됨"이 아니다 — 판정은 업로드에 맡긴다.
        if (State == EPlayerSaveCloudState.Offline && s_dirtySerial == s_uploadedSerial)
        {
            LastError = string.Empty;
            SetUploadFailures(0);
            SetState(EPlayerSaveCloudState.Ready);
        }

        // 재기준화는 "로컬 = 마지막 업로드 = 방금 서버가 쓴 문서"일 때만 성립한다. baseline 비교만으로는 못 거른다 —
        // 통화 중 변경이 없으면 dirty == baseline이 항상 참이라, 아직 못 올린 변경이 "서버에 있다"고 기록되고
        // 서버 트랜잭션은 updatedSlots만 쓰므로 그 변경분이 영구 유실된다.
        if (s_dirtySerial == s_uploadedSerial && s_dirtySerial == s_suspendBaselineSerial)
        {
            s_uploadedSnapshot = DataSaveManager.CreateSnapshot();
            s_uploadedSerial = s_dirtySerial;
        }
        else
        {
            // 통화 중 로컬이 움직였거나 못 올린 변경이 남아 있다 → 서버 문서 내용을 복원할 수 없으니 업로드 1회를 강제해 맞춘다.
            // 여기서 스냅샷을 찍으면 그 변경분이 "서버에 이미 있다"고 거짓 기록되어 영원히 안 올라간다.
            MarkUploadPending();
        }
    }

    /// <summary>서버 호출이 끝났다 — 봉인을 풀고 밀린 변경분이 있으면 업로드를 예약한다.</summary>
    internal static void ResumeUploads()
    {
        if (s_serverCommandDepth > 0) s_serverCommandDepth--;
        if (s_serverCommandDepth != 0) return;
        if (!s_initialized || !s_uploadApproved) return;
        if (s_serverCommandGeneration != s_generation) return;
        if (s_dirtySerial == s_uploadedSerial) return;

        ScheduleUpload();
    }

    /// <summary>서버 호출 실패를 클라우드 상태에 반영하고 판정한 갈래를 돌려준다.
    /// 도메인이 거절당한 것(세션은 산다)과 세션이 못 쓰게 된 것을 여기서 가른다.</summary>
    internal static ECloudFailureKind ReportServerCommandFailure(Exception _exception)
    {
        ECloudFailureKind t_kind = CloudFailureClassifier.Classify(_exception);
        if (_exception == null) return t_kind;
        if (!s_initialized || s_serverCommandGeneration != s_generation) return t_kind;

        string t_message =
            $"Server command failed [{CloudFailureClassifier.Describe(_exception)}]: " +
            _exception.GetBaseException().Message;

        switch (t_kind)
        {
            // 도메인 거절의 표면은 세션이 아니라 예외를 되받는 호출한 도메인이다(ServerCommandRejectedException).
            case ECloudFailureKind.Rejected:
                Debug.LogWarning($"[PlayerSaveCloud] {t_message}");
                return t_kind;

            // 미배포·리전 오타·인증 붕괴·스키마 드리프트·직렬화 오류. 다음 명령도 같은 답이라 표면 없이 두면 아무 일도 안 일어난다.
            case ECloudFailureKind.Unusable:
                BlockSession(ECloudBlockReason.SessionUnusable, t_message);
                return t_kind;

            // 요청이 닿아 문서가 이미 바뀌었는데 응답만 못 받았을 수 있다 — 로컬이 서버보다 뒤처졌을 위험이 있다.
            default:
                LastError = t_message;
                SetUploadFailures(ConsecutiveUploadFailures + 1);
                SetState(EPlayerSaveCloudState.Offline);
                LogTransient(t_message, _exception);
                return t_kind;
        }
    }

    /// <summary>복구 화면의 재시도. 채택이 실패로 끝난 경우에만 인증·원격 읽기를 다시 태운다.</summary>
    internal static void ResetForRetry()
    {
        if (!s_initialized || State != EPlayerSaveCloudState.Failed) return;

        // Initialize를 다시 부르지 않는다 — 훅·구독이 살아 있어 재배선하면 이중으로 걸린다.
        s_generation++;
        s_gateComplete = false;
        LastError = string.Empty;
        SetState(EPlayerSaveCloudState.Loading);

        LoadAsync(s_generation).Forget();
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
        s_serverCommandDepth = 0;
        s_suspendBaselineSerial = 0;
        s_serverCommandGeneration = 0;
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
        PlayerPrefs.Save();
    }

    internal static void ClearTestAccountSession()
    {
        s_disabledForTestAccountSession = false;
        PlayerPrefs.DeleteKey(TEST_DISABLED_KEY);
        PlayerPrefs.Save();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetTestRuntimeState() => s_disabledForTestAccountSession = false;
#endif

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
        // 망이 끊긴 게 확실하면 auth 5초를 태울 이유가 없다 — 기다려도 답은 정해져 있다.
        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            Fail("Network is unreachable.");
            return;
        }

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
            t_document = await ReadAsync(_generation, t_userId);
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
            Fail("Remote save metadata is missing or has a broken type. " +
                 $"[{PlayerSaveDocument.DescribeMeta(t_document)}]");
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
            // 승급 코드가 없어 변환 대신 Fail이다. UserSaveData.VERSION이 동결인 한 이 갈래는 서지 않는다.
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

    // 업로드 실패 전용 매핑. Fail/BlockSession이 private이라 분류기는 값만 돌려주고 매핑은 여기서 한다.
    // 게이트 갈래를 두지 않는 이유: UploadAsync는 s_uploadApproved를 전제하고, 그건 게이트 완료와 함께 선다.
    static void ApplyUploadFailure(Exception _exception)
    {
        string t_message =
            $"Upload failed [{CloudFailureClassifier.Describe(_exception)}]: " +
            _exception.GetBaseException().Message;

        if (CloudFailureClassifier.Classify(_exception) == ECloudFailureKind.Transient)
        {
            LastError = t_message;
            SetUploadFailures(ConsecutiveUploadFailures + 1);
            SetState(EPlayerSaveCloudState.Offline);
            LogTransient(t_message, _exception);
            return;
        }

        // 여긴 클라가 문서를 직접 쓰는 경로라 Rejected가 곧 룰 거부다 — callable의 도메인 거절과 성질이 다르다.
        // 배선 오류(Unusable)와 마찬가지로 다시 태워도 같은 답이니 이 세션의 업로드는 여기서 끝이다.
        BlockSession(ECloudBlockReason.SessionUnusable, t_message);
    }

    // Transient 로그 한 곳. 갈래는 그대로 두되 결정적 서버 버그가 "동기화 지연"으로 위장하지 않게 등급을 가른다.
    static void LogTransient(string _message, Exception _exception)
    {
        if (CloudFailureClassifier.IsUnhandledServerFault(_exception))
        {
            Debug.LogError(
                $"[PlayerSaveCloud] {_message} This is most likely an unhandled exception inside the function. " +
                "Retrying will not fix it — check the function logs.");
            return;
        }

        Debug.LogWarning($"[PlayerSaveCloud] {_message}");
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
    static void BlockSession(ECloudBlockReason _reason, string _message)
    {
        LastError = _message;
        BlockReason = _reason;
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

    // 자동 재시도를 두지 않는다 — 재시도의 주체는 복구 화면의 사람이다.
    static async UniTask<DocumentSnapshot> ReadAsync(int _generation, string _userId)
    {
        Task<DocumentSnapshot> t_readTask = Document(_userId).GetSnapshotAsync(Source.Server);
        (bool t_hasResult, DocumentSnapshot t_document) = await UniTask.WhenAny(
            t_readTask.AsUniTask(),
            UniTask.Delay(FirebaseTimeouts.AuthAndReadMilliseconds, DelayType.Realtime));
        if (_generation != s_generation) return null;
        if (!t_hasResult) throw new TimeoutException("Firestore read timed out.");

        return t_document;
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
        // 서버 호출 중에는 문서의 주인이 서버다. dirty는 s_dirtySerial에 그대로 쌓여 ResumeUploads가 태운다.
        if (s_serverCommandDepth > 0) return;
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
            // 다시 태워도 같은 스냅샷이라 같은 바이트 수다 — Offline 재시도로 두면 표면 없이 영원히 돈다.
            BlockSession(
                ECloudBlockReason.DocumentTooLarge,
                $"Save document is too large: {t_bytes} bytes (limit {DOCUMENT_MAX_BYTES}).");
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
                BlockSession(ECloudBlockReason.RemoteAhead, $"Upload rejected: {t_conflict.Message}");
                return;
            }

            // 오류 코드를 보지 않고 전부 Offline으로 접으면, 룰이 닫힌 뒤의 PermissionDenied가
            // "동기화 지연" 배너로 위장한 채 영원히 재시도만 돈다.
            ApplyUploadFailure(t_exception);
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

        BlockSession(ECloudBlockReason.SessionUnusable, "Firebase account changed during the session.");
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
