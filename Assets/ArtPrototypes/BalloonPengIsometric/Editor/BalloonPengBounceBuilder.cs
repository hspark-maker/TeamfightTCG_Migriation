using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Adds a rigid-part hop and a shared Idle/Walk/Bounce controller as a separate variant.</summary>
public static class BalloonPengBounceBuilder
{
    private const string Folder = "Assets/ArtPrototypes/BalloonPengIsometric/Cutout/";
    private const string SourcePath = Folder + "BalloonPeng_Cutout_Walk.prefab";
    private const string PrefabPath = Folder + "BalloonPeng_Cutout_Motions.prefab";
    private const string ClipPath = Folder + "Bounce.anim";
    private const string ControllerPath = Folder + "BalloonPeng_Motions.controller";
    private const string ScenePath = Folder + "BalloonPeng_Motions_Preview.unity";
    private const float Duration = 1.2f;

    [MenuItem("Tools/Art Prototypes/Build BalloonPeng Bounce")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build bounce outside Play Mode.");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "Idle.anim");
        var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "Walk.anim");
        var expressions = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "ExpressionLoop.anim");
        if (source == null || idle == null || walk == null || expressions == null)
            throw new InvalidOperationException("Build the cutout walk, idle and expressions before bounce.");
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            var animator = instance.GetComponent<Animator>();
            if (animator == null) throw new InvalidOperationException("The source Animator is missing.");
            var bounce = SaveBounce(instance.transform);
            var controller = SaveController(idle, walk, bounce, expressions);
            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (variant == null)
            {
                animator.runtimeAnimatorController = controller;
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                variant = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
                if (variant == null) throw new InvalidOperationException("Could not save the motions prefab variant.");
            }
            else
            {
                var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    var variantAnimator = contents.GetComponent<Animator>();
                    if (variantAnimator == null) throw new InvalidOperationException("The motions Animator is missing.");
                    variantAnimator.runtimeAnimatorController = controller;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(variantAnimator);
                    if (PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath) == null)
                        throw new InvalidOperationException("Could not update the motions prefab variant.");
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            UnityEngine.Object.DestroyImmediate(instance);
            // Preserve an existing preview scene; its prefab reference inherits the updated variant.
            if (!File.Exists(ScenePath) && string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(ScenePath)))
            {
                PrefabUtility.InstantiatePrefab(variant, scene);
                var camera = new GameObject("Motions Preview Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.transform.position = new Vector3(0, 1.8f, -10);
                camera.orthographic = true;
                camera.orthographicSize = 2.45f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.45f, 0.53f, 0.38f);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 30;
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Could not save the motions preview scene.");
            }
            Selection.activeObject = variant;
            Debug.Log("BalloonPeng motions saved: Motion 0=Idle, 1=Walk, 2=Bounce (default), plus shared expressions.");
        }
        finally
        {
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("Tools/Art Prototypes/Update BalloonPeng Bounce")]
    public static void Update() => Build();

    private static AnimationClip SaveBounce(Transform root)
    {
        var pose = Require(root, "Pose");
        var farWing = Require(root, "Pose/WingFar");
        var nearWing = Require(root, "Pose/WingNear");
        var temporary = new AnimationClip { name = "Bounce", frameRate = 30, wrapMode = WrapMode.Loop };
        try
        {
            var bodyTimes = new[] { 0f, 0.12f, 0.20f, 0.52f, 0.88f, 0.94f, 1.08f, Duration };
            var flightTimes = new[] { 0f, 0.20f, 0.52f, 0.88f, Duration };
            // During flight the body and feet use the same Hermite intervals and height offsets.
            // Only the body dips before takeoff and after landing; every painted part stays rigid.
            Curve(temporary, "Pose", "m_LocalPosition.y", pose.localPosition.y, bodyTimes,
                new[] { 0f, -0.055f, 0f, 0.48f, 0f, -0.07f, 0f, 0f });
            Curve(temporary, "Pose", "localEulerAnglesRaw.z", pose.localEulerAngles.z, bodyTimes,
                new[] { 0f, -0.4f, 0f, 1.2f, 0f, -1.2f, 0f, 0f });
            Curve(temporary, "Pose/WingFar", "localEulerAnglesRaw.z", Mathf.DeltaAngle(0, farWing.localEulerAngles.z), bodyTimes,
                new[] { 0f, 12f, -15f, -40f, 0f, 20f, 0f, 0f });
            Curve(temporary, "Pose/WingNear", "localEulerAnglesRaw.z", Mathf.DeltaAngle(0, nearWing.localEulerAngles.z), bodyTimes,
                new[] { 0f, -12f, 15f, 40f, 0f, -20f, 0f, 0f });
            foreach (var name in new[] { "FootFar", "FootNear" })
            {
                var foot = Require(root, name);
                Curve(temporary, name, "m_LocalPosition.x", foot.localPosition.x,
                    new[] { 0f, Duration }, new[] { 0f, 0f });
                Curve(temporary, name, "m_LocalPosition.y", foot.localPosition.y, flightTimes,
                    new[] { 0f, 0f, 0.48f, 0f, 0f });
                Curve(temporary, name, "localEulerAnglesRaw.z", foot.localEulerAngles.z, flightTimes,
                    new[] { 0f, 0f, name == "FootFar" ? 3f : -3f, 0f, 0f });
            }
            var settings = AnimationUtility.GetAnimationClipSettings(temporary);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(temporary, settings);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (clip == null)
            {
                clip = new AnimationClip { name = "Bounce" };
                AssetDatabase.CreateAsset(clip, ClipPath);
            }
            EditorUtility.CopySerialized(temporary, clip);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
        }
        finally { UnityEngine.Object.DestroyImmediate(temporary); }
    }

    private static Transform Require(Transform root, string path)
    {
        var part = root.Find(path);
        if (part == null) throw new InvalidOperationException("The source cutout is missing " + path);
        return part;
    }

    private static AnimatorController SaveController(AnimationClip idle, AnimationClip walk,
        AnimationClip bounce, AnimationClip expressions)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var parameters = controller.parameters;
        var parameterIndex = Array.FindIndex(parameters, parameter => parameter.name == "Motion");
        if (parameterIndex < 0)
            controller.AddParameter(new AnimatorControllerParameter { name = "Motion", type = AnimatorControllerParameterType.Int, defaultInt = 2 });
        else
        {
            parameters[parameterIndex].type = AnimatorControllerParameterType.Int;
            parameters[parameterIndex].defaultInt = 2;
            controller.parameters = parameters;
        }
        var machine = controller.layers[0].stateMachine;
        var clips = new[] { idle, walk, bounce };
        var names = new[] { "Idle", "Walk", "Bounce" };
        for (var i = 0; i < clips.Length; i++)
        {
            var state = State(machine, names[i], clips[i]);
            if (i == 2) machine.defaultState = state;
            AnimatorStateTransition transition = null;
            foreach (var item in machine.anyStateTransitions)
                if (item.destinationState == state) { transition = item; break; }
            if (transition == null) transition = machine.AddAnyStateTransition(state);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.16f;
            transition.canTransitionToSelf = false;
            transition.conditions = new[] { new AnimatorCondition { mode = AnimatorConditionMode.Equals, parameter = "Motion", threshold = i } };
            EditorUtility.SetDirty(transition);
        }
        var expressionIndex = Array.FindIndex(controller.layers, candidate => candidate.name == "Expressions");
        if (expressionIndex < 0) { controller.AddLayer("Expressions"); expressionIndex = controller.layers.Length - 1; }
        var layers = controller.layers;
        var layer = layers[expressionIndex];
        layer.defaultWeight = 1;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.syncedLayerIndex = -1;
        layer.stateMachine.defaultState = State(layer.stateMachine, "ExpressionLoop", expressions);
        layers[expressionIndex] = layer;
        controller.layers = layers;
        EditorUtility.SetDirty(machine);
        EditorUtility.SetDirty(layer.stateMachine);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssetIfDirty(controller);
        return controller;
    }

    private static AnimatorState State(AnimatorStateMachine machine, string name, AnimationClip clip)
    {
        AnimatorState state = null;
        foreach (var item in machine.states) if (item.state.name == name) { state = item.state; break; }
        if (state == null) state = machine.AddState(name);
        state.motion = clip;
        state.writeDefaultValues = false;
        EditorUtility.SetDirty(state);
        return state;
    }

    private static void Curve(AnimationClip clip, string path, string property, float rest, float[] times, float[] offsets)
    {
        var keys = new Keyframe[times.Length];
        for (var i = 0; i < keys.Length; i++) keys[i] = new Keyframe(times[i], rest + offsets[i], 0, 0);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property),
            new AnimationCurve(keys) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.Loop });
    }
}
