using System;
using UnityEngine;

// UI 프리팹 색인은 UiPrefabCache(static)가 갖는다 — 여기 남은 것은 키워드 아이콘 표와,
// 기존 호출부(DataLibrary.instance.GetUI<T>() 등)를 깨지 않기 위한 얇은 전달뿐이다.
public class DataLibrary : MonoBehaviour
{
    public static DataLibrary instance;

    [SerializeField] public KeywordIconConfig keywordIconConfig;

    public void Awake()
    {
        InitializeSingleton();
    }

    bool InitializeSingleton()
    {
        if (instance != null)
        {
            Destroy(gameObject);
            return false;
        }

        instance = this;
        DontDestroyOnLoad(transform.root.gameObject);   // 초기화 프리팹의 자식이라 루트 기준(단독 배치면 자기 자신)
        return true;
    }

    #region Get

    public GameObject GetUI<T>() where T : PooledUIBase => UiPrefabCache.Get<T>();

    public bool TryGetUiPrefab(Type _type, out GameObject _prefab) => UiPrefabCache.TryGet(_type, out _prefab);

    #endregion
}
