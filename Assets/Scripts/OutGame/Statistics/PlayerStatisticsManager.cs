using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>프로필과 업적이 함께 읽는 서버 통계. 로컬에서는 누적하거나 과거 패배를 추정하지 않는다.</summary>
internal static class PlayerStatisticsManager
{
    internal static event Action OnChanged;
    internal static PlayerStatisticsSnapshot Snapshot { get; private set; }
    internal static bool IsReady => Snapshot != null;
    internal static long StateVersion { get; private set; }

    internal static bool Adopt(PlayerStatisticsSnapshot _snapshot)
    {
        if (_snapshot?.Lifetime == null || _snapshot.Battle?.All == null ||
            _snapshot.Battle.Ranked == null || _snapshot.Battle.Adventure == null ||
            _snapshot.TrackedSinceMs <= 0 || _snapshot.Revision < 0) return false;
        if (Snapshot != null)
        {
            if (_snapshot.Revision < Snapshot.Revision) return false;
            if (_snapshot.Revision == Snapshot.Revision) return true;
        }
        _snapshot.Lifetime.SynergyPlays ??= new Dictionary<string, long>();
        Snapshot = _snapshot;
        unchecked { StateVersion++; }
        NotifyChanged();
        return true;
    }

    internal static long AchievementProgress(string _event, string _synergyId)
    {
        var t_lifetime = Snapshot?.Lifetime;
        if (t_lifetime == null) return 0;
        switch (_event)
        {
            case "WinBattle": return t_lifetime.Wins;
            case "DestroyCards": return t_lifetime.CardsDestroyed;
            case "WinStreak": return t_lifetime.BestWinStreak;
            case "OpenPack": return t_lifetime.PacksOpened;
            case "CompleteAlbum": return t_lifetime.AlbumsCompleted;
            case "PlaySynergy":
                return !string.IsNullOrEmpty(_synergyId) &&
                    t_lifetime.SynergyPlays.TryGetValue(_synergyId, out long t_count) ? t_count : 0;
            default: return 0;
        }
    }

    internal static void ResetSession()
    {
        Snapshot = null;
        unchecked { StateVersion++; }
        NotifyChanged();
    }

    static void NotifyChanged()
    {
        try { OnChanged?.Invoke(); }
        catch (Exception t_error) { Debug.LogException(t_error); }
        AchievementManager.NotifyStatisticsChanged();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        OnChanged = null;
        ResetSession();
    }
}
