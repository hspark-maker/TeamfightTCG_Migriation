using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Assembles independent painted body parts; no skinning or body deformation.</summary>
public static class BalloonPengCutoutBuilder
{
    private const string Folder = "Assets/ArtPrototypes/BalloonPengIsometric/Cutout/";
    private const string AtlasPath = Folder + "BalloonPeng_Parts.png";
    private const string MaterialPath = Folder + "BalloonPeng_Cutout.mat";
    private const string ClipPath = Folder + "Walk.anim";
    private const string ControllerPath = Folder + "Walk.controller";
    private const string PrefabPath = Folder + "BalloonPeng_Cutout_Walk.prefab";
    private const string ScenePath = Folder + "BalloonPeng_Cutout_Preview.unity";
    private const string LimbAtlasPath = Folder + "BalloonPeng_LimbsExtended.png";
    private const string LimbMaterialPath = Folder + "BalloonPeng_Limbs.mat";
    // WingFar, WingNear, FootFar, FootNear: independent dimensions for hidden-root overlap.
    private static readonly Vector2[] HiddenPartSizes =
    {
        new Vector2(0.86f, 0.78f), new Vector2(1.14f, 0.64f),
        new Vector2(0.64f, 0.78f), new Vector2(0.60f, 0.738f)
    };
    private static readonly Vector2[] HiddenPartPivots =
    {
        new Vector2(0.55f, 0.49f), new Vector2(0.448f, 0.69f),
        new Vector2(0.49f, 0.35f), new Vector2(0.4f, 0.41f)
    };
    private const float Duration = 1.2f;
    private const float NearWingRestAngle = -22f;
    private const float FarWingRestAngle = 22f;
    private static readonly Vector3 PoseOrigin = Position(0.5f, 0.14f);
    private static readonly string[] Names = { "Body", "WingFar", "WingNear", "FootFar", "FootNear" };
    private static readonly Vector2[] Pivots =
    {
        new Vector2(0.5f, 0), new Vector2(0.85f, 0.25f), new Vector2(0.15f, 0.55f),
        new Vector2(0.65f, 0.78f), new Vector2(0.5f, 0.82f)
    };
    private static readonly Vector3[] Anchors =
    {
        Position(0.495f, 0.555f) - new Vector3(0, 1.6f, 0), Position(0.265f, 0.60f),
        Position(0.727f, 0.445f), Position(0.32f, 0.215f), Position(0.53f, 0.166f)
    };

    [MenuItem("Tools/Art Prototypes/Build BalloonPeng Cutout")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build the cutout outside Play Mode.");
        foreach (var path in new[] { MaterialPath, ClipPath, ControllerPath, PrefabPath, ScenePath }) CheckNew(path);
        foreach (var name in Names) CheckNew(Folder + name + "_Mesh.asset");
        var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (importer == null || shader == null)
            throw new InvalidOperationException("The 3-by-2 cutout atlas and URP Unlit shader are required.");
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Rect[] regions;
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(AtlasPath))) throw new InvalidOperationException("Cannot read cutout PNG.");
            regions = ReadRegions(source);
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 4096;
        importer.SaveAndReimport();

        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var material = CreateMaterial(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
            var root = new GameObject("BalloonPeng_Cutout_Walk");
            var pose = new GameObject("Pose").transform;
            pose.SetParent(root.transform, false);
            pose.localPosition = PoseOrigin;
            var sorting = new[] { 0, -2, 1, -1, -1 };
            for (var i = 0; i < Names.Length; i++)
            {
                var part = new GameObject(Names[i]);
                part.transform.SetParent(i < 3 ? pose : root.transform, false);
                part.transform.localPosition = Anchors[i] - (i < 3 ? PoseOrigin : Vector3.zero);
                if (i == 1) part.transform.localRotation = Quaternion.Euler(0, 0, FarWingRestAngle);
                if (i == 2) part.transform.localRotation = Quaternion.Euler(0, 0, NearWingRestAngle);
                var mesh = CreateQuad(i, regions[i]);
                AssetDatabase.CreateAsset(mesh, Folder + Names[i] + "_Mesh.asset");
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.sortingOrder = sorting[i];
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                AssetDatabase.SaveAssetIfDirty(mesh);
            }
            var clip = CreateWalk();
            AssetDatabase.CreateAsset(clip, ClipPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var state = controller.layers[0].stateMachine.AddState("Walk");
            state.motion = clip;
            controller.layers[0].stateMachine.defaultState = state;
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            AssetDatabase.SaveAssetIfDirty(clip);
            AssetDatabase.SaveAssetIfDirty(material);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefab == null) throw new InvalidOperationException("Could not save cutout prefab.");
            UnityEngine.Object.DestroyImmediate(root);
            PrefabUtility.InstantiatePrefab(prefab, scene);
            var camera = new GameObject("Cutout Preview Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 1.58f, -10);
            camera.orthographic = true;
            camera.orthographicSize = 2.12f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.45f, 0.53f, 0.38f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 30;
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save cutout scene.");
            Selection.activeObject = prefab;
            Debug.Log("BalloonPeng cutout walk created: 5 independent painted parts, no body deformation.");
        }
        finally
        {
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
        if (File.Exists(LimbAtlasPath)) UpdateHiddenParts();
    }

    [MenuItem("Tools/Art Prototypes/Update BalloonPeng Hidden Parts")]
    public static void UpdateHiddenParts()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Update outside Play Mode.");
        var importer = AssetImporter.GetAtPath(LimbAtlasPath) as TextureImporter;
        var meshes = new Mesh[4];
        var bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (importer == null || bodyMaterial == null || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            throw new InvalidOperationException("Build the cutout and import the extended 2-by-2 limb atlas first.");
        for (var i = 0; i < meshes.Length; i++)
        {
            meshes[i] = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + Names[i + 1] + "_Mesh.asset");
            if (meshes[i] == null) throw new InvalidOperationException("Missing cutout mesh: " + Names[i + 1]);
        }
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Rect[] regions;
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(LimbAtlasPath))) throw new InvalidOperationException("Cannot read extended limb PNG.");
            regions = ReadLimbRegions(source);
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 4096;
        importer.SaveAndReimport();

        var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var renderers = new MeshRenderer[4];
            for (var i = 0; i < renderers.Length; i++)
            {
                var part = prefab.transform.Find((i < 2 ? "Pose/" : "") + Names[i + 1]);
                if (part == null || !part.TryGetComponent(out renderers[i]))
                    throw new InvalidOperationException("Missing cutout renderer: " + Names[i + 1]);
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(LimbMaterialPath);
            if (material == null)
            {
                CheckNew(LimbMaterialPath);
                material = new Material(bodyMaterial) { name = "BalloonPeng_Limbs" };
                AssetDatabase.CreateAsset(material, LimbMaterialPath);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(LimbAtlasPath));
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            for (var i = 0; i < meshes.Length; i++)
            {
                var temporary = i == 1 ? CreateOriginalNearWing() : CreateHiddenQuad(i, regions[i]);
                try
                {
                    // Refresh native vertex/UV buffers as well as keeping the existing asset identity.
                    meshes[i].Clear();
                    meshes[i].vertices = temporary.vertices;
                    meshes[i].uv = temporary.uv;
                    meshes[i].triangles = temporary.triangles;
                    meshes[i].normals = temporary.normals;
                    meshes[i].bounds = temporary.bounds;
                    EditorUtility.SetDirty(meshes[i]);
                    AssetDatabase.SaveAssetIfDirty(meshes[i]);
                }
                finally { UnityEngine.Object.DestroyImmediate(temporary); }
                renderers[i].sharedMaterial = i == 1 ? bodyMaterial : material;
                renderers[i].sortingOrder = i == 1 ? 1 : i == 0 ? -2 : -1;
            }
            // Only limb mesh data and renderer settings change. Body and all transforms stay authored.
            if (PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath) == null)
                throw new InvalidOperationException("Could not save extended limb references.");
            Debug.Log("BalloonPeng hidden limb roots updated; body, joint anchors and walk clip preserved.");
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
    }

    // The front-facing wing uses its original short artwork, without the hidden shoulder extension.
    private static Mesh CreateOriginalNearWing()
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(AtlasPath)))
                throw new InvalidOperationException("Cannot read original wing PNG.");
            return CreateQuad(2, ReadRegions(source)[2]);
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
    }

    private static Rect[] ReadLimbRegions(Texture2D source)
    {
        var pixels = source.GetPixels32();
        var regions = new Rect[4];
        for (var i = 0; i < regions.Length; i++)
        {
            var col = i % 2; var row = i < 2 ? 1 : 0;
            var left = col * source.width / 2; var right = (col + 1) * source.width / 2;
            var bottom = row * source.height / 2; var top = (row + 1) * source.height / 2;
            var minX = right; var maxX = left - 1; var minY = top; var maxY = bottom - 1;
            for (var y = bottom; y < top; y++)
            for (var x = left; x < right; x++)
                if (pixels[y * source.width + x].a > 25)
                {
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            if (maxX < minX) throw new InvalidOperationException("Missing extended limb: " + Names[i + 1]);
            minX = Mathf.Max(left, minX - 2); maxX = Mathf.Min(right, maxX + 3);
            minY = Mathf.Max(bottom, minY - 2); maxY = Mathf.Min(top, maxY + 3);
            regions[i] = new Rect(minX / (float)source.width, minY / (float)source.height,
                (maxX - minX) / (float)source.width, (maxY - minY) / (float)source.height);
        }
        return regions;
    }

    private static Mesh CreateHiddenQuad(int index, Rect uv)
    {
        var size = HiddenPartSizes[index];
        var pivot = HiddenPartPivots[index];
        var x = -pivot.x * size.x; var y = -pivot.y * size.y;
        var mesh = new Mesh { name = Names[index + 1] + "_Mesh" };
        mesh.vertices = new[] { new Vector3(x, y, 0), new Vector3(x + size.x, y, 0),
            new Vector3(x, y + size.y, 0), new Vector3(x + size.x, y + size.y, 0) };
        mesh.uv = new[] { new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMax, uv.yMin),
            new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax) };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    [MenuItem("Tools/Art Prototypes/Update BalloonPeng Cutout Motion")]
    public static void UpdateMotion()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Update outside Play Mode.");
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (existing == null) throw new InvalidOperationException("Build the cutout before updating its motion.");
        var clip = CreateWalk();
        try
        {
            EditorUtility.CopySerialized(clip, existing);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssetIfDirty(existing);
        }
        finally { UnityEngine.Object.DestroyImmediate(clip); }
    }

    private static void CheckNew(string path)
    {
        if (File.Exists(path) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
            throw new InvalidOperationException("Cutout asset already exists; nothing changed: " + path);
    }

    private static Rect[] ReadRegions(Texture2D source)
    {
        var pixels = source.GetPixels32();
        var regions = new Rect[5];
        for (var i = 0; i < regions.Length; i++)
        {
            var col = i % 3;
            var row = i < 3 ? 1 : 0;
            var left = col * source.width / 3;
            var right = (col + 1) * source.width / 3;
            // The body extends below the nominal cell; the blank gutter ends before the feet.
            var bottom = i == 0 ? source.height * 3 / 8 : row * source.height / 2;
            var top = i == 3 ? source.height * 3 / 8 : (row + 1) * source.height / 2;
            var minX = right; var maxX = left - 1; var minY = top; var maxY = bottom - 1;
            for (var y = bottom; y < top; y++)
            for (var x = left; x < right; x++)
                if (pixels[y * source.width + x].a > 25)
                {
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            if (maxX < minX) throw new InvalidOperationException("Missing transparent atlas part: " + Names[i]);
            minX = Mathf.Max(left, minX - 2); maxX = Mathf.Min(right, maxX + 3);
            minY = Mathf.Max(bottom, minY - 2); maxY = Mathf.Min(top, maxY + 3);
            regions[i] = new Rect(minX / (float)source.width, minY / (float)source.height,
                (maxX - minX) / (float)source.width, (maxY - minY) / (float)source.height);
        }
        return regions;
    }

    private static Mesh CreateQuad(int index, Rect uv)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        var aspect = uv.width * texture.width / (uv.height * texture.height);
        var size = index == 0 ? new Vector2(3.20f * aspect, 3.20f) : index == 1
            ? new Vector2(0.52f * aspect, 0.52f) : new Vector2(index == 2 ? 0.74f : 0.48f, (index == 2 ? 0.74f : 0.48f) / aspect);
        var p = Pivots[index];
        var x = -p.x * size.x; var y = -p.y * size.y;
        var mesh = new Mesh { name = Names[index] + "_Mesh" };
        mesh.vertices = new[] { new Vector3(x, y, 0), new Vector3(x + size.x, y, 0),
            new Vector3(x, y + size.y, 0), new Vector3(x + size.x, y + size.y, 0) };
        mesh.uv = new[] { new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMax, uv.yMin),
            new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax) };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Material CreateMaterial(Shader shader)
    {
        var material = new Material(shader) { name = "BalloonPeng_Cutout" };
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0); material.SetFloat("_Cull", (float)CullMode.Off);
        material.SetFloat("_AlphaClip", 0);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON"); material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.renderQueue = (int)RenderQueue.Transparent;
        return material;
    }

    private static Vector3 Position(float u, float v) => new Vector3((u - 0.5f) * 4, (v - 0.105f) * 4, 0);

    private static AnimationClip CreateWalk()
    {
        var clip = new AnimationClip { name = "Walk", frameRate = 30, wrapMode = WrapMode.Loop };
        Curve(clip, "Pose", "m_LocalPosition.y", a => PoseOrigin.y + 0.015f * (1 - Mathf.Cos(2 * a)));
        Curve(clip, "Pose", "localEulerAnglesRaw.z", a => 1.5f * Mathf.Sin(a));
        Curve(clip, "Pose/WingFar", "localEulerAnglesRaw.z", a => FarWingRestAngle - 24 * Mathf.Sin(a - 0.25f));
        Curve(clip, "Pose/WingNear", "localEulerAnglesRaw.z", a => NearWingRestAngle + 24 * Mathf.Sin(a - 0.25f));
        for (var i = 3; i < 5; i++)
        {
            var rest = Anchors[i];
            var phase = i == 3 ? 0 : Mathf.PI;
            Curve(clip, Names[i], "m_LocalPosition.x", a => rest.x + 0.06f * Mathf.Cos(a + phase));
            Curve(clip, Names[i], "m_LocalPosition.y", a => rest.y + 0.10f * Mathf.Pow(Mathf.Max(0, Mathf.Sin(a + phase)), 2));
            Curve(clip, Names[i], "localEulerAnglesRaw.z", a => 6 * Mathf.Sin(a + phase));
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true; settings.loopBlend = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    private static void Curve(AnimationClip clip, string path, string property, Func<float, float> value)
    {
        var keys = new Keyframe[33];
        var omega = 2 * Mathf.PI / Duration;
        for (var i = 0; i < keys.Length; i++)
        {
            var t = Duration * i / (keys.Length - 1);
            var angle = i == keys.Length - 1 ? 0 : t * omega;
            var tangent = (value(angle + 0.001f) - value(angle - 0.001f)) * omega / 0.002f;
            keys[i] = new Keyframe(t, value(angle), tangent, tangent);
        }
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property),
            new AnimationCurve(keys) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.Loop });
    }
}
