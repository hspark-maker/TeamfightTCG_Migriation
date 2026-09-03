using System;
using UnityEngine;

/// <summary>초기화(InitializationRunner)가 없는 독립 테스트 씬에서 표 기반 카드 카탈로그를 준비한다.</summary>
public static class CardStandaloneInitializer
{
    public static bool Ensure(SynergyRegistry _synergyRegistry = null)
    {
        if (CardCatalog.IsReady) return true;

#if UNITY_EDITOR
        if (_synergyRegistry == null)
        {
            string[] t_guids = UnityEditor.AssetDatabase.FindAssets("t:SynergyRegistry");
            if (t_guids.Length == 1)
                _synergyRegistry = UnityEditor.AssetDatabase.LoadAssetAtPath<SynergyRegistry>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(t_guids[0]));
        }
#endif
        if (_synergyRegistry == null)
        {
            Debug.LogError("[CardStandaloneInitializer] SynergyRegistry가 없다.");
            return false;
        }

        try
        {
            ContentProfileConfig t_profile = ContentProfileConfig.Active;
            SpecSource.Init();
            CardCatalog.SetSource(_synergyRegistry, t_profile.IncludeTestCards);
            return true;
        }
        catch (Exception t_exception)
        {
            Debug.LogException(t_exception);
            return false;
        }
    }
}
