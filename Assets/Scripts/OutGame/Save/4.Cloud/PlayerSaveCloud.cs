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
    const int UPLOAD_DEBOUNCE_MS = 1000;
    const int COALESCED_UPLOAD_DEBOUNCE_MS = 3000;

    // 슬라이딩 디바운스의 기아 방지선. 편집이 끊이지 않아도 첫 변경으로부터 이 안에는 올린다.
    // 앱이 언제 백그라운드로 갈지 모르므로 미업로드분이 머무는 시간에 상한이 있어야 한다.
    const int MAX_UPLOAD_DELAY_MS = 5000;
    const int DOCUMENT_WARNING_BYTES = 256 * 1024;
    const int DOCUMENT_MAX_BYTES = 300000;
    const int BANNER_FAILURE_THRESHOLD = 3;

    static FirebaseContext s_context;
    static string s_envId = string.Empty;
    static string s_activeUserId = string.Empty;
    static readonly string[] s_uploadedSlotSnapshots = new string[DataSaveManager.SaveSlotCount];
    static UniTaskCompletionSource s_uploadCompletion;
    static int s_dirtySerial;
    static int s_uploadedSerial;
    static int s_pendingVersion;
    static long s_pendingUploadDeadlineTicks;
    static long s_pendingBatchStartTicks;
    static int s_pendingDelayMs;
    static int s_generation;
    static int s_serverCommandDepth;
    static int s_suspendBaselineSerial;
    static int s_serverCommandGeneration;
    static bool s_initialized;
    static bool s_uploading;
    static bool s_gateComplete;
    static bool s_uploadApproved;
    static int s_sessionUploadAttempts;
    static int s_sessionUploadSuccesses;
    static int s_sessionRevisionConflicts;
    static int s_sessionImmediateRequests;
    static int s_sessionWriteAttempts;
    static int s_sessionReclassificationReads;
    static int s_sessionSelfHealedWrites;

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

    // 초기화 게이트 해제 — 채택이 끝났거나 진입 불가 판정이 났다.
    internal static bool IsGateComplete => s_gateComplete;


    internal static long Revision { get; private set; }
    internal static int UploadCountThisSession => s_sessionUploadSuccesses;
    internal static bool HasPendingUpload => s_dirtySerial != s_uploadedSerial;
    internal static int DirtySerialForMetrics => s_dirtySerial;
    internal static int UploadedSerialForMetrics => s_uploadedSerial;

    // LastError는 서버 원문이라 유저에게 못 보여 준다 — 재시작 모달이 문구를 고르려면 분류된 값이 따로 필요하다.
    internal static ECloudBlockReason BlockReason { get; private set; } = ECloudBlockReason.None;

    // 초기화 게이트를 통과했고, 문서를 쓸 수 있는 상태다. Failed/Blocked/Loading에서 서버를 부르면
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

        s_context = _context;
        s_envId = _context.EnvId;
        s_initialized = true;
        s_dirtySerial = 0;
        s_uploadedSerial = 0;
        s_pendingUploadDeadlineTicks = 0;
        ClearUploadedSlotSnapshots();
        ResetUploadMetrics();
        Revision = 0;
        LastError = string.Empty;
        BlockReason = ECloudBlockReason.None;
        SetUploadFailures(0);
        SetState(EPlayerSaveCloudState.Loading);

        WalletCloud.Initialize(in _context);
        TutorialGrantsCloud.Initialize(in _context);
        PlayerSaveDocument.CacheDeviceInfo();

        DataSaveManager.SetImmediateUploadHandler(RequestImmediateUpload);
        DataSaveManager.OnSaved += MarkDirty;
        FirebaseAuthService.Instance.OnStateChanged += HandleAuthStateChanged;

        LoadAsync(s_generation).Forget();
    }

    /// <summary>메모리 세이브가 바뀌었다 — 업로드를 디바운스 예약한다.</summary>
    internal static void MarkDirty(ESaveUploadTiming _timing)
    {
        if (!s_initialized) return;

        s_dirtySerial++;
        if (!s_uploadApproved) return;
        ScheduleUpload(_timing == ESaveUploadTiming.Coalesced
            ? COALESCED_UPLOAD_DEBOUNCE_MS
            : UPLOAD_DEBOUNCE_MS);
    }

    /// <summary>디바운스를 건너뛰고 지금 업로드한다(결과를 기다리지 않는다).</summary>
    internal static void RequestImmediateUpload()
    {
        if (!s_initialized || !s_uploadApproved) return;

        s_pendingVersion++;
        s_pendingUploadDeadlineTicks = 0;
        s_sessionImmediateRequests++;
        UploadAsync(s_generation, true).Forget();
    }

    /// <summary>대기 중인 업로드를 즉시 태우고 끝날 때까지 기다린다. 내부 재시도는 없다 —
    /// OS가 준 백그라운드 창을 넘기면 재시도가 무의미하기 때문이다.</summary>
    internal static async UniTask FlushAsync()
    {
        if (!s_initialized || !s_uploadApproved) return;

        s_pendingVersion++;
        s_pendingUploadDeadlineTicks = 0;

        // 진행 중인 업로드가 있으면 그것부터 끝나야 이번 변경분을 태울 수 있다.
        UniTaskCompletionSource t_inFlight = s_uploadCompletion;
        if (t_inFlight != null) await t_inFlight.Task;

        s_sessionImmediateRequests++;
        await UploadAsync(s_generation, true);
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
            CaptureUploadedSlotSnapshots();
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
        WalletCloud.ResetForRetry();
        TutorialGrantsCloud.ResetForRetry();
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
        s_pendingUploadDeadlineTicks = 0;
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
        Revision = 0;
        WalletCloud.Shutdown();
        TutorialGrantsCloud.Shutdown();
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
        ClearUploadedSlotSnapshots();
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


    // 채택 경로의 파일 IO·PlayerPrefs까지 전부 감싼다 — 여기서 예외가 새면 게이트가 열리지 않아
    // 초기화(InitializationRunner)가 로딩 화면에서 타임아웃까지 돈다.
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

        // 지갑 읽기를 세이브 읽기 왕복에 얹는다 — 초기화 예산에 문서 하나치 지연을 더하지 않으려는 것이다.
        // TryReadAsync는 던지지 않는다(세이브 쪽이 먼저 실패해도 관측되지 않는 예외가 남지 않게).
        UniTask<bool> t_walletRead = WalletCloud.TryReadAsync(t_userId);
        // 튜토리얼 무료 한 방 표식도 같은 자리에 얹는다. 읽기 실패는 접지 않는다 —
        // 최종 판정은 서버가 하고 클라 값은 표시·통과 판정용이라, 못 읽었다고 신규 계정을 복구 화면으로 보내면 그게 사고다.
        UniTask<bool> t_grantsRead = TutorialGrantsCloud.TryReadAsync(t_userId);

        DocumentSnapshot t_document;
        try
        {
            t_document = await ReadAsync(_generation, t_userId);
        }
        catch (Exception t_exception)
        {
            await t_walletRead;
            await t_grantsRead;
            if (_generation != s_generation) return;
            Fail($"Remote save read failed ({t_exception.GetBaseException().Message}).");
            return;
        }

        bool t_walletReadOk = await t_walletRead;
        bool t_grantsReadOk = await t_grantsRead;
        if (_generation != s_generation) return;
        if (t_document == null)
        {
            Fail("Remote save read was cancelled.");
            return;
        }

        if (!t_document.Exists)
        {
            // 문서 생성은 서버만 한다(firestore.rules의 allow create: if false). 스타터 지급도 거기서 난다.
            if (!await TryEnsureAccountAsync(_generation)) return;

            try
            {
                t_document = await ReadAsync(_generation, t_userId);
            }
            catch (Exception t_exception)
            {
                if (_generation != s_generation) return;
                Fail($"Save read after account creation failed ({t_exception.GetBaseException().Message}).");
                return;
            }

            // ensureAccount는 세이브와 지갑을 한 트랜잭션에 만들지만 응답에 지갑을 싣지 않는다 — 다시 읽어 채택한다.
            t_walletReadOk = await WalletCloud.TryReadAsync(t_userId);
            if (_generation != s_generation) return;

            // 재귀하지 않는다 — 서버가 만들었다고 답했는데 없으면 우리가 모르는 일이 벌어진 것이다.
            if (t_document == null || !t_document.Exists)
            {
                Fail("Account creation reported success but the save document is still missing.");
                return;
            }
        }

        if (!t_grantsReadOk)
            Debug.LogWarning($"[PlayerSaveCloud] Tutorial grants read failed ({TutorialGrantsCloud.LastError}).");

        if (!t_walletReadOk)
        {
            Fail($"Remote wallet read failed ({WalletCloud.LastError}).");
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

        // 지갑 확보 — v7 문서의 currency 삭제와 지갑 생성이 서버에서 한 트랜잭션이다.
        // 반드시 첫 업로드보다 앞이어야 한다: 업로드는 원격 schemaVersion이 클라 버전과 같을 때만 커밋되므로
        // 승급 전에 업로드가 먼저 나가면 RevisionConflict로 세션이 접힌다.
        if (t_schemaVersion < UserSaveData.VERSION || !WalletCloud.HasDocument)
        {
            EnsureWalletResult t_wallet = await EnsureWalletAsync(_generation);
            if (t_wallet == null) return;

            // revision > 0 = 이 호출이 세이브를 썼다(승급). 그때만 기준 revision과 스키마가 함께 올라간다.
            if (t_wallet.Revision > 0)
            {
                t_revision = t_wallet.Revision;
                t_schemaVersion = UserSaveData.VERSION;
            }

            WalletCloud.Adopt(t_wallet.Wallet);

            // 다른 기기가 방금 승급을 커밋했다 — 지갑이 이미 있어 서버는 세이브를 쓰지 않았고(revision 미탑재가 맞다),
            // 우리 손의 스냅샷만 승급 전이라 아래 스키마 판정이 그대로면 멀쩡한 계정이 복구 화면을 본다.
            // 드문 경로라 왕복 1회를 더 태워 판정을 이어간다.
            if (!t_wallet.Created && t_schemaVersion < UserSaveData.VERSION)
            {
                try
                {
                    t_document = await ReadAsync(_generation, t_userId);
                }
                catch (Exception t_exception)
                {
                    if (_generation != s_generation) return;
                    Fail($"Save re-read after wallet creation failed ({t_exception.GetBaseException().Message}).");
                    return;
                }

                if (_generation != s_generation) return;
                if (t_document == null || !t_document.Exists)
                {
                    Fail("Save document is missing after wallet creation.");
                    return;
                }

                if (!PlayerSaveDocument.TryReadMeta(t_document, out t_schemaVersion, out t_revision))
                {
                    Fail("Remote save metadata is missing or has a broken type after wallet creation. " +
                         $"[{PlayerSaveDocument.DescribeMeta(t_document)}]");
                    return;
                }
            }
        }

        // 개명 전 이름이 남은 문서는 룰의 최상위 전수 검증을 어느 쪽으로도 통과하지 못한다 —
        // 접속은 되는데 저장만 전부 거부된다. 서버만 고칠 수 있으므로(Admin SDK가 룰을 우회한다)
        // 여기서 한 번 고치고 문서를 다시 읽어 그 뒤 판정을 이어간다. 고칠 것이 없으면 왕복도 없다.
        if (PlayerSaveDocument.NeedsSlotRepair(t_document))
        {
            if (!await TryRepairSaveSlotsAsync(_generation)) return;

            try
            {
                t_document = await ReadAsync(_generation, t_userId);
            }
            catch (Exception t_exception)
            {
                if (_generation != s_generation) return;
                Fail($"Save re-read after slot repair failed ({t_exception.GetBaseException().Message}).");
                return;
            }

            if (_generation != s_generation) return;
            if (t_document == null || !t_document.Exists)
            {
                Fail("Save document is missing after slot repair.");
                return;
            }

            if (!PlayerSaveDocument.TryReadMeta(t_document, out t_schemaVersion, out t_revision))
            {
                Fail("Remote save metadata is missing or has a broken type after slot repair. " +
                     $"[{PlayerSaveDocument.DescribeMeta(t_document)}]");
                return;
            }
        }

        if (t_schemaVersion < UserSaveData.VERSION)
        {
            // 승급은 지갑 이관(v7 → v8)까지만 있다. 그보다 오래된 문서는 변환할 코드가 없다.
            Fail($"Remote schema v{t_schemaVersion} is older than client v{UserSaveData.VERSION}.");
            return;
        }

        if (!WalletCloud.HasDocument)
        {
            Fail("Wallet document is still missing after ensureWallet.");
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
        Revision = _revision;
        DataSaveManager.AdoptRemote(_data);
        CaptureUploadedSlotSnapshots();
        s_uploadedSerial = s_dirtySerial;
        CompleteAdoption(_userId);
        Debug.Log($"[PlayerSaveCloud] Adopted the remote save. env={s_envId}, revision={_revision}");
    }

    // 원격 문서가 없을 때 서버에게 만들어 달라고 한다. 실패하면 여기서 Fail까지 마치고 false를 준다.
    static async UniTask<bool> TryEnsureAccountAsync(int _generation)
    {
        try
        {
            EnsureAccountResult t_result = await ServerSaveCommands.InvokeInitAsync<EnsureAccountResult>(
                "ensureAccount",
                new
                {
                    env = s_envId,
                    deviceId = PlayerSaveDocument.DeviceId(),
                    appVersion = PlayerSaveDocument.AppVersion(),
                });

            if (_generation != s_generation) return false;
            if (t_result == null)
            {
                Fail("Account creation returned nothing.");
                return false;
            }

            Debug.Log($"[PlayerSaveCloud] ensureAccount created={t_result.Created} " +
                      $"revision={t_result.Revision} starter={t_result.StarterSource} env={s_envId}");
            return true;
        }
        catch (Exception t_exception)
        {
            if (_generation != s_generation) return false;

            // 분류로 갈래를 두지 않는다 — 초기화에는 오프라인 폴백이 없어 Transient든 아니든 결론이 복구 화면 하나다.
            Fail($"Account creation failed [{CloudFailureClassifier.Describe(t_exception)}]: " +
                 t_exception.GetBaseException().Message);
            return false;
        }
    }

    // 개명된 슬롯을 새 이름으로 옮긴다. 멱등이라 이미 고쳐진 문서면 서버가 아무것도 쓰지 않는다.
    static async UniTask<bool> TryRepairSaveSlotsAsync(int _generation)
    {
        try
        {
            RepairSaveSlotsResult t_result = await ServerSaveCommands.InvokeInitAsync<RepairSaveSlotsResult>(
                "repairSaveSlots",
                new { env = s_envId });

            if (_generation != s_generation) return false;
            if (t_result == null)
            {
                Fail("Save slot repair returned nothing.");
                return false;
            }

            if (t_result.Repaired)
                Debug.Log($"[PlayerSaveCloud] repairSaveSlots renamed=[{string.Join(", ", t_result.Renamed ?? new string[0])}] " +
                          $"filled=[{string.Join(", ", t_result.Filled ?? new string[0])}] env={s_envId}");

            return true;
        }
        catch (Exception t_exception)
        {
            if (_generation != s_generation) return false;

            // ensureAccount와 같은 이유로 분류 갈래를 두지 않는다 — 고치지 못하면 저장이 영영 거부되므로
            // 오프라인 폴백이 없고 결론이 복구 화면 하나다.
            Fail($"Save slot repair failed [{CloudFailureClassifier.Describe(t_exception)}]: " +
                 t_exception.GetBaseException().Message);
            return false;
        }
    }

    // 지갑을 확보하고 세이브 v7 승급분을 받아 온다. 실패하면 여기서 Fail까지 마치고 null을 준다.
    static async UniTask<EnsureWalletResult> EnsureWalletAsync(int _generation)
    {
        try
        {
            EnsureWalletResult t_result = await ServerSaveCommands.InvokeInitAsync<EnsureWalletResult>(
                "ensureWallet",
                new { env = s_envId });

            if (_generation != s_generation) return null;
            if (t_result?.Wallet == null)
            {
                Fail("Wallet creation returned nothing.");
                return null;
            }

            Debug.Log($"[PlayerSaveCloud] ensureWallet created={t_result.Created} " +
                      $"revision={t_result.Revision} rev={t_result.Wallet.Rev} env={s_envId}");
            return t_result;
        }
        catch (Exception t_exception)
        {
            if (_generation != s_generation) return null;

            // ensureAccount와 같은 이유로 분류 갈래를 두지 않는다 — 초기화에는 오프라인 폴백이 없다.
            Fail($"Wallet creation failed [{CloudFailureClassifier.Describe(t_exception)}]: " +
                 t_exception.GetBaseException().Message);
            return null;
        }
    }

    static void MarkUploadPending()
    {
        ClearUploadedSlotSnapshots();
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
        //
        // 예산은 두 갈래다. 이 대기에는 네이티브 SDK의 첫 적재(CheckAndFixDependencies)가 같이 실려 있는데,
        // 그건 왕복이 아니라 프로세스 1회성 비용이라 5초로 재면 에디터 첫 Play가 통째로 실패한다
        // (두 번째 Play부터 멀쩡했던 이유가 이것 — 네이티브가 이미 프로세스에 남아 즉시 끝난다).
        // 한 번 데워진 뒤부터는 남는 게 실제 왕복뿐이라 원래 예산으로 돌아온다.
        int t_budget = FirebaseAuthService.DependenciesReady
            ? FirebaseTimeouts.AuthAndReadMilliseconds
            : FirebaseTimeouts.SdkColdStartMilliseconds;

        int t_winner = await UniTask.WhenAny(
            FirebaseAuthService.Instance.InitializeAsync(),
            UniTask.Delay(t_budget, DelayType.Realtime));
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

    static void ScheduleUpload(int _delayMs = UPLOAD_DEBOUNCE_MS)
    {
        long t_now = DateTime.UtcNow.Ticks;

        if (s_pendingUploadDeadlineTicks == 0)
        {
            // 새 배치다 — 기아 상한의 기준점을 여기서 잡는다.
            s_pendingBatchStartTicks = t_now;
            s_pendingDelayMs = _delayMs;
        }
        else
        {
            // 급한 쪽이 이긴다. coalesced 저장이 이미 예약된 기본 저장의 마감을 뒤로 밀면 안 된다.
            s_pendingDelayMs = Math.Min(s_pendingDelayMs, _delayMs);
        }

        // 마감은 저장이 올 때마다 미룬다(슬라이딩) — 이래야 연속 편집이 업로드 1회로 합쳐진다.
        // 고정 창으로 두면 편집이 이어지는 동안 창이 끝날 때마다 업로드가 나가 합치는 의미가 사라진다.
        // 다만 편집이 멈추지 않으면 영원히 안 올라가므로 배치 시작 기준 상한을 함께 건다.
        long t_slidingDeadline = t_now + s_pendingDelayMs * TimeSpan.TicksPerMillisecond;
        long t_hardDeadline = s_pendingBatchStartTicks + MAX_UPLOAD_DELAY_MS * TimeSpan.TicksPerMillisecond;
        s_pendingUploadDeadlineTicks = Math.Min(t_slidingDeadline, t_hardDeadline);

        int t_version = ++s_pendingVersion;
        DebounceUploadAsync(t_version, s_generation, s_pendingUploadDeadlineTicks).Forget();
    }

    static async UniTaskVoid DebounceUploadAsync(int _version, int _generation, long _deadlineTicks)
    {
        long t_remainingTicks = _deadlineTicks - DateTime.UtcNow.Ticks;
        if (t_remainingTicks > 0)
        {
            int t_delayMs = Math.Max(1, (int)Math.Ceiling(t_remainingTicks / (double)TimeSpan.TicksPerMillisecond));
            await UniTask.Delay(t_delayMs, DelayType.Realtime);
        }
        if (_generation != s_generation || _version != s_pendingVersion) return;

        s_pendingUploadDeadlineTicks = 0;
        await UploadAsync(_generation, false);
    }

    static async UniTask UploadAsync(int _generation, bool _isImmediate)
    {
        // 서버 호출 중에는 문서의 주인이 서버다. dirty는 s_dirtySerial에 그대로 쌓여 ResumeUploads가 태운다.
        if (s_serverCommandDepth > 0) return;
        if (!s_initialized || !s_uploadApproved || s_uploading) return;
        if (_generation != s_generation) return;

        // 마지막 업로드 이후 저장이 한 번도 없었으면 시작조차 하지 않는다.
        if (s_dirtySerial == s_uploadedSerial) return;

        int t_serial = s_dirtySerial;
        string[] t_slotSnapshots = new string[DataSaveManager.SaveSlotCount];
        DataSaveManager.WriteSlotSnapshots(t_slotSnapshots);

        ESaveSlot t_dirtySlots = ESaveSlot.None;
        int t_slotPayloadBytes = 0;
        int t_allSlotBytes = 0;
        for (int i = 0; i < DataSaveManager.SaveSlotCount; i++)
        {
            int t_slotBytes = Encoding.UTF8.GetByteCount(t_slotSnapshots[i]);
            t_allSlotBytes += t_slotBytes;

            if (t_slotSnapshots[i] == s_uploadedSlotSnapshots[i]) continue;

            t_dirtySlots |= DataSaveManager.SaveSlotAt(i);
            t_slotPayloadBytes += t_slotBytes;
        }

        // 내용이 직전 업로드와 같으면 revision을 올리지 않는다.
        if (t_dirtySlots == ESaveSlot.None)
        {
            s_uploadedSerial = t_serial;
            return;
        }

        // 부분 쓰기라도 가드는 문서 전체 크기다 — payload 기준으로 재면 문서가 상한을 넘게 자라도 아무도 못 본다.
        // 슬롯 합계는 문서 스냅샷보다 항상 작다(빠진 것은 키 이름과 구분자뿐). 경고선 아래면 상한과는 3만 바이트 넘게 벌어져
        // 정확한 값이 필요 없다 — 그때만 문서를 한 번 더 직렬화해 정확히 잰다.
        int t_bytes = t_allSlotBytes;
        if (t_allSlotBytes > DOCUMENT_WARNING_BYTES)
        {
            t_bytes = Encoding.UTF8.GetByteCount(DataSaveManager.CreateSnapshot());
            if (t_bytes > DOCUMENT_MAX_BYTES)
            {
                // 다시 태워도 같은 스냅샷이라 같은 바이트 수다 — Offline 재시도로 두면 표면 없이 영원히 돈다.
                BlockSession(
                    ECloudBlockReason.DocumentTooLarge,
                    $"Save document is too large: {t_bytes} bytes (limit {DOCUMENT_MAX_BYTES}).");
                return;
            }

            Debug.LogWarning($"[PlayerSaveCloud] Save document is {t_bytes} bytes.");
        }

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
        int t_uploadNumber = ++s_sessionUploadAttempts;

        try
        {
            PushResult t_result = await PushAsync(t_userId, Revision, t_dirtySlots);
            if (_generation != s_generation) return;

            Revision = t_result.Revision;

            // self-heal은 "원격 revision이 방금 우리가 쓰려던 값이고 deviceId도 우리 것"이라는 정황 증거다 —
            // 내용까지 대조한 것이 아니다. deviceId는 PlayerPrefs GUID라 기기 백업 복원으로 복제될 수 있고,
            // 그 복제본이 같은 revision을 먼저 썼다면 우리 슬롯을 "올렸다"고 기록하는 순간 조용히 유실된다.
            // 그래서 revision만 맞추고 기준선은 그대로 둔다 — 아래 재예약이 같은 내용을 한 번 더 올려 확정한다.
            if (!t_result.SelfHealed)
            {
                for (int i = 0; i < DataSaveManager.SaveSlotCount; i++)
                {
                    ESaveSlot t_slot = DataSaveManager.SaveSlotAt(i);
                    if ((t_dirtySlots & t_slot) != 0)
                        s_uploadedSlotSnapshots[i] = t_slotSnapshots[i];
                }
                s_uploadedSerial = t_serial;
            }

            LastError = string.Empty;
            SetUploadFailures(0);
            SetState(EPlayerSaveCloudState.Ready);
            t_uploaded = true;
            s_sessionUploadSuccesses++;
            Debug.Log(
                $"[PlayerSaveCloudMetrics] upload={t_uploadNumber} success={s_sessionUploadSuccesses} " +
                $"mode={(_isImmediate ? "immediate" : "debounce")} dirty=0x{(int)t_dirtySlots:X} " +
                $"slotPayloadBytes={t_slotPayloadBytes} documentBytes={t_bytes} " +
                $"writeAttempts={s_sessionWriteAttempts} revision={Revision} " +
                $"reclassificationReads={s_sessionReclassificationReads} " +
                $"selfHealed={t_result.SelfHealed} selfHealedWrites={s_sessionSelfHealedWrites} " +
                $"immediateRequests={s_sessionImmediateRequests}");
        }
        catch (Exception t_exception)
        {
            if (_generation != s_generation) return;

            if (t_exception.GetBaseException() is SaveSchemaMismatchException t_schemaMismatch)
            {
                GameInitialization.MarkUpdateRequired();
                BlockSession(ECloudBlockReason.SessionUnusable, $"Upload rejected: {t_schemaMismatch.Message}");
                return;
            }

            if (t_exception.GetBaseException() is RevisionConflictException t_conflict)
            {
                s_sessionRevisionConflicts++;
                Debug.LogWarning(
                    $"[PlayerSaveCloudMetrics] revisionConflict={s_sessionRevisionConflicts} " +
                    $"upload={t_uploadNumber} dirty=0x{(int)t_dirtySlots:X} " +
                    $"writeAttempts={s_sessionWriteAttempts} " +
                    $"reclassificationReads={s_sessionReclassificationReads}");
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

    static async UniTask<PushResult> PushAsync(
        string _userId,
        long _expectedRevision,
        ESaveSlot _dirtySlots)
    {
        DocumentReference t_document = Document(_userId);
        UserSaveData t_data = DataSaveManager.Data;
        long t_nextRevision = _expectedRevision + 1;
        s_sessionWriteAttempts++;

        try
        {
            UniTask<long> t_writeTask = UpdateDocumentAsync(
                t_document,
                t_data,
                _dirtySlots,
                t_nextRevision);
            (bool t_hasResult, long t_revision) = await UniTask.WhenAny(
                t_writeTask,
                UniTask.Delay(FirebaseTimeouts.TransactionMilliseconds, DelayType.Realtime));
            if (!t_hasResult) throw new TimeoutException("Firestore save update timed out.");

            return new PushResult(t_revision, false);
        }
        catch (Exception t_exception)
        {
            if (CloudFailureClassifier.Classify(t_exception) != ECloudFailureKind.Rejected)
                throw;

            return await ReclassifyRejectedWriteAsync(
                t_document,
                _expectedRevision,
                t_nextRevision,
                t_exception);
        }
    }

    static async UniTask<long> UpdateDocumentAsync(
        DocumentReference _document,
        UserSaveData _data,
        ESaveSlot _dirtySlots,
        long _nextRevision)
    {
        // 키는 반드시 "ownership" 같은 최상위 한 칸이어야 한다. 최상위 필드에 맵을 통째로 주면 그 필드는 교체돼
        // 삭제가 전파되지만, "ownership.someCard" 같은 중첩 경로를 쓰는 순간 지운 항목이 원격에 영원히 남는다.
        // (같은 이유로 SetOptions.MergeAll도 쓸 수 없다.)
        //
        // revision CAS는 여기서 읽어 확인하지 않는다 — firestore.rules 의 update 조건
        // (revision == resource.data.revision + 1, schemaVersion 일치)이 서버에서 강제한다.
        // 규칙이 거절하면 ReclassifyRejectedWriteAsync 가 그때 한 번만 읽어 원인을 가른다.
        await _document.UpdateAsync(
            PlayerSaveDocument.ToSlotFieldMap(_data, _dirtySlots, _nextRevision));
        return _nextRevision;
    }

    static async UniTask<PushResult> ReclassifyRejectedWriteAsync(
        DocumentReference _document,
        long _expectedRevision,
        long _attemptedRevision,
        Exception _writeException)
    {
        s_sessionReclassificationReads++;
        Task<DocumentSnapshot> t_readTask = _document.GetSnapshotAsync(Source.Server);
        (bool t_hasResult, DocumentSnapshot t_snapshot) = await UniTask.WhenAny(
            t_readTask.AsUniTask(),
            UniTask.Delay(FirebaseTimeouts.AuthAndReadMilliseconds, DelayType.Realtime));
        if (!t_hasResult) throw _writeException;

        if (t_snapshot == null || !t_snapshot.Exists)
            throw new RevisionConflictException("Remote save document no longer exists.");

        if (!PlayerSaveDocument.TryReadMeta(
                t_snapshot,
                out long t_schemaVersion,
                out long t_remoteRevision))
            throw new SaveSchemaMismatchException("Remote save metadata is unreadable.");

        if (t_schemaVersion != UserSaveData.VERSION)
            throw new SaveSchemaMismatchException(
                $"Remote schema {t_schemaVersion} does not match client schema {UserSaveData.VERSION}.");

        string t_remoteDeviceId = string.Empty;
        try
        {
            t_snapshot.TryGetValue(PlayerSaveDocument.FIELD_DEVICE_ID, out t_remoteDeviceId);
        }
        catch (Exception)
        {
            t_remoteDeviceId = string.Empty;
        }

        if (t_remoteRevision == _attemptedRevision &&
            t_remoteDeviceId == PlayerSaveDocument.DeviceId())
        {
            s_sessionSelfHealedWrites++;
            Debug.LogWarning(
                $"[PlayerSaveCloudMetrics] selfHealedWrite revision={t_remoteRevision} " +
                $"reclassificationReads={s_sessionReclassificationReads}");
            return new PushResult(t_remoteRevision, true);
        }

        if (t_remoteRevision > _expectedRevision)
            throw new RevisionConflictException(
                $"Remote revision {t_remoteRevision} is ahead of expected {_expectedRevision}.");

        Debug.LogWarning(
            $"[PlayerSaveCloudMetrics] rejectedWriteUnclassified " +
            $"expectedRevision={_expectedRevision} attemptedRevision={_attemptedRevision} " +
            $"remoteRevision={t_remoteRevision} remoteDeviceMatches=" +
            $"{t_remoteDeviceId == PlayerSaveDocument.DeviceId()}");
        throw _writeException;
    }

    // Firebase Auth 콜백의 스레드가 보장되지 않는다 — GameInitialization을 만지기 전에 메인으로 올린다.
    static void CaptureUploadedSlotSnapshots()
    {
        DataSaveManager.WriteSlotSnapshots(s_uploadedSlotSnapshots);
    }

    static void ClearUploadedSlotSnapshots()
    {
        Array.Clear(s_uploadedSlotSnapshots, 0, s_uploadedSlotSnapshots.Length);
    }

    static void ResetUploadMetrics()
    {
        s_sessionUploadAttempts = 0;
        s_sessionUploadSuccesses = 0;
        s_sessionRevisionConflicts = 0;
        s_sessionImmediateRequests = 0;
        s_sessionWriteAttempts = 0;
        s_sessionReclassificationReads = 0;
        s_sessionSelfHealedWrites = 0;
    }

    readonly struct PushResult
    {
        internal readonly long Revision;
        internal readonly bool SelfHealed;

        internal PushResult(long _revision, bool _selfHealed)
        {
            Revision = _revision;
            SelfHealed = _selfHealed;
        }
    }

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

        // 채택 전에 오는 통지는 초기화 중의 최초 로그인이다 — s_activeUserId는 채택이 세운다.
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>배포된 보안 규칙이 경로 하나의 읽기를 실제로 막는지 잰 결과.</summary>
    internal readonly struct RuleProbe
    {
        /// <summary>규칙이 읽기를 거부했다 — 진단이 기대하는 결과다.</summary>
        internal bool Denied { get; }

        /// <summary>판정 근거. 거부면 오류 코드, 통과했으면 문서 존재 여부다.</summary>
        internal string Detail { get; }

        internal RuleProbe(bool _denied, string _detail)
        {
            this.Denied = _denied;
            this.Detail = _detail;
        }
    }

    /// <summary>임의 경로를 한 번 읽어 배포된 규칙의 차단 여부를 잰다. 세이브 상태는 건드리지 않는다.</summary>
    // 캐시에서 답이 나오면 규칙을 거치지 않아 진단이 성립하지 않는다 — Source.Server는 필수다.
    internal static async UniTask<RuleProbe> ProbeReadDeniedAsync(string _documentPath)
    {
        if (!s_context.IsValid) return new RuleProbe(false, "firebase 미초기화");

        try
        {
            Task<DocumentSnapshot> t_readTask =
                Firestore().Document(_documentPath).GetSnapshotAsync(Source.Server);
            (bool t_hasResult, DocumentSnapshot t_snapshot) = await UniTask.WhenAny(
                t_readTask.AsUniTask(),
                UniTask.Delay(FirebaseTimeouts.AuthAndReadMilliseconds, DelayType.Realtime));

            if (!t_hasResult) return new RuleProbe(false, "시간 초과");

            return new RuleProbe(false, t_snapshot.Exists ? "문서 있음" : "문서 없음");
        }
        catch (Exception t_exception)
        {
            bool t_denied = t_exception.GetBaseException() is FirestoreException t_firestore &&
                            t_firestore.ErrorCode == FirestoreError.PermissionDenied;
            return new RuleProbe(t_denied, CloudFailureClassifier.Describe(t_exception));
        }
    }
#endif

    sealed class RevisionConflictException : Exception
    {
        internal RevisionConflictException(string _message) : base(_message) { }
    }

    sealed class SaveSchemaMismatchException : Exception
    {
        internal SaveSchemaMismatchException(string _message) : base(_message) { }
    }
}
