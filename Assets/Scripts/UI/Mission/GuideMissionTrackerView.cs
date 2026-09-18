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
    [SerializeField] Image progressFill;
    [SerializeField] GameObject hintRoot;
    [SerializeField] CanvasGroup canvasGroup;

    [Header("보상")]
    [SerializeField] Image rewardIcon;
    [SerializeField] TMP_Text rewardCountText;

    [Header("오른쪽 접이식 미션")]
    [SerializeField] Button missionButton;
    [SerializeField] Button drawerHandle;
    [SerializeField] TMP_Text drawerArrow;
    [SerializeField] RectTransform drawerViewport;
    [SerializeField] RectTransform drawerContent;
    [SerializeField] CanvasGroup drawerContentGroup;
    [SerializeField, Min(0f)] float drawerSlideSeconds = 0.22f;

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
    RectTransform m_rect;
    string m_actLabel;
    bool m_expanded;
    float m_drawerReveal;
    Tween m_drawerTween;

    public event Action PresentationFinished;
    internal bool IsHoldingClaim => m_holdingClaim || m_transitioning;
    internal static GuideMissionTrackerView Visible => s_visible;
    internal static bool HasPendingPresentation => s_visible != null && s_visible.IsHoldingClaim;
    internal static bool IsShowingNextMission => s_visible != null && s_visible.m_showingNextMission;

    internal static bool IsPresenting(string _missionId) => s_visible != null
        && s_visible.isActiveAndEnabled && s_visible.m_settled && s_visible.m_visible
        && s_visible.m_expanded && s_visible.m_drawerReveal > 0.99f
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
        m_button = missionButton;
        if (m_button != null) m_button.onClick.AddListener(HandleClick);
        if (drawerHandle != null) drawerHandle.onClick.AddListener(ToggleDrawer);
        m_rect = transform as RectTransform;
        if (titleText != null) titleText.maxVisibleLines = 2;
        if (progressText != null) m_progressColor = progressText.color;
        SetDrawerExpanded(false, false);
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
        if (drawerHandle != null) drawerHandle.onClick.RemoveListener(ToggleDrawer);
        m_drawerTween?.Kill();
    }

    void LateUpdate() => RefreshLayout();

    void RefreshLayout()
    {
        if (m_rect == null || drawerViewport == null || drawerContent == null) return;
        // 안전 영역 오른쪽 중앙에 고정한다. 콘텐츠 탭의 RectTransform은 변경하지 않는다.
        m_rect.anchorMin = m_rect.anchorMax = m_rect.pivot = new Vector2(1f, 0.5f);
        m_rect.anchoredPosition = new Vector2(-12f, 0f);
        if (m_rect.parent is RectTransform t_parent)
        {
            float t_scale = Mathf.Min(1.25f, Mathf.Max(0f, t_parent.rect.width - 24f) / m_rect.sizeDelta.x);
            m_rect.localScale = Vector3.one * t_scale;
        }
        drawerViewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, drawerContent.rect.width * m_drawerReveal);
        if (drawerContentGroup != null)
        {
            bool t_open = m_expanded && m_drawerReveal > 0.99f;
            drawerContentGroup.interactable = t_open;
            drawerContentGroup.blocksRaycasts = t_open;
            drawerContentGroup.alpha = m_drawerReveal > 0f ? 1f : 0f;
        }
    }

    void ToggleDrawer()
    {
        if (!m_visible || !m_settled || IsHoldingClaim || !GuidanceCoordinator.CanNavigateFromLobby(null)) return;
        SetDrawerExpanded(!m_expanded, true);
    }

    void SetDrawerExpanded(bool _expanded, bool _animate)
    {
        m_drawerTween?.Kill();
        m_drawerTween = null;
        m_expanded = _expanded;
        if (drawerArrow != null) drawerArrow.text = _expanded ? ">" : "<";
        if (!_expanded && hintRoot != null) hintRoot.SetActive(false);
        float t_target = _expanded ? 1f : 0f;
        if (_animate && drawerSlideSeconds > 0f)
            m_drawerTween = DOTween.To(() => m_drawerReveal, _value =>
            {
                m_drawerReveal = _value;
                RefreshLayout();
            }, t_target, drawerSlideSeconds).SetEase(Ease.OutCubic).SetUpdate(true)
                .OnComplete(() => m_drawerTween = null);
        else m_drawerReveal = t_target;
        RefreshLayout();
    }

    void RefreshPresentationText()
    {
        if (m_displayed == null || m_transitioning) return;
        if (titleText != null) titleText.text = m_displayed.Title;
        if (actText != null) actText.text = m_actLabel;
        ApplyProgress(m_progress, m_displayed.Target, m_complete);
    }

    void ApplyProgress(long _progress, long _target, bool _complete)
    {
        if (progressText != null) progressText.text = _complete ? "완료" : $"{_progress} / {_target}";
        if (progressFill == null) return;
        float t_ratio = _complete ? 1f : _target > 0 ? Mathf.Clamp01((float)_progress / _target) : 0f;
        progressFill.rectTransform.anchorMax = new Vector2(t_ratio, 1f);
        progressFill.gameObject.SetActive(t_ratio > 0f);
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
        if (hintRoot == null || !m_expanded || m_drawerReveal < 0.99f) return;
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
            if (this.progressFill != null) this.progressFill.gameObject.SetActive(false);
            if (this.rewardIcon != null) this.rewardIcon.gameObject.SetActive(false);
            if (this.rewardCountText != null) this.rewardCountText.text = string.Empty;
            m_displayed = null;
            RefreshInteractable(false);
            return;
        }

        m_actLabel = GuideMissionTrack.TryGetAct(t_definition, out GuideMissionTrack.GuideAct t_act)
            ? $"가이드미션 {t_act.Name}" : "가이드미션";
        if (this.actText != null) this.actText.text = m_actLabel;
        if (this.titleText != null) this.titleText.text = t_definition.Title ?? string.Empty;

        long t_target = t_definition.Target;
        long t_progress = t_target > 0 ? Math.Min(MissionManager.ProgressOf(t_definition), t_target) : MissionManager.ProgressOf(t_definition);
        bool t_complete = MissionManager.IsComplete(t_definition);
        bool t_same = m_displayed?.Id == t_definition.Id;
        bool t_animate = t_same && !m_holdingClaim && IsPresenting(t_definition.Id);
        ApplyProgress(t_progress, t_target, t_complete);
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
        if (m_button != null) m_button.interactable = m_visible && _ready && m_expanded && !IsHoldingClaim && t_state.CanExecute;
        if (drawerHandle != null) drawerHandle.interactable = m_visible && _ready && !IsHoldingClaim;
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
        if (!_visible) SetDrawerExpanded(false, false);
        RefreshLayout();
    }

    void HandleClick()
    {
        if (!m_visible || !m_expanded || m_drawerReveal < 0.99f || IsHoldingClaim || !GuidanceCoordinator.CanNavigateFromLobby(null)) return;
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
        ApplyProgress(1, 1, true);
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
    /// <summary>계정·탭 진입 로직 없이 프리팹 복사본의 접이식 미션을 미리 본다.</summary>
    public void PreviewLayoutForEditor(LobbyTabPanel _panel, bool _expanded = false)
    {
        if (Application.isPlaying) throw new InvalidOperationException("에디트 모드 프리팹 복사본에서 실행하세요.");
        EnsureInitialized();
        SetVisible(true);
        SetDrawerExpanded(_expanded, false);
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
            Require(t_view.progressFill != null, "진행도 채움 막대가 배선되어야 합니다.");
            t_view.ApplyProgress(0, 3, false);
            Require(!t_view.progressFill.gameObject.activeSelf, "0 진행도에서는 배경만 보여야 합니다.");
            t_view.ApplyProgress(1, 3, false);
            Require(t_view.progressFill.gameObject.activeSelf
                && Mathf.Approximately(t_view.progressFill.rectTransform.anchorMax.x, 1f / 3f), "1/3 진행도를 채워야 합니다.");
            t_view.ApplyProgress(3, 3, true);
            Require(t_view.progressFill.rectTransform.anchorMax.x == 1f && t_view.progressText.text == "완료", "완료 게이지는 가득 차야 합니다.");
            Require(t_view.m_button != null && t_view.drawerHandle != null
                && t_view.drawerArrow != null && t_view.drawerContentGroup != null, "미션과 접기 버튼의 배선이 필요합니다.");
            Require(!t_view.m_expanded && t_view.drawerViewport.rect.width == 0f
                && !t_view.drawerContentGroup.blocksRaycasts && t_view.drawerArrow.text == "<", "처음에는 화살표만 표시해야 합니다.");
            t_view.SetDrawerExpanded(true, true);
            t_view.m_drawerTween.Complete(true);
            Require(t_view.drawerViewport.rect.width == t_view.drawerContent.rect.width
                && t_view.drawerContentGroup.blocksRaycasts && t_view.drawerArrow.text == ">", "펼친 카드만 입력을 받아야 합니다.");
            t_view.SetDrawerExpanded(false, true);
            t_view.m_drawerTween.Goto(t_view.drawerSlideSeconds * 0.5f);
            Require(!t_view.drawerContentGroup.blocksRaycasts, "접히는 도중에는 미션 입력을 막아야 합니다.");
            t_view.SetDrawerExpanded(true, true);
            t_view.m_drawerTween.Complete(true);
            Require(t_view.m_drawerReveal == 1f, "연속 전환은 마지막 요청으로 끝나야 합니다.");
            t_view.SetVisible(false);
            Require(!t_view.m_expanded && t_view.m_drawerTween == null
                && t_view.drawerViewport.rect.width == 0f, "숨김 시 접힘 상태와 애니메이션을 정리해야 합니다.");
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
            Debug.Log("[GuideMissionPreview] drawer / rapid toggle / lifecycle / stale callback / failed claim / final exit / empty rewards PASS");
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
