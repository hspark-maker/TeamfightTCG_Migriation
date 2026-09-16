using System;
using UnityEditor;
using UnityEngine;

/// <summary>해금 저작의 독립성과 조건 조합을 검증한다.</summary>
public static class ContentUnlockSeparationValidation
{
    [MenuItem("Tools/Tutorial/Validate Content Unlock Separation")]
    public static void Run()
    {
        var t_data = ContentUnlockAuthoring.Data;
        Require(t_data != null, "Runtime catalog must reference content unlock data.");
        Require(ContentUnlockConfig.TryValidate(t_data.contentUnlocks, out var t_error), t_error);
        var t_tutorial = AssetDatabase.LoadAssetAtPath<OutgameTutorialData>(
            "Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset");
        var t_serialized = new SerializedObject(t_tutorial);
        Require(t_serialized.FindProperty("guide.contentUnlocks") == null
            && t_serialized.FindProperty("guide.contentIntros") == null,
            "Tutorial data must not own content unlock authoring.");
        var t_copy = UnityEngine.Object.Instantiate(t_data);
        try
        {
            Require(t_copy.contentUnlocks.Count == t_data.contentUnlocks.Count
                && t_copy.contentIntros.Count == t_data.contentIntros.Count,
                "Independent asset serialization lost authored rows.");
            var t_row = new ContentUnlockDef { feature = EOutgameFeature.Adventure,
                requireFtue = true, guideMissionId = "guide.test", minAccountLevel = 5 };
            var t_rule = new ContentUnlockRule(t_row);
            Require(Evaluate(t_rule, true, 5, true, true).IsUnlocked, "Reached conditions must unlock.");
            Require(Evaluate(t_rule, false, 5, true, true).Missing == EContentUnlockRequirement.Ftue,
                "Mission progress must not bypass FTUE.");
            Require(Evaluate(t_rule, true, 4, true, true).Missing == EContentUnlockRequirement.AccountLevel,
                "Mission progress must not bypass account level.");
            Require(Evaluate(t_rule, true, 5, false, false).Missing == EContentUnlockRequirement.Data,
                "Mission condition must await server data.");
            Require(Evaluate(t_rule, true, 5, true, false).Missing == EContentUnlockRequirement.GuideMission,
                "Future mission must remain locked.");
            t_row.guideMissionId = null;
            Require(Evaluate(new ContentUnlockRule(t_row), true, 5, false, false).IsUnlocked,
                "Content without a mission condition must not depend on mission availability.");
            Require(t_copy.TryGetContentIntro(EContentUnlockIntro.Mission, out var t_intro) && t_intro.TryValidate(out _),
                "Content presentation must resolve without tutorial data.");
        }
        finally { UnityEngine.Object.DestroyImmediate(t_copy); }
        Debug.Log("[ContentUnlockSeparationValidation] PASS: independent asset, catalog binding, AND conditions, optional missions.");
    }

    static ContentUnlockEvaluation Evaluate(ContentUnlockRule _rule, bool _ftue, int _level, bool _ready, bool _reached)
        => ContentUnlockRules.Evaluate(_rule, _ftue, true, true, 0, 0, true, _level, _ready, _reached);

    static void Require(bool _condition, string _message)
    {
        if (!_condition) throw new InvalidOperationException(_message);
    }
}
