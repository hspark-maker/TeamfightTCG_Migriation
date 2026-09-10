using System;
using System.Collections.Generic;
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
    static readonly HashSet<string> s_pending = new HashSet<string>();
    static readonly HashSet<string> s_shown = new HashSet<string>();
    static GuideNoticeSaveData State => DataSaveManager.Data.Tutorial.GuideNotices;

    internal static void Initialize()
    {
        s_generation++;
        s_refreshing = false;
        s_nextRefresh = 0;
        s_pending.Clear();
        s_shown.Clear();
        var tutorial = DataSaveManager.Data.Tutorial;
        if (tutorial.GuideNotices == null) tutorial.GuideNotices = new GuideNoticeSaveData();
        if (State.PendingMissionIds == null) State.PendingMissionIds = new List<string>();
        if (State.ShownMissionIds == null) State.ShownMissionIds = new List<string>();
        foreach (string id in State.PendingMissionIds)
            if (!string.IsNullOrEmpty(id)) s_pending.Add(id);
        foreach (string id in State.ShownMissionIds)
            if (!string.IsNullOrEmpty(id)) s_shown.Add(id);
        s_pending.ExceptWith(s_shown);
        if (State.Version == 0)
        {
            // 기존 계정에 과거 달성 팝업을 소급해서 연속 표시하지 않는다.
            bool existingProgress = tutorial.OutgameCompleted || tutorial.ChapterIndex > 0
                || tutorial.ChapterStepIndex > 0 || tutorial.StepId > 1;
            State.Guide1Shown = State.Guide2Shown = existingProgress;
            State.Version = 1;
            DataSaveManager.Save();
        }
        if (State.Guide1Shown) s_shown.Add(FirstGuide);
        else if (State.Guide1Pending) s_pending.Add(FirstGuide);
        if (State.Guide2Shown) s_shown.Add(AdventureGuide);
        else if (State.Guide2Pending) s_pending.Add(AdventureGuide);
        s_pending.ExceptWith(s_shown);
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
        if (State.Version == 0 || string.IsNullOrEmpty(missionId) || s_shown.Contains(missionId)) return;
        // 서버 카탈로그의 가이드만 저장한다. 1·2번은 기존 온보딩의 조회 전 요청을 유지한다.
        if (missionId != FirstGuide && missionId != AdventureGuide && MissionManager.Find(missionId)?.Period != "guide") return;
        if ((missionId == FirstGuide && State.Guide1Shown) ||
            (missionId == AdventureGuide && State.Guide2Shown)) return;
        bool added = s_pending.Add(missionId);
        if (missionId == FirstGuide && !State.Guide1Shown && !State.Guide1Pending)
            State.Guide1Pending = true;
        else if (missionId == AdventureGuide && !State.Guide2Shown && !State.Guide2Pending)
            State.Guide2Pending = true;
        else if (!added) return;
        s_nextRefresh = 0;
        SaveRequests();
        Debug.Log($"[GuideNotice] Requested {missionId}");
    }

    internal static void ObserveAdventure()
    {
        if (State.Version > 0 && !State.Guide2Shown && AdventureProgress.IsCleared("node_01"))
            RequestGuidePopup(AdventureGuide);
    }

    internal static bool HasPending => State.Version > 0 &&
        (s_pending.Count > 0 || (State.Guide1Pending && !State.Guide1Shown) || (State.Guide2Pending && !State.Guide2Shown));

    internal static bool OwnsNotification(string id)
    {
        var state = DataSaveManager.Data?.Tutorial?.GuideNotices;
        return state != null && state.Version > 0 &&
            (s_pending.Contains(id) || s_shown.Contains(id) ||
             (id == FirstGuide && state.Guide1Pending) || (id == AdventureGuide && state.Guide2Pending));
    }

    internal static string ReadyMission()
    {
        if (!MissionManager.IsReady || MissionManager.Definitions.Count == 0) return null;
        // 삭제된 정의가 영구 조회 요청이나 무한히 커지는 표시 이력으로 남지 않게 한다.
        int removed = s_pending.RemoveWhere(id => MissionManager.Find(id)?.Period != "guide");
        removed += s_shown.RemoveWhere(id => MissionManager.Find(id)?.Period != "guide");
        if (removed > 0) SaveRequests();
        if (State.Guide1Pending && !State.Guide1Shown && MissionManager.Find(FirstGuide)?.Period == "guide") s_pending.Add(FirstGuide);
        if (State.Guide2Pending && !State.Guide2Shown && MissionManager.Find(AdventureGuide)?.Period == "guide") s_pending.Add(AdventureGuide);
        foreach (var definition in MissionManager.Definitions)
        {
            if (definition.Period != "guide" || !s_pending.Contains(definition.Id)) continue;
            string ready = Ready(definition.Id);
            if (ready != null) return ready;
        }
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
        s_pending.Remove(id);
        s_shown.Add(id);
        if (id == FirstGuide) { State.Guide1Shown = true; State.Guide1Pending = false; }
        else if (id == AdventureGuide) { State.Guide2Shown = true; State.Guide2Pending = false; }
        SaveRequests();
        Debug.Log($"[GuideNotice] Finished {id}");
    }

    static void SaveRequests()
    {
        State.PendingMissionIds = new List<string>(s_pending);
        State.ShownMissionIds = new List<string>(s_shown);
        DataSaveManager.Save();
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
