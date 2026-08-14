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
        Boot();
    }

    void OnApplicationPause(bool _pause)
    {
        if (_pause) Flush();
    }

    void OnApplicationQuit() => Flush();

    // 전역 서브시스템 부트 초기화. 순서 의존은 여기서 보장한다.
    void Boot()
    {
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
        DataSaveManager.SetRepository(new JsonFileRepository(t_profile.SaveFolder));
        DataSaveManager.Load();             // 프로필별 세이브 로드

        // 디버그 되감기 예약 소비(1단) — 매니저 Init들이 슬롯을 캐싱하기 전이어야 갈아끼운 슬롯이 반영된다.
        // 예약이 없으면 아무 일도 없다. 지급 재생(2단)은 BootInstaller 끝에서 이어진다.
        OutgameTutorialRewind.ApplyWipeIfScheduled();

        CurrencyManager.Init();             // 세이브 → 재화 메모리 캐싱
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
    void Flush()
    {
        CurrencyManager.Save();
    }
}
