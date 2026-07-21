using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class GLBAssetExtractor
{
    [MenuItem("Assets/Extract GLB Assets", validate = false)]
    static void Extract()
    {
        foreach (Object t_selected in Selection.objects)
        {
            string t_glbPath = AssetDatabase.GetAssetPath(t_selected);
            if (!t_glbPath.EndsWith(".glb",  System.StringComparison.OrdinalIgnoreCase)
             && !t_glbPath.EndsWith(".gltf", System.StringComparison.OrdinalIgnoreCase))
                continue;

            string t_root    = Path.GetDirectoryName(t_glbPath)
                             + "/" + Path.GetFileNameWithoutExtension(t_glbPath) + "_Extracted";
            string t_dirTex  = t_root + "/Textures";
            string t_dirMat  = t_root + "/Materials";
            string t_dirMesh = t_root + "/Meshes";
            string t_dirAnim = t_root + "/Animations";

            Directory.CreateDirectory(t_dirTex);
            Directory.CreateDirectory(t_dirMat);
            Directory.CreateDirectory(t_dirMesh);
            Directory.CreateDirectory(t_dirAnim);

            Object[] t_assets = AssetDatabase.LoadAllAssetsAtPath(t_glbPath);

            var t_texMap  = new Dictionary<Texture2D, Texture2D>();
            var t_meshMap = new Dictionary<Mesh, Mesh>();
            var t_matMap  = new Dictionary<Material, Material>();

            foreach (Object t_asset in t_assets)
                if (t_asset is Texture2D t_tex)
                    t_texMap[t_tex] = ExtractTexture(t_tex, t_dirTex);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            foreach (Object t_asset in t_assets)
            {
                if (t_asset is AnimationClip t_clip)
                    ExtractClip(t_clip, t_dirAnim);
                else if (t_asset is Mesh t_mesh)
                    t_meshMap[t_mesh] = ExtractMesh(t_mesh, t_dirMesh);
                else if (t_asset is Material t_mat)
                    t_matMap[t_mat] = ExtractMaterial(t_mat, t_dirMat, t_texMap);
            }

            AssetDatabase.SaveAssets();

            GameObject t_srcRoot = AssetDatabase.LoadMainAssetAtPath(t_glbPath) as GameObject;
            if (t_srcRoot != null)
                CreatePrefab(t_srcRoot, t_root, t_glbPath, t_meshMap, t_matMap);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("GLB extraction complete.");
    }

    [MenuItem("Assets/Extract GLB Assets", validate = true)]
    static bool ValidateExtract()
    {
        foreach (Object t_sel in Selection.objects)
        {
            string t_path = AssetDatabase.GetAssetPath(t_sel);
            if (t_path.EndsWith(".glb",  System.StringComparison.OrdinalIgnoreCase)
             || t_path.EndsWith(".gltf", System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static Texture2D ExtractTexture(Texture2D _src, string _dir)
    {
        byte[] t_bytes = ReadTexturePNG(_src);
        string t_path  = UniqueAssetPath(_dir, _src.name, ".png");
        File.WriteAllBytes(t_path, t_bytes);
        AssetDatabase.ImportAsset(t_path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(t_path);
    }

    static byte[] ReadTexturePNG(Texture2D _tex)
    {
        if (_tex.isReadable)
            return _tex.EncodeToPNG();

        RenderTexture t_rt   = RenderTexture.GetTemporary(_tex.width, _tex.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(_tex, t_rt);
        RenderTexture t_prev = RenderTexture.active;
        RenderTexture.active = t_rt;
        Texture2D t_readable = new Texture2D(_tex.width, _tex.height, TextureFormat.RGBA32, false);
        t_readable.ReadPixels(new Rect(0, 0, _tex.width, _tex.height), 0, 0);
        t_readable.Apply();
        RenderTexture.active = t_prev;
        RenderTexture.ReleaseTemporary(t_rt);
        byte[] t_bytes = t_readable.EncodeToPNG();
        Object.DestroyImmediate(t_readable);
        return t_bytes;
    }

    static void ExtractClip(AnimationClip _src, string _dir)
    {
        AnimationClip t_copy = Object.Instantiate(_src);
        t_copy.name = _src.name;
        AnimationClipSettings t_settings = AnimationUtility.GetAnimationClipSettings(t_copy);
        t_settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(t_copy, t_settings);
        AssetDatabase.CreateAsset(t_copy, UniqueAssetPath(_dir, _src.name, ".anim"));
    }

    static Mesh ExtractMesh(Mesh _src, string _dir)
    {
        Mesh t_copy = Object.Instantiate(_src);
        t_copy.name = _src.name;
        AssetDatabase.CreateAsset(t_copy, UniqueAssetPath(_dir, _src.name, ".asset"));
        return t_copy;
    }

    static Material ExtractMaterial(Material _src, string _dir, Dictionary<Texture2D, Texture2D> _texMap)
    {
        Material t_copy = Object.Instantiate(_src);
        t_copy.name = _src.name;

        foreach (string t_prop in t_copy.GetTexturePropertyNames())
        {
            if (t_copy.GetTexture(t_prop) is Texture2D t_orig
             && _texMap.TryGetValue(t_orig, out Texture2D t_extracted))
                t_copy.SetTexture(t_prop, t_extracted);
        }

        AssetDatabase.CreateAsset(t_copy, UniqueAssetPath(_dir, _src.name, ".mat"));
        return t_copy;
    }

    static void CreatePrefab(GameObject _srcRoot, string _outDir, string _glbPath,
        Dictionary<Mesh, Mesh> _meshMap, Dictionary<Material, Material> _matMap)
    {
        GameObject t_instance = Object.Instantiate(_srcRoot);

        foreach (SkinnedMeshRenderer t_smr in t_instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (t_smr.sharedMesh != null && _meshMap.TryGetValue(t_smr.sharedMesh, out Mesh t_mesh))
                t_smr.sharedMesh = t_mesh;
            RemapMaterials(t_smr, _matMap);
        }

        foreach (MeshFilter t_mf in t_instance.GetComponentsInChildren<MeshFilter>(true))
            if (t_mf.sharedMesh != null && _meshMap.TryGetValue(t_mf.sharedMesh, out Mesh t_mesh))
                t_mf.sharedMesh = t_mesh;

        foreach (MeshRenderer t_mr in t_instance.GetComponentsInChildren<MeshRenderer>(true))
            RemapMaterials(t_mr, _matMap);

        string t_name       = Path.GetFileNameWithoutExtension(_glbPath);
        string t_prefabPath = UniqueAssetPath(_outDir, t_name, ".prefab");
        PrefabUtility.SaveAsPrefabAsset(t_instance, t_prefabPath);
        Object.DestroyImmediate(t_instance);
    }

    static void RemapMaterials(Renderer _renderer, Dictionary<Material, Material> _matMap)
    {
        Material[] t_mats   = _renderer.sharedMaterials;
        bool       t_changed = false;
        for (int t_i = 0; t_i < t_mats.Length; t_i++)
        {
            if (t_mats[t_i] != null && _matMap.TryGetValue(t_mats[t_i], out Material t_newMat))
            { t_mats[t_i] = t_newMat; t_changed = true; }
        }
        if (t_changed) _renderer.sharedMaterials = t_mats;
    }

    static string UniqueAssetPath(string _dir, string _name, string _ext)
        => AssetDatabase.GenerateUniqueAssetPath(_dir + "/" + _name + _ext);
}
