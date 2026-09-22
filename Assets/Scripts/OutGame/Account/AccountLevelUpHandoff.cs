using System;
using System.Collections.Generic;

/// <summary>서버가 확정한 레벨업을 로비 연출에 인계한다.</summary>
internal static class AccountLevelUpHandoff
{
    static readonly HashSet<long> s_revisions = new HashSet<long>();
    static int s_pendingLevel;

    internal static bool HasPending => s_pendingLevel > 0;
    internal static event Action OnReset;

    /// <summary>보상 팝업 소비와 별개로 같은 서버 지급은 한 번만 기록한다.</summary>
    internal static void Enqueue(long _revision, AccountExperienceResult _experience)
    {
        if (_revision <= 0 || _experience == null || !_experience.IsLevelUp) return;
        if (!s_revisions.Add(_revision)) return;
        s_pendingLevel = Math.Max(s_pendingLevel, _experience.Level);
    }

    /// <summary>누적된 레벨업을 최종 레벨 하나로 합쳐 소비한다.</summary>
    internal static int Consume()
    {
        int t_level = s_pendingLevel;
        s_pendingLevel = 0;
        return t_level;
    }

    /// <summary>계정·환경 전환 시 대기와 진행 중 연출을 정리한다.</summary>
    internal static void ResetSession()
    {
        s_pendingLevel = 0;
        s_revisions.Clear();
        OnReset?.Invoke();
    }
}
