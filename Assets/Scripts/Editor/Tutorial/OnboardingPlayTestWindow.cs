#if UNITY_EDITOR
using System;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>서버 미션 배포 없이 2막 온보딩을 조작하는 에디터 진입점.</summary>
public sealed class OnboardingPlayTestWindow : EditorWindow
{
    bool m_starting;
    string m_error;

    [MenuItem("Tools/Tutorial/Play Test Act 2 Onboarding")]
    public static void Open() => GetWindow<OnboardingPlayTestWindow>("2막 온보딩 테스트");

    void OnInspectorUpdate() => Repaint();

    void OnGUI()
    {
        EditorGUILayout.LabelField("2막 온보딩 로컬 플레이", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Play 실행 → 첫 시작 튜토리얼을 완료한 계정의 로비 → 아래 테스트 선택.\n"
            + "복제 세이브와 가상 샤드로 실제 안내·강화·덱 편집을 재생합니다.\n"
            + "성장 테스트는 보유 카드의 성장 상태를 재설정합니다. 시너지 테스트는 전체 카드·빈 덱을 제공합니다.\n"
            + "서버 저장·명령은 차단됩니다. 테스트 종료는 Play 중지입니다.\n"
            + "초기 로그인은 온라인 연결이 필요하며, 실제 전투·미션 보상 검증은 포함하지 않습니다.", MessageType.Info);
        bool t_ready = GuidanceCoordinator.CanStartOnboardingPlayTest(out string t_reason);
        if (!t_ready) EditorGUILayout.HelpBox(t_reason, MessageType.None);
        using (new EditorGUI.DisabledScope(!t_ready || m_starting))
        {
            if (GUILayout.Button("성장 도입 — 직접 강화·해금 연출"))
                StartTestAsync(EOutgameTutorialTrigger.SynergyGrowthIntroduction, false).Forget();
            if (GUILayout.Button("성장 도입 — 이미 2스타 달성"))
                StartTestAsync(EOutgameTutorialTrigger.SynergyGrowthIntroduction, true).Forget();
            if (GUILayout.Button("시너지 실전 도입 — 덱 편집·저장"))
                StartTestAsync(EOutgameTutorialTrigger.SynergyBattleIntroduction, false).Forget();
        }
        if (OnboardingPlayTest.IsActive)
        {
            EditorGUILayout.LabelField("현재 안내", OutgameTutorialRunner.IsGuidedRunning
                ? OutgameTutorialRunner.GuidedTrigger.ToString() : "안내 종료 (격리 유지)");
            if (OutgameTutorialRunner.TryGetGuidedStep(out var t_step))
                EditorGUILayout.LabelField("현재 스텝", $"#{t_step.StepId} {t_step.Action}");
        }
        if (!string.IsNullOrEmpty(m_error)) EditorGUILayout.HelpBox(m_error, MessageType.Error);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
            if (GUILayout.Button("테스트 종료 — Play 중지")) EditorApplication.isPlaying = false;
    }

    async UniTask StartTestAsync(EOutgameTutorialTrigger _trigger, bool _alreadyGrown)
    {
        m_starting = true;
        m_error = null;
        try { await GuidanceCoordinator.StartOnboardingPlayTestAsync(_trigger, _alreadyGrown); }
        catch (Exception t_exception) { m_error = t_exception.GetBaseException().Message; }
        finally { m_starting = false; }
    }
}
#endif
