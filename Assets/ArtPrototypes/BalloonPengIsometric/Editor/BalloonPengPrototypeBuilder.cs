using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Creates the isolated BalloonPeng art prototype without touching gameplay scenes.</summary>
public static class BalloonPengPrototypeBuilder
{
    private const string Folder = "Assets/ArtPrototypes/BalloonPengIsometric/";
    private const string TexturePath = Folder + "BalloonPeng_Isometric.png";
    private const string MeshPath = Folder + "BalloonPeng_Mesh.asset";
    private const string MaterialPath = Folder + "BalloonPeng_Material.mat";
    private const string ClipPath = Folder + "Idle.anim";
    private const string ControllerPath = Folder + "BalloonPeng_Animator.controller";
    private const string PrefabPath = Folder + "BalloonPeng_Isometric.prefab";
    private const string ScenePath = Folder + "BalloonPeng_Preview.unity";
    private const int Cells = 48;
    private const float Duration = 2.4f;

    [MenuItem("Tools/Art Prototypes/Build BalloonPeng Isometric")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build the prototype outside Play Mode.");

        // First-run builder: authored or previously generated assets must never be overwritten.
        foreach (var path in new[] { MeshPath, MaterialPath, ClipPath, ControllerPath, PrefabPath, ScenePath })
            if (File.Exists(path) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                throw new InvalidOperationException("Prototype asset already exists; nothing changed: " + path);

        var importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (importer == null || shader == null)
            throw new InvalidOperationException("The BalloonPeng texture and URP Unlit shader are required.");

        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();

        var previousScene = SceneManager.GetActiveScene();
        var previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(previewScene);
            var root = new GameObject("BalloonPeng_Isometric");
            var bones = CreateBones(root.transform);
            var mesh = CreateMesh(root.transform, bones);
            AssetDatabase.CreateAsset(mesh, MeshPath);

            var material = new Material(shader) { name = "BalloonPeng_Material" };
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.SetFloat("_AlphaClip", 0);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.renderQueue = (int)RenderQueue.Transparent;
            AssetDatabase.CreateAsset(material, MaterialPath);

            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.bones = bones;
            renderer.rootBone = bones[0];
            renderer.quality = SkinQuality.Bone4;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.updateWhenOffscreen = true;
            renderer.localBounds = new Bounds(new Vector3(0, 1.6f, 0), new Vector3(5, 5, 1));

            var clip = CreateIdle();
            AssetDatabase.CreateAsset(clip, ClipPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var state = controller.layers[0].stateMachine.AddState("Idle");
            state.motion = clip;
            controller.layers[0].stateMachine.defaultState = state;
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            AssetDatabase.SaveAssetIfDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(material);
            AssetDatabase.SaveAssetIfDirty(clip);
            AssetDatabase.SaveAssetIfDirty(controller);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefab == null)
                throw new InvalidOperationException("Could not save the BalloonPeng prototype prefab.");
            UnityEngine.Object.DestroyImmediate(root);
            PrefabUtility.InstantiatePrefab(prefab, previewScene);

            var cameraObject = new GameObject("Preview Camera");
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 1.58f, -10);
            camera.orthographic = true;
            camera.orthographicSize = 2.12f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.45f, 0.53f, 0.38f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 30;
            if (!EditorSceneManager.SaveScene(previewScene, ScenePath))
                throw new InvalidOperationException("Could not save the BalloonPeng preview scene.");

            Selection.activeObject = prefab;
            Debug.Log("BalloonPeng prototype created: 6 bones, skinned mesh and a 2.4-second looping Idle. Open " + ScenePath);
        }
        finally
        {
            if (previousScene.IsValid() && previousScene.isLoaded)
                SceneManager.SetActiveScene(previousScene);
            if (previewScene.IsValid() && previewScene.isLoaded)
                EditorSceneManager.CloseScene(previewScene, true);
        }
    }

    [MenuItem("Tools/Art Prototypes/Update BalloonPeng Flutter")]
    public static void UpdateFlutter()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Update the prototype outside Play Mode.");
        var existingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (existingClip == null || existingMesh == null)
            throw new InvalidOperationException("Build the BalloonPeng prototype before updating its flutter.");

        var previousScene = SceneManager.GetActiveScene();
        var temporaryScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        AnimationClip clip = null;
        Mesh mesh = null;
        try
        {
            SceneManager.SetActiveScene(temporaryScene);
            var root = new GameObject("BalloonPeng_UpdateTemporary");
            mesh = CreateMesh(root.transform, CreateBones(root.transform));
            clip = CreateIdle();
            // Keep asset identities so the existing prefab, scene and animator keep their references.
            EditorUtility.CopySerialized(mesh, existingMesh);
            EditorUtility.CopySerialized(clip, existingClip);
            EditorUtility.SetDirty(existingMesh);
            EditorUtility.SetDirty(existingClip);
            AssetDatabase.SaveAssetIfDirty(existingMesh);
            AssetDatabase.SaveAssetIfDirty(existingClip);
            Debug.Log("BalloonPeng flutter updated: 3 wingbeats and 0.2-unit lift per 2.4-second loop.");
        }
        finally
        {
            if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
            if (previousScene.IsValid() && previousScene.isLoaded)
                SceneManager.SetActiveScene(previousScene);
            if (temporaryScene.IsValid() && temporaryScene.isLoaded)
                EditorSceneManager.CloseScene(temporaryScene, true);
        }
    }

    private const string WalkMeshPath = Folder + "BalloonPeng_Walk_Mesh.asset";
    private const string WalkClipPath = Folder + "Walk.anim";
    private const string WalkControllerPath = Folder + "BalloonPeng_Walk.controller";
    private const string WalkPrefabPath = Folder + "BalloonPeng_Walk.prefab";
    private const string WalkScenePath = Folder + "BalloonPeng_Walk_Preview.unity";
    private const float WalkDuration = 1.2f;

    [MenuItem("Tools/Art Prototypes/Build BalloonPeng Walk")]
    public static void BuildWalk() => SaveWalk(false);

    [MenuItem("Tools/Art Prototypes/Update BalloonPeng Walk")]
    public static void UpdateWalk() => SaveWalk(true);

    private static void SaveWalk(bool update)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Build or update the walk outside Play Mode.");
        if (!update)
            foreach (var path in new[] { WalkMeshPath, WalkClipPath, WalkControllerPath, WalkPrefabPath, WalkScenePath })
                if (File.Exists(path) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                    throw new InvalidOperationException("Walk asset already exists; nothing changed: " + path);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(WalkMeshPath);
        var existingClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkClipPath);
        if (material == null || (update && (existingMesh == null || existingClip == null)))
            throw new InvalidOperationException("Build the base prototype, and build the walk before updating it.");
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        Mesh mesh = null;
        AnimationClip clip = null;
        try
        {
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("BalloonPeng_Walk");
            var bones = CreateWalkBones(root.transform);
            mesh = CreateMesh(root.transform, bones);
            mesh.name = "BalloonPeng_Walk_Mesh";
            var uv = mesh.uv;
            var weights = new BoneWeight[uv.Length];
            for (var i = 0; i < weights.Length; i++) weights[i] = WalkWeights(uv[i].x, uv[i].y);
            mesh.boneWeights = weights;
            clip = CreateWalk(bones);
            if (update)
            {
                EditorUtility.CopySerialized(mesh, existingMesh);
                EditorUtility.CopySerialized(clip, existingClip);
                EditorUtility.SetDirty(existingMesh);
                EditorUtility.SetDirty(existingClip);
                AssetDatabase.SaveAssetIfDirty(existingMesh);
                AssetDatabase.SaveAssetIfDirty(existingClip);
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, WalkMeshPath);
                AssetDatabase.CreateAsset(clip, WalkClipPath);
                var renderer = root.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.sharedMaterial = material;
                renderer.bones = bones;
                renderer.rootBone = bones[0];
                renderer.quality = SkinQuality.Bone4;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.updateWhenOffscreen = true;
                renderer.localBounds = new Bounds(new Vector3(0, 1.6f, 0), new Vector3(5, 5, 1));
                var controller = AnimatorController.CreateAnimatorControllerAtPath(WalkControllerPath);
                var state = controller.layers[0].stateMachine.AddState("Walk");
                state.motion = clip;
                controller.layers[0].stateMachine.defaultState = state;
                var animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
                AssetDatabase.SaveAssetIfDirty(mesh);
                AssetDatabase.SaveAssetIfDirty(clip);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, WalkPrefabPath);
                if (prefab == null) throw new InvalidOperationException("Could not save the walk prefab.");
                UnityEngine.Object.DestroyImmediate(root);
                PrefabUtility.InstantiatePrefab(prefab, scene);
                var camera = new GameObject("Walk Preview Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.transform.position = new Vector3(0, 1.58f, -10);
                camera.orthographic = true;
                camera.orthographicSize = 2.12f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.45f, 0.53f, 0.38f);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 30;
                if (!EditorSceneManager.SaveScene(scene, WalkScenePath))
                    throw new InvalidOperationException("Could not save the walk preview scene.");
                Selection.activeObject = prefab;
            }
            Debug.Log("BalloonPeng Walk saved: 8 bones, alternating feet and a 1.2-second two-step loop.");
        }
        finally
        {
            if (mesh != null && !AssetDatabase.Contains(mesh)) UnityEngine.Object.DestroyImmediate(mesh);
            if (clip != null && !AssetDatabase.Contains(clip)) UnityEngine.Object.DestroyImmediate(clip);
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static Transform[] CreateWalkBones(Transform root)
    {
        var bones = new Transform[8];
        Array.Copy(CreateBones(root), bones, 6);
        for (var i = 6; i < 8; i++)
        {
            bones[i] = new GameObject(i == 6 ? "FootFar" : "FootNear").transform;
            bones[i].SetParent(bones[0], false);
            bones[i].position = root.TransformPoint(i == 6 ? Position(0.32f, 0.215f) : Position(0.53f, 0.166f));
        }
        return bones;
    }

    private static BoneWeight WalkWeights(float u, float v)
    {
        var far = Smooth(0.22f, 0.275f, u) * (1 - Smooth(0.37f, 0.43f, u))
                  * (1 - Smooth(0.19f, 0.25f, v)) * 0.97f;
        var near = Smooth(0.44f, 0.49f, u) * (1 - Smooth(0.59f, 0.65f, u))
                   * (1 - Smooth(0.14f, 0.20f, v)) * 0.97f;
        if (far + near <= 0) return Weights(u, v);
        // Foot masks occupy only the root/body region, and do not overlap any accessory mask.
        var body = Smooth(0.15f, 0.28f, v);
        var w0 = (1 - body) * (1 - far - near);
        var w1 = body * (1 - far - near);
        var w2 = far;
        var w3 = near;
        var i0 = 0; var i1 = 1; var i2 = 6; var i3 = 7;
        SortInfluences(ref w0, ref i0, ref w1, ref i1);
        SortInfluences(ref w1, ref i1, ref w2, ref i2);
        SortInfluences(ref w2, ref i2, ref w3, ref i3);
        SortInfluences(ref w0, ref i0, ref w1, ref i1);
        SortInfluences(ref w1, ref i1, ref w2, ref i2);
        SortInfluences(ref w0, ref i0, ref w1, ref i1);
        return new BoneWeight { boneIndex0 = i0, weight0 = w0, boneIndex1 = i1, weight1 = w1,
            boneIndex2 = i2, weight2 = w2, boneIndex3 = i3, weight3 = w3 };
    }

    private static AnimationClip CreateWalk(Transform[] bones)
    {
        var clip = new AnimationClip { name = "Walk", frameRate = 30, wrapMode = WrapMode.Loop };
        Func<float, float> bob = a => 0.0175f * (1 - Mathf.Cos(2 * a));
        WalkCurve(clip, "Root", "m_LocalPosition.y", bob);
        WalkCurve(clip, "Root/Body", "localEulerAnglesRaw.z", a => 3 * Mathf.Sin(a));
        WalkCurve(clip, "Root/Body", "m_LocalScale.x", a => 1 + 0.009f * Mathf.Cos(2 * a));
        WalkCurve(clip, "Root/Body", "m_LocalScale.y", a => 1 - 0.014f * Mathf.Cos(2 * a));
        WalkCurve(clip, "Root/Body/WingFar", "localEulerAnglesRaw.z", a => 5 * Mathf.Sin(a + 0.25f));
        WalkCurve(clip, "Root/Body/WingNear", "localEulerAnglesRaw.z", a => 5 * Mathf.Sin(a - 0.25f));
        WalkCurve(clip, "Root/Body/Plume", "localEulerAnglesRaw.z", a => 3 * Mathf.Sin(a - 0.5f));
        WalkCurve(clip, "Root/Body/Scarf", "localEulerAnglesRaw.z", a => 3 * Mathf.Sin(a - 0.8f));
        for (var i = 6; i < 8; i++)
        {
            var rest = bones[i].localPosition;
            var phase = i == 6 ? 0 : Mathf.PI;
            var path = "Root/" + bones[i].name;
            WalkCurve(clip, path, "m_LocalPosition.x", a => rest.x + 0.045f * Mathf.Cos(a + phase));
            WalkCurve(clip, path, "m_LocalPosition.y", a => rest.y - bob(a)
                + 0.055f * Mathf.Pow(Mathf.Max(0, Mathf.Sin(a + phase)), 2));
            WalkCurve(clip, path, "localEulerAnglesRaw.z", a => 6 * Mathf.Sin(a + phase));
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        settings.loopBlend = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    private static void WalkCurve(AnimationClip clip, string path, string property, Func<float, float> value)
    {
        var keys = new Keyframe[33];
        var omega = 2 * Mathf.PI / WalkDuration;
        for (var i = 0; i < keys.Length; i++)
        {
            var t = WalkDuration * i / (keys.Length - 1);
            var angle = i == keys.Length - 1 ? 0 : t * omega;
            var tangent = (value(angle + 0.001f) - value(angle - 0.001f)) * omega / 0.002f;
            keys[i] = new Keyframe(t, value(angle), tangent, tangent);
        }
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property),
            new AnimationCurve(keys) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.Loop });
    }

    private static Vector3 Position(float u, float v)
    {
        return new Vector3((u - 0.5f) * 4, (v - 0.105f) * 4, 0);
    }

    private static Transform[] CreateBones(Transform root)
    {
        var names = new[] { "Root", "Body", "WingFar", "WingNear", "Plume", "Scarf" };
        var pivots = new[]
        {
            Vector3.zero, Position(0.5f, 0.14f), Position(0.235f, 0.60f),
            Position(0.727f, 0.445f), Position(0.53f, 0.792f), Position(0.598f, 0.427f)
        };
        var bones = new Transform[names.Length];
        for (var i = 0; i < bones.Length; i++)
        {
            bones[i] = new GameObject(names[i]).transform;
            bones[i].SetParent(i == 0 ? root : i == 1 ? bones[0] : bones[1], false);
            bones[i].position = root.TransformPoint(pivots[i]);
        }
        return bones;
    }

    private static Mesh CreateMesh(Transform root, Transform[] bones)
    {
        var count = (Cells + 1) * (Cells + 1);
        var vertices = new Vector3[count];
        var uv = new Vector2[count];
        var weights = new BoneWeight[count];
        var triangles = new int[Cells * Cells * 6];
        for (var y = 0; y <= Cells; y++)
        for (var x = 0; x <= Cells; x++)
        {
            var index = y * (Cells + 1) + x;
            var u = x / (float)Cells;
            var v = y / (float)Cells;
            vertices[index] = Position(u, v);
            uv[index] = new Vector2(u, v);
            weights[index] = Weights(u, v);
            if (x == Cells || y == Cells) continue;
            var t = (y * Cells + x) * 6;
            triangles[t] = index;
            triangles[t + 1] = index + Cells + 1;
            triangles[t + 2] = index + 1;
            triangles[t + 3] = index + 1;
            triangles[t + 4] = index + Cells + 1;
            triangles[t + 5] = index + Cells + 2;
        }
        var bindposes = new Matrix4x4[bones.Length];
        for (var i = 0; i < bones.Length; i++)
            bindposes[i] = bones[i].worldToLocalMatrix * root.localToWorldMatrix;
        var mesh = new Mesh
        {
            name = "BalloonPeng_Mesh", vertices = vertices, uv = uv,
            triangles = triangles, boneWeights = weights, bindposes = bindposes
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static float Smooth(float low, float high, float value)
    {
        return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(low, high, value));
    }

    private static float Region(float u, float v, float cx, float cy, float rx, float ry)
    {
        var dx = (u - cx) / rx;
        var dy = (v - cy) / ry;
        return 1 - Smooth(0.35f, 1, Mathf.Sqrt(dx * dx + dy * dy));
    }

    private static BoneWeight Weights(float u, float v)
    {
        var body = Smooth(0.15f, 0.28f, v);
        // Shoulder-to-tip masks keep the outer wing silhouette moving with each wing bone.
        var local = (1 - Smooth(0.19f, 0.245f, u)) * Smooth(0.46f, 0.51f, v)
                    * (1 - Smooth(0.645f, 0.69f, v));
        var bone = 2;
        var secondLocal = 0f;
        var secondBone = 3;
        var near = Smooth(0.69f, 0.755f, u) * Smooth(0.29f, 0.41f, v)
                   * (1 - Smooth(0.49f, 0.535f, v));
        KeepTwoRegions(near, 3, ref local, ref bone, ref secondLocal, ref secondBone);
        // The fan reaches far to the right; a vertical mask keeps both tips with the plume.
        var plume = Smooth(0.775f, 0.835f, v) * Smooth(0.47f, 0.535f, u);
        KeepTwoRegions(plume, 4, ref local, ref bone, ref secondLocal, ref secondBone);
        var scarf = Region(u, v, 0.65f, 0.35f, 0.11f, 0.105f);
        KeepTwoRegions(scarf, 5, ref local, ref bone, ref secondLocal, ref secondBone);
        // Blend both overlapping details continuously instead of switching the winning bone.
        var scale = 0.97f / Mathf.Max(1, local + secondLocal);
        var w0 = 1 - body;
        var w2 = body * local * scale;
        var w3 = body * secondLocal * scale;
        var w1 = body - w2 - w3;
        var i0 = 0;
        var i1 = 1;
        var i2 = bone;
        var i3 = secondBone;
        // Unity skinning requires descending weights with their matching bone indices.
        SortInfluences(ref w0, ref i0, ref w1, ref i1);
        SortInfluences(ref w1, ref i1, ref w2, ref i2);
        SortInfluences(ref w2, ref i2, ref w3, ref i3);
        SortInfluences(ref w0, ref i0, ref w1, ref i1);
        SortInfluences(ref w1, ref i1, ref w2, ref i2);
        SortInfluences(ref w0, ref i0, ref w1, ref i1);
        return new BoneWeight
        {
            boneIndex0 = i0, weight0 = w0,
            boneIndex1 = i1, weight1 = w1,
            boneIndex2 = i2, weight2 = w2,
            boneIndex3 = i3, weight3 = w3
        };
    }

    private static void KeepTwoRegions(float candidate, int candidateBone, ref float first, ref int firstBone,
        ref float second, ref int secondBone)
    {
        if (candidate > first)
        {
            second = first;
            secondBone = firstBone;
            first = candidate;
            firstBone = candidateBone;
        }
        else if (candidate > second)
        {
            second = candidate;
            secondBone = candidateBone;
        }
    }

    private static void SortInfluences(ref float firstWeight, ref int firstBone, ref float secondWeight, ref int secondBone)
    {
        if (firstWeight >= secondWeight) return;
        var weight = firstWeight;
        var bone = firstBone;
        firstWeight = secondWeight;
        firstBone = secondBone;
        secondWeight = weight;
        secondBone = bone;
    }

    private static AnimationClip CreateIdle()
    {
        var clip = new AnimationClip { name = "Idle", frameRate = 30, wrapMode = WrapMode.Loop };
        // Each downstroke lifts the whole rig, including its feet; the soft parts trail behind.
        Curve(clip, "Root", "m_LocalPosition.y", 0.1f, 0.1f, -Mathf.PI / 2, 3);
        Curve(clip, "Root/Body", "m_LocalScale.x", 1, 0.015f, Mathf.PI / 2, 3);
        Curve(clip, "Root/Body", "m_LocalScale.y", 1, -0.025f, Mathf.PI / 2, 3);
        Curve(clip, "Root/Body", "localEulerAnglesRaw.z", 0, 2, 0, 1);
        Curve(clip, "Root/Body/WingFar", "localEulerAnglesRaw.z", 0, 20, 0, 3);
        Curve(clip, "Root/Body/WingNear", "localEulerAnglesRaw.z", 0, -22, -0.12f, 3);
        Curve(clip, "Root/Body/Plume", "localEulerAnglesRaw.z", 0, 6, -0.6f, 3);
        Curve(clip, "Root/Body/Scarf", "localEulerAnglesRaw.z", 0, 7, -1.0f, 3);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        settings.loopBlend = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    private static void Curve(AnimationClip clip, string path, string property, float center, float amplitude, float phase, int cycles)
    {
        var keys = new Keyframe[cycles * 8 + 1];
        var omega = Mathf.PI * 2 * cycles / Duration;
        for (var i = 0; i < keys.Length; i++)
        {
            var t = Duration * i / (keys.Length - 1);
            var angle = i == keys.Length - 1 ? phase : omega * t + phase;
            var tangent = amplitude * omega * Mathf.Cos(angle);
            keys[i] = new Keyframe(t, center + amplitude * Mathf.Sin(angle), tangent, tangent);
        }
        var curve = new AnimationCurve(keys) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.Loop };
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
    }
}
