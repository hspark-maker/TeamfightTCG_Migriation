using UnityEngine;

/// <summary>부트 초기화(InitializationRunner)가 없는 독립 테스트 씬에서 성장 매니저를 준비한다.
/// 설치기의 배선 순서를 그대로 따른다 — 곡선 조회가 Config를 쓰므로 SetConfig가 Init보다 먼저다.</summary>
public static class GrowthStandaloneInitializer
{
    public static bool Ensure(CardGrowthConfig _cardConfig = null, KeywordGrowthConfig _keywordConfig = null)
    {
        if (CardGrowthManager.IsReady && CardGrowthManager.IsConfigReady && KeywordGrowthManager.IsReady)
            return true;

#if UNITY_EDITOR
        if (_cardConfig == null) _cardConfig = FindSingle<CardGrowthConfig>();
        if (_keywordConfig == null) _keywordConfig = FindSingle<KeywordGrowthConfig>();
#endif

        // CardGrowthConfig가 없으면 IsConfigReady가 false로 남아 멀티 성장 스냅샷이 만들어지지 않는다.
        if (_cardConfig == null)
        {
            Debug.LogError("[GrowthStandaloneInitializer] CardGrowthConfig를 찾지 못했다. 인스펙터에 직접 배선해라.");
            return false;
        }

        KeywordGrowthManager.SetConfig(_keywordConfig);
        CardGrowthManager.SetConfig(_cardConfig);

        // Init은 세이브 채택 이후에 부른다 — DataSaveManager.Data를 그대로 캐싱한다.
        KeywordGrowthManager.Init();
        CardGrowthManager.Init();
        return true;
    }

#if UNITY_EDITOR
    static T FindSingle<T>() where T : ScriptableObject
    {
        string[] t_guids = UnityEditor.AssetDatabase.FindAssets($"t:{typeof(T).Name}");
        if (t_guids.Length != 1) return null;
        return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(UnityEditor.AssetDatabase.GUIDToAssetPath(t_guids[0]));
    }
#endif
}
