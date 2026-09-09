using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates a cutout idle variant, preserving the source walk and painted parts.</summary>
public static class BalloonPengIdleBuilder
{
    private const string Folder = "Assets/ArtPrototypes/BalloonPengIsometric/Cutout/";
    private const string SourcePath = Folder + "BalloonPeng_Cutout_Walk.prefab";
    private const string PrefabPath = Folder + "BalloonPeng_Cutout_Idle.prefab";
    private const string ClipPath = Folder + "Idle.anim";
    private const string ControllerPath = Folder + "Idle.controller";
    private const string ScenePath = Folder + "BalloonPeng_Idle_Preview.unity";
    private const float Duration = 2.4f;

    [MenuItem("Tools/Art Prototypes/Build BalloonPeng Cutout Idle")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build idle outside Play Mode.");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "Walk.anim");
        var expressions = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "ExpressionLoop.anim");
        if (source == null || walk == null || expressions == null)
            throw new InvalidOperationException("Build the cutout walk and expressions before the idle variant.");
        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            var animator = instance.GetComponent<Animator>();
            if (animator == null) throw new InvalidOperationException("The source cutout Animator is missing.");
            var idle = SaveIdle(instance.transform);
            var controller = SaveController(idle, walk, expressions);
            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (variant == null)
            {
                // The connected source instance makes this a prefab variant, sharing every painted part.
                animator.runtimeAnimatorController = controller;
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                variant = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
                if (variant == null) throw new InvalidOperationException("Could not save the idle prefab variant.");
            }
            else
            {
                var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    var variantAnimator = contents.GetComponent<Animator>();
                    if (variantAnimator == null) throw new InvalidOperationException("The idle variant Animator is missing.");
                    variantAnimator.runtimeAnimatorController = controller;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(variantAnimator);
                    if (PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath) == null)
                        throw new InvalidOperationException("Could not update the idle prefab variant.");
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            UnityEngine.Object.DestroyImmediate(instance);
            // Existing preview scenes retain their authored settings and inherit variant asset updates.
            if (!File.Exists(ScenePath) && string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(ScenePath)))
            {
                PrefabUtility.InstantiatePrefab(variant, scene);
                var camera = new GameObject("Idle Preview Camera").AddComponent<Camera>();
                camera.tag = "MainCamera";
                camera.transform.position = new Vector3(0, 1.58f, -10);
                camera.orthographic = true;
                camera.orthographicSize = 2.12f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.45f, 0.53f, 0.38f);
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 30;
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Could not save the idle preview scene.");
            }
            Selection.activeObject = variant;
            Debug.Log("BalloonPeng Idle variant saved: 2.4-second idle, IsWalking transitions and shared expressions.");
        }
        finally
        {
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("Tools/Art Prototypes/Update BalloonPeng Cutout Idle")]
    public static void Update() => Build();

    private static AnimationClip SaveIdle(Transform root)
    {
        var pose = Require(root, "Pose");
        var far = Require(root, "Pose/WingFar");
        var near = Require(root, "Pose/WingNear");
        var temporary = new AnimationClip { name = "Idle", frameRate = 30, wrapMode = WrapMode.Loop };
        try
        {
            var poseY = pose.localPosition.y;
            var poseAngle = pose.localEulerAngles.z;
            var farAngle = Mathf.DeltaAngle(0, far.localEulerAngles.z);
            var nearAngle = Mathf.DeltaAngle(0, near.localEulerAngles.z);
            Curve(temporary, "Pose", "m_LocalPosition.y", a => poseY + 0.035f * (1 - Mathf.Cos(a)));
            Curve(temporary, "Pose", "localEulerAnglesRaw.z", a => poseAngle + 0.6f * Mathf.Sin(a));
            Curve(temporary, "Pose/WingFar", "localEulerAnglesRaw.z", a => farAngle - 20 * Mathf.Sin(a + 0.35f));
            Curve(temporary, "Pose/WingNear", "localEulerAnglesRaw.z", a => nearAngle + 20 * Mathf.Sin(a + 0.35f));
            foreach (var name in new[] { "FootFar", "FootNear" })
            {
                var foot = Require(root, name);
                var rest = foot.localPosition;
                var angle = foot.localEulerAngles.z;
                // Explicitly overwrite every foot property animated by Walk, preventing transition residue.
                Curve(temporary, name, "m_LocalPosition.x", a => rest.x);
                Curve(temporary, name, "m_LocalPosition.y", a => rest.y);
                Curve(temporary, name, "localEulerAnglesRaw.z", a => angle);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(temporary);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(temporary, settings);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            if (clip == null)
            {
                clip = new AnimationClip { name = "Idle" };
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
        var child = root.Find(path);
        if (child == null) throw new InvalidOperationException("The source cutout is missing " + path);
        return child;
    }

    private static AnimatorController SaveController(AnimationClip idle, AnimationClip walk, AnimationClip expressions)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        if (!Array.Exists(controller.parameters, parameter => parameter.name == "IsWalking"))
            controller.AddParameter("IsWalking", AnimatorControllerParameterType.Bool);
        var machine = controller.layers[0].stateMachine;
        var idleState = State(machine, "Idle", idle);
        var walkState = State(machine, "Walk", walk);
        machine.defaultState = idleState;
        Transition(idleState, walkState, AnimatorConditionMode.If);
        Transition(walkState, idleState, AnimatorConditionMode.IfNot);
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
        foreach (var child in machine.states) if (child.state.name == name) { state = child.state; break; }
        if (state == null) state = machine.AddState(name);
        state.motion = clip;
        state.writeDefaultValues = false;
        EditorUtility.SetDirty(state);
        return state;
    }

    private static void Transition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode)
    {
        AnimatorStateTransition transition = null;
        foreach (var item in from.transitions) if (item.destinationState == to) { transition = item; break; }
        if (transition == null) transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = 0.18f;
        transition.canTransitionToSelf = false;
        transition.conditions = new[] { new AnimatorCondition { mode = mode, parameter = "IsWalking", threshold = 0 } };
        EditorUtility.SetDirty(transition);
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
