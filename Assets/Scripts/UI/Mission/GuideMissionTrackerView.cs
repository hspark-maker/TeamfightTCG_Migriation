using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>로비 공통 영역에서 현재 가이드 미션을 표시하고 안내 이동·보상 수령을 처리한다.</summary>
public sealed class GuideMissionTrackerView : MonoBehaviour
{
    [Header("문구")]
    [SerializeField] TMP_Text actText;
    [SerializeField] TMP_Text titleText;
    [SerializeField] TMP_Text progressText;
    [SerializeField] RectTransform progressFill;
    [SerializeField] GameObject hintRoot;
    [SerializeField] CanvasGroup canvasGroup;

    [Header("보상")]
    [SerializeField] Image rewardIcon;
    [SerializeField] TMP_Text rewardCountText;

    [Header("다른 로비 탭 배치")]
    [SerializeField, Min(80f)] float dockHeight = 112f;
    [SerializeField, Min(0f)] float dockInset = 24f;
    [SerializeField, Min(0f)] float dockBottomGap = 72f;
    [SerializeField] Color dockBackgroundColor = new Color(0f, 0f, 0f, 0.8f);

    Sprite m_authoredRewardIcon;
    static GuideMissionTrackerView s_visible;
    Button m_button;
    MissionDefinition m_displayed;
    long m_progress;
    bool m_complete;
    bool m_settled;
    bool m_holdingClaim;
    bool m_rewardsClosed;
    bool m_transitioning;
    bool m_showingNextMission;
    int m_claimVersion;
    int m_closedFrame;
    float m_hintUntil;
    float m_nextRefresh;
    Tween m_transition;
    Tween m_progressTween;
    Tween m_rewardTween;
    Color m_progressColor;
    bool m_initialized;
    bool m_visible;
    bool m_ready;
    LobbyTabController m_shell;
    RectTransform m_rect;
    RectTransform m_tabBar;
    RectTransform m_gauge;
    RectTransform m_alertDot;
    GameObject m_rewardRow;
    bool m_homeRewardRowActive;
    LobbyTabPanel[] m_tabs;
    float[] m_tabBottoms;
    readonly Vector3[] m_corners = new Vector3[4];
    Vector2 m_homePosition;
    Vector2 m_homeSize;
    RectHome[] m_homeRects;
    TextAlignmentOptions m_homeActAlignment;
    MissionDefinition m_nextMission;
    string m_actLabel;
    Image m_background;
    Color m_homeBackgroundColor;
    Color m_homeActColor;
    Color m_homeTitleColor;
    bool m_docked;

    readonly struct RectHome
    {
        readonly RectTransform rect;
        readonly Vector2 min, max, pivot, position, size;
        public RectHome(RectTransform _rect)
        {
            rect = _rect;
            min = _rect != null ? _rect.anchorMin : default;
            max = _rect != null ? _rect.anchorMax : default;
            pivot = _rect != null ? _rect.pivot : default;
            position = _rect != null ? _rect.anchoredPosition : default;
            size = _rect != null ? _rect.sizeDelta : default;
        }
        public void Restore()
        {
            if (rect == null) return;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }
    }

    public event Action PresentationFinished;
    internal bool IsHoldingClaim => m_holdingClaim || m_transitioning;
    internal static GuideMissionTrackerView Visible => s_visible;
    internal static bool HasPendingPresentation => s_visible != null && s_visible.IsHoldingClaim;
    internal static bool IsShowingNextMission => s_visible != null && s_visible.m_showingNextMission;

    internal static bool IsPresenting(string _missionId) => s_visible != null
        && s_visible.isActiveAndEnabled && s_visible.m_settled && s_visible.m_visible
        && s_visible.m_displayed?.Id == _missionId
        && (s_visible.canvasGroup == null || s_visible.canvasGroup.alpha > 0.99f)
        && GuidanceCoordinator.CanNavigateFromLobby(null);

    internal void SetSettled(bool _settled)
    {
        m_settled = _settled;
        if (_settled) Rebind();
        else
        {
            CancelPresentation();
            Rebind();
        }
    }

    internal void DismissHint()
    {
        if (GuideMissionHintHistory.IsPending) GuideMissionHintHistory.Consume();
        if (hintRoot != null) hintRoot.SetActive(false);
    }

    internal int BeginClaim(MissionDefinition _mission)
    {
        DismissHint();
        m_displayed = _mission;
        m_holdingClaim = true;
        m_rewardsClosed = false;
        return ++m_claimVersion;
    }

    internal void EndClaim(int _version, bool _success)
    {
        if (_version != m_claimVersion || !m_holdingClaim) return;
        if (!_success)
        {
            CancelPresentation();
            Rebind();
            PresentationFinished?.Invoke();
            return;
        }
        m_rewardsClosed = true;
        m_closedFrame = Time.frameCount;
    }

    void Awake() => EnsureInitialized();

    void EnsureInitialized()
    {
        if (m_initialized) return;
        m_initialized = true;
        if (rewardIcon != null) m_authoredRewardIcon = rewardIcon.sprite;
        m_button = GetComponent<Button>();
        if (m_button != null) m_button.onClick.AddListener(HandleClick);
        m_rect = transform as RectTransform;
        if (m_rect != null) { m_homePosition = m_rect.anchoredPosition; m_homeSize = m_rect.sizeDelta; }
        m_shell = GetComponentInParent<LobbyTabController>();
        if (m_shell != null)
        {
            var t_bar = m_shell.GetComponentInChildren<LobbyTabBarView>(true);
            if (t_bar != null) m_tabBar = t_bar.transform as RectTransform;
            m_tabs = m_shell.GetComponentsInChildren<LobbyTabPanel>(true);
            m_tabBottoms = new float[m_tabs.Length];
            for (int i = 0; i < m_tabs.Length; i++) m_tabBottoms[i] = m_tabs[i].Root.offsetMin.y;
        }
        m_gauge = progressText != null ? progressText.transform.parent as RectTransform : null;
        m_alertDot = transform.Find("RedDot") as RectTransform;
        m_rewardRow = rewardIcon != null ? rewardIcon.transform.parent.gameObject : null;
        m_homeRewardRowActive = m_rewardRow != null && m_rewardRow.activeSelf;
        m_homeRects = new[] { new RectHome(titleText?.rectTransform), new RectHome(actText?.rectTransform),
            new RectHome(m_gauge), new RectHome(progressText?.rectTransform), new RectHome(m_alertDot) };
        if (actText != null) m_homeActAlignment = actText.alignment;
        m_background = GetComponent<Image>();
        if (m_background != null) m_homeBackgroundColor = m_background.color;
        if (actText != null) m_homeActColor = actText.color;
        if (titleText != null) m_homeTitleColor = titleText.color;
        if (titleText != null) titleText.maxVisibleLines = 2;
        if (progressText != null) m_progressColor = progressText.color;
    }

    void OnEnable()
    {
        MissionManager.OnChanged += this.Rebind;
        OutgameTutorialRunner.OnGuidedChanged += this.Rebind;
        OutgameFeatureLock.OnChanged += this.Rebind;
        s_visible = this;
        SetSettled(true);
    }

    void OnDisable()
    {
        MissionManager.OnChanged -= this.Rebind;
        OutgameTutorialRunner.OnGuidedChanged -= this.Rebind;
        OutgameFeatureLock.OnChanged -= this.Rebind;
        CancelPresentation();
        m_displayed = null;
        m_settled = false;
        m_ready = false;
        SetVisible(false);
        if (s_visible == this) s_visible = null;
    }

    void OnDestroy()
    {
        if (m_button != null) m_button.onClick.RemoveListener(HandleClick);
    }

    void LateUpdate() => RefreshLayout();

    void RefreshLayout(LobbyTabPanel _previewPanel = null)
    {
        if (m_shell == null || m_rect == null || !(m_rect.parent is RectTransform t_parent)) return;
        var t_current = _previewPanel != null ? _previewPanel : m_shell.CurrentPanel;
        bool t_dock = t_current != null && !(t_current is LobbyMatchTabPanel) && m_tabBar != null;
        float t_bottom = t_parent.rect.yMin;
        if (m_tabBar != null)
        {
            m_tabBar.GetWorldCorners(m_corners);
            t_bottom = t_parent.InverseTransformPoint(m_corners[1]).y + dockBottomGap;
        }
        // 안내 바 높이만큼 콘텐츠 영역을 확보해 카드·목록 위로 겹치지 않게 한다.
        if (m_tabs != null)
            for (int i = 0; i < m_tabs.Length; i++)
            {
                var t_tab = m_tabs[i];
                if (t_tab == null || t_tab is LobbyMatchTabPanel) continue;
                var t_root = t_tab.Root;
                float t_offset = m_tabBottoms[i];
                if (m_visible && m_tabBar != null && t_root.parent is RectTransform t_tabParent)
                {
                    float t_top = t_tabParent.InverseTransformPoint(
                        t_parent.TransformPoint(new Vector3(0f, t_bottom + dockHeight + 8f, 0f))).y;
                    float t_anchor = Mathf.Lerp(t_tabParent.rect.yMin, t_tabParent.rect.yMax, t_root.anchorMin.y);
                    t_offset = Mathf.Max(t_offset, t_top - t_anchor);
                }
                var t_min = t_root.offsetMin;
                if (!Mathf.Approximately(t_min.y, t_offset)) { t_min.y = t_offset; t_root.offsetMin = t_min; }
            }
        if (m_docked != t_dock)
        {
            m_docked = t_dock;
            if (!t_dock) foreach (var t_home in m_homeRects) t_home.Restore();
            if (m_rewardRow != null) m_rewardRow.SetActive(!t_dock && m_homeRewardRowActive);
            if (hintRoot != null) hintRoot.SetActive(false);
            if (m_background != null) m_background.color = t_dock ? dockBackgroundColor : m_homeBackgroundColor;
            if (actText != null)
            {
                actText.color = t_dock ? new Color(1f, 1f, 1f, 0.65f) : m_homeActColor;
                actText.alignment = t_dock ? TextAlignmentOptions.MidlineLeft : m_homeActAlignment;
            }
            if (titleText != null)
            {
                titleText.color = t_dock ? Color.white : m_homeTitleColor;
                titleText.maxVisibleLines = t_dock ? 1 : 2;
            }
            RefreshPresentationText();
        }
        Vector2 t_position = m_homePosition;
        Vector2 t_size = m_homeSize;
        if (t_dock)
        {
            t_size = new Vector2(Mathf.Max(400f, t_parent.rect.width - dockInset * 2f), dockHeight);
            float t_anchorX = Mathf.Lerp(t_parent.rect.xMin, t_parent.rect.xMax, m_rect.anchorMin.x);
            float t_anchorY = Mathf.Lerp(t_parent.rect.yMin, t_parent.rect.yMax, m_rect.anchorMin.y);
            t_position.x = t_parent.rect.xMin - t_anchorX + dockInset + t_size.x * m_rect.pivot.x;
            t_position.y = t_bottom - t_anchorY + t_size.y * m_rect.pivot.y;
            PlaceDockRect(titleText?.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -12f), new Vector2(t_size.x - 228f, 42f));
            PlaceDockRect(actText?.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -62f), new Vector2(t_size.x - 228f, 30f));
            PlaceDockRect(m_gauge, Vector2.one, new Vector2(-20f, -24f), new Vector2(164f, 64f));
            PlaceDockRect(progressText?.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(148f, 44f));
            PlaceDockRect(m_alertDot, Vector2.one, new Vector2(-8f, -8f), new Vector2(20f, 20f));
        }
        if (m_rect.sizeDelta != t_size) m_rect.sizeDelta = t_size;
        if (m_rect.anchoredPosition != t_position) m_rect.anchoredPosition = t_position;
    }

    static void PlaceDockRect(RectTransform _rect, Vector2 _anchor, Vector2 _position, Vector2 _size)
    {
        if (_rect == null) return;
        _rect.anchorMin = _rect.anchorMax = _rect.pivot = _anchor;
        _rect.anchoredPosition = _position;
        _rect.sizeDelta = _size;
    }

    void RefreshPresentationText()
    {
        if (progressFill != null)
        {
            float t_ratio = m_displayed == null ? 0f : m_displayed.Target > 0
                ? Mathf.Clamp01((float)m_progress / m_displayed.Target) : m_complete ? 1f : 0f;
            progressFill.anchorMax = new Vector2(t_ratio, 1f);
            progressFill.gameObject.SetActive(t_ratio > 0f);
        }
        if (m_displayed == null || m_transitioning) return;
        string t_progress = m_complete ? "완료" : $"{m_progress} / {m_displayed.Target}";
        if (titleText != null) titleText.text = m_docked ? $"지금 · {m_displayed.Title}  {t_progress}" : m_displayed.Title;
        if (actText != null) actText.text = m_docked
            ? m_nextMission != null ? $"다음 · {m_nextMission.Title}" : "마지막 단계예요"
            : m_actLabel;
        if (progressText != null) progressText.text = m_docked
            ? GuideMissionPreviewState.Of(m_displayed).Action switch
            {
                GuideMissionPreviewState.EAction.Claim => "보상 받기 ›",
                GuideMissionPreviewState.EAction.Claiming => "받는 중…",
                GuideMissionPreviewState.EAction.Resume => "이어서 ›",
                GuideMissionPreviewState.EAction.Start => "안내 받기 ›",
                GuideMissionPreviewState.EAction.Move => "이동하기 ›",
                _ => "준비 중",
            } : t_progress;
    }

    void Update()
    {
        if (!m_settled || Time.unscaledTime < m_nextRefresh) return;
        m_nextRefresh = Time.unscaledTime + 0.1f;
        bool t_ready = GuidanceCoordinator.CanNavigateFromLobby(null);
        if (t_ready && !m_ready && OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission))
            RefreshMissionsAsync().Forget();
        m_ready = t_ready;
        if (m_holdingClaim && m_rewardsClosed && !m_transitioning
            && Time.frameCount > m_closedFrame && t_ready) PlayNextMission();
        if (!m_transitioning) RefreshInteractable(t_ready);
        if (hintRoot == null || m_docked) return;
        if (hintRoot.activeSelf && (!t_ready || Time.unscaledTime >= m_hintUntil)) hintRoot.SetActive(false);
        if (!m_holdingClaim && m_displayed != null && t_ready && GuidanceCoordinator.CanPresent
            && GuideMissionHintHistory.IsPending)
        {
            bool t_hasProgress = false;
            foreach (var t_mission in MissionManager.Definitions)
                if (GuideMissionTrack.IsGuide(t_mission) && MissionManager.IsClaimed(t_mission.Id)) t_hasProgress = true;
            GuideMissionHintHistory.Consume();
            if (!t_hasProgress)
            {
                hintRoot.SetActive(true);
                m_hintUntil = Time.unscaledTime + 4f;
                if (canvasGroup != null)
                {
                    m_transition?.Kill();
                    canvasGroup.alpha = 0f;
                    m_transition = canvasGroup.DOFade(1f, 0.3f).SetUpdate(true);
                }
            }
        }
    }

    void Rebind()
    {
        EnsureInitialized();
        if (m_transitioning) return;
        MissionDefinition t_definition = m_holdingClaim ? m_displayed : GuideMissionTrack.Current;
        SetVisible(MissionManager.IsReady && (t_definition != null || m_holdingClaim)
            && OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission));
        if (t_definition == null)
        {
            if (this.actText != null) this.actText.text = string.Empty;
            if (this.titleText != null) this.titleText.text = string.Empty;
            if (this.progressText != null) this.progressText.text = string.Empty;
            if (this.rewardIcon != null) this.rewardIcon.gameObject.SetActive(false);
            if (this.rewardCountText != null) this.rewardCountText.text = string.Empty;
            m_displayed = null;
            RefreshInteractable(false);
            return;
        }

        m_actLabel = GuideMissionTrack.TryGetAct(t_definition, out GuideMissionTrack.GuideAct t_act)
            ? $"가이드미션 {t_act.Name}" : "가이드미션";
        m_nextMission = GuideMissionTrack.NextOf(t_definition);
        if (this.actText != null) this.actText.text = m_actLabel;
        if (this.titleText != null) this.titleText.text = t_definition.Title ?? string.Empty;

        long t_target = t_definition.Target;
        long t_progress = t_target > 0 ? Math.Min(MissionManager.ProgressOf(t_definition), t_target) : MissionManager.ProgressOf(t_definition);
        bool t_complete = MissionManager.IsComplete(t_definition);
        bool t_same = m_displayed?.Id == t_definition.Id;
        bool t_animate = t_same && !m_holdingClaim && IsPresenting(t_definition.Id);
        if (this.progressText != null) this.progressText.text = t_complete ? "완료" : $"{t_progress} / {t_target}";
        if (t_animate && t_complete && !m_complete) PlayCompleted();
        else if (t_animate && t_progress > m_progress) PlayProgress();
        m_displayed = t_definition;
        m_progress = t_progress;
        m_complete = t_complete;
        RefreshInteractable(m_settled && GuidanceCoordinator.CanNavigateFromLobby(null));
        this.ApplyReward(t_definition);
    }

    void RefreshInteractable(bool _ready)
    {
        var t_state = GuideMissionPreviewState.Of(m_displayed);
        if (m_button != null) m_button.interactable = m_visible && _ready && !m_holdingClaim && t_state.CanExecute;
        RefreshPresentationText();
    }

    void SetVisible(bool _visible)
    {
        bool t_changed = m_visible != _visible;
        m_visible = _visible;
        // 오브젝트는 살아 있어야 잠금 해제·첫 조회 완료를 다른 탭에서도 받을 수 있다.
        if (canvasGroup != null)
        {
            if (!_visible || t_changed) canvasGroup.alpha = _visible ? 1f : 0f;
            canvasGroup.blocksRaycasts = _visible;
            canvasGroup.interactable = _visible;
        }
        if (!_visible && hintRoot != null) hintRoot.SetActive(false);
        RefreshLayout();
    }

    void HandleClick()
    {
        if (!m_visible || m_holdingClaim || !GuidanceCoordinator.CanNavigateFromLobby(null)) return;
        MissionDefinition t_current = GuideMissionTrack.Current;
        var t_state = GuideMissionPreviewState.Of(t_current);
        if (!t_state.CanExecute) return;
        DismissHint();
        if (t_state.Action == GuideMissionPreviewState.EAction.Claim) ClaimAsync(t_current).Forget();
        else GuideMissionNavigator.Go(t_current);
    }

    async UniTaskVoid ClaimAsync(MissionDefinition _mission)
    {
        int t_version = BeginClaim(_mission);
        ClaimMissionResult t_result = null;
        ServerWaitOverlay.Hold(this);
        try { t_result = await MissionCommands.ClaimAsync(_mission.Id); }
        finally
        {
            ServerWaitOverlay.Release(this);
            if (t_result == null && this != null) EndClaim(t_version, false);
        }
        if (t_result != null) MissionPanel.ShowClaimedRewards(new[] { t_result }, _onClosed: () =>
        {
            if (this != null) EndClaim(t_version, true);
        }, _showCardsIndividually: true);
    }

    /// <summary>탭 이동·상세 화면 복귀 뒤 저장을 먼저 확정하고 가이드 진행도를 갱신한다.</summary>
    static async UniTaskVoid RefreshMissionsAsync()
    {
        try
        {
            if (PlayerSaveCloud.HasPendingUpload) await PlayerSaveCloud.FlushAsync();
            await MissionCommands.RefreshAsync();
        }
        catch (Exception t_exception)
        {
            Debug.LogWarning($"[GuideMissionTrackerView] Guide mission refresh failed — {t_exception.GetBaseException().Message}");
        }
    }

    void PlayProgress()
    {
        if (progressText == null) return;
        m_progressTween?.Kill();
        progressText.color = new Color(1f, 0.86f, 0.42f);
        m_progressTween = progressText.DOColor(m_progressColor, 0.25f).SetUpdate(true);
    }

    void PlayCompleted()
    {
        PlayProgress();
        if (rewardIcon == null) return;
        m_rewardTween?.Kill();
        rewardIcon.transform.localScale = Vector3.one;
        m_rewardTween = rewardIcon.transform.DOPunchScale(Vector3.one * 0.12f, 0.35f, 1, 0.5f).SetUpdate(true);
    }

    void PlayNextMission()
    {
        m_transitioning = true;
        if (m_button != null) m_button.interactable = false;
        var t_next = GuideMissionTrack.Current;
        if (t_next != null)
        {
            // 서버가 다음 미션으로 넘어간 수령만 소개한다. 갱신·탭 복귀에는 재생하지 않는다.
            if (t_next.Id == m_displayed?.Id) { FinishNextMission(); return; }
            m_showingNextMission = true;
            var t_gate = OutgameTutorialBridge.EnsureGateForGuidance() ?? OutgameTutorialGateUI.Ensure();
            t_gate.ShowMessageGate(this, null,
                $"<size=70%>다음 가이드 미션</size>\n\n{t_next.Title}", FinishNextMission);
            m_transition?.Kill();
            m_transition = DOVirtual.DelayedCall(2.2f, FinishNextMission).SetUpdate(true);
            return;
        }

        var t_sequence = DOTween.Sequence().SetUpdate(true);
        m_transition?.Kill();
        m_transition = t_sequence;
        if (titleText != null) titleText.text = "가이드 완료";
        if (progressText != null) progressText.text = "완료";
        PlayCompleted();
        t_sequence.AppendInterval(0.8f);
        if (canvasGroup != null) t_sequence.Append(canvasGroup.DOFade(0f, 0.125f));
        else t_sequence.AppendInterval(0.125f);
        t_sequence.OnComplete(() =>
        {
            m_transition = null;
            m_holdingClaim = false;
            m_transitioning = false;
            Rebind();
            PresentationFinished?.Invoke();
        });
    }

    void FinishNextMission()
    {
        if (!m_transitioning) return;
        OutgameTutorialGateUI.Instance?.Clear(this);
        m_transition?.Kill();
        m_transition = null;
        m_showingNextMission = m_holdingClaim = m_rewardsClosed = m_transitioning = false;
        m_displayed = null;
        Rebind();
        PresentationFinished?.Invoke();
    }

    void CancelPresentation()
    {
        EnsureInitialized();
        if (m_showingNextMission) OutgameTutorialGateUI.Instance?.Clear(this);
        m_showingNextMission = false;
        m_claimVersion++;
        m_transition?.Kill();
        m_progressTween?.Kill();
        m_rewardTween?.Kill();
        m_transition = m_progressTween = m_rewardTween = null;
        m_holdingClaim = m_rewardsClosed = m_transitioning = false;
        if (canvasGroup != null) canvasGroup.alpha = 1f;
        if (progressText != null) progressText.color = m_progressColor;
        if (rewardIcon != null) rewardIcon.transform.localScale = Vector3.one;
        if (hintRoot != null) hintRoot.SetActive(false);
    }

    void ApplyReward(MissionDefinition _definition)
    {
        ClaimRewardGain t_gain = MissionRowView.CurrencyAt(_definition, 0);
        ClaimRewardItem t_item = t_gain == null ? MissionRowView.FirstItem(_definition) : null;

        if (this.rewardIcon != null)
        {
            this.rewardIcon.sprite = m_authoredRewardIcon;
            if (t_gain != null && Enum.TryParse(t_gain.Currency, out ECurrencyType t_type))
            {
                Sprite t_sprite = CurrencyLook.IconOf(t_type);
                if (t_sprite != null) this.rewardIcon.sprite = t_sprite;
            }
            this.rewardIcon.gameObject.SetActive(t_gain != null || t_item != null);
        }
        if (this.rewardCountText != null)
        {
            long t_amount = t_gain != null ? t_gain.Amount : t_item != null ? t_item.Amount : 0;
            this.rewardCountText.text = t_amount > 0 ? t_amount.ToString("N0") : string.Empty;
        }
    }

#if UNITY_EDITOR
    /// <summary>계정·탭 진입 로직 없이 프리팹 복사본의 공통 바 배치를 미리 본다.</summary>
    public void PreviewLayoutForEditor(LobbyTabPanel _panel)
    {
        if (Application.isPlaying) throw new InvalidOperationException("에디트 모드 프리팹 복사본에서 실행하세요.");
        EnsureInitialized();
        SetVisible(true);
        RefreshLayout(_panel);
    }

    /// <summary>실계정 없이 비활성 초기화·늦은 수령 콜백·완주 퇴장을 검증한다.</summary>
    public static void ValidatePreviewForEditor()
    {
        if (Application.isPlaying || MissionManager.IsReady || !string.IsNullOrEmpty(FirebaseAuthService.Instance.UserId))
            throw new InvalidOperationException("미션·계정이 초기화되지 않은 에디트 모드에서 실행하세요.");
        var t_asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Assets/Prefabs/UI/LobbyUI/MissionUI/GuideMissionPreview.prefab");
        var t_host = new GameObject("GuidePreviewValidation");
        t_host.SetActive(false);
        GuideMissionTrackerView t_view = null;
        try
        {
            t_view = Instantiate(t_asset, t_host.transform).GetComponent<GuideMissionTrackerView>();
            Sprite t_icon = t_view.rewardIcon.sprite;
            Color t_color = t_view.progressText.color;
            t_view.SetSettled(false);
            Require(t_icon != null && t_view.m_authoredRewardIcon == t_icon
                && t_view.progressText.color == t_color && t_color.a > 0f, "비활성 초기화가 저작값을 보존해야 합니다.");
            Require(t_view.titleText.maxVisibleLines == 2
                && t_view.titleText.overflowMode == TextOverflowModes.Ellipsis, "목표는 두 줄 말줄임이어야 합니다.");
            Require(!GuideMissionPreviewState.Of(null).CanExecute, "정의 미도착 시 실행할 수 없습니다.");
            var t_mission = new MissionDefinition { Id = "guide.test", Period = "guide", Target = 1 };
            int t_old = t_view.BeginClaim(t_mission);
            t_view.SetSettled(false);
            int t_current = t_view.BeginClaim(t_mission);
            t_view.EndClaim(t_old, true);
            Require(t_view.m_holdingClaim && !t_view.m_rewardsClosed, "이전 화면의 콜백이 새 수령을 끝내면 안 됩니다.");
            t_view.EndClaim(t_current, false);
            Require(!t_view.m_holdingClaim && !t_view.m_transitioning, "수령 실패 시 대기를 해제해야 합니다.");
            t_current = t_view.BeginClaim(t_mission);
            t_view.EndClaim(t_current, true);
            Require(t_view.m_holdingClaim && t_view.m_rewardsClosed, "보상 종료 뒤 로비 준비까지 이전 목표를 유지해야 합니다.");
            int t_finished = 0;
            t_view.PresentationFinished += () => t_finished++;
            t_view.PlayNextMission();
            t_view.m_transition.Complete(true);
            Require(t_finished == 1 && !t_view.m_holdingClaim && !t_view.m_transitioning
                && t_view.canvasGroup.alpha == 0f, "완주 후 빈 프리뷰를 다시 표시하면 안 됩니다.");
            t_view.EndClaim(t_current, true);
            Require(t_finished == 1 && !t_view.m_holdingClaim, "중복 종료 콜백은 무시해야 합니다.");
            int t_rewardsClosed = 0;
            MissionPanel.ShowClaimedRewards(Array.Empty<ClaimMissionResult>(), _onClosed: () => t_rewardsClosed++);
            MissionPanel.ShowClaimedRewards(new[] { new ClaimMissionResult() }, _onClosed: () => t_rewardsClosed++);
            Require(t_rewardsClosed == 2, "빈 보상과 무보상 수령도 각각 한 번 종료해야 합니다.");
            Debug.Log("[GuideMissionPreview] lifecycle / stale callback / failed claim / final exit / empty rewards PASS");
        }
        finally
        {
            if (t_view != null) t_view.CancelPresentation();
            DestroyImmediate(t_host);
        }
    }

    static void Require(bool _condition, string _message)
    {
        if (!_condition) throw new InvalidOperationException(_message);
    }
#endif
}
