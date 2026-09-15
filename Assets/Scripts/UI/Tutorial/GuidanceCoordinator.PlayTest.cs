#if UNITY_EDITOR
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

public sealed partial class GuidanceCoordinator
{
    /// <summary>테스트가 기존 안내나 서버 왕복을 끊지 않도록 빈 로비에서만 시작한다.</summary>
    public static bool CanStartOnboardingPlayTest(out string _reason)
    {
        _reason = null;
        if (!Application.isPlaying || !GameInitialization.IsReady || s_instance == null)
            _reason = "Unity Play 실행 후 로비 초기화를 기다리세요.";
        else if (OnboardingPlayTest.IsActive || OnboardingPlayTest.IsPreparing)
            _reason = "테스트 세션입니다. 다른 조건은 Play를 중지한 뒤 다시 실행하세요.";
        else if (OutgameTutorialRunner.IsRunning)
            _reason = "첫 시작 튜토리얼을 완료한 계정으로 로비에 진입하세요.";
        else if (s_instance.m_shell == null || !s_instance.m_shell.CanSwipe
            || s_instance.AdventureMapOpen || StageBusyForGuided || !s_instance.SafeToPresent())
            _reason = "진행 중인 안내·연출을 마치고 팝업을 닫아 로비로 돌아오세요.";
        return _reason == null;
    }

    /// <summary>미션 배포 여부와 완료 기록에 관계없이 실제 온보딩 UI를 재생한다.</summary>
    public static async UniTask StartOnboardingPlayTestAsync(EOutgameTutorialTrigger _trigger, bool _alreadyGrown = false)
    {
        if (!CanStartOnboardingPlayTest(out string t_reason)) throw new InvalidOperationException(t_reason);
        if ((_trigger != EOutgameTutorialTrigger.SynergyGrowthIntroduction
                && _trigger != EOutgameTutorialTrigger.SynergyBattleIntroduction)
            || !OutgameTutorialRunner.TryGetGuidedChapter(_trigger, out _, out var t_chapter)
            || t_chapter.StepCount == 0)
            throw new InvalidOperationException("로비에 테스트할 온보딩 데이터가 없습니다.");

        var t_owner = s_instance;
        await OnboardingPlayTest.BeginAsync();
        if (t_owner == null || t_owner != s_instance || !t_owner.isActiveAndEnabled)
            throw new InvalidOperationException("로비가 변경됐습니다. Play를 중지하고 다시 시작하세요.");
        t_owner.CancelMatchMission();
        t_owner.CancelMissionFlow(false);
        OutgameFeatureLock.ForceUnlockAllForDebug = true;
        if (_trigger == EOutgameTutorialTrigger.SynergyBattleIntroduction) OnboardingPlayTest.PrepareSynergy();
        else OnboardingPlayTest.PrepareGrowth(_alreadyGrown);

        bool t_arrived = false;
        var t_destination = _trigger == EOutgameTutorialTrigger.SynergyBattleIntroduction
            ? EOutgameFeature.LobbyDeckTab : EOutgameFeature.LobbyMatchTab;
        if (!t_owner.m_shell.TrySelectFeature(t_destination, _onArrived: () => t_arrived = true))
            throw new InvalidOperationException("온보딩 시작 탭을 열 수 없습니다. Play를 중지하세요.");
        int t_selection = t_owner.m_shell.SelectionRequestVersion;
        float t_deadline = Time.realtimeSinceStartup + 10f;
        while (!t_arrived)
        {
            await UniTask.Yield(t_owner.GetCancellationTokenOnDestroy());
            if (Time.realtimeSinceStartup >= t_deadline || t_owner.m_shell.SelectionRequestVersion != t_selection)
                throw new InvalidOperationException("온보딩 시작 탭 이동이 중단됐습니다. Play를 중지하세요.");
        }
        if (_trigger == EOutgameTutorialTrigger.SynergyGrowthIntroduction)
        {
            OutgameTutorialGuide.PrepareSynergyGrowth();
            if (!_alreadyGrown && !OutgameTutorialGuide.CanContinueEnhance())
                throw new InvalidOperationException("열린 도감에 강화 가능한 보유 카드가 없습니다. Play를 중지하고 계정 구성을 확인하세요.");
        }
        OutgameTutorialRunner.FirePlayTest(_trigger);
    }
}
#endif
