using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>온보딩이 기다리지 않는 가이드 알림 요청. 지급과 진행 커서를 변경하지 않는다.</summary>
internal static class GuideMissionNoticeService
{
    internal const string FirstGuide = "guide.01";
    internal const string AdventureGuide = "guide.02";
    static bool s_refreshing;
    static float s_nextRefresh;
    static int s_generation;
    static GuideNoticeSaveData State => DataSaveManager.Data.Tutorial.GuideNotices;

    internal static void Initialize()
    {
        s_generation++;
        s_refreshing = false;
        s_nextRefresh = 0;
        var tutorial = DataSaveManager.Data.Tutorial;
        if (tutorial.GuideNotices == null) tutorial.GuideNotices = new GuideNoticeSaveData();
        if (State.Version == 0)
        {
            // 기존 계정에 과거 달성 팝업을 소급해서 연속 표시하지 않는다.
            bool existingProgress = tutorial.OutgameCompleted || tutorial.ChapterIndex > 0
                || tutorial.ChapterStepIndex > 0 || tutorial.StepId > 1;
            State.Guide1Shown = State.Guide2Shown = existingProgress;
            State.Version = 1;
            DataSaveManager.Save();
        }
    }

    internal static void RequestFromOnboardingDeckSave()
    {
        if (OutgameTutorialProgress.ChapterIndex != 2 ||
            !OutgameTutorialRunner.IsCurrentAction(EOutgameTutorialAction.WaitDeckSave)) return;
        // 부가 알림의 저장 실패가 기존 덱 저장 완료 신호를 삼켜서는 안 된다.
        try { RequestGuidePopup(FirstGuide); }
        catch (Exception error) { Debug.LogException(error); }
    }

    internal static void RequestGuidePopup(string missionId)
    {
        if (State.Version == 0) return;
        if (missionId == FirstGuide && !State.Guide1Shown && !State.Guide1Pending)
            State.Guide1Pending = true;
        else if (missionId == AdventureGuide && !State.Guide2Shown && !State.Guide2Pending)
            State.Guide2Pending = true;
        else return;
        s_nextRefresh = 0;
        DataSaveManager.Save();
        Debug.Log($"[GuideNotice] Requested {missionId}");
    }

    internal static void ObserveAdventure()
    {
        if (State.Version > 0 && !State.Guide2Shown && AdventureProgress.IsCleared("node_01"))
            RequestGuidePopup(AdventureGuide);
    }

    internal static bool HasPending => State.Version > 0 &&
        ((State.Guide1Pending && !State.Guide1Shown) || (State.Guide2Pending && !State.Guide2Shown));

    internal static bool OwnsNotification(string id)
    {
        var state = DataSaveManager.Data?.Tutorial?.GuideNotices;
        return state != null && state.Version > 0 &&
            ((id == FirstGuide && state.Guide1Pending) || (id == AdventureGuide && state.Guide2Pending));
    }

    internal static string ReadyMission()
    {
        if (State.Guide1Pending && !State.Guide1Shown) return Ready(FirstGuide);
        if (State.Guide2Pending && !State.Guide2Shown) return Ready(AdventureGuide);
        return null;
    }

    static string Ready(string id)
    {
        if (MissionManager.IsClaimed(id)) { MarkShown(id); return null; }
        var definition = MissionManager.Find(id);
        return definition != null && MissionManager.CanClaim(definition) ? id : null;
    }

    internal static void MarkShown(string id)
    {
        if (id == FirstGuide) { State.Guide1Shown = true; State.Guide1Pending = false; }
        else if (id == AdventureGuide) { State.Guide2Shown = true; State.Guide2Pending = false; }
        else return;
        DataSaveManager.Save();
        Debug.Log($"[GuideNotice] Finished {id}");
    }

    // UI는 안전한 로비 시점에서만 이 함수를 부른다. 프레임마다 서버를 조회하지 않는다.
    internal static void RefreshIfNeeded()
    {
        if (!HasPending || s_refreshing || Time.unscaledTime < s_nextRefresh) return;
        RefreshAsync(s_generation).Forget();
    }

    static async UniTaskVoid RefreshAsync(int generation)
    {
        s_refreshing = true;
        s_nextRefresh = Time.unscaledTime + 10f;
        try
        {
            await PlayerSaveCloud.FlushAsync();
            if (generation != s_generation || PlayerSaveCloud.HasPendingUpload) return;
            await MissionCommands.RefreshAsync();
        }
        catch (Exception error) { Debug.LogWarning($"[GuideNotice] Refresh deferred: {error.Message}"); }
        finally { if (generation == s_generation) s_refreshing = false; }
    }
}
