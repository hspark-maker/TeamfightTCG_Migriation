using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>튜토리얼 스텝이 요청한 해금 소개와 대표 버튼 연출을 재생한다.</summary>
public sealed class ContentUnlockPresentation : MonoBehaviour
{
    static ContentUnlockPresentation s_instance;
    FeatureLockView[] m_views;
    ContentUnlockIntroView m_intro;
    List<ContentUnlockIntroDef> m_intros;
    int m_introIndex;
    Action m_confirmed;
    Action m_cancelled;
    bool m_visible;
    bool m_playing;
    bool m_pendingIntro;
    int m_sessionVersion;

    public static bool IsPlaying => s_instance != null && s_instance.m_playing;
    public static bool IsReady => s_instance != null && s_instance.m_visible && s_instance.isActiveAndEnabled;

    /// <summary>콘텐츠별 대표 버튼을 연결한다. 버튼이 없어도 소개 화면은 표시한다.</summary>
    public void Bind(FeatureLockView _mission, FeatureLockView _adventure, FeatureLockView _roulette)
    {
        s_instance = this;
        m_views = new[] { _mission, _adventure, _roulette };
    }

    public void SetVisible(bool _visible)
    {
        m_visible = _visible;
        if (!_visible) Cancel();
    }

    /// <summary>현재 스텝의 소개를 시작한다. 무대 준비 전에는 소비하지 않고 기다린다.</summary>
    public static bool TryPresent(OutgameTutorialData _data, TutorialStepDef _step,
        Action _onConfirmed, Action _onCancelled)
    {
        if (!IsReady || IsPlaying || !GuidanceCoordinator.CanRunContentIntro) return false;
        if (_data == null || _step.ContentIntros == null || _step.ContentIntros.Count == 0) return false;
        var t_intros = new List<ContentUnlockIntroDef>();
        foreach (EContentUnlockIntro t_content in _step.ContentIntros)
        {
            if (!_data.TryGetContentIntro(t_content, out var t_intro)
                || string.IsNullOrWhiteSpace(t_intro.contentName) || t_intro.icon == null) return false;
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
        t_owner.ShowCurrent();
        return true;
    }

    void ShowCurrent()
    {
        m_pendingIntro = false;
        ContentUnlockIntroDef t_intro = m_intros[m_introIndex];
        m_intro.Show(t_intro.contentName + " 오픈 !", t_intro.description,
            new[] { t_intro.icon }, Finish, Cancel);
        PlayButton(ContentUnlockIntroDef.KeyOf(t_intro.content));
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
    }

    void PlayButton(string _key)
    {
        if (_key == null || m_views == null) return;
        foreach (FeatureLockView t_view in m_views)
            if (t_view != null && t_view.isActiveAndEnabled && !t_view.IsLocked
                && ContentUnlockManager.TryGetKey(t_view.Feature, out string t_key) && t_key == _key)
                t_view.PresentUnlock(null);
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
        m_playing = false;
        m_pendingIntro = false;
        m_intro = null;
        m_intros = null;
        m_introIndex = 0;
        m_confirmed = null;
        m_cancelled = null;
        if (m_views != null)
            foreach (FeatureLockView t_view in m_views)
                if (t_view != null && t_view.IsPresenting) t_view.CancelPresentation();
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
}
