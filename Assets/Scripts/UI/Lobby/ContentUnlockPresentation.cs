using UnityEngine;

/// <summary>매치 탭이 보이고 로비 무대가 비었을 때 보관된 콘텐츠 해금을 재생한다.</summary>
public sealed class ContentUnlockPresentation : MonoBehaviour
{
    static ContentUnlockPresentation s_owner;
    FeatureLockView[] m_views;
    FeatureLockView m_playingView;
    string m_playingKey;
    int m_sessionVersion;
    bool m_visible;

    /// <summary>후속 안내와 컷인이 기다려야 하는 해금 연출 상태.</summary>
    public static bool IsPlaying => s_owner != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => s_owner = null;

    /// <summary>콘텐츠별 대표 버튼을 명시적으로 연결한다.</summary>
    public void Bind(FeatureLockView _mission, FeatureLockView _adventure, FeatureLockView _roulette)
        => m_views = new[] { _mission, _adventure, _roulette };

    /// <summary>탭 전환이 끝난 뒤만 재생을 허용한다.</summary>
    public void SetVisible(bool _visible)
    {
        m_visible = _visible;
        if (!_visible) Cancel();
    }

    void Update()
    {
        if (m_playingView != null)
        {
            if (m_sessionVersion != ContentUnlockManager.SessionVersion
                || !m_visible || !m_playingView.isActiveAndEnabled || !m_playingView.IsPresenting || m_playingView.IsLocked
                || !ContentUnlockManager.IsPending(m_playingKey)
                || !GuidanceCoordinator.CanPresentContentUnlock)
                Cancel();
            return;
        }

        if (!m_visible || IsPlaying || m_views == null || !GuidanceCoordinator.CanPresentContentUnlock) return;
        var t_pending = ContentUnlockManager.PendingKeys;
        for (int i = 0; i < t_pending.Count; i++)
        {
            string t_key = t_pending[i];
            for (int j = 0; j < m_views.Length; j++)
            {
                FeatureLockView t_view = m_views[j];
                if (t_view == null || !t_view.isActiveAndEnabled || t_view.IsLocked
                    || !ContentUnlockManager.TryGetKey(t_view.Feature, out string t_viewKey)
                    || t_key != t_viewKey) continue;
                s_owner = this;
                m_playingView = t_view;
                m_playingKey = t_key;
                m_sessionVersion = ContentUnlockManager.SessionVersion;
                if (!t_view.PresentUnlock(Finish)) Cancel();
                return;
            }
        }
    }

    void Finish()
    {
        string t_key = m_playingKey;
        bool t_completed = m_sessionVersion == ContentUnlockManager.SessionVersion
            && m_visible && m_playingView != null && m_playingView.isActiveAndEnabled
            && !m_playingView.IsLocked && GuidanceCoordinator.CanPresentContentUnlock;
        m_playingView = null;
        m_playingKey = null;
        if (s_owner == this) s_owner = null;
        if (t_completed && ContentUnlockManager.IsPending(t_key)) ContentUnlockManager.MarkPresented(t_key);
    }

    void Cancel()
    {
        FeatureLockView t_view = m_playingView;
        m_playingView = null;
        m_playingKey = null;
        if (s_owner == this) s_owner = null;
        if (t_view != null) t_view.CancelPresentation();
    }

    void OnDisable()
    {
        m_visible = false;
        Cancel();
    }
}
