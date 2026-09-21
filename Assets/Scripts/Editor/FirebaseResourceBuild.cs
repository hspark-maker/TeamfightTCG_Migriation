using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

// 리소스만 빌드한다. 스펙 임포터·앱 빌드·서버 명령은 실행하지 않는다.
public static class FirebaseResourceBuild
{
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("플레이를 종료한 뒤 리소스를 빌드하세요.");
        if (!Regex.IsMatch(PlayerSettings.bundleVersion, @"^[A-Za-z0-9][A-Za-z0-9._-]*$"))
            throw new InvalidOperationException("앱 버전은 영문·숫자·점·밑줄·하이픈만 사용할 수 있습니다.");

        RemoteAudioBuildValidation.Validate();

        var t_settings = AddressableAssetSettingsDefaultObject.Settings;
        if (t_settings == null || !t_settings.BuildRemoteCatalog || !t_settings.EnableJsonCatalog)
            throw new InvalidOperationException("원격 JSON 카탈로그 설정이 필요합니다.");
#if !ENABLE_JSON_CATALOG
        throw new InvalidOperationException("현재 플랫폼의 Scripting Define Symbols에 ENABLE_JSON_CATALOG를 추가하고 컴파일이 끝난 뒤 다시 빌드하세요.");
#else

        string t_loadPath = t_settings.RemoteCatalogLoadPath.GetValue(t_settings);
        if (!t_loadPath.StartsWith("https://bm-cardbattle-assets.web.app/", StringComparison.Ordinal))
            throw new InvalidOperationException("Firebase 리소스 배포 주소를 확인하세요: " + t_loadPath);

        // 공통 MonoScript·내장 셰이더 번들도 리소스 업데이트를 따라야 한다.
        var t_sharedBundles = t_settings.DefaultGroup.GetSchema<BundledAssetGroupSchema>();
        var t_sharedUpdates = t_settings.DefaultGroup.GetSchema<ContentUpdateGroupSchema>();
        if (t_sharedBundles == null || t_sharedUpdates == null || t_sharedUpdates.StaticContent ||
            t_sharedBundles.LoadPath.GetValue(t_settings).TrimEnd('/') != t_loadPath.TrimEnd('/') ||
            t_sharedBundles.BuildPath.GetValue(t_settings).TrimEnd('/') !=
                t_settings.RemoteCatalogBuildPath.GetValue(t_settings).TrimEnd('/'))
            throw new InvalidOperationException("기본 그룹의 공통 번들은 Remote Build/Load Path와 Can Change Post Release 설정이 필요합니다.");

        UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.BuildPlayerContent(out var t_result);
        if (!string.IsNullOrEmpty(t_result.Error)) throw new InvalidOperationException(t_result.Error);

        // 콘텐츠 업데이트에 필요한 상태 파일은 공개 Hosting 폴더 밖에 보존한다.
        string t_statePath = ContentUpdateScript.GetContentStateDataPath(false, t_settings);
        if (File.Exists(t_statePath))
        {
            string t_archive = Path.Combine("Build", "AddressablesState", PlayerSettings.bundleVersion,
                EditorUserBuildSettings.activeBuildTarget.ToString(), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(t_archive);
            File.Copy(t_statePath, Path.Combine(t_archive, "addressables_content_state.bin"));
            Debug.Log("[FirebaseResourceBuild] 콘텐츠 업데이트 상태 파일: " + t_archive);
        }
        Debug.Log("[FirebaseResourceBuild] 리소스 빌드 완료: " + t_loadPath +
                  "\n배포: firebase deploy --only hosting --project bm-cardbattle");
#endif
    }
}
