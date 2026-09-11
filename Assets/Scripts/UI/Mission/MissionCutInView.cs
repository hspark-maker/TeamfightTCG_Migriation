using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>서버가 확정한 미션 진행을 로비 오른쪽에서 한 건씩 알린다. 입력과 보상 수령에는 관여하지 않는다.</summary>
public sealed class MissionCutInView : SingletonOverlayBase
{
    static MissionCutInView s_instance;

    [SerializeField] Canvas overlayCanvas;
    [SerializeField] CanvasGroup canvasGroup;
    [SerializeField] RectTransform panel;
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
    [SerializeField, Min(0.1f)] float holdSeconds = 2.2f;
    [SerializeField, Min(0.1f)] float exitSeconds = 0.22f;

    Vector2 m_home;
    Sequence m_sequence;
    MissionProgressNotification m_current;
    LobbyMatchLauncher m_matchLauncher;
    bool m_hasCurrent;

    public static bool IsPlaying => s_instance != null && s_instance.isActiveAndEnabled
        && s_instance.m_hasCurrent && s_instance.canvasGroup != null && s_instance.canvasGroup.alpha > 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState() => s_instance = null;

    public static void Install()
    {
        MissionProgressNotifications.Install();
        if (s_instance != null) return;
        GameObject t_prefab = RuntimeOverlayPrefabs.Get<MissionCutInView>();
        if (t_prefab == null) return;
        GameObject t_object = Instantiate(t_prefab);
        s_instance = t_object.GetComponent<MissionCutInView>();
        DontDestroyOnLoad(t_object);
    }

    void Awake()
    {
        m_home = panel.anchoredPosition;
        UiSortingOrder.Stamp(overlayCanvas, UiSortingOrder.MissionCutIn);
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        canvasGroup.alpha = 0f;
        FindLobby();
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        m_sequence?.Pause();
        if (canvasGroup != null) canvasGroup.alpha = 0f;
    }

    void OnSceneLoaded(Scene _scene, LoadSceneMode _mode) => FindLobby();
    void FindLobby() => m_matchLauncher = FindFirstObjectByType<LobbyMatchLauncher>(FindObjectsInactive.Include);

    bool CanShow => GameInitialization.IsReady
        && !ContentUnlockPresentation.IsPlaying
        && SceneManager.GetActiveScene().name == "LobbyScene"
        && !CurtainView.IsBusy
        && !SynergyIntroduction.IsActive
        && (UIPoolManager.instance == null || !UIPoolManager.instance.HasVisibleUIExcept())
        && (m_matchLauncher == null || !m_matchLauncher.IsRunning)
        && OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission) && !OutgameTutorialRunner.IsGuidedRunning;

    void Update()
    {
#if UNITY_EDITOR
        if (m_preview) return;
#endif
        if (m_hasCurrent && !MissionProgressNotifications.IsCurrent(m_current)) Finish();
        if (!CanShow)
        {
            canvasGroup.alpha = 0f;
            m_sequence?.Pause();
            return;
        }

        if (m_hasCurrent)
        {
            canvasGroup.alpha = 1f;
            m_sequence?.Play();
            return;
        }
        if (!MissionProgressNotifications.TryTake(out m_current)) return;
        m_hasCurrent = true;
        ShowVisual(m_current.Title, m_current.Period, m_current.PreviousProgress,
            m_current.Progress, m_current.Target, m_current.IsComplete);
    }

    void ShowVisual(string _title, string _period, long _previous, long _progress, long _target, bool _complete)
    {
        m_sequence?.Kill();
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
        progressFill.color = t_color;
        float t_before = _target > 0 ? Mathf.Clamp01((float)_previous / _target) : 0f;
        float t_after = _target > 0 ? Mathf.Clamp01((float)_progress / _target) : 1f;
        progressFill.rectTransform.anchorMax = new Vector2(t_before, 1f);
        canvasGroup.alpha = 1f;
        panel.localScale = Vector3.one;

        // 안전 영역 오른쪽 여백까지 넘어가야 노치 기기에서도 화면 밖에서 들어온다.
        var t_root = (RectTransform)transform;
        var t_safe = (RectTransform)panel.parent;
        float t_outX = panel.rect.width + Mathf.Max(0f, t_root.rect.width - t_safe.rect.width) + 64f;
        panel.anchoredPosition = new Vector2(t_outX, m_home.y);
        m_sequence = DOTween.Sequence().SetUpdate(true).SetTarget(this);
        m_sequence.Append(panel.DOAnchorPosX(m_home.x, enterSeconds).SetEase(Ease.OutCubic));
        m_sequence.Append(progressFill.rectTransform.DOAnchorMax(new Vector2(t_after, 1f), 0.3f).SetEase(Ease.OutQuad));
        if (_complete)
            m_sequence.Join(panel.DOPunchScale(Vector3.one * 0.035f, 0.3f, 1, 0.3f));
        m_sequence.AppendInterval(holdSeconds);
        m_sequence.Append(panel.DOAnchorPosX(t_outX, exitSeconds).SetEase(Ease.InCubic));
        m_sequence.OnComplete(Finish);
#if UNITY_EDITOR
        // 편집 모드 미리보기는 들어온 상태에서 멈춘다. 실제 재생과 같은 시퀀스를 사용한다.
        if (!Application.isPlaying) m_sequence.Goto(enterSeconds + 0.3f, false);
#endif
    }

    void Finish()
    {
        m_sequence?.Kill();
        m_sequence = null;
        m_hasCurrent = false;
        canvasGroup.alpha = 0f;
        panel.anchoredPosition = m_home;
        panel.localScale = Vector3.one;
#if UNITY_EDITOR
        m_preview = false;
#endif
    }

    void OnDestroy()
    {
        m_sequence?.Kill();
        if (s_instance == this) s_instance = null;
    }

#if UNITY_EDITOR
    bool m_preview;

    [ContextMenu("미리보기/미션 진행")]
    public void PreviewProgress()
    {
        if (!Application.isPlaying) m_home = panel.anchoredPosition;
        m_preview = true;
        ShowVisual("전투 3회 완료", "daily", 1, 2, 3, false);
    }

    [ContextMenu("미리보기/미션 달성")]
    public void PreviewComplete()
    {
        if (!Application.isPlaying) m_home = panel.anchoredPosition;
        m_preview = true;
        ShowVisual("전투 3회 완료", "daily", 2, 3, 3, true);
    }
#endif
}
