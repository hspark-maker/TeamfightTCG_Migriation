using UnityEngine;

// 게임 전역을 관리하는 지속 싱글턴.
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

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
        DataSaveManager.Load();     // 세이브 로드
        CurrencyManager.Init();     // 세이브 → 재화 메모리 캐싱
    }

    // 앱이 떠날 때 영속화를 flush(모바일 종료 콜백 누락 대비).
    void Flush()
    {
        CurrencyManager.Save();
        // 도감 진행도 스냅샷(현재 누적·정산시각). 미초기화 시 no-op이라 빈 캐시로 덮어쓸 위험 없음.
        CollectionProductionManager.Save();
    }
}
