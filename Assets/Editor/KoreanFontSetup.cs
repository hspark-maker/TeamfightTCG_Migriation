using UnityEngine;
using UnityEditor;
using TMPro;
using TMPro.EditorUtilities;

public static class KoreanFontSetup
{
    const string FONT_PATH   = "Assets/Fonts/MalgunGothic.ttf";
    const string ASSET_PATH  = "Assets/Fonts/MalgunGothic_TMP.asset";

    [MenuItem("Tools/Setup Korean Font")]
public static void SetupFont()
    {
        TMP_FontAsset t_tmpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ASSET_PATH);
        if (t_tmpFont == null)
        {
            Font t_font = AssetDatabase.LoadAssetAtPath<Font>(FONT_PATH);
            if (t_font == null) { Debug.LogError("[KoreanFontSetup] Font not found at " + FONT_PATH); return; }
            t_tmpFont = TMP_FontAsset.CreateFontAsset(t_font, 90, 9,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                1024, 1024, AtlasPopulationMode.Dynamic, true);
            t_tmpFont.name = "MalgunGothic_TMP";
            AssetDatabase.CreateAsset(t_tmpFont, ASSET_PATH);

            // Save atlas textures and material as sub-assets
            if (t_tmpFont.atlasTextures != null)
                foreach (var t_tex in t_tmpFont.atlasTextures)
                    if (t_tex != null) { t_tex.name = "Atlas"; AssetDatabase.AddObjectToAsset(t_tex, t_tmpFont); }
            if (t_tmpFont.material != null)
                { t_tmpFont.material.name = "Material"; AssetDatabase.AddObjectToAsset(t_tmpFont.material, t_tmpFont); }

            AssetDatabase.SaveAssets();
            Debug.Log("[KoreanFontSetup] Font asset created.");
        }

        TMP_Text[] t_all = Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None);
        foreach (var t_t in t_all) { t_t.font = t_tmpFont; EditorUtility.SetDirty(t_t); }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"[KoreanFontSetup] {t_all.Length}개 TMP_Text에 한글 폰트 적용 완료.");
    }
}
