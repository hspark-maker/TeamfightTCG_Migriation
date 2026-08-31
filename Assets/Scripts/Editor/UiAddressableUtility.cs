using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

static class UiAddressableUtility
{
    const string UiLabel = "UIPrefab";
    const string DeprecatedUiLabel = "UIElements";

    public static bool TryResolveAddress(string _assetPath, out string _address)
    {
        _address = null;

        GameObject t_prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_assetPath);
        if (t_prefab == null)
        {
            Debug.LogError($"[UiAddressableUtility] 프리팹을 못 찾음: {_assetPath}");
            return false;
        }

        PooledUIBase t_pooledUi = t_prefab.GetComponent<PooledUIBase>();
        SingletonOverlayBase t_singletonOverlay = t_prefab.GetComponent<SingletonOverlayBase>();

        if (t_pooledUi != null)
            _address = t_pooledUi.GetType().Name;
        else if (t_singletonOverlay != null)
            _address = t_singletonOverlay.GetType().Name;
        else
        {
            Debug.LogError($"[UiAddressableUtility] 루트 UI 컴포넌트가 없음: {_assetPath}");
            return false;
        }

        return true;
    }

    [MenuItem("Tools/Addressables/Normalize UI Addresses")]
    static void NormalizeUiAddresses()
    {
        AddressableAssetSettings t_settings = AddressableAssetSettingsDefaultObject.Settings;
        if (t_settings == null)
        {
            Debug.LogError("[UiAddressableUtility] Addressables 설정이 없다.");
            return;
        }

        List<AddressableAssetEntry> t_entries = new List<AddressableAssetEntry>();
        List<string> t_addresses = new List<string>();
        HashSet<string> t_uniqueAddresses = new HashSet<string>();

        foreach (AddressableAssetGroup t_group in t_settings.groups)
        {
            if (t_group == null) continue;

            foreach (AddressableAssetEntry t_entry in t_group.entries)
            {
                if (t_entry == null || t_entry.labels == null || !t_entry.labels.Contains(UiLabel)) continue;

                if (!TryResolveAddress(t_entry.AssetPath, out string t_address))
                {
                    Debug.LogError("[UiAddressableUtility] 주소 정규화를 중단한다.");
                    return;
                }

                if (!t_uniqueAddresses.Add(t_address))
                {
                    Debug.LogError($"[UiAddressableUtility] 중복 UI 주소라 정규화를 중단한다: {t_address}");
                    return;
                }

                t_entries.Add(t_entry);
                t_addresses.Add(t_address);
            }
        }

        int t_changed = 0;
        for (int t_i = 0; t_i < t_entries.Count; t_i++)
        {
            if (t_entries[t_i].address == t_addresses[t_i]) continue;

            t_entries[t_i].address = t_addresses[t_i];
            t_changed++;
        }

        bool t_removedLabel = t_settings.GetLabels().Contains(DeprecatedUiLabel);
        if (t_removedLabel) t_settings.RemoveLabel(DeprecatedUiLabel, false);

        if (t_changed > 0 || t_removedLabel)
        {
            t_settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();
        }

        Debug.Log($"[UiAddressableUtility] 주소 {t_changed}건 정규화, 전체 {t_entries.Count}건, " +
                  $"'{DeprecatedUiLabel}' 라벨 제거: {t_removedLabel}");
    }
}
