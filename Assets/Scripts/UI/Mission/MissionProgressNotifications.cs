using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>서버에서 채택한 진행도 차이를 보관한다. 씬이 바뀌어도 로비가 소비할 때까지 남는다.</summary>
internal static class MissionProgressNotifications
{
    static Dictionary<string, MissionProgressNotification> s_observed = new Dictionary<string, MissionProgressNotification>();
    static readonly List<MissionProgressNotification> s_pending = new List<MissionProgressNotification>();
    static string s_userId;
    static bool s_installed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    internal static void Install()
    {
        // 계정 재시작은 도메인 리로드 없이 이벤트 구독을 비우기도 한다.
        MissionManager.OnChanged -= HandleChanged;
        MissionManager.OnChanged += HandleChanged;
        FirebaseAuthService.Instance.OnStateChanged -= SynchronizeAccount;
        FirebaseAuthService.Instance.OnStateChanged += SynchronizeAccount;
        SynchronizeAccount();
        if (s_installed) return;
        s_installed = true;
        HandleChanged();
    }

    internal static bool TryTake(out MissionProgressNotification _notification)
    {
        SynchronizeAccount();
        s_pending.RemoveAll(t_item => !IsCurrent(t_item));
        if (s_pending.Count == 0)
        {
            _notification = default;
            return false;
        }

        // 달성 알림을 먼저 보여주되 같은 종류끼리는 도착 순서를 유지한다.
        int t_index = s_pending.FindIndex(t_item => t_item.IsComplete);
        if (t_index < 0) t_index = 0;
        _notification = s_pending[t_index];
        s_pending.RemoveAt(t_index);
        return true;
    }

    internal static bool IsCurrent(MissionProgressNotification _notification)
    {
        if (!MissionManager.IsReady || string.IsNullOrEmpty(_notification.MissionId) ||
            _notification.UserId != FirebaseAuthService.Instance.UserId) return false;
        MissionDefinition t_definition = MissionManager.Find(_notification.MissionId);
        return t_definition != null && _notification.Matches(t_definition, PeriodKey(t_definition.Period)) &&
               MissionManager.IsGuideUnlocked(t_definition);
    }

    static void HandleChanged()
    {
        SynchronizeAccount();
        if (!MissionManager.IsReady) return;

        var t_next = new Dictionary<string, MissionProgressNotification>();
        foreach (MissionDefinition t_definition in MissionManager.Definitions)
        {
            if (t_definition.Target <= 0L) continue;
            long t_progress = Math.Min(MissionManager.ProgressOf(t_definition), t_definition.Target);
            string t_periodKey = PeriodKey(t_definition.Period);
            var t_current = new MissionProgressNotification(t_definition, s_userId, t_periodKey, t_progress, t_progress);

            // 첫 조회·새 정의·기간 변경은 기준선이다. 로그인하자마자 과거 달성을 쏟아내지 않는다.
            if (s_observed.TryGetValue(t_definition.Id, out MissionProgressNotification t_previous) &&
                t_previous.Matches(t_definition, t_periodKey))
            {
                long t_before = t_previous.Progress;
                t_current = new MissionProgressNotification(t_definition, s_userId, t_periodKey,
                    t_before, Math.Max(t_before, t_progress));
                if (t_progress > t_before && MissionManager.IsGuideUnlocked(t_definition) &&
                    !MissionManager.IsClaimed(t_definition.Id))
                    Enqueue(t_current);
            }

            // 완료 이후 카운터는 목표에서 멈춘다. 오래된 응답이 도착해도 같은 달성을 다시 만들지 않는다.
            // 잠긴 가이드 역시 관측해 두므로 해금 자체가 진행 알림으로 바뀌지 않는다.
            t_next[t_definition.Id] = t_current;
        }
        s_observed = t_next;
        s_pending.RemoveAll(t_item => !IsCurrent(t_item));
    }

    static void Enqueue(MissionProgressNotification _notification)
    {
        for (int i = 0; i < s_pending.Count; i++)
        {
            MissionProgressNotification t_pending = s_pending[i];
            if (t_pending.MissionId != _notification.MissionId) continue;
            if (t_pending.PeriodKey == _notification.PeriodKey && t_pending.Event == _notification.Event &&
                t_pending.Target == _notification.Target)
                _notification = _notification.WithPreviousProgress(t_pending.PreviousProgress);
            s_pending[i] = _notification;
            return;
        }
        s_pending.Add(_notification);
    }

    static string PeriodKey(string _period)
    {
        switch (_period)
        {
            case "daily": return MissionManager.DailyKey;
            case "weekly": return MissionManager.WeeklyKey;
            default: return string.Empty;
        }
    }

    static void SynchronizeAccount()
    {
        string t_userId = FirebaseAuthService.Instance.UserId;
        if (s_userId == t_userId) return;
        s_userId = t_userId;
        s_observed.Clear();
        s_pending.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        MissionManager.OnChanged -= HandleChanged;
        FirebaseAuthService.Instance.OnStateChanged -= SynchronizeAccount;
        s_observed.Clear();
        s_pending.Clear();
        s_userId = null;
        s_installed = false;
    }
}

internal readonly struct MissionProgressNotification
{
    internal string MissionId { get; }
    internal string Title { get; }
    internal string Period { get; }
    internal long PreviousProgress { get; }
    internal long Progress { get; }
    internal long Target { get; }
    internal bool IsComplete => Progress >= Target;
    internal string UserId { get; }
    internal string PeriodKey { get; }
    internal string Event { get; }

    internal MissionProgressNotification(MissionDefinition _definition, string _userId, string _periodKey,
        long _previousProgress, long _progress)
    {
        MissionId = _definition.Id;
        Title = _definition.Title;
        Period = _definition.Period;
        Event = _definition.Event;
        Target = _definition.Target;
        UserId = _userId;
        PeriodKey = _periodKey;
        PreviousProgress = _previousProgress;
        Progress = _progress;
    }

    MissionProgressNotification(MissionProgressNotification _source, long _previousProgress)
    {
        MissionId = _source.MissionId;
        Title = _source.Title;
        Period = _source.Period;
        Event = _source.Event;
        Target = _source.Target;
        UserId = _source.UserId;
        PeriodKey = _source.PeriodKey;
        PreviousProgress = _previousProgress;
        Progress = _source.Progress;
    }

    internal bool Matches(MissionDefinition _definition, string _periodKey)
        => Period == _definition.Period && Event == _definition.Event && Target == _definition.Target &&
           PeriodKey == _periodKey;

    internal MissionProgressNotification WithPreviousProgress(long _previousProgress)
        => new MissionProgressNotification(this, _previousProgress);
}
