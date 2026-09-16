using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>FTUE·가이드 분리 저작과 기존 논리 좌표의 호환을 검증한다.</summary>
public static class TutorialChapterSeparationValidation
{
    [MenuItem("Tools/Tutorial/Validate FTUE Guide Separation")]
    public static void Run()
    {
        var t_data = AssetDatabase.LoadAssetAtPath<OutgameTutorialData>(
            "Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset");
        Require(t_data != null, "Tutorial data missing.");
        var t_serialized = new SerializedObject(t_data);
        Require(t_serialized.FindProperty("chapters") == null, "Mixed chapter storage remains.");
        Require(t_serialized.FindProperty("contentUnlocks") == null
            && t_serialized.FindProperty("contentIntros") == null
            && t_serialized.FindProperty("guide.contentUnlocks") == null
            && t_serialized.FindProperty("guide.contentIntros") == null,
            "Content unlock authoring must belong to the guide axis.");
        Require(t_serialized.FindProperty("ftueChapters") != null
            && t_serialized.FindProperty("guide.guideChapters") != null, "Separate chapter storage missing.");
        Require(t_data.FtueChapterCount == 4 && t_data.guide.guideChapters.Count == 5,
            "Existing FTUE boundary or guide chapter count changed.");
        var t_ids = new HashSet<int>();
        for (int t_i = 0; t_i < t_data.Chapters.Count; t_i++)
        {
            var t_chapter = t_data.Chapters[t_i];
            bool t_guide = t_i >= t_data.FtueChapterCount;
            Require(t_chapter != null && t_chapter.IsGuided == t_guide, "Chapter kind differs from its section.");
            var t_property = TutorialChapterProperties.GetChapter(t_serialized, t_i);
            Require(t_property != null && t_property.FindPropertyRelative("kind") == null,
                "Chapter kind must be derived, not independently authored.");
            Require(t_property.FindPropertyRelative("label").stringValue == t_chapter.Label,
                "Editor chapter coordinate maps to a different chapter.");
            for (int t_step = 0; t_step < t_chapter.StepCount; t_step++)
            {
                Require(t_chapter.TryGetStep(t_step, out var t_definition)
                    && t_definition.StepId > 0 && t_ids.Add(t_definition.StepId), "Step identity lost or duplicated.");
            }
        }

        var t_copy = ScriptableObject.CreateInstance<OutgameTutorialData>();
        try
        {
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(t_data), t_copy);
            t_copy.OnAfterDeserialize();
            Require(t_copy.FtueChapterCount == t_data.FtueChapterCount
                && t_copy.Chapters.Count == t_data.Chapters.Count, "Serialized chapter counts changed.");
            for (int t_i = 0; t_i < t_data.Chapters.Count; t_i++)
            {
                var t_source = t_data.Chapters[t_i];
                var t_restored = t_copy.Chapters[t_i];
                Require(t_source.Kind == t_restored.Kind && t_source.Trigger == t_restored.Trigger
                    && t_source.StepCount == t_restored.StepCount, "Chapter identity changed after round trip.");
                for (int t_step = 0; t_step < t_source.StepCount; t_step++)
                {
                    t_source.TryGetStep(t_step, out var t_before);
                    t_restored.TryGetStep(t_step, out var t_after);
                    Require(t_before.StepId == t_after.StepId, "Saved step coordinate changed after round trip.");
                }
            }
            Require(!TutorialSequenceEditOps.MoveChapter(t_copy, t_copy.FtueChapterCount - 1, 1)
                && !TutorialSequenceEditOps.MoveChapter(t_copy, t_copy.FtueChapterCount, -1),
                "Reordering must not move a chapter across FTUE/Guide sections.");
            var t_view = t_copy.Chapters;
            int t_count = t_view.Count;
            t_copy.guide.guideChapters.Add(new OutgameTutorialChapter());
            t_copy.NormalizeChapterKinds();
            Require(t_view.Count == t_count + 1 && t_view[t_count].IsGuided,
                "Logical chapter view must track the real lists without a stale copy.");
            t_copy.guide.guideFlows.Clear();
            t_copy.guide.guideChapters.Clear();
            Require(GuideMissionFlowValidation.Validate(t_copy).Count == 0,
                "Guide mission authoring must be optional.");
            Require(ContentUnlockConfig.TryValidate(ContentUnlockAuthoring.Data.contentUnlocks, out var t_error), t_error);
            Require(ContentUnlockAuthoring.Data.TryGetContentIntro(EContentUnlockIntro.Mission, out _),
                "Removing missions must preserve guide content introductions.");
            Require(ContentUnlockConfig.TryGet(ContentUnlockAuthoring.Data.contentUnlocks, ContentUnlockManager.MISSION, out var t_rule)
                && ContentUnlockRules.Evaluate(t_rule, true, true, true, 100, 0, true, 100).IsUnlocked,
                "Guide content rules must work without mission flows or guide chapters.");
        }
        finally { UnityEngine.Object.DestroyImmediate(t_copy); }
        Debug.Log("[TutorialChapterSeparationValidation] PASS: split storage, editor coordinates, saved IDs, kind derivation, section boundaries.", t_data);
    }

    static void Require(bool _condition, string _message)
    {
        if (!_condition) throw new InvalidOperationException(_message);
    }
}
