using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

static class ImageAssetMigration
{
    const string ImagesRoot = "Assets/Assets/Images/";
    const string ReferencesRoot = ImagesRoot + "References";
    const string UiPrefabRoot = "Assets/Assets/Prefabs/UI";
    const string VendorSourceRoot = "Assets/PurchasedAssets/Layer Lab/";
    const string VendorDestinationRoot = ImagesRoot + "_Vendor/LayerLab/";
    const int ExpectedReferenceImages = 9;
    // AssetDatabase 의존성 그래프 실측값. 정적 grep 조사(161)는 머티리얼·중첩 프리팹 경유를 놓친다.
    const int ExpectedVendorImages = 169;

    // UI 프리팹만으로는 부족하다 — BattleScene 이 Vendor 이미지를 직접 참조한다.
    static readonly string[] BuildScenes =
    {
        "Assets/Scenes/StartScene.unity",
        "Assets/Scenes/LobbyScene.unity",
        "Assets/Scenes/BattleScene.unity"
    };

    static readonly string[] OrphanPaths =
    {
        ImagesRoot + "0 (4).unity",
        ImagesRoot + "Cards/CardFrame/Image_CardFrame_Ranged_Bow_Variant0003.png",
        ImagesRoot + "Emotes/Anim_Emote_LongCat.anim",
        ImagesRoot + "Emotes/Image_Emote_IKnow.png",
        ImagesRoot + "Emotes/Image_Emote_KKang.png",
        ImagesRoot + "Emotes/Image_Emote_Monitoring.png",
        ImagesRoot + "Emotes/Image_Emote_Pepe.png",
        ImagesRoot + "Emotes/Image_Emote_WeirdCat.png",
        ImagesRoot + "Image_Reference_Screenshot_20260814163310.png",
        ImagesRoot + "Image_Shield_temp.png",
        ImagesRoot + "Profile/Frame/Image_ProfileFrame_HudSky.png",
        ImagesRoot + "Synergy/Flower.png",
        ImagesRoot + "Synergy/Image_Synergy_Scale_Emblem.png",
        ImagesRoot + "Adventure/AdventureMap_Chapter04_AshenVolcano_v3.png",
        ImagesRoot + "Adventure/Transitions/Transition_01_ForestToRuins_v1.png",
        ImagesRoot + "Adventure/Transitions/Transition_01_ForestToRuins_v2.png",
        ImagesRoot + "Adventure/Transitions/Transition_02_RuinsToFrost_v1.png",
        ImagesRoot + "Adventure/Transitions/Transition_02_RuinsToFrost_v2.png",
        ImagesRoot + "Adventure/Transitions/Transition_03_FrostToVolcano_v1.png",
        ImagesRoot + "Adventure/Transitions/Transition_03_FrostToVolcano_v2.png",
        ImagesRoot + "UI/BackGroundPattern.png",
        ImagesRoot + "UI/Bar_05.png",
        ImagesRoot + "UI/Button_Bar_05.psd",
        ImagesRoot + "UI/Focus_Bar.png",
        ImagesRoot + "UI/Icon 1.png",
        ImagesRoot + "UI/Image_UI_Screenshot_20260811193526.png",
        ImagesRoot + "UI/Image_UI_Untitled.png",
        ImagesRoot + "UI/Item_Diamond.png",
        ImagesRoot + "UI/Item_Energy.png",
        ImagesRoot + "UI/Item_Shard.png",
        ImagesRoot + "UI/Main_Block_Edge.png",
        ImagesRoot + "UI/bar_02.psd",
        ImagesRoot + "UI/pinger 2.png",
        ImagesRoot + "UI/background/Frozen_Tundra_Background.png",
        ImagesRoot + "UI/background/Image_UI_Generated_2026729023929.png",
        ImagesRoot + "UI/background/MistForset_Background.png",
        ImagesRoot + "UI/background/background_01.png"
    };

    static readonly HashSet<string> ExpectedVendorPrefabs = new HashSet<string>
    {
        VendorSourceRoot + "GUI Pro-SimpleCasual/Prefabs/Prefabs_Component_Frames/BasicFrame_Square02_Yellow.prefab",
        VendorSourceRoot + "GUI Pro-SimpleCasual/Prefabs/Prefabs_Component_UI_Etc/StatusBar_Group_Dark.prefab",
        VendorSourceRoot + "GUI Pro-SuperCasual/Prefabs/Prefabs_Component_Buttons/Menu_BottomBtn.prefab",
        VendorSourceRoot + "GUI Pro-SuperCasual/Prefabs/Prefabs_DemoScene_Panels/Loading_Maching.prefab"
    };

    [MenuItem("Tools/Assets/Images/Preview Verified Orphan Cleanup")]
    static void PreviewOrphanCleanup()
    {
        if (!TryCollectOrphans(out List<string> t_paths)) return;

        foreach (string t_path in t_paths) Debug.Log($"[ImageAssetMigration] DELETE {t_path}");
        Debug.Log($"[ImageAssetMigration] Delete check passed: {t_paths.Count} asset(s)");
    }

    [MenuItem("Tools/Assets/Images/Delete Verified Orphans")]
    static void DeleteVerifiedOrphans()
    {
        if (!TryCollectOrphans(out List<string> t_paths)) return;
        if (t_paths.Count == 0)
        {
            Debug.Log("[ImageAssetMigration] Nothing to delete — already cleaned up.");
            return;
        }

        if (!EditorUtility.DisplayDialog("Images 고아 자산 삭제",
                $"검증된 고아 자산 {t_paths.Count}개를 삭제합니다. Git으로 복구할 수 있습니다.", "삭제", "취소")) return;

        int t_failed = 0;
        foreach (string t_path in OrphanPaths)
        {
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(t_path))) continue;
            if (!AssetDatabase.DeleteAsset(t_path))
            {
                Debug.LogError($"[ImageAssetMigration] Delete failed: {t_path}");
                t_failed++;
            }
        }

        if (AssetDatabase.IsValidFolder(ReferencesRoot) && !AssetDatabase.DeleteAsset(ReferencesRoot))
        {
            Debug.LogError($"[ImageAssetMigration] Folder delete failed: {ReferencesRoot}");
            t_failed++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ImageAssetMigration] Orphan assets deleted, {t_failed} failure(s)");
    }

    [MenuItem("Tools/Assets/Images/Preview Used Vendor Migration")]
    static void PreviewUsedVendorMigration()
    {
        if (!TryCollectVendorMoves(out List<MovePair> t_moves)) return;

        foreach (MovePair t_move in t_moves)
            Debug.Log($"[ImageAssetMigration] MOVE {t_move.Source} -> {t_move.Destination}");
        Debug.Log($"[ImageAssetMigration] Move check passed: {t_moves.Count} asset(s)");
    }

    [MenuItem("Tools/Assets/Images/Move Used Vendor Assets")]
    static void MoveUsedVendorAssets()
    {
        if (!TryCollectVendorMoves(out List<MovePair> t_moves)) return;
        if (t_moves.Count == 0)
        {
            Debug.Log("[ImageAssetMigration] Nothing to move — already migrated.");
            return;
        }

        // 모달 다이얼로그는 자동 실행을 막는다. 게이트는 Preview 메뉴, 되돌리기는 git 이다.
        Debug.Log($"[ImageAssetMigration] Moving {t_moves.Count} Layer Lab asset(s) under Images/_Vendor.");

        List<string> t_createdFolders = new List<string>();
        foreach (MovePair t_move in t_moves)
            EnsureAssetFolder(Path.GetDirectoryName(t_move.Destination)?.Replace('\\', '/'), t_createdFolders);

        foreach (MovePair t_move in t_moves)
        {
            string t_error = AssetDatabase.ValidateMoveAsset(t_move.Source, t_move.Destination);
            if (!string.IsNullOrEmpty(t_error))
            {
                Debug.LogError($"[ImageAssetMigration] Move check failed: {t_move.Source} -> {t_move.Destination}\n{t_error}");
                DeleteEmptyFolders(t_createdFolders);
                return;
            }
        }

        int t_failed = 0;
        foreach (MovePair t_move in t_moves)
        {
            string t_error = AssetDatabase.MoveAsset(t_move.Source, t_move.Destination);
            if (!string.IsNullOrEmpty(t_error))
            {
                Debug.LogError($"[ImageAssetMigration] Move failed: {t_move.Source} -> {t_move.Destination}\n{t_error}");
                t_failed++;
                continue;
            }

            Debug.Log($"[ImageAssetMigration] Moved: {t_move.Source} -> {t_move.Destination}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[ImageAssetMigration] Vendor assets moved, {t_moves.Count - t_failed} succeeded, {t_failed} failed");
    }

    static bool TryCollectOrphans(out List<string> _paths)
    {
        _paths = OrphanPaths.Where(t_path => !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(t_path))).ToList();

        List<string> t_referenceAssets = AssetDatabase.FindAssets("", new[] { ReferencesRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(t_path => !AssetDatabase.IsValidFolder(t_path))
            .ToList();

        if (AssetDatabase.IsValidFolder(ReferencesRoot) && t_referenceAssets.Count != ExpectedReferenceImages)
        {
            Debug.LogError($"[ImageAssetMigration] References asset count differs from what was expected: {t_referenceAssets.Count}/{ExpectedReferenceImages}");
            return false;
        }

        _paths.AddRange(t_referenceAssets);
        if (_paths.Count == 0) return true;

        HashSet<string> t_targets = new HashSet<string>(_paths);
        AddressableAssetSettings t_settings = AddressableAssetSettingsDefaultObject.Settings;
        if (t_settings != null)
        {
            foreach (string t_path in _paths)
            {
                string t_guid = AssetDatabase.AssetPathToGUID(t_path);
                AddressableAssetEntry t_entry = t_settings.FindAssetEntry(t_guid);
                if (t_entry == null) continue;

                Debug.LogError($"[ImageAssetMigration] It is an Addressables entry, so the delete is aborted: {t_path}");
                return false;
            }
        }

        string[] t_nonTargets = AssetDatabase.GetAllAssetPaths()
            .Where(t_path => t_path.StartsWith("Assets/", StringComparison.Ordinal) &&
                             !AssetDatabase.IsValidFolder(t_path) && !t_targets.Contains(t_path))
            .ToArray();
        string[] t_dependencies = AssetDatabase.GetDependencies(t_nonTargets, true);
        List<string> t_referencedTargets = t_dependencies.Where(t_targets.Contains).OrderBy(t_path => t_path).ToList();

        if (t_referencedTargets.Count > 0)
        {
            foreach (string t_path in t_referencedTargets)
                Debug.LogError($"[ImageAssetMigration] Another asset references it, so the delete is aborted: {t_path}");
            return false;
        }

        _paths.Sort(StringComparer.Ordinal);
        return true;
    }

    static bool TryCollectVendorMoves(out List<MovePair> _moves)
    {
        _moves = new List<MovePair>();

        string[] t_roots = AssetDatabase.FindAssets("t:Prefab", new[] { UiPrefabRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Concat(BuildScenes.Where(t_scene => !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(t_scene))))
            .ToArray();
        string[] t_dependencies = AssetDatabase.GetDependencies(t_roots, true);

        List<string> t_sourceImages = t_dependencies.Where(t_path =>
            t_path.StartsWith(VendorSourceRoot, StringComparison.Ordinal) && IsImage(t_path)).ToList();
        List<string> t_destinationImages = t_dependencies.Where(t_path =>
            t_path.StartsWith(VendorDestinationRoot, StringComparison.Ordinal) && IsImage(t_path)).ToList();

        HashSet<string> t_sourcePrefabs = new HashSet<string>(t_dependencies.Where(t_path =>
            t_path.StartsWith(VendorSourceRoot, StringComparison.Ordinal) && t_path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)));
        HashSet<string> t_destinationPrefabs = new HashSet<string>(t_dependencies.Where(t_path =>
            t_path.StartsWith(VendorDestinationRoot, StringComparison.Ordinal) && t_path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            .Select(ToSourcePath));

        if (t_sourceImages.Count + t_destinationImages.Count != ExpectedVendorImages)
        {
            Debug.LogError($"[ImageAssetMigration] UI-dependent Vendor image count differs from what was expected: " +
                            $"source {t_sourceImages.Count} + moved {t_destinationImages.Count} / {ExpectedVendorImages}");

            // 숫자만으론 원인을 못 찾는다 — 실제 집합을 그대로 남긴다.
            foreach (string t_path in t_sourceImages.Concat(t_destinationImages).OrderBy(t_p => t_p, StringComparer.Ordinal))
                Debug.LogError($"[ImageAssetMigration] Counted: {t_path}");
            return false;
        }

        t_sourcePrefabs.UnionWith(t_destinationPrefabs);
        if (!t_sourcePrefabs.SetEquals(ExpectedVendorPrefabs))
        {
            Debug.LogError($"[ImageAssetMigration] UI-dependent Vendor prefab set differs from what was expected: {t_sourcePrefabs.Count}/4");
            return false;
        }

        foreach (string t_source in t_sourceImages.Concat(ExpectedVendorPrefabs).OrderBy(t_path => t_path))
        {
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(t_source))) continue;

            string t_destination = ToDestinationPath(t_source);
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(t_destination)))
            {
                Debug.LogError($"[ImageAssetMigration] Destination path conflict: {t_destination}");
                return false;
            }

            _moves.Add(new MovePair(t_source, t_destination));
        }

        return true;
    }

    static bool IsImage(string _path)
    {
        Type t_type = AssetDatabase.GetMainAssetTypeAtPath(_path);
        return t_type == typeof(Texture2D) || t_type == typeof(Sprite);
    }

    static string ToDestinationPath(string _sourcePath)
    {
        return VendorDestinationRoot + _sourcePath.Substring(VendorSourceRoot.Length);
    }

    static string ToSourcePath(string _destinationPath)
    {
        return VendorSourceRoot + _destinationPath.Substring(VendorDestinationRoot.Length);
    }

    static void EnsureAssetFolder(string _folderPath, List<string> _createdFolders)
    {
        if (string.IsNullOrEmpty(_folderPath) || AssetDatabase.IsValidFolder(_folderPath)) return;

        string t_parent = Path.GetDirectoryName(_folderPath)?.Replace('\\', '/');
        EnsureAssetFolder(t_parent, _createdFolders);
        AssetDatabase.CreateFolder(t_parent, Path.GetFileName(_folderPath));
        _createdFolders.Add(_folderPath);
    }

    static void DeleteEmptyFolders(List<string> _folders)
    {
        for (int t_i = _folders.Count - 1; t_i >= 0; t_i--)
        {
            string[] t_children = AssetDatabase.FindAssets("", new[] { _folders[t_i] });
            if (t_children.Length == 0) AssetDatabase.DeleteAsset(_folders[t_i]);
        }
    }

    readonly struct MovePair
    {
        public readonly string Source;
        public readonly string Destination;

        public MovePair(string _source, string _destination)
        {
            Source = _source;
            Destination = _destination;
        }
    }
}
