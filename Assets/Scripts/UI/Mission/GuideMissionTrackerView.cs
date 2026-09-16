using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>현재 가이드 미션의 막·제목·진행도·보상을 표시한다. 클릭과 잠금은 LobbyMatchTabPanel이 담당한다.</summary>
public sealed class GuideMissionTrackerView : MonoBehaviour
{
    [Header("문구")]
    [SerializeField] TMP_Text actText;
    [SerializeField] TMP_Text titleText;
    [SerializeField] TMP_Text progressText;
    [SerializeField] TMP_Text actionText;
    [SerializeField] GameObject hintRoot;
    [SerializeField] CanvasGroup canvasGroup;

    [Header("보상")]
    [SerializeField] Image rewardIcon;
    [SerializeField] TMP_Text rewardCountText;

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
    bool m_claimFinale;
    string m_claimAct;
    int m_claimVersion;
    int m_closedFrame;
    float m_hintUntil;
    float m_nextRefresh;
    Tween m_transition;
    Tween m_progressTween;
    Tween m_rewardTween;
    Color m_progressColor;
    Color m_actionColor;
    bool m_initialized;

    public event Action PresentationFinished;
    internal bool IsHoldingClaim => m_holdingClaim || m_transitioning;

    internal static bool IsPresenting(string _missionId) => s_visible != null
        && s_visible.isActiveAndEnabled && s_visible.m_settled
        && s_visible.m_displayed?.Id == _missionId
        && (s_visible.canvasGroup == null || s_visible.canvasGroup.alpha > 0.99f)
        && GuidanceCoordinator.CanNavigateFromMatchTab(null);

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
        m_claimFinale = GuideMissionTrack.IsActFinale(_mission);
        m_claimAct = GuideMissionTrack.TryGetAct(_mission, out var t_act) ? $"{t_act.Number}막 완료" : "미션 완료";
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
        if (titleText != null) titleText.maxVisibleLines = 2;
        if (progressText != null) m_progressColor = progressText.color;
        if (actionText != null) m_actionColor = actionText.color;
    }

    void OnEnable()
    {
        MissionManager.OnChanged += this.Rebind;
        OutgameTutorialRunner.OnGuidedChanged += this.Rebind;
        s_visible = this;
        this.Rebind();
    }

    void OnDisable()
    {
        MissionManager.OnChanged -= this.Rebind;
        OutgameTutorialRunner.OnGuidedChanged -= this.Rebind;
        CancelPresentation();
        m_displayed = null;
        m_settled = false;
        if (s_visible == this) s_visible = null;
    }

    void Update()
    {
        if (!m_settled || Time.unscaledTime < m_nextRefresh) return;
        m_nextRefresh = Time.unscaledTime + 0.1f;
        bool t_ready = GuidanceCoordinator.CanNavigateFromMatchTab(null);
        if (m_holdingClaim && m_rewardsClosed && !m_transitioning
            && Time.frameCount > m_closedFrame && t_ready) PlayNextMission();
        if (!m_transitioning) RefreshAction(t_ready);
        if (hintRoot == null) return;
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
        if (t_definition == null)
        {
            if (this.actText != null) this.actText.text = string.Empty;
            if (this.titleText != null) this.titleText.text = string.Empty;
            if (this.progressText != null) this.progressText.text = string.Empty;
            if (this.actionText != null) this.actionText.text = string.Empty;
            if (this.rewardIcon != null) this.rewardIcon.gameObject.SetActive(false);
            if (this.rewardCountText != null) this.rewardCountText.text = string.Empty;
            m_displayed = null;
            return;
        }

        if (this.actText != null)
            this.actText.text = GuideMissionTrack.TryGetAct(t_definition, out GuideMissionTrack.GuideAct t_act) ? t_act.Label : string.Empty;
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
        if (actionText != null) actionText.color = t_complete ? new Color(1f, 0.86f, 0.42f) : m_actionColor;
        RefreshAction(m_settled && GuidanceCoordinator.CanNavigateFromMatchTab(null));
        this.ApplyReward(t_definition);
    }

    void RefreshAction(bool _ready)
    {
        var t_state = GuideMissionPreviewState.Of(m_displayed);
        if (actionText != null) actionText.text = m_holdingClaim
            ? MissionCommands.IsInFlight(m_displayed.Id) ? "수령 중…" : "보상 확인 중…"
            : t_state.Label;
        if (m_button != null) m_button.interactable = _ready && !m_holdingClaim && t_state.CanExecute;
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
        bool t_finished = GuideMissionTrack.Current == null;
        var t_sequence = DOTween.Sequence().SetUpdate(true);
        m_transition?.Kill();
        m_transition = t_sequence;
        if (m_claimFinale || t_finished)
        {
            if (titleText != null) titleText.text = t_finished ? "가이드 완료" : m_claimAct;
            if (progressText != null) progressText.text = "완료";
            if (actionText != null) actionText.text = string.Empty;
            PlayCompleted();
            t_sequence.AppendInterval(0.8f);
        }
        if (canvasGroup != null) t_sequence.Append(canvasGroup.DOFade(0f, 0.125f));
        else t_sequence.AppendInterval(0.125f);
        if (!t_finished) t_sequence.AppendCallback(() =>
        {
            m_holdingClaim = false;
            m_transitioning = false;
            m_displayed = null;
            Rebind();
            m_transitioning = true;
            if (m_button != null) m_button.interactable = false;
        });
        if (!t_finished)
        {
            if (canvasGroup != null) t_sequence.Append(canvasGroup.DOFade(1f, 0.125f));
            else t_sequence.AppendInterval(0.125f);
        }
        t_sequence.OnComplete(() =>
        {
            m_transition = null;
            m_holdingClaim = false;
            m_transitioning = false;
            Rebind();
            PresentationFinished?.Invoke();
        });
    }

    void CancelPresentation()
    {
        EnsureInitialized();
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
