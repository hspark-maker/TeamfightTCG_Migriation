using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Source of truth for the Victory banner prefab, Animator assets,
/// particle atlas setup, and review scene. The prefab it bakes is the production
/// title used by WinUI — the TEST scene is a lab for it, not a separate copy.
/// </summary>
public static class VictoryBannerTestBuilder
{
    private const string ImageFolder = "Assets/Assets/Images/UI/VictoryTitleDynamic";
    private const string ParticleAtlasPath = ImageFolder + "/victory_particle_color_atlas.png";
    private const string ParticleAtlasGuid = "9c7cb9793b744886aa7991c09237fe71";
    private const int ParticleAtlasColumns = 2;
    private const int ParticleAtlasRows = 2;
    private const int ParticleAtlasTileSize = 64;
    private const int ParticleAtlasSize = ParticleAtlasTileSize * ParticleAtlasColumns;
    private const string PrefabFolder = "Assets/Assets/Prefabs/UI";
    private const string PrefabPath = PrefabFolder + "/VictoryBanner.prefab";
    private const string SceneFolder = "Assets/Scenes/TEST";
    private const string ScenePath = SceneFolder + "/VictoryBannerTest.unity";
    private const string MotionFolder = "Assets/Assets/Animations/UI";
    private const string HiddenClipPath = MotionFolder + "/VictoryBanner_Hidden.anim";
    private const string ShowClipPath = MotionFolder + "/VictoryBanner_Show.anim";
    private const string ShownClipPath = MotionFolder + "/VictoryBanner_Shown.anim";
    private const string HideClipPath = MotionFolder + "/VictoryBanner_Hide.anim";
    private const string ControllerPath = MotionFolder + "/VictoryBanner.controller";

    private const float SourceWidth = 1672f;
    private const float SourceHeight = 941f;
    private const float BannerWidth = 960f;
    private const float ShowDuration = 2f;
    private const float HideDuration = 0.75f;

    private static readonly RectInt RibbonSourceRect = new RectInt(55, 370, 1560, 535);
    private static readonly RectInt MedalSourceRect = new RectInt(608, 649, 456, 236);

    // This is intentionally limited to the seven decorations left by the user.
    // Deleted stars and all sprite confetti must never be recreated.
    private static readonly DecorationAssetSpec[] BackDecorationSpecs =
    {
        new DecorationAssetSpec("WingLeft", "victory_decor_wing_left.png", new RectInt(268, 176, 386, 347), 0.84f, 0.30f, 0.08f, 0.36f, new Vector2(-8f, -72f), 0.42f, -5f, 1.06f),
        new DecorationAssetSpec("WingRight", "victory_decor_wing_right.png", new RectInt(1059, 170, 354, 354), 0.87f, 0.30f, 0.10f, 0.36f, new Vector2(8f, -72f), 0.42f, 5f, 1.06f),
        new DecorationAssetSpec("CardLeft", "victory_decor_card_left.png", new RectInt(400, 164, 286, 339), 0.88f, 0.30f, 0.12f, 0.36f, new Vector2(-6f, -68f), 0.38f, -6f, 1.07f),
        new DecorationAssetSpec("CardRight", "victory_decor_card_right.png", new RectInt(1003, 170, 277, 325), 0.91f, 0.30f, 0.14f, 0.36f, new Vector2(6f, -68f), 0.38f, 6f, 1.07f),
        new DecorationAssetSpec("Crown", "victory_decor_crown.png", new RectInt(575, 119, 540, 360), 0.91f, 0.32f, 0.16f, 0.38f, new Vector2(0f, -88f), 0.32f, -4f, 1.08f),
        new DecorationAssetSpec("TrumpetLeft", "victory_decor_trumpet_left.png", new RectInt(58, 278, 439, 263), 0.94f, 0.28f, 0.18f, 0.34f, new Vector2(-8f, -54f), 0.40f, -7f, 1.10f),
        new DecorationAssetSpec("TrumpetRight", "victory_decor_trumpet_right.png", new RectInt(1183, 286, 435, 257), 0.97f, 0.28f, 0.20f, 0.34f, new Vector2(8f, -54f), 0.40f, 7f, 1.10f)
    };

    private static readonly string[] LetterNames = { "V", "I", "C", "T", "O", "R", "Y" };
    private static readonly string[] LetterPaths =
    {
        ImageFolder + "/victory_letter_v.png",
        ImageFolder + "/victory_letter_i.png",
        ImageFolder + "/victory_letter_c.png",
        ImageFolder + "/victory_letter_t.png",
        ImageFolder + "/victory_letter_o.png",
        ImageFolder + "/victory_letter_r.png",
        ImageFolder + "/victory_letter_y.png"
    };

    private static readonly Vector2[] LetterCenters =
    {
        new Vector2(387f, 601f), new Vector2(515f, 574f), new Vector2(644f, 545f),
        new Vector2(798f, 521f), new Vector2(968f, 535f), new Vector2(1138f, 568f),
        new Vector2(1320f, 603f)
    };

    private static readonly Vector2[] LetterSourceSizes =
    {
        new Vector2(204f, 245f), new Vector2(104f, 211f), new Vector2(178f, 211f),
        new Vector2(161f, 204f), new Vector2(185f, 210f), new Vector2(181f, 239f),
        new Vector2(190f, 237f)
    };

    [MenuItem("Tools/Result Banner/Rebuild Victory Banner + Lab Scene")]
    public static void BuildAndOpen()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        BuildTestEnvironment();
    }

    public static void BuildForBatch()
    {
        BuildTestEnvironment(true);
    }

    [MenuItem("Tools/Result Banner/Open Victory Lab Scene %#v")]
    public static void OpenTestSceneOnLaunch()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        if (currentScene.path != ScenePath && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
            throw new InvalidOperationException($"Could not open Victory banner test scene: {ScenePath}");

        VictoryBannerView banner = UnityEngine.Object.FindFirstObjectByType<VictoryBannerView>();
        if (banner != null)
            Selection.activeGameObject = banner.gameObject;

        EditorApplication.delayCall += () =>
        {
            SceneView.FrameLastActiveSceneView();
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    EditorApplication.isPlaying = true;
            };
        };
    }

    private static void BuildTestEnvironment(bool replaceCurrentScene = false)
    {
        EnsureFolder(PrefabFolder);
        EnsureFolder(SceneFolder);
        EnsureFolder(MotionFolder);
        ImportSprites();
        ImportParticleAtlas();
        GameObject prefab = BuildPrefab();
        BuildScene(prefab, replaceCurrentScene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[VictoryBannerTestBuilder] Test environment ready: {ScenePath}");
    }

    private static void ImportSprites()
    {
        var paths = new HashSet<string>
        {
            ImageFolder + "/victory_ribbon.png",
            ImageFolder + "/victory_decor_front.png",
            ImageFolder + "/victory_reference_original.png"
        };

        foreach (string path in LetterPaths)
            paths.Add(path);
        foreach (DecorationAssetSpec spec in BackDecorationSpecs)
            paths.Add(spec.Path);

        foreach (string path in paths)
        {
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException($"Victory sprite is missing or not importable: {path}");

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.crunchedCompression = false;

            TextureImporterSettings settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(0.5f, 0.5f);
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            ConfigureMobilePlatform(importer, "Android", 2048);
            ConfigureMobilePlatform(importer, "iPhone", 2048);
            importer.SaveAndReimport();
        }
    }

    private static void ImportParticleAtlas()
    {
        GenerateParticleAtlasPng();
        AssetDatabase.ImportAsset(
            ParticleAtlasPath,
            ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(ParticleAtlasPath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException($"Victory particle atlas is missing or not importable: {ParticleAtlasPath}");

        importer.textureType = TextureImporterType.Sprite;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.generateCubemap = TextureImporterGenerateCubemap.None;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 100f;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = ParticleAtlasSize;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        settings.spritePivot = new Vector2(0.5f, 0.5f);
        settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);
        ConfigureMobilePlatform(importer, "Android", ParticleAtlasSize);
        ConfigureMobilePlatform(importer, "iPhone", ParticleAtlasSize);
        importer.SaveAndReimport();

        AssetDatabase.ImportAsset(
            ParticleAtlasPath,
            ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        Sprite atlas = AssetDatabase.LoadAssetAtPath<Sprite>(ParticleAtlasPath);
        if (atlas == null)
            throw new InvalidOperationException($"Victory particle atlas failed to import: {ParticleAtlasPath}");

        string importedGuid = AssetDatabase.AssetPathToGUID(ParticleAtlasPath);
        if (!string.Equals(importedGuid, ParticleAtlasGuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Victory particle atlas GUID changed: {importedGuid}");
    }

    private static void GenerateParticleAtlasPng()
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        if (string.IsNullOrEmpty(projectRoot))
            throw new InvalidOperationException("Could not resolve the Unity project root.");

        string absolutePath = Path.Combine(projectRoot, ParticleAtlasPath.Replace('/', Path.DirectorySeparatorChar));
        string metaPath = absolutePath + ".meta";
        if (!File.Exists(metaPath))
            throw new InvalidOperationException($"Victory particle atlas meta must keep its fixed GUID: {ParticleAtlasPath}.meta");

        string existingGuid = AssetDatabase.AssetPathToGUID(ParticleAtlasPath);
        if (!string.IsNullOrEmpty(existingGuid) &&
            !string.Equals(existingGuid, ParticleAtlasGuid, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Victory particle atlas GUID mismatch: {existingGuid}");
        }

        var texture = new Texture2D(ParticleAtlasSize, ParticleAtlasSize, TextureFormat.RGBA32, false)
        {
            name = "VictoryParticleSimpleMaskAtlas",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        var pixels = new Color32[ParticleAtlasSize * ParticleAtlasSize];
        for (int tileY = 0; tileY < ParticleAtlasRows; tileY++)
        {
            for (int tileX = 0; tileX < ParticleAtlasColumns; tileX++)
            {
                int tileIndex = tileY * ParticleAtlasColumns + tileX;
                for (int y = 0; y < ParticleAtlasTileSize; y++)
                {
                    float normalizedY = ((y + 0.5f) / ParticleAtlasTileSize) * 2f - 1f;
                    for (int x = 0; x < ParticleAtlasTileSize; x++)
                    {
                        float normalizedX = ((x + 0.5f) / ParticleAtlasTileSize) * 2f - 1f;
                        float alpha = EvaluateParticleMask(tileIndex, normalizedX, normalizedY);
                        int atlasX = tileX * ParticleAtlasTileSize + x;
                        int atlasY = tileY * ParticleAtlasTileSize + y;
                        pixels[atlasY * ParticleAtlasSize + atlasX] =
                            new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                    }
                }
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        byte[] encoded = texture.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(texture);
        if (encoded == null || encoded.Length == 0)
            throw new InvalidOperationException("Could not encode the Victory particle mask atlas.");

        if (!File.Exists(absolutePath) || !BytesEqual(File.ReadAllBytes(absolutePath), encoded))
            File.WriteAllBytes(absolutePath, encoded);
    }

    private static float EvaluateParticleMask(int tileIndex, float x, float y)
    {
        float absoluteX = Mathf.Abs(x);
        float absoluteY = Mathf.Abs(y);
        float radius = Mathf.Sqrt(x * x + y * y);
        float signedDistance;

        switch (tileIndex)
        {
            case 0: // Dot
                signedDistance = 0.62f - radius;
                break;
            case 1: // Diamond
                signedDistance = 0.78f - absoluteX - absoluteY;
                break;
            case 2: // Four-point spark
                float angle = Mathf.Atan2(y, x);
                float sparkRadius = 0.18f + 0.62f * Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 2f)), 5f);
                signedDistance = sparkRadius - radius;
                break;
            default: // Slanted shard
                const float rotation = 18f * Mathf.Deg2Rad;
                float cosine = Mathf.Cos(rotation);
                float sine = Mathf.Sin(rotation);
                float rotatedX = x * cosine - y * sine;
                float rotatedY = x * sine + y * cosine;
                float taper = Mathf.Lerp(0.30f, 0.12f, Mathf.InverseLerp(-0.74f, 0.74f, rotatedY));
                float centerX = -0.10f * rotatedY;
                signedDistance = Mathf.Min(0.74f - Mathf.Abs(rotatedY), taper - Mathf.Abs(rotatedX - centerX));
                break;
        }

        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.045f, 0.045f, signedDistance));
    }

    private static bool BytesEqual(byte[] left, byte[] right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null || left.Length != right.Length)
            return false;
        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
                return false;
        }
        return true;
    }

    private static void ConfigureMobilePlatform(TextureImporter importer, string platformName, int maxTextureSize)
    {
        TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings(platformName);
        platform.name = platformName;
        platform.overridden = true;
        platform.maxTextureSize = maxTextureSize;
        platform.resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
        platform.format = TextureImporterFormat.ASTC_4x4;
        platform.textureCompression = TextureImporterCompression.CompressedHQ;
        platform.compressionQuality = 100;
        importer.SetPlatformTextureSettings(platform);
    }

    private static GameObject BuildPrefab()
    {
        float bannerHeight = BannerWidth * SourceHeight / SourceWidth;
        Vector2 bannerSize = new Vector2(BannerWidth, bannerHeight);
        float sourceToUi = BannerWidth / SourceWidth;

        GameObject root = CreateRectObject("VictoryBannerTest", null);
        SetCenteredRect(root.GetComponent<RectTransform>(), bannerSize, Vector2.zero);
        Animator animator = root.AddComponent<Animator>();
        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        VictoryBannerView view = root.AddComponent<VictoryBannerView>();

        GameObject visualRoot = CreateRectObject("VisualRoot", root.transform);
        SetCenteredRect(visualRoot.GetComponent<RectTransform>(), bannerSize, Vector2.zero);
        Sprite particleAtlas = LoadSprite(ParticleAtlasPath);

        GameObject particleRoot = CreateRectObject("RearParticleBurstRoot", visualRoot.transform);
        SetCenteredRect(particleRoot.GetComponent<RectTransform>(), bannerSize, Vector2.zero);
        ParticleSystem[] particles =
        {
            CreateRearBurst("RearBurstLeft", particleRoot.transform, new Vector2(-365f, -65f), 1, particleAtlas),
            CreateRearBurst("RearBurstRight", particleRoot.transform, new Vector2(365f, -65f), -1, particleAtlas)
        };

        GameObject backRoot = CreateRectObject("BackFanfareRiseRoot", visualRoot.transform);
        SetCenteredRect(backRoot.GetComponent<RectTransform>(), bannerSize, Vector2.zero);
        var decorations = new List<DecorationBuildData>(BackDecorationSpecs.Length);
        foreach (DecorationAssetSpec spec in BackDecorationSpecs)
            decorations.Add(CreateDecoration(spec, backRoot.transform));

        LayerData ribbon = CreatePlacedLayer("Ribbon", visualRoot.transform, ImageFolder + "/victory_ribbon.png", RibbonSourceRect);

        GameObject letterRootObject = CreateRectObject("LetterRoot", visualRoot.transform);
        RectTransform letterRoot = letterRootObject.GetComponent<RectTransform>();
        SetCenteredRect(letterRoot, bannerSize, Vector2.zero);
        var letters = new List<LayerData>(LetterPaths.Length);
        var shines = new List<ShineBuildData>(LetterPaths.Length + 1);

        for (int index = 0; index < LetterPaths.Length; index++)
        {
            Sprite sprite = LoadSprite(LetterPaths[index]);
            GameObject letterObject = CreateRectObject($"Letter_{LetterNames[index]}", letterRoot);
            RectTransform rect = letterObject.GetComponent<RectTransform>();
            Vector2 uiPosition = SourcePointToUi(LetterCenters[index]);
            Vector2 uiSize = LetterSourceSizes[index] * sourceToUi;
            SetCenteredRect(rect, uiSize, uiPosition);

            CreateLetterLayer(letterObject.transform, "ShadowFar", sprite, uiSize, new Vector2(4.2f, -7.2f), new Color(0.18f, 0.065f, 0.018f, 0.28f));
            CreateLetterLayer(letterObject.transform, "ShadowNear", sprite, uiSize, new Vector2(2.1f, -3.6f), new Color(0.34f, 0.13f, 0.028f, 0.44f));
            Image face = CreateLetterLayer(letterObject.transform, "Face", sprite, uiSize, Vector2.zero, Color.white);
            Mask faceMask = face.gameObject.AddComponent<Mask>();
            faceMask.showMaskGraphic = true;
            shines.Add(CreateShineBand(face.transform, $"LetterShine_{LetterNames[index]}", uiSize, 1.58f + index * 0.035f, 0.16f));
            letters.Add(new LayerData(rect, letterObject.AddComponent<CanvasGroup>()));
        }

        LayerData medal = CreatePlacedLayer("ForegroundMedalImpactRoot", visualRoot.transform, ImageFolder + "/victory_decor_front.png", MedalSourceRect);
        GameObject medalMaskObject = CreateRectObject("ForegroundMedalStarShineMask", medal.Rect);
        Vector2 medalStarSize = new Vector2(154f, 160f) * sourceToUi;
        SetCenteredRect(medalMaskObject.GetComponent<RectTransform>(), medalStarSize, new Vector2(0f, 4f * sourceToUi));
        VictoryStarMaskGraphic medalMaskGraphic = medalMaskObject.AddComponent<VictoryStarMaskGraphic>();
        medalMaskGraphic.raycastTarget = false;
        medalMaskGraphic.color = Color.white;
        Mask medalMask = medalMaskObject.AddComponent<Mask>();
        medalMask.showMaskGraphic = false;
        shines.Add(CreateShineBand(medalMaskObject.transform, "ForegroundMedalStarShine", medalStarSize, 1.72f, 0.20f));

        MotionAssets motion = BuildMotionAssets(root.transform, ribbon, letters, decorations, medal, shines);
        animator.runtimeAnimatorController = motion.Controller;

        SerializedObject serializedView = new SerializedObject(view);
        serializedView.FindProperty("visualRoot").objectReferenceValue = visualRoot;
        serializedView.FindProperty("animator").objectReferenceValue = animator;
        serializedView.FindProperty("showDuration").floatValue = ShowDuration;
        serializedView.FindProperty("hideDuration").floatValue = HideDuration;
        serializedView.FindProperty("reversalBlendDuration").floatValue = 0.06f;
        AssignArray(serializedView.FindProperty("rearBurstParticles"), particles);
        Image[] shineImages = new Image[shines.Count];
        for (int index = 0; index < shines.Count; index++)
            shineImages[index] = shines[index].Band;
        AssignArray(serializedView.FindProperty("shineBands"), shineImages);
        serializedView.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        if (prefab == null)
            throw new InvalidOperationException($"Could not save Victory banner prefab: {PrefabPath}");
        return prefab;
    }

    private static MotionAssets BuildMotionAssets(
        Transform root,
        LayerData ribbon,
        IReadOnlyList<LayerData> letters,
        IReadOnlyList<DecorationBuildData> decorations,
        LayerData medal,
        IReadOnlyList<ShineBuildData> shines)
    {
        AnimationClip hidden = LoadOrCreateClip(HiddenClipPath, "VictoryBanner_Hidden");
        AnimationClip show = LoadOrCreateClip(ShowClipPath, "VictoryBanner_Show");
        AnimationClip shown = LoadOrCreateClip(ShownClipPath, "VictoryBanner_Shown");
        AnimationClip hide = LoadOrCreateClip(HideClipPath, "VictoryBanner_Hide");
        ClearClip(hidden);
        ClearClip(show);
        ClearClip(shown);
        ClearClip(hide);

        BuildHiddenClip(hidden, root, ribbon, letters, decorations, medal, shines);
        BuildShowClip(show, root, ribbon, letters, decorations, medal, shines);
        BuildShownClip(shown, root, ribbon, letters, decorations, medal, shines);
        BuildHideClip(hide, root, ribbon, letters, decorations, medal, shines);

        AnimationUtility.SetAnimationEvents(show, Array.Empty<AnimationEvent>());
        AnimationUtility.SetAnimationEvents(hide, new[]
        {
            new AnimationEvent { time = 0f, functionName = nameof(VictoryBannerView.StopRearBurst) }
        });

        EditorUtility.SetDirty(hidden);
        EditorUtility.SetDirty(show);
        EditorUtility.SetDirty(shown);
        EditorUtility.SetDirty(hide);
        AnimatorController controller = BuildController(hidden, show, shown, hide);
        return new MotionAssets(controller);
    }

    private static void BuildHiddenClip(
        AnimationClip clip,
        Transform root,
        LayerData ribbon,
        IReadOnlyList<LayerData> letters,
        IReadOnlyList<DecorationBuildData> decorations,
        LayerData medal,
        IReadOnlyList<ShineBuildData> shines)
    {
        SetPositionCurves(clip, root, ribbon.Rect, Keys(0f, ribbon.Rect.anchoredPosition));
        SetScaleCurves(clip, root, ribbon.Rect, Keys3(0f, new Vector3(0.08f, 1f, 1f)));
        SetRotationCurve(clip, root, ribbon.Rect, Keys(0f, 0f));
        SetAlphaCurve(clip, root, ribbon.Group, Keys(0f, 0f));

        foreach (LayerData letter in letters)
        {
            SetPositionCurves(clip, root, letter.Rect, Keys(0f, letter.Rect.anchoredPosition + new Vector2(0f, 72f)));
            SetScaleCurves(clip, root, letter.Rect, Keys3(0f, new Vector3(0.86f, 0.86f, 1f)));
            SetRotationCurve(clip, root, letter.Rect, Keys(0f, 0f));
            SetAlphaCurve(clip, root, letter.Group, Keys(0f, 0f));
        }

        foreach (DecorationBuildData decoration in decorations)
        {
            SetPositionCurves(clip, root, decoration.Rect, Keys(0f, decoration.AuthoredPosition + decoration.Spec.HiddenOffset));
            SetScaleCurves(clip, root, decoration.Rect, Keys3(0f, new Vector3(decoration.Spec.HiddenScale, decoration.Spec.HiddenScale, 1f)));
            SetRotationCurve(clip, root, decoration.Rect, Keys(0f, decoration.Spec.HiddenRotation));
            SetAlphaCurve(clip, root, decoration.Group, Keys(0f, 0f));
        }

        SetPositionCurves(clip, root, medal.Rect, Keys(0f, medal.Rect.anchoredPosition + new Vector2(0f, 20f)));
        SetScaleCurves(clip, root, medal.Rect, Keys3(0f, new Vector3(1.18f, 1.18f, 1f)));
        SetRotationCurve(clip, root, medal.Rect, Keys(0f, 0f));
        SetAlphaCurve(clip, root, medal.Group, Keys(0f, 0f));
        SetShineHidden(clip, root, shines, 0f);
        PinClipLength(clip, root, HideDuration);
    }

    private static void BuildShowClip(
        AnimationClip clip,
        Transform root,
        LayerData ribbon,
        IReadOnlyList<LayerData> letters,
        IReadOnlyList<DecorationBuildData> decorations,
        LayerData medal,
        IReadOnlyList<ShineBuildData> shines)
    {
        Vector2 ribbonPosition = ribbon.Rect.anchoredPosition;
        SetPositionCurves(clip, root, ribbon.Rect, Keys(0f, ribbonPosition, ShowDuration, ribbonPosition));
        SetRotationCurve(clip, root, ribbon.Rect, Keys(0f, 0f, ShowDuration, 0f));
        SetScaleCurves(clip, root, ribbon.Rect, Keys3(
            0f, new Vector3(0.08f, 1f, 1f),
            0.16f, new Vector3(1.10f, 1f, 1f),
            0.30f, new Vector3(0.99f, 1f, 1f),
            0.38f, Vector3.one,
            ShowDuration, Vector3.one));
        SetAlphaCurve(clip, root, ribbon.Group, Keys(0f, 0f, 0.025f, 1f, ShowDuration, 1f));

        for (int index = 0; index < letters.Count; index++)
        {
            LayerData letter = letters[index];
            float start = 0.22f + index * 0.09f;
            float end = start + 0.30f;
            Vector2 authored = letter.Rect.anchoredPosition;
            SetPositionCurves(clip, root, letter.Rect, Keys(
                0f, authored + new Vector2(0f, 72f),
                start, authored + new Vector2(0f, 72f),
                start + 0.20f, authored + new Vector2(0f, -3f),
                end, authored,
                ShowDuration, authored));
            SetScaleCurves(clip, root, letter.Rect, Keys3(
                0f, new Vector3(0.86f, 0.86f, 1f),
                start, new Vector3(0.86f, 0.86f, 1f),
                start + 0.19f, new Vector3(1.07f, 0.94f, 1f),
                start + 0.245f, new Vector3(0.97f, 1.04f, 1f),
                end, Vector3.one,
                ShowDuration, Vector3.one));
            SetRotationCurve(clip, root, letter.Rect, Keys(0f, 0f, ShowDuration, 0f));
            SetAlphaCurve(clip, root, letter.Group, Keys(0f, 0f, start, 0f, start + 0.025f, 1f, ShowDuration, 1f));
        }

        foreach (DecorationBuildData decoration in decorations)
        {
            DecorationAssetSpec spec = decoration.Spec;
            float settle = spec.ShowAt + spec.ShowDuration;
            Vector2 hidden = decoration.AuthoredPosition + spec.HiddenOffset;
            SetPositionCurves(clip, root, decoration.Rect, Keys(
                0f, hidden,
                spec.ShowAt, hidden,
                spec.ShowAt + spec.ShowDuration * 0.70f, decoration.AuthoredPosition + new Vector2(0f, 3f),
                settle, decoration.AuthoredPosition,
                ShowDuration, decoration.AuthoredPosition));
            SetScaleCurves(clip, root, decoration.Rect, Keys3(
                0f, new Vector3(spec.HiddenScale, spec.HiddenScale, 1f),
                spec.ShowAt, new Vector3(spec.HiddenScale, spec.HiddenScale, 1f),
                spec.ShowAt + spec.ShowDuration * 0.70f, new Vector3(spec.OvershootScale, spec.OvershootScale, 1f),
                settle, Vector3.one,
                ShowDuration, Vector3.one));
            SetRotationCurve(clip, root, decoration.Rect, Keys(0f, spec.HiddenRotation, spec.ShowAt, spec.HiddenRotation, settle, 0f, ShowDuration, 0f));
            SetAlphaCurve(clip, root, decoration.Group, Keys(0f, 0f, spec.ShowAt, 0f, spec.ShowAt + 0.025f, 1f, ShowDuration, 1f));
        }

        Vector2 medalAuthored = medal.Rect.anchoredPosition;
        SetPositionCurves(clip, root, medal.Rect, Keys(0f, medalAuthored + new Vector2(0f, 20f), 1.36f, medalAuthored + new Vector2(0f, 20f), 1.52f, medalAuthored, ShowDuration, medalAuthored));
        SetScaleCurves(clip, root, medal.Rect, Keys3(0f, new Vector3(1.18f, 1.18f, 1f), 1.36f, new Vector3(1.18f, 1.18f, 1f), 1.43f, new Vector3(0.96f, 0.96f, 1f), 1.52f, Vector3.one, ShowDuration, Vector3.one));
        SetRotationCurve(clip, root, medal.Rect, Keys(0f, 0f, ShowDuration, 0f));
        SetAlphaCurve(clip, root, medal.Group, Keys(0f, 0f, 1.36f, 0f, 1.385f, 1f, ShowDuration, 1f));

        foreach (ShineBuildData shine in shines)
        {
            float middle = shine.StartTime + shine.Duration * 0.5f;
            float end = shine.StartTime + shine.Duration;
            SetPositionCurves(clip, root, shine.Sweep, Keys(0f, shine.StartPosition, shine.StartTime, shine.StartPosition, end, shine.EndPosition, ShowDuration, shine.EndPosition));
            SetAlphaCurve(clip, root, shine.Group, Keys(0f, 0f, shine.StartTime, 0f, shine.StartTime + 0.025f, 1f, middle, 0.92f, end - 0.025f, 1f, end, 0f, ShowDuration, 0f));
        }

        PinClipLength(clip, root, ShowDuration);
    }

    private static void BuildShownClip(
        AnimationClip clip,
        Transform root,
        LayerData ribbon,
        IReadOnlyList<LayerData> letters,
        IReadOnlyList<DecorationBuildData> decorations,
        LayerData medal,
        IReadOnlyList<ShineBuildData> shines)
    {
        const float stableTime = 0.1f;
        SetPositionCurves(clip, root, ribbon.Rect, Keys(0f, ribbon.Rect.anchoredPosition, stableTime, ribbon.Rect.anchoredPosition));
        SetScaleCurves(clip, root, ribbon.Rect, Keys3(0f, Vector3.one, stableTime, Vector3.one));
        SetRotationCurve(clip, root, ribbon.Rect, Keys(0f, 0f, stableTime, 0f));
        SetAlphaCurve(clip, root, ribbon.Group, Keys(0f, 1f, stableTime, 1f));

        foreach (LayerData letter in letters)
        {
            SetPositionCurves(clip, root, letter.Rect, Keys(0f, letter.Rect.anchoredPosition, stableTime, letter.Rect.anchoredPosition));
            SetScaleCurves(clip, root, letter.Rect, Keys3(0f, Vector3.one, stableTime, Vector3.one));
            SetRotationCurve(clip, root, letter.Rect, Keys(0f, 0f, stableTime, 0f));
            SetAlphaCurve(clip, root, letter.Group, Keys(0f, 1f, stableTime, 1f));
        }

        foreach (DecorationBuildData decoration in decorations)
        {
            SetPositionCurves(clip, root, decoration.Rect, Keys(0f, decoration.AuthoredPosition, stableTime, decoration.AuthoredPosition));
            SetScaleCurves(clip, root, decoration.Rect, Keys3(0f, Vector3.one, stableTime, Vector3.one));
            SetRotationCurve(clip, root, decoration.Rect, Keys(0f, 0f, stableTime, 0f));
            SetAlphaCurve(clip, root, decoration.Group, Keys(0f, 1f, stableTime, 1f));
        }

        SetPositionCurves(clip, root, medal.Rect, Keys(0f, medal.Rect.anchoredPosition, stableTime, medal.Rect.anchoredPosition));
        SetScaleCurves(clip, root, medal.Rect, Keys3(0f, Vector3.one, stableTime, Vector3.one));
        SetRotationCurve(clip, root, medal.Rect, Keys(0f, 0f, stableTime, 0f));
        SetAlphaCurve(clip, root, medal.Group, Keys(0f, 1f, stableTime, 1f));
        SetShineHidden(clip, root, shines, stableTime);
        PinClipLength(clip, root, stableTime);
    }

    private static void BuildHideClip(
        AnimationClip clip,
        Transform root,
        LayerData ribbon,
        IReadOnlyList<LayerData> letters,
        IReadOnlyList<DecorationBuildData> decorations,
        LayerData medal,
        IReadOnlyList<ShineBuildData> shines)
    {
        SetShineHidden(clip, root, shines, HideDuration);

        Vector2 medalAuthored = medal.Rect.anchoredPosition;
        SetPositionCurves(clip, root, medal.Rect, Keys(0f, medalAuthored, HideDuration, medalAuthored));
        SetScaleCurves(clip, root, medal.Rect, Keys3(0f, Vector3.one, HideDuration, Vector3.one));
        SetRotationCurve(clip, root, medal.Rect, Keys(0f, 0f, HideDuration, 0f));
        SetAlphaCurve(clip, root, medal.Group, Keys(0f, 1f, 0.05f, 0.85f, 0.16f, 0f, HideDuration, 0f));

        foreach (LayerData letter in letters)
        {
            Vector2 authored = letter.Rect.anchoredPosition;
            SetPositionCurves(clip, root, letter.Rect, Keys(0f, authored, HideDuration, authored));
            SetScaleCurves(clip, root, letter.Rect, Keys3(0f, Vector3.one, HideDuration, Vector3.one));
            SetRotationCurve(clip, root, letter.Rect, Keys(0f, 0f, HideDuration, 0f));
            SetAlphaCurve(clip, root, letter.Group, Keys(0f, 1f, 0.05f, 0.85f, 0.16f, 0f, HideDuration, 0f));
        }

        Vector2 ribbonPosition = ribbon.Rect.anchoredPosition;
        SetPositionCurves(clip, root, ribbon.Rect, Keys(0f, ribbonPosition, HideDuration, ribbonPosition));
        SetRotationCurve(clip, root, ribbon.Rect, Keys(0f, 0f, HideDuration, 0f));
        SetScaleCurves(clip, root, ribbon.Rect, Keys3(
            0f, Vector3.one,
            0.28f, Vector3.one,
            0.40f, new Vector3(1.02f, 0.78f, 1f),
            0.54f, new Vector3(0.38f, 0.18f, 1f),
            0.64f, new Vector3(0.04f, 0.04f, 1f),
            HideDuration, new Vector3(0.04f, 0.04f, 1f)));
        SetAlphaCurve(clip, root, ribbon.Group, Keys(
            0f, 1f,
            0.36f, 1f,
            0.46f, 0.82f,
            0.58f, 0.25f,
            0.64f, 0f,
            HideDuration, 0f));

        for (int index = 0; index < decorations.Count; index++)
        {
            DecorationBuildData decoration = decorations[index];
            Vector2 suctionCenter = ConvertRectCenterToParent(ribbon.Rect, decoration.Rect.parent as RectTransform);
            Vector2 atFifteenPercent = Vector2.LerpUnclamped(decoration.AuthoredPosition, suctionCenter, 0.15f);
            Vector2 atSixtyFivePercent = Vector2.LerpUnclamped(decoration.AuthoredPosition, suctionCenter, 0.65f);
            float horizontalDelta = decoration.AuthoredPosition.x - suctionCenter.x;
            float spin = Mathf.Approximately(horizontalDelta, 0f)
                ? (index % 2 == 0 ? 10f : -10f)
                : (horizontalDelta < 0f ? 14f : -14f);

            SetPositionCurves(clip, root, decoration.Rect, Keys(
                0f, decoration.AuthoredPosition,
                0.14f, atFifteenPercent,
                0.30f, atSixtyFivePercent,
                0.48f, suctionCenter,
                HideDuration, suctionCenter));
            SetScaleCurves(clip, root, decoration.Rect, Keys3(
                0f, Vector3.one,
                0.14f, new Vector3(0.94f, 0.94f, 1f),
                0.34f, new Vector3(0.48f, 0.48f, 1f),
                0.48f, new Vector3(0.06f, 0.06f, 1f),
                HideDuration, new Vector3(0.06f, 0.06f, 1f)));
            SetRotationCurve(clip, root, decoration.Rect, Keys(
                0f, 0f,
                0.14f, spin * 0.25f,
                0.34f, spin,
                0.48f, 0f,
                HideDuration, 0f));
            SetAlphaCurve(clip, root, decoration.Group, Keys(
                0f, 1f,
                0.14f, 0.95f,
                0.32f, 0.55f,
                0.48f, 0f,
                HideDuration, 0f));
        }

        PinClipLength(clip, root, HideDuration);
    }

    private static AnimationClip LoadOrCreateClip(string path, string assetName)
    {
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, path);
        }

        clip.name = assetName;
        clip.frameRate = 60f;
        clip.wrapMode = WrapMode.ClampForever;
        clip.legacy = false;
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        settings.loopBlend = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    private static void ClearClip(AnimationClip clip)
    {
        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            AnimationUtility.SetEditorCurve(clip, binding, null);
        foreach (EditorCurveBinding binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
        AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
    }

    private static AnimatorController BuildController(AnimationClip hidden, AnimationClip show, AnimationClip shown, AnimationClip hide)
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        ChildAnimatorState[] oldStates = stateMachine.states;
        foreach (ChildAnimatorState oldState in oldStates)
            stateMachine.RemoveState(oldState.state);

        AnimatorState hiddenState = stateMachine.AddState(VictoryBannerView.HiddenStateName);
        AnimatorState showState = stateMachine.AddState(VictoryBannerView.ShowStateName);
        AnimatorState shownState = stateMachine.AddState(VictoryBannerView.ShownStateName);
        AnimatorState hideState = stateMachine.AddState(VictoryBannerView.HideStateName);
        hiddenState.motion = hidden;
        showState.motion = show;
        shownState.motion = shown;
        hideState.motion = hide;
        stateMachine.defaultState = hiddenState;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    private static void PinClipLength(AnimationClip clip, Transform root, float time)
    {
        SetCurve(clip, root, root, typeof(Transform), "m_LocalScale.z", Keys(0f, 1f, time, 1f));
    }

    private static void SetShineHidden(AnimationClip clip, Transform root, IReadOnlyList<ShineBuildData> shines, float endTime)
    {
        foreach (ShineBuildData shine in shines)
        {
            SetPositionCurves(clip, root, shine.Sweep, Keys(0f, shine.StartPosition, endTime, shine.StartPosition));
            SetAlphaCurve(clip, root, shine.Group, Keys(0f, 0f, endTime, 0f));
        }
    }

    private static void SetPositionCurves(AnimationClip clip, Transform root, RectTransform target, Vector2Key[] values)
    {
        var x = new FloatKey[values.Length];
        var y = new FloatKey[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            x[i] = new FloatKey(values[i].Time, values[i].Value.x);
            y[i] = new FloatKey(values[i].Time, values[i].Value.y);
        }
        SetCurve(clip, root, target, typeof(RectTransform), "m_AnchoredPosition.x", x);
        SetCurve(clip, root, target, typeof(RectTransform), "m_AnchoredPosition.y", y);
    }

    private static void SetScaleCurves(AnimationClip clip, Transform root, RectTransform target, Vector3Key[] values)
    {
        var x = new FloatKey[values.Length];
        var y = new FloatKey[values.Length];
        var z = new FloatKey[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            x[i] = new FloatKey(values[i].Time, values[i].Value.x);
            y[i] = new FloatKey(values[i].Time, values[i].Value.y);
            z[i] = new FloatKey(values[i].Time, values[i].Value.z);
        }
        SetCurve(clip, root, target, typeof(Transform), "m_LocalScale.x", x);
        SetCurve(clip, root, target, typeof(Transform), "m_LocalScale.y", y);
        SetCurve(clip, root, target, typeof(Transform), "m_LocalScale.z", z);
    }

    private static void SetRotationCurve(AnimationClip clip, Transform root, RectTransform target, FloatKey[] values)
    {
        SetCurve(clip, root, target, typeof(Transform), "localEulerAnglesRaw.z", values);
    }

    private static void SetAlphaCurve(AnimationClip clip, Transform root, CanvasGroup target, FloatKey[] values)
    {
        SetCurve(clip, root, target.transform, typeof(CanvasGroup), "m_Alpha", values);
    }

    private static void SetCurve(AnimationClip clip, Transform root, Transform target, Type type, string property, FloatKey[] values)
    {
        Keyframe[] frames = new Keyframe[values.Length];
        for (int i = 0; i < values.Length; i++)
            frames[i] = new Keyframe(values[i].Time, values[i].Value);
        AnimationCurve curve = new AnimationCurve(frames);
        for (int i = 0; i < curve.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
        }
        string path = AnimationUtility.CalculateTransformPath(target, root);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);
    }

    private static ParticleSystem CreateRearBurst(
        string name,
        Transform parent,
        Vector2 localPosition,
        int direction,
        Sprite particleAtlas)
    {
        if (particleAtlas == null)
            throw new ArgumentNullException(nameof(particleAtlas));

        GameObject target = CreateRectObject(name, parent);
        RectTransform rect = target.GetComponent<RectTransform>();
        SetCenteredRect(rect, new Vector2(2400f, 1500f), localPosition);

        ParticleSystem particles = target.GetComponent<ParticleSystem>();
        if (particles == null)
            particles = target.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.35f;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        main.maxParticles = 112;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.05f, 1.70f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(420f, 720f);
        main.startSize = new ParticleSystem.MinMaxCurve(11f, 28f);
        main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
        main.useUnscaledTime = true;
        main.startColor = Color.white;
        particles.useAutoRandomSeed = false;
        particles.randomSeed = direction > 0 ? 0x5EED1234u : 0xBADC0FFEu;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(direction > 0
            ? new[] { new ParticleSystem.Burst(0f, (short)104) }
            : new[] { new ParticleSystem.Burst(0.300f, (short)104) });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 20f;
        shape.radiusThickness = 0.9f;
        shape.arc = 140f;
        shape.rotation = new Vector3(0f, 0f, 20f);
        shape.randomDirectionAmount = 0f;
        shape.scale = Vector3.one;

        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = direction > 0
            ? new ParticleSystem.MinMaxCurve(100f, 180f)
            : new ParticleSystem.MinMaxCurve(-180f, -100f);
        velocity.y = new ParticleSystem.MinMaxCurve(110f, 170f);
        // x/y/z는 반드시 같은 MinMaxCurve 모드여야 한다. z를 기본값(Constant)으로 두면
        // 재생마다 "Particle Velocity curves must all be in the same mode" 에러가 뜬다.
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        ParticleSystem.ForceOverLifetimeModule force = particles.forceOverLifetime;
        force.enabled = true;
        force.space = ParticleSystemSimulationSpace.Local;
        force.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        force.y = new ParticleSystem.MinMaxCurve(-1200f, -1000f);
        force.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        ParticleSystem.RotationOverLifetimeModule rotation = particles.rotationOverLifetime;
        rotation.enabled = true;
        rotation.z = new ParticleSystem.MinMaxCurve(-Mathf.PI * 2.5f, Mathf.PI * 2.5f);

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.02f),
                new GradientAlphaKey(0.96f, 0.52f),
                new GradientAlphaKey(0.72f, 0.72f),
                new GradientAlphaKey(0.25f, 0.90f),
                new GradientAlphaKey(0f, 1f)
            });
        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(gradient);

        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.16f), new Keyframe(0.05f, 1f),
            new Keyframe(0.45f, 0.92f), new Keyframe(0.72f, 0.70f),
            new Keyframe(1f, 0.28f));
        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        ParticleSystem.NoiseModule noise = particles.noise;
        noise.enabled = false;
        ParticleSystem.CollisionModule collision = particles.collision;
        collision.enabled = false;
        ParticleSystem.TrailModule trails = particles.trails;
        trails.enabled = false;

        ParticleSystemRenderer renderer = target.GetComponent<ParticleSystemRenderer>();
        renderer.enabled = false;
        VictoryUiParticleGraphic graphic = target.AddComponent<VictoryUiParticleGraphic>();
        graphic.Configure(particles, 112, particleAtlas, ParticleAtlasColumns, ParticleAtlasRows, 2f);
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return particles;
    }

    private static void BuildScene(GameObject prefab, bool replaceCurrentScene)
    {
        float bannerHeight = BannerWidth * SourceHeight / SourceWidth;
        Scene previousActiveScene = SceneManager.GetActiveScene();
        bool useSingleScene = previousActiveScene.path == ScenePath ||
                              (replaceCurrentScene && Application.isBatchMode && string.IsNullOrEmpty(previousActiveScene.path));
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, useSingleScene ? NewSceneMode.Single : NewSceneMode.Additive);

        try
        {
            if (!useSingleScene && !SceneManager.SetActiveScene(scene))
                throw new InvalidOperationException("Could not activate the temporary Victory banner build scene.");

            GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.015f, 0.025f, 0.06f, 1f);
            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 0f, -10f);

            GameObject canvasObject = CreateRectObject("VictoryBannerTestCanvas", null);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject background = CreateRectObject("Background", canvasObject.transform);
            StretchToParent(background.GetComponent<RectTransform>());
            background.AddComponent<Image>().color = new Color(0.02f, 0.035f, 0.085f, 1f);

            GameObject glow = CreateRectObject("PopupBackdrop", canvasObject.transform);
            SetCenteredRect(glow.GetComponent<RectTransform>(), new Vector2(1030f, 1430f), new Vector2(0f, -15f));
            Image glowImage = glow.AddComponent<Image>();
            glowImage.color = new Color(0.075f, 0.095f, 0.17f, 0.98f);
            Outline outline = glow.AddComponent<Outline>();
            outline.effectColor = new Color(0.28f, 0.38f, 0.68f, 0.65f);
            outline.effectDistance = new Vector2(3f, -3f);

            CreateText(glow.transform, "LabTitle", "VICTORY TITLE MOTION LAB", 42, FontStyle.Bold, new Vector2(920f, 70f), new Vector2(0f, 625f), new Color(0.74f, 0.82f, 1f, 1f));
            CreateText(glow.transform, "SeparationNotice", "MOTION LAB  /  EDITS THE PRODUCTION VICTORY BANNER", 22, FontStyle.Normal, new Vector2(920f, 50f), new Vector2(0f, 572f), new Color(0.48f, 0.58f, 0.78f, 1f));

            GameObject dimOverlay = CreateRectObject("MotionReferenceDim", glow.transform);
            StretchToParent(dimOverlay.GetComponent<RectTransform>());
            Image dimImage = dimOverlay.AddComponent<Image>();
            dimImage.color = Color.black;
            dimImage.raycastTarget = false;
            CanvasGroup dimGroup = dimOverlay.AddComponent<CanvasGroup>();
            dimGroup.alpha = 0f;
            dimGroup.blocksRaycasts = false;
            dimGroup.interactable = false;

            GameObject bannerInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            bannerInstance.name = "VictoryBannerPreview";
            RectTransform bannerRect = bannerInstance.GetComponent<RectTransform>();
            bannerRect.SetParent(glow.transform, false);
            bannerRect.anchorMin = bannerRect.anchorMax = new Vector2(0.5f, 0.5f);
            bannerRect.pivot = new Vector2(0.5f, 0.5f);
            bannerRect.anchoredPosition = new Vector2(0f, 255f);
            VictoryBannerView banner = bannerInstance.GetComponent<VictoryBannerView>();

            GameObject referenceOverlay = CreateRectObject("ReferenceOriginalOverlay", glow.transform);
            SetCenteredRect(referenceOverlay.GetComponent<RectTransform>(), new Vector2(BannerWidth, bannerHeight), new Vector2(0f, 255f));
            Image referenceImage = referenceOverlay.AddComponent<Image>();
            referenceImage.sprite = LoadSprite(ImageFolder + "/victory_reference_original.png");
            referenceImage.preserveAspect = false;
            referenceImage.raycastTarget = false;
            referenceImage.color = new Color(1f, 1f, 1f, 0.5f);
            referenceOverlay.SetActive(false);

            GameObject driverObject = new GameObject("VictoryBannerTestDriver");
            driverObject.transform.SetParent(canvasObject.transform, false);
            VictoryBannerTestDriver driver = driverObject.AddComponent<VictoryBannerTestDriver>();
            Text status = CreateText(glow.transform, "Status", "STATE  EDIT PREVIEW    SHOW 2.00s    HIDE 0.75s", 26, FontStyle.Bold, new Vector2(900f, 58f), new Vector2(0f, -235f), new Color(1f, 0.83f, 0.35f, 1f));
            CreateText(glow.transform, "Hint", "PLAY MODE: SPACE = CYCLE    S = SHOW    H = HIDE    R = RESET    O = 50% ORIGINAL", 21, FontStyle.Normal, new Vector2(910f, 60f), new Vector2(0f, -292f), new Color(0.58f, 0.68f, 0.86f, 1f));

            SerializedObject serializedDriver = new SerializedObject(driver);
            serializedDriver.FindProperty("banner").objectReferenceValue = banner;
            serializedDriver.FindProperty("statusLabel").objectReferenceValue = status;
            serializedDriver.FindProperty("referenceOverlay").objectReferenceValue = referenceOverlay;
            serializedDriver.FindProperty("dimGroup").objectReferenceValue = dimGroup;
            serializedDriver.FindProperty("holdDuration").floatValue = 0.8f;
            serializedDriver.FindProperty("autoPlayOnStart").boolValue = true;
            serializedDriver.ApplyModifiedPropertiesWithoutUndo();

            Button showButton = CreateButton(glow.transform, "ShowButton", "SHOW", new Vector2(-345f, -415f));
            Button hideButton = CreateButton(glow.transform, "HideButton", "HIDE", new Vector2(-115f, -415f));
            Button cycleButton = CreateButton(glow.transform, "CycleButton", "CYCLE", new Vector2(115f, -415f));
            Button resetButton = CreateButton(glow.transform, "ResetButton", "RESET", new Vector2(345f, -415f));
            UnityEventTools.AddPersistentListener(showButton.onClick, driver.Show);
            UnityEventTools.AddPersistentListener(hideButton.onClick, driver.Hide);
            UnityEventTools.AddPersistentListener(cycleButton.onClick, driver.Cycle);
            UnityEventTools.AddPersistentListener(resetButton.onClick, driver.ResetBanner);

            CreateText(glow.transform, "LayerInfo", "V10  TWO HIGH-WIDE BURSTS + ANIMATOR SHOW / SUCTION FOLD HIDE", 20, FontStyle.Normal, new Vector2(920f, 65f), new Vector2(0f, -535f), new Color(0.4f, 0.5f, 0.7f, 1f));
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"Could not save Victory banner test scene: {ScenePath}");
        }
        finally
        {
            if (!useSingleScene)
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private static DecorationBuildData CreateDecoration(DecorationAssetSpec spec, Transform parent)
    {
        GameObject layer = CreateRectObject(spec.Name, parent);
        RectTransform rect = layer.GetComponent<RectTransform>();
        SetCenteredRect(rect, new Vector2(spec.SourceRect.width, spec.SourceRect.height) * (BannerWidth / SourceWidth), SourceCenterToUi(spec.SourceRect));
        Vector2 authored = rect.anchoredPosition;
        Image image = layer.AddComponent<Image>();
        image.sprite = LoadSprite(spec.Path);
        image.preserveAspect = false;
        image.raycastTarget = false;
        return new DecorationBuildData(rect, layer.AddComponent<CanvasGroup>(), spec, authored);
    }

    private static Image CreateLetterLayer(Transform parent, string name, Sprite sprite, Vector2 size, Vector2 offset, Color color)
    {
        GameObject layer = CreateRectObject(name, parent);
        SetCenteredRect(layer.GetComponent<RectTransform>(), size, offset);
        Image image = layer.AddComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = false;
        image.raycastTarget = false;
        image.color = color;
        return image;
    }

    private static ShineBuildData CreateShineBand(Transform parent, string name, Vector2 maskSize, float startTime, float duration)
    {
        GameObject sweepObject = CreateRectObject(name, parent);
        RectTransform sweep = sweepObject.GetComponent<RectTransform>();
        Vector2 start = new Vector2(-maskSize.x * 0.86f, 0f);
        Vector2 end = new Vector2(maskSize.x * 0.86f, 0f);
        SetCenteredRect(sweep, new Vector2(Mathf.Max(18f, maskSize.x * 0.42f), maskSize.y * 1.65f), start);
        sweep.localRotation = Quaternion.Euler(0f, 0f, -18f);
        Image band = sweepObject.AddComponent<Image>();
        band.type = Image.Type.Simple;
        band.preserveAspect = false;
        band.raycastTarget = false;
        band.color = new Color(1f, 0.94f, 0.72f, 0.92f);
        CanvasGroup group = sweepObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
        return new ShineBuildData(sweep, band, group, start, end, startTime, duration);
    }

    private static LayerData CreatePlacedLayer(string name, Transform parent, string spritePath, RectInt sourceRect)
    {
        GameObject layer = CreateRectObject(name, parent);
        RectTransform rect = layer.GetComponent<RectTransform>();
        SetCenteredRect(rect, new Vector2(sourceRect.width, sourceRect.height) * (BannerWidth / SourceWidth), SourceCenterToUi(sourceRect));
        Image image = layer.AddComponent<Image>();
        image.sprite = LoadSprite(spritePath);
        image.preserveAspect = false;
        image.raycastTarget = false;
        return new LayerData(rect, layer.AddComponent<CanvasGroup>());
    }

    private static Vector2 SourceCenterToUi(RectInt sourceRect)
    {
        return SourcePointToUi(new Vector2(sourceRect.x + sourceRect.width * 0.5f, sourceRect.y + sourceRect.height * 0.5f));
    }

    private static Vector2 ConvertRectCenterToParent(RectTransform source, RectTransform targetParent)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        if (targetParent == null)
            throw new ArgumentNullException(nameof(targetParent));

        Vector3 worldCenter = source.TransformPoint(source.rect.center);
        Vector3 parentLocalCenter = targetParent.InverseTransformPoint(worldCenter);
        return new Vector2(parentLocalCenter.x, parentLocalCenter.y);
    }

    private static Vector2 SourcePointToUi(Vector2 sourcePoint)
    {
        float scale = BannerWidth / SourceWidth;
        return new Vector2((sourcePoint.x - SourceWidth * 0.5f) * scale, (SourceHeight * 0.5f - sourcePoint.y) * scale);
    }

    private static Sprite LoadSprite(string path)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
            throw new InvalidOperationException($"Victory sprite could not be loaded: {path}");
        return sprite;
    }

    private static Button CreateButton(Transform parent, string name, string label, Vector2 position)
    {
        GameObject target = CreateRectObject(name, parent);
        SetCenteredRect(target.GetComponent<RectTransform>(), new Vector2(200f, 76f), position);
        Image image = target.AddComponent<Image>();
        image.color = new Color(0.18f, 0.27f, 0.5f, 1f);
        Button button = target.AddComponent<Button>();
        button.targetGraphic = image;
        CreateText(target.transform, "Label", label, 27, FontStyle.Bold, new Vector2(190f, 68f), Vector2.zero, Color.white);
        return button;
    }

    private static Text CreateText(Transform parent, string name, string value, int fontSize, FontStyle style, Vector2 size, Vector2 position, Color color)
    {
        GameObject target = CreateRectObject(name, parent);
        SetCenteredRect(target.GetComponent<RectTransform>(), size, position);
        Text text = target.AddComponent<Text>();
        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateRectObject(string name, Transform parent)
    {
        GameObject target = new GameObject(name, typeof(RectTransform));
        if (parent != null)
            target.transform.SetParent(parent, false);
        return target;
    }

    private static void SetCenteredRect(RectTransform rect, Vector2 size, Vector2 position)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    private static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static void AssignArray<T>(SerializedProperty property, T[] values) where T : UnityEngine.Object
    {
        property.arraySize = values.Length;
        for (int index = 0; index < values.Length; index++)
            property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int index = 1; index < parts.Length; index++)
        {
            string next = current + "/" + parts[index];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[index]);
            current = next;
        }
    }

    private static FloatKey[] Keys(params float[] timeValues)
    {
        var values = new FloatKey[timeValues.Length / 2];
        for (int i = 0; i < values.Length; i++)
            values[i] = new FloatKey(timeValues[i * 2], timeValues[i * 2 + 1]);
        return values;
    }

    private static Vector2Key[] Keys(params object[] timeValues)
    {
        var values = new Vector2Key[timeValues.Length / 2];
        for (int i = 0; i < values.Length; i++)
            values[i] = new Vector2Key((float)timeValues[i * 2], (Vector2)timeValues[i * 2 + 1]);
        return values;
    }

    private static Vector3Key[] Keys3(params object[] timeValues)
    {
        var values = new Vector3Key[timeValues.Length / 2];
        for (int i = 0; i < values.Length; i++)
            values[i] = new Vector3Key((float)timeValues[i * 2], (Vector3)timeValues[i * 2 + 1]);
        return values;
    }

    private readonly struct LayerData
    {
        public readonly RectTransform Rect;
        public readonly CanvasGroup Group;
        public LayerData(RectTransform rect, CanvasGroup group) { Rect = rect; Group = group; }
    }

    private readonly struct DecorationBuildData
    {
        public readonly RectTransform Rect;
        public readonly CanvasGroup Group;
        public readonly DecorationAssetSpec Spec;
        public readonly Vector2 AuthoredPosition;
        public DecorationBuildData(RectTransform rect, CanvasGroup group, DecorationAssetSpec spec, Vector2 authoredPosition)
        { Rect = rect; Group = group; Spec = spec; AuthoredPosition = authoredPosition; }
    }

    private readonly struct ShineBuildData
    {
        public readonly RectTransform Sweep;
        public readonly Image Band;
        public readonly CanvasGroup Group;
        public readonly Vector2 StartPosition;
        public readonly Vector2 EndPosition;
        public readonly float StartTime;
        public readonly float Duration;
        public ShineBuildData(RectTransform sweep, Image band, CanvasGroup group, Vector2 start, Vector2 end, float startTime, float duration)
        { Sweep = sweep; Band = band; Group = group; StartPosition = start; EndPosition = end; StartTime = startTime; Duration = duration; }
    }

    private readonly struct DecorationAssetSpec
    {
        public readonly string Name;
        public readonly string Path;
        public readonly RectInt SourceRect;
        public readonly float ShowAt;
        public readonly float ShowDuration;
        public readonly float HideAt;
        public readonly float HideDuration;
        public readonly Vector2 HiddenOffset;
        public readonly float HiddenScale;
        public readonly float HiddenRotation;
        public readonly float OvershootScale;
        public DecorationAssetSpec(string name, string fileName, RectInt sourceRect, float showAt, float showDuration, float hideAt, float hideDuration, Vector2 hiddenOffset, float hiddenScale, float hiddenRotation, float overshootScale)
        { Name = name; Path = ImageFolder + "/" + fileName; SourceRect = sourceRect; ShowAt = showAt; ShowDuration = showDuration; HideAt = hideAt; HideDuration = hideDuration; HiddenOffset = hiddenOffset; HiddenScale = hiddenScale; HiddenRotation = hiddenRotation; OvershootScale = overshootScale; }
    }

    private readonly struct MotionAssets
    {
        public readonly RuntimeAnimatorController Controller;
        public MotionAssets(RuntimeAnimatorController controller) { Controller = controller; }
    }

    private readonly struct FloatKey
    {
        public readonly float Time;
        public readonly float Value;
        public FloatKey(float time, float value) { Time = time; Value = value; }
    }

    private readonly struct Vector2Key
    {
        public readonly float Time;
        public readonly Vector2 Value;
        public Vector2Key(float time, Vector2 value) { Time = time; Value = value; }
    }

    private readonly struct Vector3Key
    {
        public readonly float Time;
        public readonly Vector3 Value;
        public Vector3Key(float time, Vector3 value) { Time = time; Value = value; }
    }
}
