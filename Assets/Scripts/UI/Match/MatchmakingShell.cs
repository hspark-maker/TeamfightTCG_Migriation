using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 매칭 상태와 프로필을 표시한다. 레이아웃 배율은 UniformFitContent가 관리한다.
public class MatchmakingShell : ContentsUIBehaviour
{
    [SerializeField] MatchProfileView myProfile;
    [SerializeField] MatchProfileView opponentProfile;
    [SerializeField] Button           cancelButton;
    [SerializeField] GameObject       versusRoot;
    [SerializeField] TMP_Text         modeTitleText;
    [SerializeField] TMP_Text         matchStatusText;
    [SerializeField] GameObject       searchClockRoot;
    [SerializeField] TMP_Text         searchElapsedText;
    [SerializeField] GameObject       searchingHintRoot;
    [SerializeField] GameObject       preparingHintRoot;
    [SerializeField] GameObject       battleStartingRoot;
    [SerializeField] RectTransform    loadingSpinner;
    [SerializeField] Image[]          searchDots = Array.Empty<Image>();
    [SerializeField] string           rankedTitle = "랭크전";
    [SerializeField] string versusTitle = "도전";
    [Tooltip("상대 확정 연출을 포함해 프로필을 보여 주는 시간(초).")]
    [Min(0f)] [SerializeField] float readyHold = 0.8f;
    [SerializeField] OverlayDim dim = new OverlayDim();
    [Tooltip("균등 배율을 관리하는 DesignFrame 안쪽의 Layout. 충격만 받고 원래 자세로 돌아온다.")]
    [SerializeField] RectTransform impactRoot;

    const float DOT_INTERVAL = 0.35f;
    const float FOUND_DURATION = 1.9f;
    Sequence m_foundSequence;
    Image m_foundFlash;
    CancellationTokenSource m_cts;
    bool m_cancelAllowed;
    bool m_running;
    bool m_wired;
    bool m_versusMode;
    bool m_searching;
    bool m_statusAnimating;
    float m_searchStartedAt;
    int m_searchSeconds;
    Color[] m_searchDotColors;
    int m_searchDotPhase = -1;

    protected override void OnInitializeUI()
    {
        FitToParent();
        if (loadingSpinner != null) loadingSpinner.gameObject.SetActive(false);
        m_searchDotColors = new Color[searchDots.Length];
        for (int t_i = 0; t_i < searchDots.Length; t_i++)
            if (searchDots[t_i] != null) m_searchDotColors[t_i] = searchDots[t_i].color;
        EnsureWired();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        StopFoundSequence();
        dim.Clear();
        m_cts?.Cancel();
    }

    protected override void OnViewShown()
    {
        UiSortingOrder.LiftNested(gameObject, UiSortingOrder.Matchmaking);
        dim.Show(this, UiSortingOrder.MatchmakingDim, 0f);
    }

    public async UniTask PlayVersusAsync(MatchOpponent _opponent, CancellationToken _ct)
    {
        InitializeUI();
        if (m_running || _ct.IsCancellationRequested) return;
        m_running = true;
        m_versusMode = true;
        bool t_ready = false;
        try
        {
            OpenProfiles();
            ShowFound(_opponent);
            t_ready = !await WaitAsync(readyHold, _ct);
        }
        finally
        {
            m_running = false;
            if (!t_ready) Close();
        }
    }

    void OpenProfiles()
    {
        StopFoundSequence();
        SetContentsVisible(true);
        if (myProfile != null) myProfile.Render(MatchProfile.OfLocalPlayer());
    }

    void OpenSearching()
    {
        OpenProfiles();
        if (opponentProfile != null) opponentProfile.ShowSearching();
        if (versusRoot != null) versusRoot.SetActive(true);
        if (cancelButton != null) cancelButton.gameObject.SetActive(true);
        SetCancelInteractable(true);
        ShowMatchStatus(true);
    }

    void ShowFound(in MatchOpponent _opponent)
    {
        SetCancelInteractable(false);
        if (cancelButton != null) cancelButton.gameObject.SetActive(false);
        if (opponentProfile != null) opponentProfile.Render(_opponent.Profile);
        if (versusRoot != null) versusRoot.SetActive(true);
        ShowMatchStatus(false);
        if (!m_versusMode) PlayFoundSequence();
    }

    void PlayFoundSequence()
    {
        StopFoundSequence();
        var t_opponent = opponentProfile != null ? opponentProfile.Rect : null;
        var t_mine = myProfile != null ? myProfile.Rect : null;
        var t_vs = versusRoot != null ? versusRoot.transform : null;
        Vector3 t_opponentScale = t_opponent != null ? t_opponent.localScale : Vector3.one;
        Vector3 t_vsScale = t_vs != null ? t_vs.localScale : Vector3.one;
        Vector3 t_rootScale = impactRoot != null ? impactRoot.localScale : Vector3.one;
        Vector2 t_opponentHome = t_opponent != null ? t_opponent.anchoredPosition : Vector2.zero;
        Vector2 t_myHome = t_mine != null ? t_mine.anchoredPosition : Vector2.zero;
        Vector2 t_rootHome = impactRoot != null ? impactRoot.anchoredPosition : Vector2.zero;
        Vector2 t_direction = (t_opponentHome - t_myHome).normalized;
        const float t_slam = 0.08f;
        const float t_charge = 0.38f;
        const float t_clash = 1f;
        const float t_hit = t_clash + 0.24f;
        EnsureFoundFlash();

        m_foundSequence = DOTween.Sequence().SetTarget(this).SetUpdate(true).SetLink(gameObject);
        // 중단·재진입 시 이동·섬광·딤까지 복구한다. DesignFrame의 화면 맞춤 배율은 건드리지 않는다.
        m_foundSequence.OnKill(() =>
        {
            if (t_opponent != null)
            {
                t_opponent.localScale = t_opponentScale;
                t_opponent.anchoredPosition = t_opponentHome;
            }
            if (t_mine != null) t_mine.anchoredPosition = t_myHome;
            if (t_vs != null)
            {
                t_vs.localScale = t_vsScale;
                t_vs.gameObject.SetActive(true);
            }
            if (impactRoot != null)
            {
                impactRoot.localScale = t_rootScale;
                impactRoot.anchoredPosition = t_rootHome;
            }
            if (m_foundFlash != null) m_foundFlash.gameObject.SetActive(false);
            dim.Reset();
            m_foundSequence = null;
        });

        // 예전 발견 슬램: 크게 떠 있던 상대가 0.08초에 내려꽂힌다.
        if (t_opponent != null)
        {
            t_opponent.localScale = t_opponentScale * 1.22f;
            t_opponent.anchoredPosition = t_opponentHome + Vector2.up * 64f;
            m_foundSequence.Insert(0f, t_opponent.DOScale(t_opponentScale, t_slam).SetEase(Ease.InQuad));
            m_foundSequence.Insert(0f, t_opponent.DOAnchorPos(t_opponentHome, t_slam).SetEase(Ease.InQuad));
        }
        m_foundSequence.InsertCallback(t_slam, () => SoundManager.Instance?.PlayCue(EOutgameSound.MatchFound));
        InsertFoundFlash(t_slam, 0.2f);
        m_foundSequence.Insert(t_slam, DOTween.To(() => dim.Level, value => dim.Level = value, -0.55f, 0.03f));
        m_foundSequence.Insert(t_slam + 0.03f, DOTween.To(() => dim.Level, value => dim.Level = value, 0f, 0.24f));

        // 조임 → 뒤로 당김 → 돌진 → 0.04초 정지 → 반동. 두 카드와 VS가 같은 충돌 시각을 쓴다.
        InsertFoundClash(t_mine, t_myHome, t_direction, t_charge, t_clash);
        InsertFoundClash(t_opponent, t_opponentHome, -t_direction, t_charge, t_clash);
        m_foundSequence.Insert(t_charge, DOTween.To(() => dim.Level, value => dim.Level = value, -0.32f, t_clash - t_charge));
        m_foundSequence.Insert(t_hit, DOTween.To(() => dim.Level, value => dim.Level = value, 0.2f, 0.06f));
        m_foundSequence.Insert(t_hit + 0.06f, DOTween.To(() => dim.Level, value => dim.Level = value, 0f, 0.34f));
        m_foundSequence.InsertCallback(t_hit, () => SoundManager.Instance?.PlayCue(EOutgameSound.MatchVersus));
        InsertFoundFlash(t_hit, 0.3f);
        if (impactRoot != null)
        {
            m_foundSequence.Insert(t_slam, impactRoot.DOPunchScale(t_rootScale * 0.04f, 0.24f, 2, 0.5f));
            m_foundSequence.Insert(t_hit, impactRoot.DOPunchScale(t_rootScale * 0.065f, 0.24f, 2, 0.5f));
            m_foundSequence.Insert(t_hit, impactRoot.DOShakeAnchorPos(0.2f, 4f, 20, 90f, false, true));
        }
        if (t_vs != null)
        {
            t_vs.gameObject.SetActive(false);
            t_vs.localScale = t_vsScale * 1.4f;
            m_foundSequence.InsertCallback(t_hit, () => t_vs.gameObject.SetActive(true));
            m_foundSequence.Insert(t_hit, t_vs.DOScale(t_vsScale, 0.14f).SetEase(Ease.OutQuint));
            m_foundSequence.Insert(t_hit + 0.3f,
                t_vs.DOScale(t_vsScale * 1.035f, 0.17f).SetEase(Ease.InOutSine).SetLoops(2, LoopType.Yoyo));
        }
    }

    void InsertFoundClash(RectTransform _rect, Vector2 _home, Vector2 _direction, float _charge, float _clash)
    {
        if (_rect == null) return;
        m_foundSequence.Insert(_charge, _rect.DOAnchorPos(_home + _direction * 8f, _clash - _charge).SetEase(Ease.InQuad));
        m_foundSequence.Insert(_clash, _rect.DOAnchorPos(_home - _direction * 26f, 0.1f).SetEase(Ease.OutQuad));
        m_foundSequence.Insert(_clash + 0.16f, _rect.DOAnchorPos(_home + _direction * 60f, 0.08f).SetEase(Ease.InQuad));
        m_foundSequence.Insert(_clash + 0.28f, _rect.DOAnchorPos(_home, 0.26f).SetEase(Ease.OutBack));
    }

    void EnsureFoundFlash()
    {
        if (m_foundFlash == null)
        {
            var t_go = new GameObject("MatchFoundFlash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            t_go.transform.SetParent(viewContents.transform, false);
            m_foundFlash = t_go.GetComponent<Image>();
            var t_rect = m_foundFlash.rectTransform;
            t_rect.anchorMin = Vector2.zero;
            t_rect.anchorMax = Vector2.one;
            t_rect.offsetMin = t_rect.offsetMax = Vector2.zero;
            m_foundFlash.raycastTarget = false;
        }
        m_foundFlash.color = new Color(1f, 0.92f, 0.72f, 0f);
        m_foundFlash.gameObject.SetActive(true);
    }

    void InsertFoundFlash(float _at, float _peak)
    {
        m_foundSequence.Insert(_at, m_foundFlash.DOFade(_peak, 0.02f));
        m_foundSequence.Insert(_at + 0.02f, m_foundFlash.DOFade(0f, 0.22f));
    }

    void StopFoundSequence()
    {
        m_foundSequence?.Kill();
        m_foundSequence = null;
    }

    void DisableCancelAfterPairing() => SetCancelInteractable(false);

    protected override void OnViewHidden()
    {
        StopFoundSequence();
        dim.Clear();
        m_statusAnimating = false;
        m_searching = false;
        RefreshSearchDots();
        m_cts?.Cancel();
    }

    public async UniTask<MatchOpponent?> RunMatchAsync(IMatchmaker _matchmaker, CancellationToken _ct)
    {
        InitializeUI();
        if (_matchmaker == null)
        {
            Debug.LogError("[MatchmakingShell] There is no matchmaker — skipping matchmaking.");

            return null;
        }

        if (m_running)
        {
            Debug.LogWarning("[MatchmakingShell] Matchmaking is already in progress — ignoring the duplicate entry.");

            return null;
        }

        m_running = true;
        m_cts     = CancellationTokenSource.CreateLinkedTokenSource(_ct);

        m_versusMode = false;

        PhotonRankedMatchmaker t_rankedMatchmaker = _matchmaker as PhotonRankedMatchmaker;
        if (t_rankedMatchmaker != null) t_rankedMatchmaker.OnOpponentPaired += DisableCancelAfterPairing;

        MatchOpponent? t_result = null;
        try
        {
            t_result = await RunStagesAsync(_matchmaker, _ct);

            return t_result;
        }
        finally
        {
            if (t_rankedMatchmaker != null) t_rankedMatchmaker.OnOpponentPaired -= DisableCancelAfterPairing;

            if (t_result == null) Close();

            m_running = false;
            m_cts.Dispose();
            m_cts = null;
        }
    }

    async UniTask<MatchOpponent?> RunStagesAsync(IMatchmaker _matchmaker, CancellationToken _ct)
    {
        OpenSearching();
        MissionCutInView.SetOpponentSearch(true);

        bool t_abandoned;
        MatchOpponent? t_opponent;
        try
        {
            (t_abandoned, t_opponent) = await MatchmakingRun.Run(_matchmaker, m_cts.Token)
                .AttachExternalCancellation(m_cts.Token)
                .SuppressCancellationThrow();
        }
        finally
        {
            MissionCutInView.SetOpponentSearch(false);
        }
        if (t_abandoned) return null;

        if (_ct.IsCancellationRequested) return null;

        if (t_opponent == null) return null;

        ShowFound(t_opponent.Value);
        if (await WaitAsync(Mathf.Max(readyHold, FOUND_DURATION), m_cts.Token)) return null;
        return t_opponent;
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        var t_canvas = GetComponent<Canvas>();
        if (t_canvas == null || !t_canvas.isRootCanvas) return;

        if (t_canvas.sortingOrder != UiSortingOrder.Matchmaking)
            Debug.LogWarning(
                $"[MatchmakingShell] The authored layer differs from the table (authored {t_canvas.sortingOrder} != table {UiSortingOrder.Matchmaking}) — "
              + "the runtime follows the table, so fixing only the prefab changes nothing. Fix UiSortingOrder.Matchmaking.", this);
#endif
    }

    void FitToParent()
    {
        if (transform.parent == null) return;

        var t_rect = (RectTransform)transform;

        t_rect.localScale = Vector3.one;
        t_rect.anchorMin  = Vector2.zero;
        t_rect.anchorMax  = Vector2.one;
        t_rect.pivot      = new Vector2(0.5f, 0.5f);
        t_rect.offsetMin  = Vector2.zero;
        t_rect.offsetMax  = Vector2.zero;
    }

    void EnsureWired()
    {
        if (m_wired) return;
        m_wired = true;

        if (cancelButton == null)
        {
            Debug.LogError("[MatchmakingShell] cancelButton is unwired — there is no way to back out during matchmaking.");

            return;
        }

        cancelButton.onClick.RemoveAllListeners();
        cancelButton.onClick.AddListener(Cancel);
    }

    public void Cancel()
    {
        // 버튼을 거치지 않거나 이미 전달된 클릭도 매칭 확정 뒤에는 취소할 수 없다.
        if (!m_running || !m_cancelAllowed) return;

        SetCancelInteractable(false);
        m_cts?.Cancel();
    }

    public void Close()
    {
        SetContentsVisible(false);
    }

    void SetCancelInteractable(bool _on)
    {
        m_cancelAllowed = _on;
        if (cancelButton != null) cancelButton.interactable = _on;
    }

    static async UniTask<bool> WaitAsync(float _seconds, CancellationToken _ct)
    {
        if (_seconds <= 0f) return _ct.IsCancellationRequested;

        return await UniTask.Delay(TimeSpan.FromSeconds(_seconds), ignoreTimeScale: true, cancellationToken: _ct)
                            .SuppressCancellationThrow();
    }

    void ShowMatchStatus(bool _searching)
    {
        m_searching = _searching;
        m_statusAnimating = true;

        if (modeTitleText != null) modeTitleText.text = m_versusMode ? versusTitle : rankedTitle;
        if (matchStatusText != null)
        {
            matchStatusText.gameObject.SetActive(!m_versusMode);
            matchStatusText.text = _searching ? "상대를 찾는 중.." : "매칭완료!";
        }
        if (searchClockRoot != null) searchClockRoot.SetActive(_searching);
        if (searchingHintRoot != null) searchingHintRoot.SetActive(_searching);
        if (preparingHintRoot != null) preparingHintRoot.SetActive(!_searching);
        if (battleStartingRoot != null) battleStartingRoot.SetActive(!_searching);
        if (loadingSpinner != null) loadingSpinner.gameObject.SetActive(false);

        if (_searching)
        {
            m_searchStartedAt = Time.unscaledTime;
            m_searchSeconds = -1;
            RefreshSearchElapsed();
        }
        RefreshSearchDots();
    }

    void Update()
    {
        if (!m_statusAnimating || !IsViewVisible) return;

        if (m_searching)
        {
            RefreshSearchElapsed();
            RefreshSearchDots();
        }
    }

    void RefreshSearchDots()
    {
        int t_count = m_searchDotColors?.Length ?? 0;
        if (t_count == 0) return;

        int t_phase = m_searching
            ? Mathf.FloorToInt((Time.unscaledTime - m_searchStartedAt) / DOT_INTERVAL) % t_count
            : 0;
        if (t_phase == m_searchDotPhase) return;

        m_searchDotPhase = t_phase;
        for (int t_i = 0; t_i < t_count; t_i++)
            if (searchDots[t_i] != null)
                searchDots[t_i].color = m_searchDotColors[(t_i - t_phase + t_count) % t_count];
    }

    void RefreshSearchElapsed()
    {
        int t_seconds = Mathf.Max(0, Mathf.FloorToInt(Time.unscaledTime - m_searchStartedAt));
        if (t_seconds == m_searchSeconds) return;

        m_searchSeconds = t_seconds;
        if (searchElapsedText != null)
            searchElapsedText.text = $"{t_seconds / 60:00} : {t_seconds % 60:00}";
    }
}
