#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// One-shot, GUID-preserving asset naming migration. Dry-run first; every successful
/// mutation is journaled immediately so a partial Apply can be rolled back safely.
/// </summary>
public static class AssetNamingMigration
{
    private const string PurchasedRoot = "Assets/PurchasedAssets/";
    private const string VendorRoot = "Assets/Assets/Particle/_Vendor";
    private const string ManifestPath = "docs/rename-map.csv";
    private const string JournalPath = "docs/rename-journal.csv";

    private static readonly string[] CardSpecPaths =
    {
        "docs/SpecData/Card_sheet.csv", "docs/SpecData/Card_sheet.tsv"
    };

    private sealed class Move
    {
        public string Kind;
        public string OldPath;
        public string Guid;
        public string NewPath;
    }

    private static readonly KeyValuePair<string, string>[] FolderMoves =
    {
        Pair("Assets/Assets/Images/Cards/1단계", "Assets/Assets/Images/Cards/Stage1"),
        Pair("Assets/Assets/Images/Cards/2단계", "Assets/Assets/Images/Cards/Stage2"),
        Pair("Assets/Assets/Images/Cards/3단계", "Assets/Assets/Images/Cards/Stage3"),
        Pair("Assets/Assets/Particle/Old_VFX/공격", "Assets/Assets/Particle/Old_VFX/Attack"),
        Pair("Assets/Assets/Particle/Old_VFX/교활", "Assets/Assets/Particle/Old_VFX/Cunning"),
        Pair("Assets/Assets/Particle/Old_VFX/시너지", "Assets/Assets/Particle/Old_VFX/Synergy"),
        Pair("Assets/Assets/Particle/Old_VFX/처형", "Assets/Assets/Particle/Old_VFX/Execute"),
        Pair("Assets/Assets/Particle/Old_VFX/히트", "Assets/Assets/Particle/Old_VFX/Hit"),
        Pair("Assets/Assets/Particle/Old_VFX/힐", "Assets/Assets/Particle/Old_VFX/Heal"),
        Pair("Assets/Assets/Images/Icons/KeyworkIcon", "Assets/Assets/Images/Icons/KeywordIcon")
    };

    private static readonly Dictionary<string, string> Cards = new Dictionary<string, string>
    {
        { "모닥콩", "Campbean" }, { "포슬램", "Poslamb" }, { "찌릿핀", "Sparkfin" },
        { "물방울룽", "WaterdropLong" }, { "물방울릉", "WaterdropLong" }, { "바위콩", "Rockbean" },
        { "깜밤이", "Nightchestnut" }, { "솜구름몽", "Cloudmong" }, { "솜구름몸", "Cloudmong" },
        { "얼음꼬미", "Icekomi" }, { "꿀꿀비", "Honeybee" }, { "톱니두더", "Gearmole" },
        { "화르륵스", "Flarelux" }, { "화르룩스", "Flarelux" }, { "철갑몽치", "IronMongchi" },
        { "풍선펭", "BalloonPeng" }, { "버섯냥", "MushroomCat" }, { "별토리", "Startori" },
        { "늪꾸리", "Swampfrog" }, { "번개뿔", "Thunderhorn" }, { "단풍꼬리", "Mapletail" },
        { "눈덩곰", "SnowballBear" }, { "와글도도", "Waggledodo" }, { "자석게", "MagnetCrab" },
        { "헤롱문어", "DizzyOctopus" }, { "해롱문어", "DizzyOctopus" }, { "폭탄밤", "Bombbat" },
        { "우드혼", "Woodhorn" }, { "파도리", "Waveri" }, { "모래몽", "Sandmong" },
        { "수정뿔루", "Crystalhorn" }, { "대장부리", "CaptainBeak" }, { "꿈먹이", "Dreameater" },
        { "왕밤도치", "KingChestnutHedgehog" }
    };

    private static readonly Dictionary<string, string> Synergies = new Dictionary<string, string>
    {
        { "덩치", "Bulk" }, { "돌보미", "Caretaker" }, { "무리", "Swarm" },
        { "비늘", "Scale" }, { "성벽", "Rampart" }, { "언데드", "Undead" },
        { "유산", "Legacy" }, { "청소부", "Cleaner" }, { "흐름", "Flow" }
    };

    private static readonly Dictionary<string, string> Keywords = new Dictionary<string, string>
    {
        { "기본", "Default" }, { "도발", "Taunt" }, { "교활", "Cunning" },
        { "무쌍", "Peerless" }, { "원거리", "Ranged" }, { "처형", "Execution" },
        { "힐러", "Healer" }, { "표식", "Mark" }
    };

    [MenuItem("Tools/Asset Naming Migration/1. Dry Run")]
    public static void DryRun()
    {
        var plan = BuildPlan();
        Validate(plan);
        if (plan.Count > 0)
            WriteCsv(ManifestPath, "kind,oldPath,guid,newPath", plan);
        WriteGlossary();
        Debug.Log(plan.Count == 0
            ? "[AssetNamingMigration] Dry-run: already migrated (no-op). Existing rollback journal was preserved."
            : "[AssetNamingMigration] Dry-run OK: " + plan.Count + " moves. Manifest: " + ManifestPath);
    }

    [MenuItem("Tools/Asset Naming Migration/2. Apply")]
    public static void Apply()
    {
        var plan = BuildPlan();
        Validate(plan);
        if (plan.Count == 0)
        {
            Debug.Log("[AssetNamingMigration] Apply: already migrated (no-op). Existing rollback journal was preserved.");
            return;
        }

        if (ReadJournal().Count > 0)
            throw new InvalidOperationException("A non-empty migration journal already exists. Roll back the partial run before applying again: " + JournalPath);

        WriteCsv(ManifestPath, "kind,oldPath,guid,newPath", plan);
        BeginJournal();
        var movedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var folder in plan.Where(x => x.Kind == "Folder"))
                ExecuteMove(folder, folder.OldPath, movedGuids);

            AssetDatabase.Refresh();

            foreach (var asset in plan.Where(x => x.Kind == "Asset"))
                EnsureFolder(Path.GetDirectoryName(asset.NewPath).Replace('\\', '/'));
            AssetDatabase.Refresh();

            foreach (var asset in plan.Where(x => x.Kind == "Asset"))
            {
                var currentPath = AssetDatabase.GUIDToAssetPath(asset.Guid);
                ExecuteMove(asset, currentPath, movedGuids);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[AssetNamingMigration] Apply stopped. Completed operations remain in " + JournalPath +
                           "; run Rollback after resolving the cause.\n" + ex);
            throw;
        }
        finally
        {
            AssetDatabase.Refresh();
        }

        VerifyMovedGuids(plan, movedGuids);
        UpdateAddressables(movedGuids);
        UpdateCardSpecData(true);
        AssetDatabase.SaveAssets();
        Debug.Log("[AssetNamingMigration] Apply complete: " + movedGuids.Count + " GUIDs moved. Run Apply again to verify no-op.");
    }

    [MenuItem("Tools/Asset Naming Migration/3. Rollback")]
    public static void Rollback()
    {
        var journal = ReadJournal();
        if (journal.Count == 0)
        {
            Debug.Log("[AssetNamingMigration] Rollback: journal is empty (no-op).");
            return;
        }

        var changedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            for (var i = journal.Count - 1; i >= 0; --i)
            {
                var row = journal[i];
                if (row.Kind == "CreateFolder")
                {
                    if (AssetDatabase.IsValidFolder(row.NewPath) && !AssetDatabase.DeleteAsset(row.NewPath))
                        throw new InvalidOperationException("Could not remove migration-created folder: " + row.NewPath);
                    continue;
                }

                var current = AssetDatabase.GUIDToAssetPath(row.Guid);
                if (string.IsNullOrEmpty(current))
                    throw new InvalidOperationException("Rollback GUID is missing: " + row.Guid + " (was " + row.NewPath + ")");
                if (PathsEqual(current, row.OldPath))
                    continue;
                EnsureFolderForRollback(Path.GetDirectoryName(row.OldPath).Replace('\\', '/'));
                MoveOrThrow(current, row.OldPath);
                changedGuids.Add(row.Guid);
            }
        }
        finally
        {
            AssetDatabase.Refresh();
        }

        UpdateAddressables(changedGuids);
        UpdateCardSpecData(false);
        AssetDatabase.SaveAssets();
        File.Delete(Absolute(JournalPath));
        Debug.Log("[AssetNamingMigration] Rollback complete: " + changedGuids.Count + " GUIDs restored.");
    }

    [MenuItem("Tools/Asset Naming Migration/4. Sync Spec Keys")]
    public static void SyncSpecKeys()
    {
        UpdateCardSpecData(true);
        AssetDatabase.Refresh();
        Debug.Log("[AssetNamingMigration] Card spec asset keys synchronized.");
    }

    private static List<Move> BuildPlan()
    {
        var result = new List<Move>();
        foreach (var pair in FolderMoves)
        {
            if (!AssetDatabase.IsValidFolder(pair.Key))
                continue;
            result.Add(new Move
            {
                Kind = "Folder", OldPath = pair.Key,
                Guid = AssetDatabase.AssetPathToGUID(pair.Key), NewPath = pair.Value
            });
        }

        var paths = AssetDatabase.GetAllAssetPaths()
            .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && File.Exists(Absolute(p)))
            .ToArray();

        foreach (var path in paths.Where(HasHangul))
        {
            var mappedPath = ApplyFolderMoves(path);
            var newName = NameCustomAsset(mappedPath, AssetDatabase.AssetPathToGUID(path));
            var destination = Path.GetDirectoryName(mappedPath).Replace('\\', '/') + "/" + newName + Path.GetExtension(path);
            AddAssetMove(result, path, destination);
        }

        foreach (var path in FindUsedPurchasedAssets(paths))
            AddAssetMove(result, path, VendorDestination(path));

        return result;
    }

    private static IEnumerable<string> FindUsedPurchasedAssets(string[] allFiles)
    {
        var roots = allFiles.Where(p => !p.StartsWith(PurchasedRoot, StringComparison.OrdinalIgnoreCase) &&
                                        !p.StartsWith(VendorRoot + "/", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (roots.Length == 0)
            return Array.Empty<string>();
        return AssetDatabase.GetDependencies(roots, true)
            .Where(p => p.StartsWith(PurchasedRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(Absolute(p)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
    }

    private static string VendorDestination(string path)
    {
        var relative = path.Substring(PurchasedRoot.Length);
        var slash = relative.IndexOf('/');
        var pack = slash < 0 ? "UnknownPack" : relative.Substring(0, slash);
        var underPack = slash < 0 ? relative : relative.Substring(slash + 1);
        var directory = Path.GetDirectoryName(underPack)?.Replace('\\', '/');
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(path);
        if (pack == "GUIPackCartoon")
            return "Assets/Assets/Images/UI/" + GuiPackUiName(underPack, stem) + extension;
        var targetStem = extension == ".cs" ? stem : VendorType(extension) + "_Vfx_" + PascalAscii(pack) + "_" + PascalAscii(stem);
        // Keep the established _Vendor/<original pack name>/... boundary. The pack
        // token is normalized only inside the standardized asset basename.
        return VendorRoot + "/" + pack +
               (string.IsNullOrEmpty(directory) ? string.Empty : "/" + directory) + "/" + targetStem + extension;
    }

    private static string GuiPackUiName(string underPack, string stem)
    {
        if (underPack.Contains("/Buttons/")) return "Image_UI_GUIPackCartoon_Button_Rectangle_Teal";
        if (underPack.Contains("/Lock - Key/")) return "Image_UI_GUIPackCartoon_Icon_Lock_Silver";
        if (underPack.Contains("/Tools/")) return "Image_UI_GUIPackCartoon_Icon_Tool_Hammer";
        if (underPack.Contains("/Shapes/Effects/")) return "Image_UI_GUIPackCartoon_Shape_Gradient";
        return "Image_UI_GUIPackCartoon_" + PascalAscii(stem);
    }

    private static string VendorType(string extension)
    {
        switch (extension)
        {
            case ".png": case ".tga": case ".jpg": case ".jpeg": return "Image";
            case ".mat": return "Mat";
            case ".fbx": case ".obj": return "Model";
            case ".shader": case ".shadergraph": return "Shader";
            case ".prefab": return "Vfx";
            case ".anim": return "Anim";
            case ".controller": return "Ctrl";
            default: return "Asset";
        }
    }

    private static string NameCustomAsset(string mappedPath, string guid)
    {
        var stem = Path.GetFileNameWithoutExtension(mappedPath);
        var extension = Path.GetExtension(mappedPath).ToLowerInvariant();
        var card = FindToken(stem, Cards);
        var synergy = FindToken(stem, Synergies);
        var keyword = FindToken(stem, Keywords);

        var stageMatch = Regex.Match(mappedPath, @"/Stage([123])/");
        if (mappedPath.Contains("/Images/Cards/Stage") && card != null)
            return "Image_Card_" + card + "_Stage" + stageMatch.Groups[1].Value +
                   (stem.EndsWith("_3x4", StringComparison.OrdinalIgnoreCase) ? "_Portrait3x4" : string.Empty);
        if (mappedPath.Contains("/Images/Cards/CardArt/") && card != null)
            return "Image_Card_" + card + "_Art";
        if (mappedPath.Contains("/Images/Cards/CardFrame/") && stem.StartsWith("Bow_0003_", StringComparison.Ordinal))
            return "Image_CardFrame_Ranged_Bow_Variant0003";
        if (mappedPath.Contains("/Images/Cards/CardFrame/"))
            return "Image_CardFrame_" + (keyword ?? FallbackSubject(stem, guid)) + FrameVariant(stem);
        if (mappedPath.Contains("/Images/Icons/KeywordIcon/"))
            return "Icon_Keyword_" + (keyword ?? FallbackSubject(stem, guid)) + State(stem);
        if (mappedPath.Contains("/Images/Icons/SynergyIcon/"))
            return "Icon_Synergy_" + (synergy ?? FallbackSubject(stem, guid)) + State(stem);
        if (mappedPath.Contains("/SO/Cards/AttackEffects/") && card != null)
            return "Data_AttackEffect_" + card + (Regex.IsMatch(stem, "3AttackEffect") ? "_Stage3" : string.Empty);
        if (mappedPath.Contains("/SO/Cards/") && card != null)
            return "Data_Card_" + card;
        if (mappedPath.Contains("/SO/Synergies/Effects/") && synergy != null)
            return "Data_SynergyEffect_" + synergy + "_" + PascalAscii(stem.Substring(stem.IndexOf('_') + 1));
        if (mappedPath.Contains("/SO/Synergies/") && synergy != null)
            return "Data_Synergy_" + synergy;
        if (mappedPath.Contains("/Images/Synergy/") && synergy != null)
            return SynergyVisualName(extension, synergy, stem);
        if (mappedPath.Contains("/Particle/Anim/") && keyword != null)
            return "Ctrl_Vfx_" + keyword;
        if (mappedPath.Contains("/Video/") && card != null)
            return "Video_Card_" + card + (stem.Contains(" 3") || stem.Contains("3 ") ? "_Stage3" : string.Empty) + (stem.EndsWith(" A") ? "_A" : string.Empty);
        if (mappedPath.Contains("/Images/UI/"))
            return UiName(extension, stem, guid);
        if (mappedPath.Contains("/Images/References/") || mappedPath.StartsWith("Assets/Assets/Images/화면"))
            return "Image_Reference_Screenshot_" + DateToken(stem, guid);

        return TypeFor(extension) + "_" + CategoryFor(mappedPath) + "_" +
               (card ?? synergy ?? keyword ?? FallbackSubject(stem, guid));
    }

    private static string SynergyVisualName(string extension, string synergy, string stem)
    {
        var type = TypeFor(extension);
        var suffix = stem.Contains("_arm") ? "_Arm" : stem.Contains("_body") ? "_Body" :
                     stem.Contains("_emblem_separated") ? "_Emblem_Separated" :
                     stem.Contains("_emblem") ? "_Emblem" : string.Empty;
        return type + "_Synergy_" + synergy + suffix;
    }

    private static string UiName(string extension, string stem, string guid)
    {
        if (stem == "파편") return TypeFor(extension) + "_UI_Fragment";
        if (stem == "제목 없음") return "Image_UI_Untitled";
        if (stem.StartsWith("ChatGPT Image")) return "Image_UI_Generated_" + DateToken(stem, guid);
        if (stem.Contains("화면 캡처")) return "Image_UI_Screenshot_" + DateToken(stem, guid);
        return TypeFor(extension) + "_UI_" + FallbackSubject(stem, guid);
    }

    private static string FrameVariant(string stem)
    {
        if (stem.EndsWith(" Left")) return "_Left";
        if (stem.EndsWith(" Middle")) return "_Middle";
        if (stem.EndsWith(" Right")) return "_Right";
        if (stem.EndsWith("_01")) return "_Variant01";
        if (stem.StartsWith("Bow_0003_")) return "_Bow_Variant0003";
        return string.Empty;
    }

    private static string State(string stem)
    {
        return Regex.IsMatch(stem, "_(disabled|diabled)$", RegexOptions.IgnoreCase) ? "_Disabled" : "_Enabled";
    }

    private static string TypeFor(string extension)
    {
        switch (extension)
        {
            case ".png": case ".jpg": case ".jpeg": case ".tga": return "Image";
            case ".mat": return "Mat";
            case ".anim": return "Anim";
            case ".controller": return "Ctrl";
            case ".prefab": return "Vfx";
            case ".asset": return "Data";
            case ".mp4": return "Video";
            default: return "Asset";
        }
    }

    private static string CategoryFor(string path)
    {
        if (path.Contains("/Cards/")) return "Card";
        if (path.Contains("/Synerg")) return "Synergy";
        if (path.Contains("/Particle/")) return "Vfx";
        if (path.Contains("/Images/")) return "Image";
        return "General";
    }

    private static string DateToken(string stem, string guid)
    {
        var digits = Regex.Replace(stem, @"\D", string.Empty);
        return digits.Length >= 8 ? digits : "Migrated_" + guid.Substring(0, Math.Min(8, guid.Length));
    }

    private static string FallbackSubject(string stem, string guid)
    {
        var ascii = PascalAscii(stem);
        return string.IsNullOrEmpty(ascii) ? "Migrated_" + guid.Substring(0, Math.Min(8, guid.Length)) : ascii;
    }

    private static string FindToken(string stem, Dictionary<string, string> dictionary)
    {
        foreach (var pair in dictionary.OrderByDescending(x => x.Key.Length))
            if (stem.Contains(pair.Key)) return pair.Value;
        return null;
    }

    private static string ApplyFolderMoves(string path)
    {
        foreach (var pair in FolderMoves)
            if (path.Equals(pair.Key, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(pair.Key + "/", StringComparison.OrdinalIgnoreCase))
                return pair.Value + path.Substring(pair.Key.Length);
        return path;
    }

    private static void AddAssetMove(List<Move> plan, string oldPath, string newPath)
    {
        if (PathsEqual(oldPath, newPath)) return;
        plan.Add(new Move { Kind = "Asset", OldPath = oldPath, Guid = AssetDatabase.AssetPathToGUID(oldPath), NewPath = newPath });
    }

    private static void Validate(List<Move> plan)
    {
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var move in plan)
        {
            if (string.IsNullOrEmpty(move.Guid))
                throw new InvalidOperationException("Missing GUID: " + move.OldPath);
            if (!IsAsciiPathSegment(Path.GetFileNameWithoutExtension(move.NewPath)))
                throw new InvalidOperationException("Destination basename is not ASCII: " + move.NewPath);
            if (!destinations.Add(move.NewPath))
                throw new InvalidOperationException("Duplicate destination: " + move.NewPath);

            var existingGuid = AssetDatabase.AssetPathToGUID(move.NewPath);
            if (!string.IsNullOrEmpty(existingGuid) && !existingGuid.Equals(move.Guid, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Destination already exists: " + move.NewPath);
        }
    }

    private static void ExecuteMove(Move move, string currentPath, HashSet<string> movedGuids)
    {
        if (string.IsNullOrEmpty(currentPath))
            throw new InvalidOperationException("Source GUID is missing: " + move.Guid);
        if (PathsEqual(currentPath, move.NewPath)) return;
        MoveOrThrow(currentPath, move.NewPath);
        AppendJournal(move.Kind, currentPath, move.Guid, move.NewPath);
        movedGuids.Add(move.Guid);
    }

    private static void VerifyMovedGuids(IEnumerable<Move> plan, ISet<string> movedGuids)
    {
        foreach (var move in plan.Where(x => movedGuids.Contains(x.Guid)))
        {
            var movedPath = AssetDatabase.GUIDToAssetPath(move.Guid);
            if (!PathsEqual(movedPath, move.NewPath))
                throw new InvalidOperationException("GUID verification failed after refresh: " + move.Guid + " resolved to " + movedPath);
        }
    }

    private static void MoveOrThrow(string oldPath, string newPath)
    {
        var error = AssetDatabase.MoveAsset(oldPath, newPath);
        if (!string.IsNullOrEmpty(error))
            throw new InvalidOperationException(oldPath + " -> " + newPath + ": " + error);
    }

    private static void EnsureFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
        var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        var guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Could not create folder: " + folder);
        var actualPath = AssetDatabase.GUIDToAssetPath(guid);
        if (!PathsEqual(actualPath, folder))
        {
            AssetDatabase.DeleteAsset(actualPath);
            throw new InvalidOperationException("Unity changed the requested folder path: " + folder + " -> " + actualPath);
        }
        AppendJournal("CreateFolder", string.Empty, guid, folder);
    }

    private static void EnsureFolderForRollback(string folder)
    {
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
        var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolderForRollback(parent);
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(folder))))
            throw new InvalidOperationException("Could not restore folder: " + folder);
    }

    private static void UpdateAddressables(IEnumerable<string> guids)
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return;
        foreach (var guid in guids)
        {
            var entry = settings.FindAssetEntry(guid);
            if (entry == null) continue;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || entry.address == path) continue;
            entry.address = path;
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
        }
    }

    private static void BeginJournal()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Absolute(JournalPath)));
        File.WriteAllText(Absolute(JournalPath), "kind,oldPath,guid,newPath\n", new UTF8Encoding(true));
    }

    private static void AppendJournal(string kind, string oldPath, string guid, string newPath)
    {
        File.AppendAllText(Absolute(JournalPath), Csv(kind) + "," + Csv(oldPath) + "," + Csv(guid) + "," + Csv(newPath) + "\n", Encoding.UTF8);
    }

    private static List<Move> ReadJournal()
    {
        var absolute = Absolute(JournalPath);
        if (!File.Exists(absolute)) return new List<Move>();
        return File.ReadAllLines(absolute).Skip(1).Where(x => !string.IsNullOrWhiteSpace(x)).Select(line =>
        {
            var values = ParseCsv(line);
            return new Move { Kind = values[0], OldPath = values[1], Guid = values[2], NewPath = values[3] };
        }).ToList();
    }

    private static void WriteCsv(string relativePath, string header, IEnumerable<Move> moves)
    {
        var absolute = Absolute(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute));
        var lines = new List<string> { header };
        lines.AddRange(moves.Select(x => Csv(x.Kind) + "," + Csv(x.OldPath) + "," + Csv(x.Guid) + "," + Csv(x.NewPath)));
        File.WriteAllLines(absolute, lines, new UTF8Encoding(true));
    }

    private static void WriteGlossary()
    {
        var lines = new List<string> { "korean,english,kind" };
        lines.AddRange(Cards.Where(x => !new[] { "물방울릉", "솜구름몸", "화르륵스", "헤롱문어" }.Contains(x.Key))
            .Select(x => Csv(x.Key) + "," + Csv(x.Value) + ",Card"));
        lines.AddRange(Synergies.Select(x => Csv(x.Key) + "," + Csv(x.Value) + ",Synergy"));
        lines.AddRange(Keywords.Select(x => Csv(x.Key) + "," + Csv(x.Value) + ",Keyword"));
        File.WriteAllLines(Absolute("docs/naming-glossary.csv"), lines, new UTF8Encoding(true));
    }

    private static void UpdateCardSpecData(bool forward)
    {
        var reverseCards = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Campbean", "모닥콩" }, { "Poslamb", "포슬램" }, { "Sparkfin", "찌릿핀" },
            { "WaterdropLong", "물방울룽" }, { "Rockbean", "바위콩" }, { "Nightchestnut", "깜밤이" },
            { "Cloudmong", "솜구름몽" }, { "Icekomi", "얼음꼬미" }, { "Honeybee", "꿀꿀비" },
            { "Gearmole", "톱니두더" }, { "Flarelux", "화르룩스" }, { "IronMongchi", "철갑몽치" },
            { "BalloonPeng", "풍선펭" }, { "MushroomCat", "버섯냥" }, { "Startori", "별토리" },
            { "Swampfrog", "늪꾸리" }, { "Thunderhorn", "번개뿔" }, { "Mapletail", "단풍꼬리" },
            { "SnowballBear", "눈덩곰" }, { "Waggledodo", "와글도도" }, { "MagnetCrab", "자석게" },
            { "DizzyOctopus", "해롱문어" }, { "Bombbat", "폭탄밤" }, { "Woodhorn", "우드혼" },
            { "Waveri", "파도리" }, { "Sandmong", "모래몽" }, { "Crystalhorn", "수정뿔루" },
            { "CaptainBeak", "대장부리" }, { "Dreameater", "꿈먹이" },
            { "KingChestnutHedgehog", "왕밤도치" }
        };
        var reverseSynergies = Synergies.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);

        foreach (var relativePath in CardSpecPaths)
        {
            var absolute = Absolute(relativePath);
            if (!File.Exists(absolute)) continue;
            var delimiter = relativePath.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
            var lines = File.ReadAllLines(absolute, Encoding.UTF8);
            for (var i = 3; i < lines.Length; ++i)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var fields = ParseDelimited(lines[i], delimiter);
                if (fields.Count < 8) throw new FormatException(relativePath + ": invalid row " + (i + 1));
                if (forward)
                {
                    var subject = Translate(fields[1], Cards);
                    fields[1] = subject.StartsWith("Data_Card_", StringComparison.Ordinal)
                        ? subject : "Data_Card_" + subject;
                }
                else
                {
                    var subject = fields[1].StartsWith("Data_Card_", StringComparison.Ordinal)
                        ? fields[1].Substring("Data_Card_".Length) : fields[1];
                    fields[1] = Translate(subject, reverseCards);
                }
                fields[7] = Regex.Replace(fields[7], @"[^/|]+", match =>
                {
                    var raw = match.Value;
                    var trimmed = raw.Trim();
                    string translated;
                    if (forward)
                    {
                        var subject = Translate(trimmed, Synergies);
                        translated = subject.StartsWith("Data_Synergy_", StringComparison.Ordinal)
                            ? subject : "Data_Synergy_" + subject;
                    }
                    else
                    {
                        var subject = trimmed.StartsWith("Data_Synergy_", StringComparison.Ordinal)
                            ? trimmed.Substring("Data_Synergy_".Length) : trimmed;
                        translated = Translate(subject, reverseSynergies);
                    }
                    return raw.Substring(0, raw.Length - raw.TrimStart().Length) + translated +
                           raw.Substring(raw.TrimEnd().Length);
                });
                lines[i] = string.Join(delimiter.ToString(), fields.Select(x => Delimited(x, delimiter)));
            }
            File.WriteAllLines(absolute, lines, new UTF8Encoding(true));
        }
    }

    private static string Translate(string value, IDictionary<string, string> map)
    {
        string translated;
        return map.TryGetValue(value, out translated) ? translated : value;
    }

    private static List<string> ParseDelimited(string line, char delimiter)
    {
        var values = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; ++i)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); ++i; }
                else quoted = !quoted;
            }
            else if (c == delimiter && !quoted) { values.Add(value.ToString()); value.Length = 0; }
            else value.Append(c);
        }
        if (quoted) throw new FormatException("Unterminated quoted field: " + line);
        values.Add(value.ToString());
        return values;
    }

    private static string Delimited(string value, char delimiter)
    {
        value = value ?? string.Empty;
        return value.IndexOfAny(new[] { delimiter, '"', '\n', '\r' }) < 0
            ? value
            : "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string[] ParseCsv(string line)
    {
        var values = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; ++i)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); ++i; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { values.Add(value.ToString()); value.Length = 0; }
            else value.Append(c);
        }
        values.Add(value.ToString());
        if (values.Count != 4) throw new FormatException("Invalid migration journal row: " + line);
        return values.ToArray();
    }

    private static string Csv(string value)
    {
        value = value ?? string.Empty;
        return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string PascalAscii(string value)
    {
        var words = Regex.Split(value ?? string.Empty, @"[^A-Za-z0-9]+" ).Where(x => x.Length > 0);
        return string.Concat(words.Select(x => char.ToUpperInvariant(x[0]) + x.Substring(1)));
    }

    private static bool HasHangul(string path)
    {
        return Regex.IsMatch(Path.GetFileNameWithoutExtension(path), "[가-힣]");
    }

    private static bool IsAsciiPathSegment(string value)
    {
        return !string.IsNullOrEmpty(value) && value.All(c => c <= 127 && (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ' || c == '.'));
    }

    private static bool PathsEqual(string a, string b)
    {
        return string.Equals(a?.Replace('\\', '/'), b?.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    private static string Absolute(string projectRelative)
    {
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), projectRelative));
    }

    private static KeyValuePair<string, string> Pair(string oldPath, string newPath)
    {
        return new KeyValuePair<string, string>(oldPath, newPath);
    }
}
#endif
