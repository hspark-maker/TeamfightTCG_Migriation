using Cysharp.Threading.Tasks;
using UnityEngine;

// 게임 전역을 관리하는 지속 싱글턴.
public class GameManager : MonoBehaviour
{
    const string FrameRatePrefsKey = "settings.frameRate";
    public const int DefaultFrameRate = 60;

    const string ScreenShakePrefsKey = "settings.screenShake";

    // 프레임 레이트 선택지 단일 진실원. 설정 UI는 이 목록만 보고 그린다.
    public static readonly int[] FrameRateOptions = { 30, 60, 144 };

    public static GameManager Instance { get; private set; }
    public static int CurrentFrameRate { get; private set; } = DefaultFrameRate;
    internal static EGameBootState BootState { get; private set; } = EGameBootState.Booting;

    // 차단 직전 단계. 재시도가 성공했을 때 되돌아갈 자리다.
    static EGameBootState s_stateBeforeBlock = EGameBootState.Booting;

    /// <summary>타격 화면 흔들림 사용 여부. 흔들림에 멀미를 느끼는 사용자를 위한 접근성 옵션이라 기본은 켬.
    /// 판정은 BattleCamera 한 곳에서만 본다 — 호출부(AttackSequence)는 이 값을 몰라야 한다.
    /// GameManager가 소유하는 이유: 프레임 레이트와 같은 "기기/표시 설정" 축이고 영속화 경로도 같다.</summary>
    public static bool ScreenShakeEnabled { get; private set; } = true;

    // 앱 시작 시 씬 로드 전에 자동 실행 — 지속 GameManager 하나를 만든다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoCreate()
    {
        if (Instance != null) return;
        new GameObject(nameof(GameManager)).AddComponent<GameManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        BootAsync().Forget();
    }

    void OnApplicationPause(bool _pause)
    {
        if (_pause)
        {
            Flush();
            return;
        }

        PlayerSaveSync.RetryPending();
    }

    void OnApplicationQuit() => Flush();

    // 전역 서브시스템 부트 초기화. 순서 의존은 여기서 보장한다.
    async UniTaskVoid BootAsync()
    {
        BootState = EGameBootState.Booting;
        s_stateBeforeBlock = EGameBootState.Booting;

        // 모바일 60프레임: 기본 30이라 명시 지정. vSync가 targetFrameRate를 덮어쓰지 않게 끔.
        int t_savedFrameRate = PlayerPrefs.GetInt(FrameRatePrefsKey, DefaultFrameRate);
        SetTargetFrameRate(t_savedFrameRate, false);
        if (t_savedFrameRate != CurrentFrameRate)
        {
            PlayerPrefs.SetInt(FrameRatePrefsKey, CurrentFrameRate);
            PlayerPrefs.Save();
        }

        // 저장된 흔들림 설정 복원(미저장이면 켬). 전투가 열리기 전에 확정돼 있어야 한다.
        SetScreenShake(PlayerPrefs.GetInt(ScreenShakePrefsKey, 1) != 0, false);

        ContentProfileConfig t_profile = ContentProfileConfig.Active;
        var t_mirror = new JsonFileRepository(t_profile.SaveFolder);

        if (SaveSourceMode.Current == ESaveSourceMode.Cloud)
        {
            await BootCloudSaveAsync(t_profile, t_mirror);
            return;
        }

        DataSaveManager.SetRepository(t_mirror);

        // 여기서 빠져나가면 부트 게이트를 열 주체가 없어 로딩이 영원히 끝나지 않는다 — 반드시 상태를 남긴다.
        try
        {
            await DataSaveManager.LoadAsync();  // 프로필별 세이브 로드
        }
        catch (System.Exception t_exception)
        {
            Debug.LogError($"[GameManager] 세이브 로드 실패: {t_exception.Message}\n{t_exception.StackTrace}");
            MarkRecoveryRequired();
            return;
        }

        if (DataSaveManager.IsSaveBlocked)
        {
            BootState = EGameBootState.UpdateRequired;
            Debug.LogError("[GameManager] Boot blocked because the local save requires a newer client.");
            return;
        }

        try
        {
            PlayerSaveSync.Initialize(t_profile.CloudSaveProfileId, DataSaveManager.CloudUploadAllowed);
            BootState = EGameBootState.Syncing;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GameManager] PlayerSaveSync.Initialize failed: {ex.Message}\n{ex.StackTrace}");
            MarkRecoveryRequired();
        }
    }

    // Firestore가 진실원인 부트. PlayerSaveSync는 돌지 않으므로 게이트를 열 주체가 여기밖에 없다 —
    // RecoveryRequired·UpdateRequired만이 게이트 없이 나가도 되는 종료 상태다(BootInstaller·LoadingCoverView가 따로 감지한다).
    // 네트워크 실패는 종료가 아니라 대기다 — 차단 화면이 재시도를 넣어줄 때까지 이 루프가 붙잡고 있는다.
    static async UniTask BootCloudSaveAsync(ContentProfileConfig _profile, JsonFileRepository _mirror)
    {
        // 재시도마다 새로 만들면 revision·payload 캐시와 쓰기 사슬 상태가 어긋난다 — 루프 밖에서 한 번만.
        var t_repository = new FirestoreSaveRepository(_mirror, _profile.CloudSaveProfileId, DataSaveManager.SAVE_KEY);
        DataSaveManager.SetRepository(t_repository);

        while (true)
        {
            await FirebaseAuthService.Instance.InitializeAsync();

            ESaveBootBlockReason t_reason;
            ESaveSourcePrimeResult t_prime = await t_repository.PrimeAsync();

            if (t_prime == ESaveSourcePrimeResult.Ok || t_prime == ESaveSourcePrimeResult.NotFound)
            {
                // LoadAsync보다 먼저 돌아야 한다 — 저널이 서버에 반영된 뒤라야 로드가 이관 후 상태를 읽는다.
                ESaveWriteResult t_journal = await t_repository.ConsumeJournalAsync();
                if (t_journal == ESaveWriteResult.Success) break;

                Debug.LogError($"[GameManager] 종료 저널을 서버에 반영하지 못했다: {t_journal}");
                t_reason = SaveBootBlock.ReasonOf(t_journal);
            }
            else
            {
                Debug.LogError($"[GameManager] 클라우드 세이브를 준비하지 못했다: {t_prime}");

                if (t_prime == ESaveSourcePrimeResult.Unauthenticated && IsAuthFailurePermanent())
                {
                    MarkRecoveryRequired();
                    return;
                }

                t_reason = SaveBootBlock.ReasonOf(t_prime);
            }

            MarkBlockedRetryable(t_reason);
            await BootGate.WaitForRetryAsync();
            MarkBootRetrying();
        }

        try
        {
            await DataSaveManager.LoadAsync();
        }
        catch (System.Exception t_exception)
        {
            Debug.LogError($"[GameManager] 세이브 로드 실패: {t_exception.Message}\n{t_exception.StackTrace}");
            MarkRecoveryRequired();
            return;
        }

        if (DataSaveManager.IsSaveBlocked)
        {
            BootState = EGameBootState.UpdateRequired;
            Debug.LogError("[GameManager] Boot blocked because the cloud save requires a newer client.");
            return;
        }

        BootState = EGameBootState.Syncing;
        BootGate.MarkComplete();
    }

    public static void SetTargetFrameRate(int _frameRate, bool _save = true)
    {
        if (System.Array.IndexOf(FrameRateOptions, _frameRate) < 0)
            _frameRate = DefaultFrameRate;

        CurrentFrameRate = _frameRate;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = _frameRate;

        if (!_save) return;

        PlayerPrefs.SetInt(FrameRatePrefsKey, _frameRate);
        PlayerPrefs.Save();
    }

    public static void SetScreenShake(bool _on, bool _save = true)
    {
        ScreenShakeEnabled = _on;

        if (!_save) return;

        PlayerPrefs.SetInt(ScreenShakePrefsKey, _on ? 1 : 0);
        PlayerPrefs.Save();
    }

    // 앱이 떠날 때 영속화를 flush(모바일 종료 콜백 누락 대비).
    // 종료 콜백은 await 완료를 기다려주지 않으므로 여기만 동기 쓰기를 쓴다.
    void Flush()
    {
        if (BootInstaller.IsSaveDependentInstalled)
            SaveTransaction.CommitBlocking();
        PlayerSaveSync.FlushPending();
    }

    internal static void MarkBootReady()
    {
        if (BootState == EGameBootState.Syncing)
            BootState = EGameBootState.Ready;
    }

    internal static void MarkRecoveryRequired()
    {
        BootState = EGameBootState.RecoveryRequired;
    }

    internal static void MarkUpdateRequired()
    {
        BootState = EGameBootState.UpdateRequired;
    }

    /// <summary>재시도로 풀릴 수 있는 대기 상태로 넘긴다(차단 화면이 여기서 뜬다). 사유를 먼저 남겨
    /// 화면이 상태를 본 순간에는 이미 문구를 고를 수 있게 한다.</summary>
    internal static void MarkBlockedRetryable(ESaveBootBlockReason _reason)
    {
        if (BootState != EGameBootState.BlockedRetryable)
            s_stateBeforeBlock = BootState;

        BootGate.SetBlockReason(_reason);
        BootState = EGameBootState.BlockedRetryable;
    }

    /// <summary>차단이 풀려 부트를 다시 시도한다. 막히기 직전 단계로 되돌린다 —
    /// 무조건 Booting으로 내리면 동기화 뒤에 막힌 경우 MarkBootReady가 Ready로 못 올린다.</summary>
    internal static void MarkBootRetrying()
    {
        BootGate.SetBlockReason(ESaveBootBlockReason.None);
        BootState = s_stateBeforeBlock;
    }

    // 계정이 바뀌었거나 세션이 끊긴 뒤의 인증 실패는 재시도로 풀리지 않는다 —
    // uid를 이미 한 번 잡아둔 상태에서 현재 SDK 사용자와 어긋난 경우가 그것이다(FirebaseAuthService가 재시작을 요구하는 지점).
    static bool IsAuthFailurePermanent()
    {
        FirebaseAuthService t_auth = FirebaseAuthService.Instance;
        bool t_permanent = t_auth.State == EFirebaseAuthState.Failed &&
                           !string.IsNullOrEmpty(t_auth.UserId) &&
                           !t_auth.IsCurrentUserActive;

        if (t_permanent)
            Debug.LogError($"[GameManager] 인증이 영구 실패 상태다: {t_auth.LastError}");

        return t_permanent;
    }
}
