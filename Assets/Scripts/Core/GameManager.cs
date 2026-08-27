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
        Initialize();
    }

    void OnApplicationPause(bool _pause)
    {
        if (_pause)
        {
            FlushAsync().Forget();
            return;
        }

        FirebaseManager.RetryPending();
    }

    // 종료 콜백에는 await 창이 없어 이 킥의 Firestore 트랜잭션은 착지하지 못한다.
    // 로컬 캐시가 없어 복구선도 없다 — 미업로드분은 여기서 유실된다.
    void OnApplicationQuit()
    {
        FlushLocal();
        FirebaseManager.FlushPendingAsync().Forget();
    }

    // 전역 서브시스템 초기화. 순서 의존은 여기서 보장한다.
    void Initialize()
    {
        GameInitialization.SetState(EGameInitState.Initializing);

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

        try
        {
            // 에뮬레이터 설정을 한 번만 읽어 세 백엔드에 같은 값을 흘린다 — 창구가 갈리면 함수만 로컬로 가는 상태가 재발한다.
            FirebaseEmulatorConfig t_emulators = t_profile.FirebaseEmulators;

            // 켜기로 저작했는데 주소가 틀렸다면 끈 것이 아니라 못 켠 것이다 — 폴백으로 넘기면
            // 에뮬레이터를 켠 줄 알고 프로덕션 문서에 진짜 쓰기가 나간다.
            if (t_emulators.IsMisconfigured)
                throw new System.InvalidOperationException(
                    "Firebase 에뮬레이터 설정이 잘못됐습니다: " + t_emulators.Error);

            // 등록 순서 = 초기화 순서. 채택 창구가 세이브 모듈보다 먼저 서야 부트 시점부터 산다.
            FirebaseManager.Register(new CallableFirebaseModule(t_emulators.FunctionsOrigin));
            FirebaseManager.Register(new BattleContentFirebaseModule());
            FirebaseManager.Register(new PlayerSaveFirebaseModule());
            GameInitialization.SetState(EGameInitState.SyncingSave);
            FirebaseManager.Initialize(t_profile.CloudEnvId, t_emulators);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GameManager] FirebaseManager.Initialize failed: {ex.Message}\n{ex.StackTrace}");
            GameInitialization.MarkRecoveryRequired();
        }
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

    // 앱이 백그라운드로 갈 때 영속화를 flush. 업로드 완료까지 기다린다(안드로이드는 프로세스가 살아 PlayerLoop이 돈다).
    async UniTaskVoid FlushAsync()
    {
        FlushLocal();
        await FirebaseManager.FlushPendingAsync();
    }

    // 게이트가 아니라 매니저 설치 여부로 판정한다 — 세션 중 복구 요구가 뜨면 IsReady가 false로 떨어지는데,
    // 그때 잔액 flush까지 멈추면 이미 번 재화가 업로드 대기열에도 못 들어간다.
    void FlushLocal()
    {
        if (InitializationInstaller.IsSaveDependentInstalled)
            CurrencyManager.Save();
    }
}
