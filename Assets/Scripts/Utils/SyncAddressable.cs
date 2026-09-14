using UnityEngine;

/// <summary>초기화 없는 에디터 단독 씬용 주소 조회. HTTP 동기 대기는 하지 않는다.
/// 앱에서는 UiPrefabCache가 카탈로그·폰트를 비동기 선로드해 소비자에 주입한다.</summary>
public static class SyncAddressable
{
    /// <summary>_address 에셋을 동기로 읽는다. 못 읽으면 null — 폴백은 호출부가 책임진다.</summary>
    public static T Load<T>(string _address) where T : UnityEngine.Object
    {
#if UNITY_EDITOR
        var t_settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        if (t_settings != null)
        {
            foreach (var t_group in t_settings.groups)
            {
                if (t_group == null) continue;
                foreach (var t_entry in t_group.entries)
                    if (t_entry.address == _address)
                        return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(t_entry.AssetPath);
            }
        }
#endif
        Debug.LogError($"[SyncAddressable] '{_address}' must be preloaded before use.");
        return null;
    }
}
