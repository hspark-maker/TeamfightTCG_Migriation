using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>콘텐츠 해금과 연출 대기 이력의 단일 창구.</summary>
public static class ContentUnlockManager
{
    public const string MISSION = "Mission";
    public const string ADVENTURE = "Adventure";
    public const string ROULETTE = "Roulette";

    static readonly HashSet<string> s_presented = new HashSet<string>(StringComparer.Ordinal);
    static Func<bool> s_canPersist;
    static bool s_initialized;
    static bool s_refreshRequested;
    static bool s_evaluating;
    static bool s_resetRequested;
    public static int SessionVersion { get; private set; }

    public static event Action OnChanged;
    public static IReadOnlyList<string> PendingKeys
    {
        get
        {
            if (!s_initialized || Slot?.Pending == null) return Array.Empty<string>();
            return Slot.Pending.FindAll(t_key => !s_presented.Contains(t_key)).AsReadOnly();
        }
    }

    static ContentUnlockSaveData Slot => DataSaveManager.Data.Profile?.ContentUnlocks;

    public static bool TryGetKey(EOutgameFeature _feature, out string _key)
    {
        _key = _feature switch
        {
            EOutgameFeature.Mission => MISSION,
            EOutgameFeature.Adventure => ADVENTURE,
            EOutgameFeature.Roulette => ROULETTE,
            _ => null,
        };
        return _key != null;
    }

    public static bool IsUnlocked(string _key)
        => s_initialized && Slot?.Unlocked != null && Slot.Unlocked.Contains(_key);

    public static bool IsPending(string _key)
    {
        IReadOnlyList<string> t_keys = PendingKeys;
        for (int t_i = 0; t_i < t_keys.Count; t_i++) if (t_keys[t_i] == _key) return true;
        return false;
    }

    public static ContentUnlockEvaluation Evaluate(string _key)
    {
        if (!s_initialized || !ContentUnlockConfig.TryGet(_key, out ContentUnlockRule t_rule))
            return new ContentUnlockEvaluation(EContentUnlockRequirement.Data);
        return IsUnlocked(_key) ? new ContentUnlockEvaluation(EContentUnlockRequirement.None) : EvaluateRule(t_rule);
    }

    public static void Initialize(Func<bool> _canPersist)
    {
        ResetSession();
        s_canPersist = _canPersist;
        s_initialized = true;
        RankManager.OnChanged += RequestRefresh;
        AccountLevelManager.OnChanged += RequestRefresh;
        OutgameTutorialRunner.OnGuidedChanged += RequestRefresh;
        DataSaveManager.OnSaved += HandleSaved;
        RequestRefresh();
    }

    public static void RequestRefresh()
    {
        if (!s_initialized || s_evaluating) return;
        s_refreshRequested = true;
        FlushPending();
    }

    /// <summary>서버 채택 콜백에서는 예약만 한다.</summary>
    public static void NotifyRehydrated() => s_refreshRequested = s_initialized;

    /// <summary>서버 응답의 업로드 기준선이 확정된 뒤 대기 변경을 반영한다.</summary>
    public static void FlushPending()
    {
        if (!s_initialized || !s_refreshRequested || s_evaluating || !ContentUnlockConfig.IsReady
            || s_canPersist == null || !s_canPersist()) return;
        s_evaluating = true;
        s_refreshRequested = false;
        try
        {
            if (DataSaveManager.Data.Profile == null) DataSaveManager.Data.Profile = new ProfileSaveData();
            bool t_reset = s_resetRequested;
            if (t_reset)
            {
                DataSaveManager.Data.Profile.ContentUnlocks = new ContentUnlockSaveData { Version = 1 };
                if (DataSaveManager.Data.Tutorial != null) DataSaveManager.Data.Tutorial.AdventureUnlocked = false;
                s_resetRequested = false;
            }
            if (Slot == null) DataSaveManager.Data.Profile.ContentUnlocks = new ContentUnlockSaveData();
            ContentUnlockSaveData t_slot = Slot;
            if (t_slot.Unlocked == null) t_slot.Unlocked = new List<string>();
            if (t_slot.Pending == null) t_slot.Pending = new List<string>();
            bool t_seed = t_slot.Version < 1;
            bool t_changed = t_seed || t_reset;
            foreach (ContentUnlockRule t_rule in ContentUnlockConfig.Rules)
            {
                string t_key = t_rule.ContentKey;
                if (t_slot.Unlocked.Contains(t_key)) continue;
                bool t_legacy = t_seed && ((t_key == MISSION && OutgameTutorialProgress.IsCompleted)
                    || (t_key == ADVENTURE && DataSaveManager.Data.Tutorial?.AdventureUnlocked == true));
                if (!t_legacy && !EvaluateRule(t_rule).IsUnlocked) continue;
                t_slot.Unlocked.Add(t_key);
                if (!t_seed && !t_slot.Pending.Contains(t_key)) t_slot.Pending.Add(t_key);
                t_changed = true;
            }
            t_slot.Version = 1;
            foreach (string t_key in s_presented) t_changed |= t_slot.Pending.Remove(t_key);
            s_presented.Clear();
            if (t_changed) DataSaveManager.Save();
            OnChanged?.Invoke();
            OutgameFeatureLock.NotifyContentChanged();
        }
        finally { s_evaluating = false; }
    }

    public static void MarkPresented(string _key)
    {
        if (!IsPending(_key)) return;
        s_presented.Add(_key);
        RequestRefresh();
    }

    public static void ResetForDebug()
    {
        if (!s_initialized) return;
        s_resetRequested = true;
        s_presented.Clear();
        RequestRefresh();
    }

    public static void ResetSession()
    {
        SessionVersion++;
        RankManager.OnChanged -= RequestRefresh;
        AccountLevelManager.OnChanged -= RequestRefresh;
        OutgameTutorialRunner.OnGuidedChanged -= RequestRefresh;
        DataSaveManager.OnSaved -= HandleSaved;
        s_initialized = false;
        s_refreshRequested = false;
        s_evaluating = false;
        s_resetRequested = false;
        s_presented.Clear();
        s_canPersist = null;
    }

    static ContentUnlockEvaluation EvaluateRule(ContentUnlockRule _rule)
    {
        int t_requiredTier = -1;
        if (_rule.RequireRank && RankManager.IsConfigured)
            for (int t_i = 0; RankManager.TryGetTier(t_i, out RankTier t_tier); t_i++)
                if (t_tier.Grade == _rule.MinRankGrade && t_tier.Division == _rule.MinRankDivision)
                { t_requiredTier = t_tier.Index; break; }
        return ContentUnlockRules.Evaluate(_rule, OutgameTutorialProgress.IsCompleted,
            RankManager.IsConfigured, RankManager.IsRanked, RankManager.BestTierIndex, t_requiredTier,
            AccountLevelManager.IsConfigured, AccountLevelManager.Level);
    }

    static void HandleSaved(ESaveUploadTiming _timing) => RequestRefresh();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        ResetSession();
        OnChanged = null;
    }

}
