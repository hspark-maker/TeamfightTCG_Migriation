#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>가이드 미션 연결의 중복·순환·누락을 읽기 전용으로 검사한다.</summary>
public static class GuideMissionFlowValidation
{
    [MenuItem("Tools/Tutorial/Validate Guide Mission Flows")]
    public static void Run()
    {
        GuideMissionProgressValidation.Run();
        ContentUnlockSeparationValidation.Run();
        var data = AssetDatabase.LoadAssetAtPath<OutgameTutorialData>(
            "Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset");
        var errors = Validate(data);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("\n", errors));
        Debug.Log("[GuideMissionFlowValidation] PASS: mission IDs, unique flow ownership, chapters, intro definitions, mission access cycle.", data);
    }

    /// <summary>에셋과 로컬 미션 CSV에서 연결 오류를 수집한다.</summary>
    public static List<string> Validate(OutgameTutorialData data)
    {
        var errors = new List<string>();
        var unlocks = ContentUnlockAuthoring.Data;
        if (unlocks == null)
        {
            errors.Add("Content unlock catalog reference missing.");
            return errors;
        }
        if (!ContentUnlockConfig.TryValidate(unlocks.contentUnlocks, out var unlockError))
        {
            errors.Add(unlockError);
            return errors;
        }
        bool needsMissions = unlocks.contentUnlocks.Exists(rule => !string.IsNullOrEmpty(rule?.guideMissionId))
            || (data != null && data.guide.guideFlows != null
                && data.guide.guideFlows.Exists(flow => !string.IsNullOrEmpty(flow?.missionId)));
        var missionIds = needsMissions ? ReadMissionIds(errors) : new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in unlocks.contentUnlocks)
            if (rule != null && !string.IsNullOrEmpty(rule.guideMissionId) && !missionIds.Contains(rule.guideMissionId))
                errors.Add($"Content {rule.feature}: guide mission '{rule.guideMissionId}' missing.");
        if (data == null)
        {
            errors.Add("Tutorial asset missing.");
            return errors;
        }
        if (data.guide.guideFlows == null || data.guide.guideFlows.Count == 0)
        {
            return errors;
        }
        var missions = new HashSet<string>(StringComparer.Ordinal);
        var tutorials = new HashSet<EOutgameTutorialTrigger>();
        var contents = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < data.guide.guideFlows.Count; i++)
        {
            var flow = data.guide.guideFlows[i];
            string label = $"Guide flow {i}";
            if (flow == null) { errors.Add($"{label}: empty row."); continue; }
            string mission = flow.missionId ?? string.Empty;
            if (!missions.Add(mission)) errors.Add($"{label}: duplicate mission '{mission}'.");
            if (mission.Length > 0 && !missionIds.Contains(mission))
                errors.Add($"{label}: enabled guide mission '{mission}' missing in Mission_sheet.csv.");
            if (flow.tutorial != EOutgameTutorialTrigger.None)
            {
                if (!tutorials.Add(flow.tutorial)) errors.Add($"{label}: duplicate tutorial {flow.tutorial}.");
                int matches = 0;
                if (data.Chapters != null)
                    foreach (var chapter in data.Chapters)
                        if (chapter != null && chapter.IsGuided && chapter.Trigger == flow.tutorial) matches++;
                if (matches != 1) errors.Add($"{label}: expected one guided chapter for {flow.tutorial}, found {matches}.");
            }
            bool hasIntros = flow.contentIntros != null && flow.contentIntros.Count > 0;
            if (!hasIntros && flow.tutorial == EOutgameTutorialTrigger.None)
                errors.Add($"{label}: no intro or tutorial.");
            if (!hasIntros) continue;
            foreach (var content in flow.contentIntros)
            {
                string key = ContentUnlockIntroDef.KeyOf(content);
                if (string.IsNullOrEmpty(key)) errors.Add($"{label}: {content} has no content unlock key.");
                else
                {
                    if (!contents.Add(key)) errors.Add($"{label}: duplicate content key {key}.");
                    if (!ContentUnlockConfig.TryGet(ContentUnlockAuthoring.Data.contentUnlocks, key, out _))
                        errors.Add($"{label}: content unlock rule {key} missing.");
                }
                if (content == EContentUnlockIntro.Mission && mission.Length > 0)
                    errors.Add($"{label}: mission access cannot depend on an active mission.");
                if (!ContentUnlockAuthoring.Data.TryGetContentIntro(content, out var intro) || intro.icon == null)
                    errors.Add($"{label}: intro definition/icon {content} missing.");
            }
        }
        return errors;
    }

    static HashSet<string> ReadMissionIds(List<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string path = Path.Combine(Application.dataPath, "../docs/SpecData/Mission_sheet.csv");
        if (!File.Exists(path) || !SpecDocsCsvExporter.TryParseCsv(File.ReadAllText(path), out var rows)
            || rows.Count < 3)
        {
            errors.Add("Mission_sheet.csv missing or malformed.");
            return ids;
        }
        int idColumn = rows[1].IndexOf("missionId");
        int periodColumn = rows[1].IndexOf("period");
        int enabledColumn = rows[1].IndexOf("enabled");
        if (idColumn < 0 || periodColumn < 0 || enabledColumn < 0)
        {
            errors.Add("Mission_sheet.csv requires missionId, period and enabled columns.");
            return ids;
        }
        int lastColumn = Math.Max(idColumn, Math.Max(periodColumn, enabledColumn));
        for (int i = 3; i < rows.Count; i++)
            if (rows[i].Count > lastColumn && rows[i][periodColumn] == "guide" && rows[i][enabledColumn] == "1")
                ids.Add(rows[i][idColumn]);
        return ids;
    }
}
#endif
