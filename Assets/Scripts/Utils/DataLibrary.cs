using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.AddressableAssets;
using System;
using Cysharp.Threading.Tasks;

public class DataLibrary : MonoBehaviour
{
    public static DataLibrary instance;

    public Dictionary<Type, GameObject> uiPrefabs;

    [SerializeField] public KeywordIconConfig keywordIconConfig;
    [SerializeField] BattleTimingConfig battleTimingConfig;   // 미배선 시 GameTiming 기본값 fallback
    [SerializeField] BattleReward battleRewardConfig;         // 미배선 시 RewardService 기본값 fallback
    [SerializeField] RankConfig rankConfig;                   // 미배선 시 RankManager 기본 티어 테이블 fallback
    [SerializeField] BattleVfxLibrary battleVfxLibrary;       // 규칙 기반 연출 배선(전투 씬은 GameInitializer가 따로 주입)

    AsyncOperationHandle<IList<GameObject>> uiHandle;

    bool m_loaded;
    bool m_failed;

    // 부트 로딩 완료 여부. 시작 화면(LoadingCoverView)이 커버를 걷는 기준.
    public static bool IsLoaded => instance != null && instance.m_loaded;
    public static bool HasFailed => instance != null && instance.m_failed;

    // 부트 로딩 진행도(0~1). 인스턴스가 아직 없으면 0 — 진행도의 단일 진실원.
    public static float LoadProgress
    {
        get
        {
            if (instance == null)  return 0f;
            if (instance.m_loaded) return 1f;
            return instance.uiHandle.IsValid() ? instance.uiHandle.PercentComplete : 0f;
        }
    }

    public void Awake()
    {
        if (!InitializeSingleton()) return;
        Initialization().Forget();
    }

    bool InitializeSingleton()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return false;
        }

        instance = this;
        DontDestroyOnLoad(transform.root.gameObject);   // 부트 프리팹의 자식이라 루트 기준(단독 배치면 자기 자신)
        GameTiming.SetConfig(this.battleTimingConfig);
        RewardService.SetConfig(this.battleRewardConfig);
        RankManager.SetConfig(this.rankConfig);
        RankRewardManager.SetConfig(this.rankConfig); // 보상 테이블이 티어 테이블과 같은 SO라 필드를 재사용한다(이중 진실원 방지).

        // "튜토 졸업 = 첫 티어 도달"이 불변식이다. 완료 낙인만 있고 미도달인 세이브(레거시 흡수·구 버전)를
        // 티어 테이블 주입 직후에 맞춘다. 진입 연출은 싣지 않는다 — 실제로 졸업하는 순간에만 띄운다.
        BattleVfx.SetLibrary(this.battleVfxLibrary);
        return true;
    }

    public async UniTask Initialization()
    {
        try
        {
            await LoadUIPrefab();
            this.m_loaded = true;
            LogUtil.Log("All Good");
        }
        catch (Exception t_exception)
        {
            this.m_failed = true;
            Debug.LogException(t_exception);
        }
    }

    async UniTask LoadUIPrefab()
    {
        this.uiPrefabs = new Dictionary<Type, GameObject>();
        this.uiHandle = Addressables.LoadAssetsAsync<GameObject>("UIPrefab", (t_prefab) =>
        {
            Component t_ui = t_prefab.GetComponent<PooledUIBase>();
            if (t_ui == null) t_ui = t_prefab.GetComponent<SingletonOverlayBase>();
            if (t_ui != null) RegisterUiPrefab(t_ui.GetType(), t_prefab);
        });
        await uiHandle.ToUniTask();
        if (uiHandle.Status != AsyncOperationStatus.Succeeded)
            throw new InvalidOperationException("UIPrefab Addressables load failed.");
    }


    #region Get

    public GameObject GetUI<T>() where T : PooledUIBase
    {
        if (this.uiPrefabs.TryGetValue(typeof(T), out var _value))
            return _value;

        LogUtil.Log($"UI Prefab Not Found: {typeof(T).Name}");
        return null;
    }

    public bool TryGetUiPrefab(Type _type, out GameObject _prefab)
    {
        _prefab = null;
        return this.uiPrefabs != null &&
               this.uiPrefabs.TryGetValue(_type, out _prefab) &&
               _prefab != null;
    }

    void RegisterUiPrefab(Type _type, GameObject _prefab)
    {
        if (this.uiPrefabs.TryGetValue(_type, out GameObject t_existing) &&
            t_existing != _prefab)
        {
            Debug.LogError(
                $"[DataLibrary] UIPrefab 타입 중복: {_type.Name} " +
                $"({t_existing.name}, {_prefab.name})");
            return;
        }

        this.uiPrefabs[_type] = _prefab;
    }

    #endregion
}
