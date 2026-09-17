#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>가이드 안내의 최초 저작과 저장 계약을 검증한다.</summary>
public static class GuideOnboardingDataAuthoring
{
    const string PATH = "Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset";

    [MenuItem("Tools/Tutorial/Author Guide Onboarding Data")]
    public static void Author()
    {
        var t_data = AssetDatabase.LoadAssetAtPath<OutgameTutorialData>(PATH);
        Undo.RecordObject(t_data, "Author guide onboarding");
        int t_chapterIndex = t_data.guide.guideChapters.FindIndex(_chapter => _chapter.Trigger == EOutgameTutorialTrigger.GuideMissionIntroduction);
        if (t_chapterIndex < 0)
        {
            var t_chapter = new OutgameTutorialChapter();
            t_chapter.EditorLabel = "가이드 미션 소개";
            t_chapter.EditorKind = EOutgameTutorialChapterKind.Guided;
            t_chapter.EditorTrigger = EOutgameTutorialTrigger.GuideMissionIntroduction;
            t_chapter.EditorPrerequisite = EOutgameFeature.Mission;
            t_chapter.EditorSteps.Add(new TutorialStepDef());
            t_data.guide.guideChapters.Add(t_chapter);
            t_chapterIndex = t_data.Chapters.Count - 1;
            var t_serialized = new SerializedObject(t_data);
            var t_step = TutorialChapterProperties.GetChapter(t_serialized, t_chapterIndex)
                .FindPropertyRelative("stepDefs").GetArrayElementAtIndex(0);
            t_step.FindPropertyRelative("action").intValue = (int)EOutgameTutorialAction.Message;
            t_step.FindPropertyRelative("guideMessage").stringValue = "가이드 미션이 다음 목표를 알려줘요.\n현재 목표와 보상을 확인하고 [이동]을 눌러 시작해 보세요.\n목표를 달성하면 직접 보상을 받을 수 있어요.";
            t_step.FindPropertyRelative("useDim").boolValue = true;
            t_serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        var t_so = new SerializedObject(t_data);
        for (int t_c = 0; t_c < t_data.Chapters.Count; t_c++)
        {
            var t_steps = TutorialChapterProperties.GetChapter(t_so, t_c).FindPropertyRelative("stepDefs");
            for (int t_s = 0; t_s < t_steps.arraySize; t_s++)
            {
                var t_step = t_steps.GetArrayElementAtIndex(t_s);
                int t_id = t_step.FindPropertyRelative("stepId").intValue;
                if (t_id == 28 || t_id == 29) t_step.FindPropertyRelative("anchorCardId").intValue = 0;
                if (t_id == 30) t_step.FindPropertyRelative("guideMessage").stringValue =
                    "강화 버튼을 한 번 누르세요!\n선택한 수량의 샤드가 들어가고, 필요량을 채우면 별이 늘어나요.\n{enhanceCost}";

            }
        }
        t_so.ApplyModifiedPropertiesWithoutUndo();
        t_data.NormalizeChapterKinds();
        t_data.AssignMissingStepIds();
        EditorUtility.SetDirty(t_data);
        AssetDatabase.SaveAssetIfDirty(t_data);
        Debug.Log("[GuideOnboarding] Authored introduction message and mission chapter.");
    }

    [MenuItem("Tools/Tutorial/Validate Guide Onboarding")]
    public static void Validate()
    {
        var t_data = AssetDatabase.LoadAssetAtPath<OutgameTutorialData>(PATH);
        Require(HasEnhanceUnlockStep(t_data), "Enhance unlock explanation step missing");
        var t_ids = new HashSet<int>();
        foreach (var t_chapter in t_data.Chapters)
            for (int t_i = 0; t_i < t_chapter.StepCount; t_i++)
            {
                t_chapter.TryGetStep(t_i, out var t_step);
                Require(t_step.StepId > 0 && t_ids.Add(t_step.StepId), "Duplicate step id");
                if (t_step.StepId == 30) Require(t_step.WaitUnlockIntro, "Enhance must await the unlock intro");
                if (t_step.StepId == 28 || t_step.StepId == 29) Require(t_step.AnchorCardId == 0, "Fixed enhance card remains");
            }
        var t_save = new TutorialSaveData { OutgameCompleted = true, StepId = 25 };
        t_save.CompletedTriggers.Add("CollectionTabFirstEnter");
        t_save.CompletedTriggers.Add(EOutgameTutorialTrigger.KeywordIntroduction.ToString());
        t_save.CompletedTriggers.Add(EOutgameTutorialTrigger.CaretakerPreparation.ToString());
        var t_copy = Newtonsoft.Json.JsonConvert.DeserializeObject<TutorialSaveData>(Newtonsoft.Json.JsonConvert.SerializeObject(t_save));
        Require(t_copy.OutgameCompleted && t_copy.StepId == 25 && t_copy.CompletedTriggers.Count == 3
            && t_copy.CompletedTriggers.Contains("KeywordIntroduction"), "Completed explanations lost in round trip");
        GuidanceIntegrationValidation.Run();
        Debug.Log("[GuideOnboarding] PASS: authoring, step ids, unlock wait, old/new completion round trip.");
    }

    public static bool HasEnhanceUnlockStep(OutgameTutorialData _data)
    {
        foreach (var t_chapter in _data.Chapters)
        {
            if (t_chapter.Trigger != EOutgameTutorialTrigger.CollectionTabFirstEnter) continue;
            for (int t_i = 0; t_i < t_chapter.StepCount; t_i++)
            {
                if (!t_chapter.TryGetStep(t_i, out var t_step)) continue;
                if (t_step.Action == EOutgameTutorialAction.WaitUnlockIntro && !string.IsNullOrEmpty(t_step.GuideMessage)) return true;
                if (t_step.Action == EOutgameTutorialAction.WaitEnhance && t_step.WaitUnlockIntro
                    && t_chapter.TryGetStep(t_i + 1, out var t_next)
                    && t_next.Action == EOutgameTutorialAction.Message && !string.IsNullOrEmpty(t_next.GuideMessage)) return true;
            }
        }
        return false;
    }

    static void Require(bool _condition, string _message)
    {
        if (!_condition) throw new System.InvalidOperationException(_message);
    }
}
#endif
