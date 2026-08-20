using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 패배 배너(DefeatBanner) 저작 도구.
/// 승리 배너와 같은 재생 계약(VictoryBannerView + Base Layer 4상태)을 쓰되,
/// 부품 분해 없이 defeat.png 한 장을 통째로 떨어뜨린다 — 패배용 글자 아트가 없기 때문이다.
/// 아트가 준비되면 이 빌더가 굽는 VisualRoot 안쪽만 갈아끼우면 되고 런타임 코드는 그대로다.
/// </summary>
public static class DefeatBannerBuilder
{
    private const string DefeatSpritePath = "Assets/Assets/Images/UI/defeat.png";
    private const string PrefabFolder = "Assets/Assets/Prefabs/UI";
    private const string PrefabPath = PrefabFolder + "/DefeatBanner.prefab";
    private const string MotionFolder = "Assets/Assets/Animations/UI";
    private const string HiddenClipPath = MotionFolder + "/DefeatBanner_Hidden.anim";
    private const string ShowClipPath = MotionFolder + "/DefeatBanner_Show.anim";
    private const string ShownClipPath = MotionFolder + "/DefeatBanner_Shown.anim";
    private const string HideClipPath = MotionFolder + "/DefeatBanner_Hide.anim";
    private const string ControllerPath = MotionFolder + "/DefeatBanner.controller";

    // 승리 배너와 같은 저작 크기. 두 배너가 결과창의 같은 자리에 같은 배율로 들어가야 한다.
    private const float BannerWidth = 960f;
    private const float BannerHeight = 540.2871f;

    private const float ShowDuration = 1f;
    private const float HideDuration = 0.5f;

    // 낙하 시작 높이·배율. Hidden 포즈와 Show 첫 키가 같아야 컷 진입이 튀지 않는다.
    private const float DropHeight = 260f;
    private const float DropScale = 1.12f;

    [MenuItem("Tools/Result Banner/Rebuild Defeat Banner")]
    public static void Build()
    {
        Sprite face = AssetDatabase.LoadAssetAtPath<Sprite>(DefeatSpritePath);
        if (face == null)
            throw new InvalidOperationException("defeat.png 를 Sprite 로 읽지 못했다: " + DefeatSpritePath);

        GameObject root = BuildHierarchy(face, out RectTransform visualRoot, out CanvasGroup visualGroup);

        try
        {
            AnimatorController controller = BuildMotion(root.transform, visualRoot, visualGroup);
            root.GetComponent<Animator>().runtimeAnimatorController = controller;

            WireView(root, visualRoot.gameObject);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefab == null)
                throw new InvalidOperationException("패배 배너 프리팹 저장 실패: " + PrefabPath);

            AssetDatabase.SaveAssets();
            Debug.Log("[DefeatBannerBuilder] 패배 배너 갱신 완료: " + PrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static GameObject BuildHierarchy(Sprite face, out RectTransform visualRoot, out CanvasGroup visualGroup)
    {
        var root = new GameObject("DefeatBanner", typeof(RectTransform), typeof(Animator), typeof(VictoryBannerView));
        root.layer = LayerMask.NameToLayer("UI");
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = rootRect.anchorMax = rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = Vector2.zero;
        rootRect.sizeDelta = new Vector2(BannerWidth, BannerHeight);

        var visual = new GameObject("VisualRoot", typeof(RectTransform), typeof(CanvasGroup));
        visual.layer = root.layer;
        visualRoot = (RectTransform)visual.transform;
        visualRoot.SetParent(rootRect, false);
        Stretch(visualRoot);
        visualGroup = visual.GetComponent<CanvasGroup>();

        var faceGo = new GameObject("Face", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        faceGo.layer = root.layer;
        var faceRect = (RectTransform)faceGo.transform;
        faceRect.SetParent(visualRoot, false);
        Stretch(faceRect);
        var image = faceGo.GetComponent<Image>();
        image.sprite = face;
        image.raycastTarget = false;
        image.preserveAspect = true;

        return root;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static AnimatorController BuildMotion(Transform root, RectTransform visualRoot, CanvasGroup visualGroup)
    {
        AnimationClip hidden = LoadOrCreateClip(HiddenClipPath, "DefeatBanner_Hidden");
        AnimationClip show = LoadOrCreateClip(ShowClipPath, "DefeatBanner_Show");
        AnimationClip shown = LoadOrCreateClip(ShownClipPath, "DefeatBanner_Shown");
        AnimationClip hide = LoadOrCreateClip(HideClipPath, "DefeatBanner_Hide");

        // Hidden — Show 첫 키와 같은 포즈.
        Pose(hidden, root, visualRoot, visualGroup, DropHeight, DropScale, 0f, 0.02f);

        // Show — 무겁게 떨어져 한 박에 박힌다. 착지 뒤 살짝 주저앉고 원위치.
        // 스쿼시·회전은 넣지 않는다 — 사건은 한 프레임에 몰고 형태는 건드리지 않는다.
        SetY(show, root, visualRoot, new[]
        {
            Key(0f,    DropHeight, 0f,     -260f),
            Key(0.26f, 0f,         -2600f, -420f),   // 슬램 착지
            Key(0.36f, -14f,       0f,     0f),      // 무게로 주저앉음
            Key(0.50f, 0f,         0f,     0f),
            Key(ShowDuration, 0f,  0f,     0f),
        });
        SetScale(show, root, visualRoot, new[]
        {
            Key(0f,    DropScale, 0f,    -0.35f),
            Key(0.26f, 1f,        -0.9f, 0f),
            Key(0.36f, 0.985f,    0f,    0f),
            Key(0.50f, 1f,        0f,    0f),
            Key(ShowDuration, 1f, 0f,    0f),
        });
        SetAlpha(show, root, visualGroup, new[]
        {
            Key(0f,    0f, 0f, 0f),
            Key(0.10f, 1f, 0f, 0f),
            Key(ShowDuration, 1f, 0f, 0f),
        });

        // Shown — 정지 포즈. 이 포즈가 곧 연출 전의 화면이다.
        Pose(shown, root, visualRoot, visualGroup, 0f, 1f, 1f, 0.02f);

        // Hide — 가라앉으며 사라진다.
        SetY(hide, root, visualRoot, new[]
        {
            Key(0f, 0f, 0f, 0f),
            Key(HideDuration, -30f, 0f, 0f),
        });
        SetScale(hide, root, visualRoot, new[]
        {
            Key(0f, 1f, 0f, 0f),
            Key(HideDuration, 0.94f, 0f, 0f),
        });
        SetAlpha(hide, root, visualGroup, new[]
        {
            Key(0f, 1f, 0f, 0f),
            Key(HideDuration, 0f, 0f, 0f),
        });

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        for (int i = machine.states.Length - 1; i >= 0; i--)
            machine.RemoveState(machine.states[i].state);

        // 상태 이름은 VictoryBannerView 가 들고 있는 계약이다 — 승리든 패배든 같은 이름을 쓴다.
        AnimatorState hiddenState = machine.AddState(VictoryBannerView.HiddenStateName);
        AnimatorState showState = machine.AddState(VictoryBannerView.ShowStateName);
        AnimatorState shownState = machine.AddState(VictoryBannerView.ShownStateName);
        AnimatorState hideState = machine.AddState(VictoryBannerView.HideStateName);

        hiddenState.motion = hidden;
        showState.motion = show;
        shownState.motion = shown;
        hideState.motion = hide;

        foreach (AnimatorState state in new[] { hiddenState, showState, shownState, hideState })
        {
            state.writeDefaultValues = true;
            state.speed = 1f;
        }

        machine.defaultState = hiddenState;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void Pose(
        AnimationClip clip, Transform root, RectTransform visualRoot, CanvasGroup group,
        float y, float scale, float alpha, float length)
    {
        SetY(clip, root, visualRoot, new[] { Key(0f, y, 0f, 0f), Key(length, y, 0f, 0f) });
        SetScale(clip, root, visualRoot, new[] { Key(0f, scale, 0f, 0f), Key(length, scale, 0f, 0f) });
        SetAlpha(clip, root, group, new[] { Key(0f, alpha, 0f, 0f), Key(length, alpha, 0f, 0f) });
    }

    private static Keyframe Key(float time, float value, float inTangent, float outTangent)
    {
        return new Keyframe(time, value, inTangent, outTangent);
    }

    private static void SetY(AnimationClip clip, Transform root, RectTransform target, Keyframe[] keys)
    {
        SetCurve(clip, root, target, typeof(RectTransform), "m_AnchoredPosition.y", keys);
        SetCurve(clip, root, target, typeof(RectTransform), "m_AnchoredPosition.x", Flat(keys, 0f));
    }

    private static void SetScale(AnimationClip clip, Transform root, RectTransform target, Keyframe[] keys)
    {
        SetCurve(clip, root, target, typeof(Transform), "m_LocalScale.x", keys);
        SetCurve(clip, root, target, typeof(Transform), "m_LocalScale.y", keys);
        SetCurve(clip, root, target, typeof(Transform), "m_LocalScale.z", keys);
    }

    private static void SetAlpha(AnimationClip clip, Transform root, CanvasGroup target, Keyframe[] keys)
    {
        SetCurve(clip, root, target.transform, typeof(CanvasGroup), "m_Alpha", keys);
    }

    private static Keyframe[] Flat(Keyframe[] source, float value)
    {
        var keys = new Keyframe[source.Length];
        for (int i = 0; i < source.Length; i++)
            keys[i] = new Keyframe(source[i].time, value, 0f, 0f);
        return keys;
    }

    private static void SetCurve(AnimationClip clip, Transform root, Transform target, Type type, string property, Keyframe[] keys)
    {
        string path = AnimationUtility.CalculateTransformPath(target, root);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), new AnimationCurve(keys));
    }

    private static AnimationClip LoadOrCreateClip(string path, string name)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip { name = name };
            AssetDatabase.CreateAsset(clip, path);
        }

        clip.name = name;
        clip.ClearCurves();
        AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);   // 재사용 시 잔류 이벤트까지 지워야 멱등하다
        clip.legacy = false;

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    private static void WireView(GameObject root, GameObject visualRoot)
    {
        var view = root.GetComponent<VictoryBannerView>();
        var so = new SerializedObject(view);
        so.FindProperty("visualRoot").objectReferenceValue = visualRoot;
        so.FindProperty("animator").objectReferenceValue = root.GetComponent<Animator>();
        so.FindProperty("showDuration").floatValue = ShowDuration;
        so.FindProperty("hideDuration").floatValue = HideDuration;
        so.FindProperty("reversalBlendDuration").floatValue = 0.06f;
        so.FindProperty("rearBurstParticles").arraySize = 0;
        so.FindProperty("shineBands").arraySize = 0;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
