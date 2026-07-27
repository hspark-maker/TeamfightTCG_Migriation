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

    AsyncOperationHandle<IList<GameObject>> uiHandle;

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
        DontDestroyOnLoad(gameObject);
        GameTiming.SetConfig(this.battleTimingConfig);
        RewardService.SetConfig(this.battleRewardConfig);
        RankManager.SetConfig(this.rankConfig);
        return true;
    }

    public async UniTask Initialization()
    {
        await LoadUIPrefab();
        LogUtil.Log("All Good");
    }

    async UniTask LoadUIPrefab()
    {
        this.uiPrefabs = new Dictionary<Type, GameObject>();
        this.uiHandle = Addressables.LoadAssetsAsync<GameObject>("UIPrefab", (t_prefab) =>
        {
            var t_ui = t_prefab.GetComponent<PooledUIBase>();
            if (t_ui != null)
                this.uiPrefabs[t_ui.GetType()] = t_prefab;
        });
        await uiHandle.ToUniTask();
    }


    #region Get

    public GameObject GetUI<T>() where T : PooledUIBase
    {
        if (this.uiPrefabs.TryGetValue(typeof(T), out var _value))
            return _value;

        LogUtil.Log($"UI Prefab Not Found: {typeof(T).Name}");
        return null;
    }

    #endregion
}
