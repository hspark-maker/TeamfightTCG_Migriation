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
    // 실질 복구선은 FlushLocal()이 남긴 로컬 캐시와, 다음 부트의 AdoptUnsyncedCache다.
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

        // 세이브의 진실원은 클라우드 문서다 — 여기서는 캐시 매체만 꽂고, 채택은 PlayerSaveCloud가 비동기로 한다.
        ContentProfileConfig t_profile = ContentProfileConfig.Active;
        DataSaveManager.SetRepository(new JsonFileRepository(t_profile.SaveFolder));

        try
        {
            FirebaseManager.Register(new BattleContentFirebaseModule());
            FirebaseManager.Register(new PlayerSaveFirebaseModule());
            FirebaseManager.Register(new MatchResultFirebaseModule());
            GameInitialization.SetState(EGameInitState.SyncingSave);
            FirebaseManager.Initialize(t_profile.CloudEnvId);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GameManager] FirebaseManager.Initialize failed: {ex.Message}\n{ex.StackTrace}");
            GameInitialization.MarkRecoveryRequired();
        }
    }

    /// <summary>부트가 복구 화면에서 멈췄을 때 Firebase를 처음부터 다시 태운다(씬 재로드 없음).</summary>
    // 여기에 두는 이유는 envId다 — 콘텐츠 프로필을 아는 곳이 이 클래스뿐이라, UI가 프로필을 직접 읽지 않게 한다.
    public static void RetryInitialize()
    {
        try
        {
            FirebaseManager.Reinitialize(ContentProfileConfig.Active.CloudEnvId);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[GameManager] FirebaseManager.Reinitialize failed: {ex.Message}\n{ex.StackTrace}");
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
    // 그때 잔액 flush까지 멈추면 이미 번 재화가 로컬 캐시에도 안 남는다.
    void FlushLocal()
    {
        if (SaveDependentManagersStep.IsInstalled)
            CurrencyManager.Save();
    }
}
