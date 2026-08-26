using System;
using UnityEngine;

/// <summary>InitializationInstaller가 없는 에디터 테스트 씬에서 카드 표·아트 캐시 계약을 명시적으로 준비한다.</summary>
public static class CardStandaloneInitializer
{
    public static bool Ensure(CardRegistry _registry, SynergyRegistry _synergyRegistry = null)
    {
        if (CardCatalog.IsReady) return true;

#if UNITY_EDITOR
        if (_registry == null)
        {
            string[] t_guids = UnityEditor.AssetDatabase.FindAssets("t:CardRegistry");
            if (t_guids.Length == 1)
                _registry = UnityEditor.AssetDatabase.LoadAssetAtPath<CardRegistry>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(t_guids[0]));
        }
        if (_synergyRegistry == null)
        {
            string[] t_guids = UnityEditor.AssetDatabase.FindAssets("t:SynergyRegistry");
            if (t_guids.Length == 1)
                _synergyRegistry = UnityEditor.AssetDatabase.LoadAssetAtPath<SynergyRegistry>(
                    UnityEditor.AssetDatabase.GUIDToAssetPath(t_guids[0]));
        }
#endif

        if (_registry == null)
        {
            Debug.LogError("[CardStandaloneInitializer] CardRegistry가 없다.");
            return false;
        }
        if (_synergyRegistry == null)
        {
            Debug.LogError("[CardStandaloneInitializer] SynergyRegistry가 없다.");
            return false;
        }

        try
        {
            ContentProfileConfig t_profile = ContentProfileConfig.Active;
            SpecSource.Init();
            CardCatalog.SetSource(_registry.All, _synergyRegistry, t_profile.RunMode, t_profile.IncludeTestCards);
            return true;
        }
        catch (Exception t_exception)
        {
            Debug.LogException(t_exception);
            return false;
        }
    }
}
