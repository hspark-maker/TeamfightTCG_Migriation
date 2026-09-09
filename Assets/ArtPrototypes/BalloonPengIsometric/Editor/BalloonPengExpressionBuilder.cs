using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Adds expression patches without changing the body mesh or locomotion curves.</summary>
public static class BalloonPengExpressionBuilder
{
    private const string Folder = "Assets/ArtPrototypes/BalloonPengIsometric/Cutout/";
    private const string PrefabPath = Folder + "BalloonPeng_Cutout_Walk.prefab";
    private const string ClipPath = Folder + "ExpressionLoop.anim";
    private const string BodyPath = "Pose/Body";
    private const float Duration = 4.8f;
    private static readonly string[] Names =
        { "BlinkFarEye", "BlinkNearEye", "SmileFarEye", "SmileNearEye", "SmileBeak" };
    // Pixel rectangles use the original atlas's top-left origin.
    private static readonly RectInt[] Rects =
    {
        new RectInt(181, 223, 45, 56), new RectInt(278, 260, 49, 57),
        new RectInt(181, 223, 45, 56), new RectInt(278, 260, 49, 57), new RectInt(207, 251, 70, 81)
    };

    [MenuItem("Tools/Art Prototypes/Build BalloonPeng Expressions")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build expressions outside Play Mode.");
        var original = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "BalloonPeng_Parts.png");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(Folder + "FacePatch.shader");
        if (original == null || shader == null || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            throw new InvalidOperationException("The cutout prefab, body atlas and FacePatch shader must exist.");
        var blink = ImportFace("FaceBlink.png");
        var smile = ImportFace("FaceSmile.png");
        if (blink.width != original.width || blink.height != original.height ||
            smile.width != original.width || smile.height != original.height)
            throw new InvalidOperationException("Expression atlases must match the original atlas dimensions.");

        var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var body = prefab.transform.Find(BodyPath);
            var controller = prefab.GetComponent<Animator>()?.runtimeAnimatorController as AnimatorController;
            if (body == null || controller == null) throw new InvalidOperationException("The cutout body and AnimatorController are required.");
            var bodyMesh = body.GetComponent<MeshFilter>()?.sharedMesh;
            if (bodyMesh == null) throw new InvalidOperationException("The cutout body mesh is missing.");
            for (var i = 0; i < Names.Length; i++)
            {
                var part = body.Find(Names[i]);
                if (part == null)
                {
                    part = new GameObject(Names[i]).transform;
                    part.SetParent(body, false);
                }
                part.localPosition = Vector3.zero;
                part.localRotation = Quaternion.identity;
                part.localScale = Vector3.one;
                var filter = part.GetComponent<MeshFilter>();
                if (filter == null) filter = part.gameObject.AddComponent<MeshFilter>();
                var renderer = part.GetComponent<MeshRenderer>();
                if (renderer == null) renderer = part.gameObject.AddComponent<MeshRenderer>();
                filter.sharedMesh = UpdatePatchMesh(i, bodyMesh, original.width, original.height);
                var materialPath = Folder + Names[i] + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(shader) { name = Names[i] };
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                material.shader = shader;
                material.SetTexture("_BaseMap", i < 2 ? blink : smile);
                material.SetVector("_PatchPixels", new Vector4(Rects[i].width, Rects[i].height, 0, 0));
                material.SetFloat("_FeatherPixels", 3);
                material.renderQueue = (int)RenderQueue.Transparent;
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                renderer.sharedMaterial = material;
                renderer.sortingOrder = i == 4 ? 2 : 1;
                renderer.enabled = false;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            var clip = SaveLoop();
            ConnectLayer(controller, clip);
            if (PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath) == null)
                throw new InvalidOperationException("Could not save expression patches.");
            Debug.Log("BalloonPeng expressions saved: two blinks and a smile in an independent 4.8-second loop.");
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
    }

    [MenuItem("Tools/Art Prototypes/Update BalloonPeng Expressions")]
    public static void Update() => Build();

    private static Texture2D ImportFace(string name)
    {
        var path = Folder + name;
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing expression image: " + path);
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
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Mesh UpdatePatchMesh(int index, Mesh body, int width, int height)
    {
        var rect = Rects[index];
        var uv = new Rect(rect.x / (float)width, (height - rect.yMax) / (float)height,
            rect.width / (float)width, rect.height / (float)height);
        var bodyUv = body.uv;
        var min = bodyUv[0]; var max = bodyUv[0];
        foreach (var point in bodyUv) { min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
        var bounds = body.bounds;
        var x0 = Mathf.LerpUnclamped(bounds.min.x, bounds.max.x, (uv.xMin - min.x) / (max.x - min.x));
        var x1 = Mathf.LerpUnclamped(bounds.min.x, bounds.max.x, (uv.xMax - min.x) / (max.x - min.x));
        var y0 = Mathf.LerpUnclamped(bounds.min.y, bounds.max.y, (uv.yMin - min.y) / (max.y - min.y));
        var y1 = Mathf.LerpUnclamped(bounds.min.y, bounds.max.y, (uv.yMax - min.y) / (max.y - min.y));
        var path = Folder + Names[index] + "_Mesh.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            mesh = new Mesh { name = Names[index] + "_Mesh" };
            AssetDatabase.CreateAsset(mesh, path);
        }
        mesh.Clear();
        mesh.vertices = new[] { new Vector3(x0, y0, -0.001f), new Vector3(x1, y0, -0.001f),
            new Vector3(x0, y1, -0.001f), new Vector3(x1, y1, -0.001f) };
        mesh.uv = new[] { new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMax, uv.yMin),
            new Vector2(uv.xMin, uv.yMax), new Vector2(uv.xMax, uv.yMax) };
        mesh.uv2 = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        AssetDatabase.SaveAssetIfDirty(mesh);
        return mesh;
    }

    private static AnimationClip SaveLoop()
    {
        var temporary = new AnimationClip { name = "ExpressionLoop", frameRate = 30, wrapMode = WrapMode.Loop };
        try
        {
            for (var i = 0; i < Names.Length; i++)
            {
                var times = i < 2 ? new[] { 0f, 0.95f, 1.08f, 2.05f, 2.18f, Duration }
                    : new[] { 0f, 3.05f, 3.65f, Duration };
                var keys = new Keyframe[times.Length];
                for (var k = 0; k < keys.Length; k++)
                    keys[k] = new Keyframe(times[k], k > 0 && k < keys.Length - 1 && k % 2 == 1 ? 1 : 0,
                        float.PositiveInfinity, float.PositiveInfinity);
                AnimationUtility.SetEditorCurve(temporary,
                    EditorCurveBinding.FloatCurve(BodyPath + "/" + Names[i], typeof(MeshRenderer), "m_Enabled"),
                    new AnimationCurve(keys));
            }
            var settings = AnimationUtility.GetAnimationClipSettings(temporary);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(temporary, settings);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (clip == null)
            {
                clip = new AnimationClip { name = "ExpressionLoop" };
                AssetDatabase.CreateAsset(clip, ClipPath);
            }
            EditorUtility.CopySerialized(temporary, clip);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
        }
        finally { UnityEngine.Object.DestroyImmediate(temporary); }
    }

    private static void ConnectLayer(AnimatorController controller, AnimationClip clip)
    {
        var index = Array.FindIndex(controller.layers, candidateLayer => candidateLayer.name == "Expressions");
        if (index < 0) { controller.AddLayer("Expressions"); index = controller.layers.Length - 1; }
        var layers = controller.layers;
        var layer = layers[index];
        layer.defaultWeight = 1;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.syncedLayerIndex = -1;
        AnimatorState state = null;
        foreach (var candidate in layer.stateMachine.states)
            if (candidate.state.name == "ExpressionLoop") { state = candidate.state; break; }
        if (state == null) state = layer.stateMachine.AddState("ExpressionLoop");
        state.motion = clip;
        state.writeDefaultValues = false;
        layer.stateMachine.defaultState = state;
        layers[index] = layer;
        controller.layers = layers;
        EditorUtility.SetDirty(state);
        EditorUtility.SetDirty(layer.stateMachine);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(controller);
    }
}
