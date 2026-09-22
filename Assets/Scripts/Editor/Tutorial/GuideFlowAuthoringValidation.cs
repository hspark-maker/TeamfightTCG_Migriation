using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>콘텐츠 안내 저작의 누락·중복·조건 혼용 검출을 검사한다.</summary>
public static class GuideFlowAuthoringValidation
{
    [MenuItem("Tools/Tutorial/Validate Guide Flow Authoring")]
    public static void Run() => Debug.Log($"[GuideFlowAuthoringValidation] PASS: {Validate()} checks");

    public static int Validate()
    {
        var source = AssetDatabase.LoadAssetAtPath<OutgameTutorialData>(
            "Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset");
        if (source == null) throw new InvalidOperationException("온보딩 저작 에셋이 없습니다.");
        int checks = 0;
        Check(null, false);
        Check(data => Content(data).missionId = "guide.01", true);
        Check(data => Content(data).content = EOutgameFeature.None, true);
        Check(data => data.guide.guideFlows.Remove(Content(data)), true);
        Check(data => data.guide.guideFlows.Add(Content(data)), true);
        Check(data => Content(data).contentIntros.Clear(), true);
        Check(data => Content(data).contentIntros[0] = EContentUnlockIntro.None, true);
        Check(data => Content(data).contentIntros[0] = EContentUnlockIntro.Ranked, true);
        Check(data => Content(data).tutorial = (EOutgameTutorialTrigger)int.MaxValue, true);
        Check(data => Mission(data).missionId = "", true);
        Check(data => Mission(data).content = EOutgameFeature.Mission, true);
        Check(data => Mission(data).tutorial = data.guide.guideFlows.Find(
            flow => flow.activation == EGuideFlowActivation.ContentUnlock
                && flow.tutorial != EOutgameTutorialTrigger.None).tutorial, true);
        return checks;

        void Check(Action<OutgameTutorialData> mutate, bool expectError)
        {
            var copy = UnityEngine.Object.Instantiate(source);
            try
            {
                mutate?.Invoke(copy);
                var issues = new List<TutorialIssue>();
                TutorialValidator.ValidateGuideFlows(copy, ContentUnlockAuthoring.Data, issues);
                bool hasError = issues.Exists(issue => issue.Level == ETutorialIssueLevel.Error);
                checks++;
                if (hasError != expectError)
                    throw new InvalidOperationException($"안내 저작 검사 {checks} 실패: "
                        + string.Join("; ", issues.ConvertAll(issue => issue.Message)));
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }
    }

    static GuideMissionFlow Content(OutgameTutorialData data)
        => data.guide.guideFlows.Find(flow => flow.activation == EGuideFlowActivation.ContentUnlock);

    static GuideMissionFlow Mission(OutgameTutorialData data)
        => data.guide.guideFlows.Find(flow => flow.activation == EGuideFlowActivation.Mission);
}
