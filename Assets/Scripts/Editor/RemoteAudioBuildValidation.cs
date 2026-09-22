using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 시작 씬의 직접 참조가 돌아오면 원격 사운드가 APK에도 실린다. 앱·리소스 빌드 전에 둘 다 검사한다.
public sealed class RemoteAudioBuildValidation : IPreprocessBuildWithReport
{
    const string GroupName = "RemoteAudio";
    const string CatalogPath = "Assets/SO/RuntimeContentCatalog.asset";
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport _report) => Validate();

    public static void Validate()
    {
        var t_settings = AddressableAssetSettingsDefaultObject.Settings;
        var t_group = t_settings != null ? t_settings.FindGroup(GroupName) : null;
        var t_catalog = AssetDatabase.LoadAssetAtPath<RuntimeContentCatalog>(CatalogPath);
        if (t_group == null || t_catalog == null)
            throw new BuildFailedException("RemoteAudio 그룹과 RuntimeContentCatalog가 필요합니다.");
        t_catalog.Validate();

        var t_bundle = t_group.GetSchema<BundledAssetGroupSchema>();
        var t_update = t_group.GetSchema<ContentUpdateGroupSchema>();
        if (t_bundle == null || t_update == null || !t_bundle.IncludeInBuild || t_update.StaticContent ||
            !t_bundle.IncludeLabelsInCatalog || !t_bundle.UseAssetBundleCache ||
            t_bundle.BundleMode != BundledAssetGroupSchema.BundlePackingMode.PackSeparately ||
            t_bundle.BuildPath.Id != t_settings.RemoteCatalogBuildPath.Id ||
            t_bundle.LoadPath.Id != t_settings.RemoteCatalogLoadPath.Id ||
            !t_bundle.LoadPath.GetValue(t_settings).StartsWith("https://", StringComparison.Ordinal))
            throw new BuildFailedException("RemoteAudio는 Remote 경로·Pack Separately·Can Change Post Release 설정이어야 합니다.");

        string[] t_configs = {
            AssetDatabase.GetAssetPath(t_catalog.soundConfig),
            AssetDatabase.GetAssetPath(t_catalog.outgameSoundBank)
        };
        var t_expected = new HashSet<string>(t_configs, StringComparer.Ordinal);
        foreach (string t_path in AssetDatabase.GetDependencies(t_configs, true))
            if (AssetDatabase.GetMainAssetTypeAtPath(t_path) == typeof(AudioClip)) t_expected.Add(t_path);

        foreach (string t_path in t_expected)
        {
            AddressableAssetEntry t_entry = t_settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(t_path));
            if (t_entry == null || t_entry.parentGroup != t_group || !t_entry.labels.Contains(GroupName))
                throw new BuildFailedException("사운드 원격 등록/라벨 누락: " + t_path);
        }
        foreach (AddressableAssetEntry t_entry in t_group.entries)
            if (!t_expected.Contains(t_entry.AssetPath))
                throw new BuildFailedException("사용하지 않는 RemoteAudio 항목: " + t_entry.AssetPath);

        var t_localRoots = new List<string>();
        foreach (EditorBuildSettingsScene t_scene in EditorBuildSettings.scenes)
            if (t_scene.enabled) t_localRoots.Add(t_scene.path);
        foreach (string t_path in AssetDatabase.GetAllAssetPaths())
            if (t_path.Contains("/Resources/") && !AssetDatabase.IsValidFolder(t_path))
                t_localRoots.Add(t_path);
        foreach (UnityEngine.Object t_asset in PlayerSettings.GetPreloadedAssets())
            if (t_asset != null) t_localRoots.Add(AssetDatabase.GetAssetPath(t_asset));
        foreach (string t_path in AssetDatabase.GetDependencies(t_localRoots.ToArray(), true))
            if (t_expected.Contains(t_path))
                throw new BuildFailedException("앱 내장 자산에서 원격 사운드를 직접 참조합니다: " + t_path);

        Debug.Log($"[RemoteAudio] 설정 2개·클립 {t_expected.Count - 2}개 원격 등록 및 앱 내장 참조 검사 통과.");
    }
}
