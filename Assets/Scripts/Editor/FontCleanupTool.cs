using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 빌드 용량 정리 도구. 메뉴 Tools/Build/…
///
/// 배경: 프로젝트의 한글 TMP 폰트들은 이미 <b>Dynamic 모드</b>인데도 30~38MB다.
/// Static으로 한 번 구웠던 아틀라스/글리프 테이블이 에셋 안에 그대로 남아 있기 때문이고,
/// Dynamic은 런타임에 필요한 글자만 생성하므로 그 데이터는 전부 죽은 무게다.
/// TMP 인스펙터의 "Clear Dynamic Data"와 같은 동작을 코드로 일괄 실행한다.
///
/// 안전 조건: Dynamic 모드 + 소스 폰트(.ttf/.otf)가 프로젝트에 존재해야 한다.
/// 둘 중 하나라도 아니면 글자가 통째로 안 나오므로 **건너뛰고 로그만 남긴다**.
/// </summary>
public static class FontCleanupTool
{
    // 통합 후 남길 폰트(= 실제 참조가 있는 것들).
    static readonly string[] KeepFonts =
    {
        "Assets/Fonts/Jalnan2 SDF.asset",
        "Assets/Fonts/uK4mTd9JFejCoAXNZyXHV6glsI/Jalnan2/Jalnan2TTF SDF_outline.asset",
        "Assets/Resources/Fonts/MalgunGothic_TMP.asset",
        "Assets/Fonts/ONE Mobile POP OTF SDF.asset",
    };

    // 통합으로 참조가 0이 된 폰트(삭제 대상).
    static readonly string[] UnusedFonts =
    {
        "Assets/Fonts/Jalnan2TTF SDF.asset",
        "Assets/Fonts/JalnanGothicTTF SDF.asset",
        "Assets/Fonts/JalnanGothic SDF.asset",
    };

    [MenuItem("Tools/Build/1. Clear Baked Font Atlas (Dynamic)")]
    public static void ClearBakedAtlas()
    {
        var t_done = new List<string>();
        foreach (string t_path in KeepFonts)
        {
            var t_font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(t_path);
            if (t_font == null) { Debug.LogWarning($"[FontCleanup] 없음: {t_path}"); continue; }

            if (t_font.atlasPopulationMode != AtlasPopulationMode.Dynamic)
            {
                Debug.LogWarning($"[FontCleanup] Static 모드라 건너뜀(지우면 글자가 사라진다): {t_path}");
                continue;
            }
            if (t_font.sourceFontFile == null)
            {
                Debug.LogWarning($"[FontCleanup] 소스 폰트 없음 → Dynamic 생성 불가라 건너뜀: {t_path}");
                continue;
            }

            long t_before = FileSize(t_path);
            t_font.ClearFontAssetData(setAtlasSizeToZero: true);
            EditorUtility.SetDirty(t_font);
            t_done.Add($"{System.IO.Path.GetFileName(t_path)}  {t_before / 1024 / 1024}MB → (저장 후 확인)");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        foreach (string t_line in t_done) Debug.Log($"[FontCleanup] 아틀라스 비움: {t_line}");
        Debug.Log($"[FontCleanup] 완료 {t_done.Count}건");
    }

    [MenuItem("Tools/Build/2. Delete Unused Jalnan Fonts")]
    public static void DeleteUnused()
    {
        int t_n = 0;
        foreach (string t_path in UnusedFonts)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(t_path) == null)
            {
                Debug.Log($"[FontCleanup] 이미 없음: {t_path}");
                continue;
            }
            // 남은 참조가 있으면 지우지 않는다 — 통합이 덜 끝난 상태에서 지우면 글자가 통째로 깨진다.
            if (HasReference(t_path))
            {
                Debug.LogError($"[FontCleanup] 아직 참조가 남아 삭제 취소: {t_path}");
                continue;
            }
            if (AssetDatabase.DeleteAsset(t_path)) t_n++;
        }
        AssetDatabase.Refresh();
        Debug.Log($"[FontCleanup] 폰트 {t_n}개 삭제");
    }

    /// <summary>이 에셋을 참조하는 프리팹/씬이 있는가. 폰트 자신(및 그 폴더)은 제외.</summary>
    static bool HasReference(string _assetPath)
    {
        string t_guid = AssetDatabase.AssetPathToGUID(_assetPath);
        if (string.IsNullOrEmpty(t_guid)) return false;

        foreach (string t_g in AssetDatabase.FindAssets("t:Prefab t:Scene"))
        {
            string t_p = AssetDatabase.GUIDToAssetPath(t_g);
            if (t_p.StartsWith("Assets/PurchasedAssets") || t_p.StartsWith("Assets/Layer Lab")
                || t_p.StartsWith("Assets/GUIPackCartoon")) continue;

            foreach (string t_dep in AssetDatabase.GetDependencies(t_p, true))
                if (t_dep == _assetPath) return true;
        }
        return false;
    }

    static long FileSize(string _path)
    {
        var t_info = new System.IO.FileInfo(_path);
        return t_info.Exists ? t_info.Length : 0L;
    }
}
