using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>로비에서 출석을 조회하고, 다른 연출이 끝난 뒤 하루 한 번만 자동 안내한다.</summary>
public sealed class AttendanceLobbyController : MonoBehaviour
{
    double m_nextRefresh, m_nextOpen;
    bool m_opening;
    bool m_refreshRequested = true;

    public static void Install(GameObject _owner)
    {
        if (_owner.GetComponent<AttendanceLobbyController>() == null)
            _owner.AddComponent<AttendanceLobbyController>();
    }

    void OnApplicationFocus(bool _focused)
    {
        if (!_focused) return;
        m_refreshRequested = true;
        m_nextRefresh = 0;
    }

    void Update()
    {
        if (!GameInitialization.IsReady || !PlayerSaveCloud.IsGateComplete) return;
        double t_now = Time.realtimeSinceStartupAsDouble;
        if (t_now >= m_nextRefresh && (m_refreshRequested || AttendanceCommands.NeedsRefresh)
            && !AttendanceCommands.IsClaiming && !AttendanceCommands.IsReading)
        {
            m_refreshRequested = false;
            m_nextRefresh = t_now + 30;
            AttendanceCommands.RefreshAsync(true).Forget();
        }
        if (m_opening || t_now < m_nextOpen || !CanOpenAutomatically()) return;
        m_nextOpen = t_now + 30;
        OpenAutomatically().Forget();
    }

    static bool CanOpenAutomatically() => AttendanceCommands.IsReady && AttendanceCommands.CanClaim
        && AttendanceCommands.PromptedDay != AttendanceCommands.State.DailyKey
        && GuidanceCoordinator.CanPresent && GuidanceCoordinator.CanNavigateFromLobby(null);

    async UniTaskVoid OpenAutomatically()
    {
        m_opening = true;
        try
        {
            await UiPrefabCache.LoadAsync<AttendancePanel>();
            if (this != null && isActiveAndEnabled && CanOpenAutomatically())
                UIPoolManager.Instance?.AddOrUpdateUI<AttendancePanel>();
        }
        catch (Exception t_error) { Debug.LogWarning($"[Attendance] Popup load failed: {t_error.Message}"); }
        finally { m_opening = false; }
    }
}
