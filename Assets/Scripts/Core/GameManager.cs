using UnityEngine;

// 게임 전역을 관리하는 지속 싱글턴.
public class GameManager : MonoBehaviour
{
    const string FrameRatePrefsKey = "settings.frameRate";
    public const int DefaultFrameRate = 60;

    // 프레임 레이트 선택지 단일 진실원. 설정 UI는 이 목록만 보고 그린다.
    public static readonly int[] FrameRateOptions = { 30, 60, 144 };

    public static GameManager Instance { get; private set; }
    public static int CurrentFrameRate { get; private set; } = DefaultFrameRate;

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

        DataSaveManager.Load();             // 세이브 로드
        OutgameTutorialProgress.Init();     // 튜토리얼 진행도 판정(레거시 세이브 마이그레이션 포함)
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

    // 앱이 떠날 때 영속화를 flush(모바일 종료 콜백 누락 대비).
    void Flush()
    {
        CurrencyManager.Save();
        // 도감 진행도 스냅샷(현재 누적·정산시각). 미초기화 시 no-op이라 빈 캐시로 덮어쓸 위험 없음.
        CollectionProductionManager.Save();
    }
}
