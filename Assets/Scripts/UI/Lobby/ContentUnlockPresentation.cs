using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>튜토리얼 스텝이 요청한 해금 소개와 대표 버튼 연출을 재생한다.</summary>
public sealed class ContentUnlockPresentation : MonoBehaviour
{
    static ContentUnlockPresentation s_instance;
    FeatureLockView[] m_views;
    FeatureLockView _rankedButton;
    FeatureLockView _guideMissionButton;
    FeatureLockView _attendanceButton;
    ContentUnlockIntroView m_intro;
    List<ContentUnlockIntroDef> m_intros;
    int m_introIndex;
    Action m_confirmed;
    Action m_cancelled;
    bool m_visible;
    bool m_playing;
    bool m_pendingIntro;
    int m_sessionVersion;
    readonly List<PendingUnlock> _arrivals = new List<PendingUnlock>();
    readonly List<FeatureLockView> _heldViews = new List<FeatureLockView>();

    public static bool IsPlaying => s_instance != null && s_instance.m_playing;
    public static bool IsReady => s_instance != null && s_instance.m_visible && s_instance.isActiveAndEnabled;

    /// <summary>콘텐츠별 대표 버튼을 연결한다. 버튼이 없어도 소개 화면은 표시한다.</summary>
    public void Bind(FeatureLockView _mission, FeatureLockView _adventure, FeatureLockView _roulette,
        FeatureLockView rankedButton, FeatureLockView guideMissionButton, FeatureLockView attendanceButton)
    {
        s_instance = this;
        m_views = new[] { _mission, _adventure, _roulette };
        _rankedButton = rankedButton;
        _guideMissionButton = guideMissionButton;
        _attendanceButton = attendanceButton;
    }

    public void SetVisible(bool _visible)
    {
        m_visible = _visible;
        if (!_visible) Cancel();
    }

    /// <summary>현재 스텝의 소개를 시작한다. 무대 준비 전에는 소비하지 않고 기다린다.</summary>
    public static bool TryPresent(TutorialStepDef _step,
        Action _onConfirmed, Action _onCancelled)
        => TryPresent(_step?.ContentIntros, _onConfirmed, _onCancelled);

    /// <summary>가이드 흐름이 요청한 해금 소개를 재생한다.</summary>
    public static bool TryPresent(IReadOnlyList<EContentUnlockIntro> _contents,
        Action _onConfirmed, Action _onCancelled)
    {
        if (!IsReady || IsPlaying || !GuidanceCoordinator.CanRunContentIntro) return false;
        if (ContentUnlockConfig.Data == null || _contents == null || _contents.Count == 0) return false;
        var t_intros = new List<ContentUnlockIntroDef>();
        foreach (EContentUnlockIntro t_content in _contents)
        {
            if (!ContentUnlockConfig.Data.TryGetContentIntro(t_content, out var t_intro)
                || !t_intro.TryValidate(out _)) return false;
            string t_key = ContentUnlockIntroDef.KeyOf(t_content);
            if (t_key != null && !ContentUnlockManager.IsUnlocked(t_key)) return false;
            t_intros.Add(t_intro);
        }
        if (!ContentUnlockIntroView.TryGet(out var t_view)) return false;
        var t_owner = s_instance;
        t_owner.m_intro = t_view;
        t_owner.m_intros = t_intros;
        t_owner.m_introIndex = 0;
        t_owner.m_confirmed = _onConfirmed;
        t_owner.m_cancelled = _onCancelled;
        t_owner.m_sessionVersion = ContentUnlockManager.SessionVersion;
        t_owner.m_playing = true;
        foreach (var intro in t_intros)
            foreach (var item in intro.items)
                t_owner.HoldButton(t_owner.FindButton(item.destination));
        t_owner.ShowCurrent();
        return true;
    }

    void ShowCurrent()
    {
        m_pendingIntro = false;
        ContentUnlockIntroDef t_intro = m_intros[m_introIndex];
        int count = t_intro.items.Length;
        var names = new string[count];
        var icons = new Sprite[count];
        var buttons = new FeatureLockView[count];
        var destinations = new RectTransform[count];
        for (int i = 0; i < count; i++)
        {
            var item = t_intro.items[i];
            names[i] = item.name;
            icons[i] = item.icon;
            buttons[i] = FindButton(item.destination);
            destinations[i] = buttons[i] != null ? buttons[i].UnlockTarget : null;
        }
        m_intro.ShowTogether(string.Join(" / ", names), t_intro.description, icons,
            count > 1 ? names : null, destinations,
            Finish, Cancel, (index, done) => PlayButton(buttons[index], done));
    }

    public static void CancelCurrent()
    {
        if (s_instance != null) s_instance.Cancel();
    }

    void Update()
    {
        if (m_playing && (m_sessionVersion != ContentUnlockManager.SessionVersion
            || !m_visible || !GuidanceCoordinator.CanRunContentIntro)) Cancel();
        if (m_playing && m_pendingIntro) ShowCurrent();
        for (int i = _arrivals.Count - 1; i >= 0; i--)
        {
            var arrival = _arrivals[i];
            if (arrival.View == null || !arrival.View.isActiveAndEnabled || !arrival.View.IsPresenting)
                CompleteArrival(arrival);
        }
    }

    FeatureLockView FindButton(EContentUnlockDestination destination)
    {
        if (destination == EContentUnlockDestination.CardEnhance)
        {
            var tabBar = GetComponentInParent<LobbyTabController>()?.GetComponentInChildren<LobbyTabBarView>(true);
            if (tabBar == null) return null;
            for (int i = 0; i < tabBar.Count; i++)
            {
                var view = tabBar.GetFeatureLock(i);
                if (view != null && view.isActiveAndEnabled && view.Feature == EOutgameFeature.CardEnhance)
                    return view;
            }
            return null;
        }
        if (destination == EContentUnlockDestination.Collection)
        {
            if (!TutorialAnchorRegistry.TryGet(EOutgameTutorialAnchor.LobbyCollectionTab,
                out _, out var collectionButton) || collectionButton == null) return null;
            var collectionView = collectionButton.GetComponent<FeatureLockView>();
            return collectionView != null && collectionView.isActiveAndEnabled ? collectionView : null;
        }
        if (destination == EContentUnlockDestination.Ranked)
            return _rankedButton != null && _rankedButton.isActiveAndEnabled ? _rankedButton : null;
        if (destination == EContentUnlockDestination.GuideMission) return _guideMissionButton;
        if (destination == EContentUnlockDestination.Attendance) return _attendanceButton;
        string _key = destination switch
        {
            EContentUnlockDestination.DailyMission => ContentUnlockManager.MISSION,
            EContentUnlockDestination.Adventure => ContentUnlockManager.ADVENTURE,
            EContentUnlockDestination.Roulette => ContentUnlockManager.ROULETTE,
            _ => null,
        };
        if (_key == null || m_views == null) return null;
        foreach (FeatureLockView t_view in m_views)
            if (t_view != null && t_view.isActiveAndEnabled && !t_view.IsLocked
                && ContentUnlockManager.TryGetKey(t_view.Feature, out string t_key) && t_key == _key)
                return t_view;
        return null;
    }

    void PlayButton(FeatureLockView button, Action done)
    {
        var arrival = new PendingUnlock { View = button, Complete = done };
        _arrivals.Add(arrival);
        if (button == null || !button.PresentUnlock(() => CompleteArrival(arrival))) CompleteArrival(arrival);
    }

    void CompleteArrival(PendingUnlock arrival)
    {
        if (!_arrivals.Remove(arrival)) return;
        arrival.Complete?.Invoke();
    }

    void HoldButton(FeatureLockView button)
    {
        if (button == null || _heldViews.Contains(button)) return;
        _heldViews.Add(button);
        button.HoldUnlockPresentation();
    }

    void Finish()
    {
        if (!m_playing) return;
        if (m_sessionVersion != ContentUnlockManager.SessionVersion || !m_visible)
        {
            Cancel();
            return;
        }
        m_introIndex++;
        if (m_introIndex < m_intros.Count)
        {
            // 이전 패널의 OnDisable/퇴장 정리가 끝난 다음 프레임에 다시 연다.
            m_pendingIntro = true;
            return;
        }
        Action t_callback = m_confirmed;
        Clear();
        t_callback?.Invoke();
    }

    void Cancel()
    {
        if (!m_playing) return;
        Action t_callback = m_cancelled;
        ContentUnlockIntroView t_view = m_intro;
        Clear();
        if (t_view != null) t_view.Close();
        t_callback?.Invoke();
    }

    void Clear()
    {
        _arrivals.Clear();
        m_playing = false;
        m_pendingIntro = false;
        m_intro = null;
        m_intros = null;
        m_introIndex = 0;
        m_confirmed = null;
        m_cancelled = null;
        foreach (FeatureLockView view in _heldViews)
            if (view != null) view.CancelPresentation();
        _heldViews.Clear();
    }

    void OnDisable()
    {
        m_visible = false;
        Cancel();
    }

    void OnDestroy()
    {
        Cancel();
        if (s_instance == this) s_instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => s_instance = null;

    sealed class PendingUnlock
    {
        public FeatureLockView View;
        public Action Complete;
    }
}
