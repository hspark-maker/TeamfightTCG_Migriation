using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

/// <summary>AttackEffect SO를 규칙 주소로 Addressables에 등록·갱신한다.</summary>
static class AttackEffectAddressableMigration
{
    const string Root = "Assets/SO/Cards/AttackEffects";
    const string GroupName = "AttackEffects";

    [MenuItem("Tools/Assets/Cards/Sync Attack Effects To Addressables")]
    static void Run()
    {
        AddressableAssetSettings t_settings = AddressableAssetSettingsDefaultObject.Settings;
        if (t_settings == null) return;

        AddressableAssetGroup t_group = t_settings.FindGroup(GroupName) ??
            t_settings.CreateGroup(GroupName, false, false, false, null,
                typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        if (t_group == null) return;

        bool t_changed = false;
        foreach (string t_guid in AssetDatabase.FindAssets("t:AttackEffect", new[] { Root }))
        {
            string t_path = AssetDatabase.GUIDToAssetPath(t_guid);
            AttackEffect t_effect = AssetDatabase.LoadAssetAtPath<AttackEffect>(t_path);
            if (t_effect == null) continue;

            string t_address = AttackEffectCache.AddressOf(t_effect);
            AddressableAssetEntry t_entry = t_settings.CreateOrMoveEntry(t_guid, t_group);
            if (t_entry.address != t_address)
            {
                t_entry.address = t_address;
                t_changed = true;
            }
            t_entry.SetLabel(AttackEffectCache.Label, true, true);
        }

        if (!t_changed && t_group.entries.Count == 0) return;
        t_settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
        AssetDatabase.SaveAssets();
        UnityEngine.Debug.Log($"[AttackEffectMigration] {t_group.entries.Count}개 AttackEffect Addressables 등록 완료.");
    }
}
