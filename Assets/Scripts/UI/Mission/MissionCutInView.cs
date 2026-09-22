using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>서버가 확정한 미션 진행을 로비 오른쪽에서 알리고, 클릭하면 해당 미션 화면을 연다.</summary>
public sealed class MissionCutInView : ContentsPooledUI
{
    static MissionCutInView s_instance;
    static bool s_matchEntry;
    static bool s_opponentSearch;

    [SerializeField] CanvasGroup canvasGroup;
    [SerializeField] RectTransform panel;
    [SerializeField] Button missionButton;
    [SerializeField] TMP_Text statusText;
    [SerializeField] TMP_Text titleText;
    [SerializeField] TMP_Text progressText;
    [SerializeField] TMP_Text badgeText;
    [SerializeField] TMP_Text hintText;
    [SerializeField] Image accent;
    [SerializeField] Image progressFill;
    [SerializeField] Color progressColor = new Color(0.35f, 0.78f, 1f);
    [SerializeField] Color completeColor = new Color(1f, 0.82f, 0.3f);
    [SerializeField, Min(0.1f)] float enterSeconds = 0.3f;
    [SerializeField, Min(0.1f)] float holdSeconds = 1.5f;
    [SerializeField, Min(0.1f)] float exitSeconds = 0.22f;

    Vector2 m_home;
    Sequence m_sequence;
    MissionProgressNotification m_current;
    bool m_hasCurrent;
    bool m_suspended;
    bool m_applicationPaused;
    int m_playback;

    /// <summary>현재 알림이 유효한 트윈으로 화면에 표시 중인지 반환한다.</summary>
    public static bool IsPlaying => s_instance != null && s_instance.isActiveAndEnabled
        && s_instance.isShow && s_instance.m_hasCurrent && !s_instance.m_suspended
        && !s_instance.m_applicationPaused && s_instance.HasActiveTween;

    bool HasActiveTween => m_sequence != null && m_sequence.IsActive();

    bool CanOpenMission => !s_matchEntry && OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission)
        && GuidanceCoordinator.CanNavigateFromLobby(this);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_instance = null;
        s_matchEntry = false;
        s_opponentSearch = false;
    }

    /// <summary>매치 진입 중에는 상대 탐색 외 구간의 알림 소비를 막는다.</summary>
    public static void SetMatchEntry(bool _active)
    {
        s_matchEntry = _active;
        s_opponentSearch = false;
        if (_active && s_instance != null) s_instance.Finish("MatchEntry");
    }

    /// <summary>상대 탐색 중만 알림을 허용하고 탐색 종료 시 현재 알림을 종료한다.</summary>
    public static void SetOpponentSearch(bool _searching)
    {
        s_opponentSearch = _searching;
        if (!_searching && s_instance != null) s_instance.Finish("SearchEnded");
    }

    public static void Install()
    {
        MissionProgressNotifications.Install();
        if (s_instance != null) return;
        UIPoolManager.Instance?.AddOrUpdateUI<MissionCutInView>();
    }

    protected override bool UsePopupTransition => false;
    protected override bool UseScreenDim => false;

    public override void Initialization(UIData _data)
    {
        InitializeUI();
        data = _data;
    }

    public override void Show()
    {
        SetContentsVisible(true);
    }

    public override void Hide()
    {
        InitializeUI();
        Finish("Hide");
        SetContentsVisible(false);
    }

    protected override void OnInitializeUI()
    {
        s_instance = this;
        m_home = panel.anchoredPosition;
        UiSortingOrder.LiftNested(gameObject, UiSortingOrder.MissionCutIn);
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        canvasGroup.alpha = 0f;
        missionButton.onClick.AddListener(OpenMission);
    }

    void OpenMission()
    {
        if (!IsPlaying || !CanShow || !CanOpenMission ||
            !MissionProgressNotifications.IsCurrent(m_current) || UIPoolManager.Instance == null) return;

        string t_period = m_current.Period;
        Finish("Clicked");
        if (t_period == "guide") UIPoolManager.Instance.RequestUI<GuideMissionPanel>(this);
        else UIPoolManager.Instance.RequestUI<MissionPanel>(this,
            new MissionPanelData { WeeklyTab = t_period == "weekly" });
    }

    protected override void OnViewShown()
    {
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    protected override void OnViewHidden()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        Finish("ViewHidden");
    }

    protected override void OnDisable()
    {
        Finish("Disabled");
        base.OnDisable();
    }

    void OnActiveSceneChanged(Scene _previous, Scene _next)
    {
        if (_previous.name == "LobbyScene") Finish("SceneExit");
    }

    void OnSceneUnloaded(Scene _scene)
    {
        if (_scene.name == "LobbyScene") Finish("SceneUnloaded");
    }

    void OnApplicationPause(bool _paused)
    {
        m_applicationPaused = _paused;
        if (_paused) Finish("ApplicationPause");
        else LogTransition("ApplicationResume");
    }

    bool CanShow => GameInitialization.IsReady
        && !ContentUnlockPresentation.IsPlaying
        // 강화·진화 중에는 현재 알림을 일시정지하고 새 알림도 소비하지 않는다.
        && !CardDetailOverlayView.IsGrowthPresentationFocused
        && SceneManager.GetActiveScene().name == "LobbyScene"
        && !CurtainView.IsBusy
        // 튜토리얼 완료 여부나 풀 UI는 진행 알림을 막지 않는다. 현재 재생 중인 소개만 기다린다.
        && (!s_matchEntry || s_opponentSearch);

    void Update()
    {
#if UNITY_EDITOR
        if (m_preview) return;
#endif
        if (!isShow) return;
        canvasGroup.blocksRaycasts = canvasGroup.interactable = m_hasCurrent && CanShow
            && !m_applicationPaused && CanOpenMission;
        if (m_applicationPaused || SceneManager.GetActiveScene().name != "LobbyScene")
        {
            Finish(m_applicationPaused ? "ApplicationPause" : "SceneExit");
            return;
        }
        if (m_hasCurrent && !HasActiveTween) Finish("InvalidTween");
        if (m_hasCurrent && !MissionProgressNotifications.IsCurrent(m_current)) Finish("InvalidNotification");
        if (m_hasCurrent && MissionProgressNotifications.IsPresentedByTracker(m_current)) Finish("GuidePreviewVisible");
        if (!CanShow)
        {
            canvasGroup.alpha = 0f;
            m_sequence?.Pause();
            if (m_hasCurrent && !m_suspended)
            {
                m_suspended = true;
                LogTransition("Suspended");
            }
            return;
        }

        if (m_hasCurrent)
        {
            if (m_suspended)
            {
                m_suspended = false;
                canvasGroup.alpha = 1f;
                m_sequence.Play();
                LogTransition("Resumed");
            }
            return;
        }
        if (!MissionProgressNotifications.TryTake(out m_current)) return;
        m_hasCurrent = true;
        ShowVisual(m_current.Title, m_current.Period, m_current.PreviousProgress,
            m_current.Progress, m_current.Target, m_current.IsComplete);
    }

    void ShowVisual(string _title, string _period, long _previous, long _progress, long _target, bool _complete)
    {
        int t_playback = ++m_playback;
        m_suspended = false;
        Color t_color = _complete ? completeColor : progressColor;
        string t_period = _period == "weekly" ? "주간" : _period == "guide" ? "가이드" : "일일";
        statusText.text = t_period + (_complete ? " 미션 달성!" : " 미션 진행");
        titleText.text = _title;
        progressText.text = $"{_progress:N0} / {_target:N0}";
        badgeText.text = _complete ? "완료" : $"+{_progress - _previous:N0}";
        if (hintText != null)
            hintText.text = _complete ? "미션에서 보상을 받아 주세요" : "목표까지 차근차근!";
        accent.gameObject.SetActive(_complete);
        accent.color = t_color;
        float t_before = _target > 0 ? Mathf.Clamp01((float)_previous / _target) : 0f;
        float t_after = _target > 0 ? Mathf.Clamp01((float)_progress / _target) : 1f;
        progressFill.rectTransform.anchorMax = new Vector2(t_before, 1f);
        progressFill.gameObject.SetActive(t_before > 0f);
        canvasGroup.alpha = 1f;
        panel.localScale = Vector3.one;

        // 안전 영역 오른쪽 여백까지 넘어가야 노치 기기에서도 화면 밖에서 들어온다.
        var t_root = (RectTransform)transform;
        var t_safe = (RectTransform)panel.parent;
        float t_outX = panel.rect.width + Mathf.Max(0f, t_root.rect.width - t_safe.rect.width) + 64f;
        panel.anchoredPosition = new Vector2(t_outX, m_home.y);
        m_sequence = DOTween.Sequence().SetUpdate(true).SetTarget(this);
        m_sequence.Append(panel.DOAnchorPosX(m_home.x, enterSeconds).SetEase(Ease.OutCubic));
        m_sequence.Append(progressFill.rectTransform.DOAnchorMax(new Vector2(t_after, 1f), 0.3f).SetEase(Ease.OutQuad)
            .OnUpdate(() => progressFill.gameObject.SetActive(progressFill.rectTransform.anchorMax.x > 0f)));
        if (_complete)
            m_sequence.Join(panel.DOPunchScale(Vector3.one * 0.035f, 0.3f, 1, 0.3f));
        m_sequence.AppendInterval(holdSeconds);
        m_sequence.Append(panel.DOAnchorPosX(t_outX, exitSeconds).SetEase(Ease.InCubic));
        m_sequence.OnComplete(() => FinishPlayback(t_playback, "Completed"));
        m_sequence.OnKill(() => FinishPlayback(t_playback, "Killed"));
        LogTransition("Started");
#if UNITY_EDITOR
        // 편집 모드 미리보기는 들어온 상태에서 멈춘다. 실제 재생과 같은 시퀀스를 사용한다.
        if (!Application.isPlaying) m_sequence.Goto(enterSeconds + 0.3f, false);
#endif
    }

    void FinishPlayback(int _playback, string _reason)
    {
        if (_playback == m_playback) Finish(_reason, _reason != "Killed");
    }

    void Finish(string _reason, bool _killTween = true)
    {
        Sequence t_sequence = m_sequence;
        if (m_hasCurrent || t_sequence != null) LogTransition(_reason);
        if (m_hasCurrent || t_sequence != null) ++m_playback;
        m_sequence = null;
        m_hasCurrent = false;
        m_current = default;
        m_suspended = false;
        if (_killTween && t_sequence != null && t_sequence.IsActive())
        {
            t_sequence.OnComplete(null);
            t_sequence.OnKill(null);
            t_sequence.Kill();
        }
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
        if (panel != null)
        {
            panel.anchoredPosition = m_home;
            panel.localScale = Vector3.one;
        }
#if UNITY_EDITOR
        m_preview = false;
#endif
    }

    protected override void OnDestroy()
    {
        Finish("Destroyed");
        if (s_instance == this) s_instance = null;
        base.OnDestroy();
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    void LogTransition(string _reason)
    {
        Debug.Log($"[MissionCutIn] mission={m_current.MissionId}, playback={m_playback}, reason={_reason}, " +
            $"scene={SceneManager.GetActiveScene().name}, appPaused={m_applicationPaused}, " +
            $"current={m_hasCurrent}, tweenActive={HasActiveTween}, suspended={m_suspended}, " +
            $"matchEntry={s_matchEntry}, searching={s_opponentSearch}", this);
    }

#if UNITY_EDITOR
    bool m_preview;

    [ContextMenu("진단/미션 알림 상태")]
    public void LogNotificationState()
    {
        Debug.Log($"[MissionCutIn] show={isShow}, active={gameObject.activeInHierarchy}, " +
            $"canShow={CanShow}, preview={m_preview}, current={m_hasCurrent}, alpha={canvasGroup.alpha}, " +
            $"ready={MissionManager.IsReady}, definitions={MissionManager.Definitions.Count}");
        foreach (MissionDefinition t_definition in MissionManager.Definitions)
            if (t_definition.Event == "OpenPack")
                Debug.Log($"[MissionCutIn] {t_definition.Id}: {MissionManager.ProgressOf(t_definition)}/{t_definition.Target}, " +
                    $"claimed={MissionManager.IsClaimed(t_definition.Id)}");
        MissionProgressNotifications.LogState();
    }

    [ContextMenu("미리보기/미션 진행")]
    public void PreviewProgress()
    {
        PreparePreview();
        m_preview = true;
        ShowVisual("전투 3회 완료", "daily", 1, 2, 3, false);
    }

    [ContextMenu("미리보기/미션 달성")]
    public void PreviewComplete()
    {
        PreparePreview();
        m_preview = true;
        ShowVisual("전투 3회 완료", "daily", 2, 3, 3, true);
    }

    void PreparePreview()
    {
        if (Application.isPlaying) Show();
        else
        {
            if (m_sequence == null) m_home = panel.anchoredPosition;
            if (contents != null) contents.SetActive(true);
        }
        Finish("PreviewReplaced");
    }
#endif
}
