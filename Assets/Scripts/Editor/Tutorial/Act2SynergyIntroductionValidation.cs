#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>2막 시너지 입문의 미션 연결·성장 순서·저장 식별자를 읽기 전용으로 검증한다.</summary>
public static class Act2SynergyIntroductionValidation
{
    const string ASSET_PATH = "Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset";

    /// <summary>실제 온보딩 에셋과 기존 검증기의 오류를 함께 검사한다.</summary>
    [MenuItem("Tools/Tutorial/Validate Act 2 Synergy Introduction")]
    public static void Run()
    {
        var t_data = AssetDatabase.LoadAssetAtPath<OutgameTutorialData>(ASSET_PATH);
        if (t_data == null) throw new InvalidOperationException("Outgame tutorial asset missing.");
        var t_errors = GuideMissionFlowValidation.Validate(t_data);
        foreach (var t_issue in TutorialValidator.Validate(t_data))
            if (t_issue.Level == ETutorialIssueLevel.Error)
                t_errors.Add($"{t_issue.Coord} {t_issue.Rule}: {t_issue.Message}");
        ValidateTriggerIds(t_errors);
        ValidateStepIds(t_data, t_errors);
        var t_growth = ValidateFlow(t_data, "guide.05", EOutgameTutorialTrigger.SynergyGrowthIntroduction, t_errors);
        var t_battle = ValidateFlow(t_data, "guide.16", EOutgameTutorialTrigger.SynergyBattleIntroduction, t_errors);
        if (t_growth != null) ValidateGrowth(t_growth, t_errors);
        if (t_battle != null) ValidateBattle(t_battle, t_errors);
        if (t_errors.Count > 0) throw new InvalidOperationException(string.Join("\n", t_errors));
        Debug.Log("[Act2SynergyIntroductionValidation] PASS: authored mission flows, trigger and step IDs, free synergy growth, unlock intro order, synergy deck guidance. Runtime play is not covered.", t_data);
    }

    static void ValidateTriggerIds(List<string> _errors)
    {
        string[] t_names =
        {
            "None", "DeckTabFirstEnter", "CollectionTabFirstEnter", "FirstEvolutionReady",
            "KeywordGrowthFirstOpen", "AdventureMapFirstOpen", "AdventureUnlocked", "RankDivisionFirstUp",
            "RankGradeFirstUp", "ContentUnlocksAvailable", "GuideCaretakerEnhanceArrived", "GuideAceEnhanceArrived",
            "GuideMissionIntroduction", "KeywordIntroduction", "SynergyIntroduction", "CaretakerPreparation",
            "CaretakerReady", "CaretakerActivation", "SynergyGrowthIntroduction", "SynergyBattleIntroduction",
        };
        for (int t_index = 0; t_index < t_names.Length; t_index++)
            if (!Enum.TryParse(t_names[t_index], out EOutgameTutorialTrigger t_trigger) || (int)t_trigger != t_index)
                _errors.Add($"Stored trigger identity changed: {t_names[t_index]} must remain {t_index}.");
        var t_values = new HashSet<int>();
        foreach (EOutgameTutorialTrigger t_trigger in Enum.GetValues(typeof(EOutgameTutorialTrigger)))
            if (!t_values.Add((int)t_trigger)) _errors.Add($"Duplicate trigger value: {(int)t_trigger}.");
    }

    static void ValidateStepIds(OutgameTutorialData _data, List<string> _errors)
    {
        var t_ids = new HashSet<int>();
        foreach (var t_chapter in _data.Chapters)
        {
            if (t_chapter == null) continue;
            for (int t_index = 0; t_index < t_chapter.StepCount; t_index++)
            {
                if (!t_chapter.TryGetStep(t_index, out var t_step)) continue;
                if (t_step.StepId <= 0 || !t_ids.Add(t_step.StepId))
                    _errors.Add($"{t_chapter.Label} step {t_index}: invalid or duplicate step ID {t_step.StepId}.");
            }
        }
    }

    static OutgameTutorialChapter ValidateFlow(OutgameTutorialData _data, string _missionId,
        EOutgameTutorialTrigger _trigger, List<string> _errors)
    {
        int t_matches = 0;
        if (_data.guide?.guideFlows != null)
            foreach (var t_flow in _data.guide.guideFlows)
                if (t_flow != null && t_flow.missionId == _missionId)
                {
                    t_matches++;
                    if (t_flow.tutorial != _trigger) _errors.Add($"{_missionId} must use new trigger {_trigger}.");
                }
        if (t_matches != 1) _errors.Add($"{_missionId}: expected one mission flow, found {t_matches}.");
        OutgameTutorialChapter t_result = null;
        t_matches = 0;
        foreach (var t_chapter in _data.Chapters)
            if (t_chapter != null && t_chapter.EditorTrigger == _trigger)
            {
                t_matches++;
                t_result = t_chapter;
                if (!t_chapter.IsGuided) _errors.Add($"{_trigger} must belong to guided chapters.");
            }
        if (t_matches != 1) _errors.Add($"{_trigger}: expected one chapter, found {t_matches}.");
        return t_result;
    }

    static void ValidateGrowth(OutgameTutorialChapter _chapter, List<string> _errors)
    {
        int t_enhance = IndexOf(_chapter, EOutgameTutorialAction.WaitEnhance);
        int t_intro = IndexOf(_chapter, EOutgameTutorialAction.WaitUnlockIntro);
        if (t_enhance < 0 || t_intro >= 0)
            _errors.Add("Growth must wait within WaitEnhance, without a separate WaitUnlockIntro action.");
        if (_chapter.TryGetStep(t_enhance, out var t_enhanceStep) && !t_enhanceStep.WaitUnlockIntro)
            _errors.Add("Growth enhancement must wait for the actual unlock introduction.");
        if (t_enhanceStep != null && !t_enhanceStep.FreeOfCharge)
            _errors.Add("Synergy introduction growth must be free.");
        if (!_chapter.TryGetStep(t_enhance + 1, out var t_message)
            || t_message.Action != EOutgameTutorialAction.Message || string.IsNullOrWhiteSpace(t_message.GuideMessage)
            || t_message.Anchor != EOutgameTutorialAnchor.CardDetailSynergyDescription
            || t_message.Spotlight != EOutgameTutorialAnchor.CardDetailCardView)
            _errors.Add("Growth must show a synergy explanation with the card spotlight after the unlock-waiting enhancement.");
        if (!_chapter.TryGetStep(_chapter.StepCount - 1, out var t_final)
            || t_final.Action != EOutgameTutorialAction.Message || string.IsNullOrWhiteSpace(t_final.GuideMessage))
            _errors.Add("Growth chapter must end with an authored message.");
        foreach (var t_step in _chapter.EditorSteps)
        {
            if (t_step == null) continue;
            if (t_step.FreeOfCharge && t_step.Action != EOutgameTutorialAction.WaitEnhance)
                _errors.Add($"Growth step {t_step.StepId} grants free support outside the enhancement.");
            if (t_step.Action == EOutgameTutorialAction.CardGrant || t_step.Action == EOutgameTutorialAction.CardSetGrant
                || t_step.Action == EOutgameTutorialAction.DeckGrant || t_step.Action == EOutgameTutorialAction.AutoPurchase)
                _errors.Add($"Growth step {t_step.StepId} must not grant additional growth support.");
        }
    }

    static void ValidateBattle(OutgameTutorialChapter _chapter, List<string> _errors)
    {
        int t_open = IndexOf(_chapter, EOutgameTutorialAction.OpenSynergyDeck);
        int t_wait = IndexOf(_chapter, EOutgameTutorialAction.WaitSynergyDeck);
        if (t_open < 0 || t_wait <= t_open) _errors.Add("Battle introduction must open and then guide synergy deck preparation.");
        if (!_chapter.TryGetStep(_chapter.StepCount - 1, out var t_final)
            || t_final.Action != EOutgameTutorialAction.Message || string.IsNullOrWhiteSpace(t_final.GuideMessage))
            _errors.Add("Battle introduction must end with an authored battle invitation.");
    }

    static int IndexOf(OutgameTutorialChapter _chapter, EOutgameTutorialAction _action)
    {
        for (int t_index = 0; t_index < _chapter.StepCount; t_index++)
            if (_chapter.TryGetStep(t_index, out var t_step) && t_step.Action == _action) return t_index;
        return -1;
    }
}
#endif
