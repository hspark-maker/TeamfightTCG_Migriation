using System;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 매치 탭의 가이드 미션 트래커 카드. 현재 미션 하나의 제목·진행도·보상과 액션(이동/받기)만 그린다.
///
/// <para>표시 데이터는 전부 <see cref="GuideMissionTrack"/>·<see cref="MissionManager"/> 에서 읽고 낙관 갱신을 하지 않는다.
/// 카드 GameObject 의 켜고 끄기·잠금 룩은 <see cref="LobbyMatchTabPanel"/> 이 그대로 소유한다(가이드가 끝나면 카드째 사라진다).
/// 카드 몸통(기존 Button)은 전체 목록을 열고, 알약 버튼만 여기서 다룬다.</para>
/// </summary>
public sealed class GuideMissionTrackerView : MonoBehaviour
{
    [Header("문구")]
    [SerializeField] TMP_Text actText;
    [SerializeField] TMP_Text titleText;
    [SerializeField] TMP_Text progressText;

    [Tooltip("달성 상태에서 다음 미션 이름·보상을 한 줄로 미리 보여 주는 문구. 비워 두면 그리지 않는다.")]
    [SerializeField] TMP_Text nextText;

    [Header("게이지")]
    [Tooltip("Sliced 채움 이미지. 부모가 최대 영역, anchorMax.x 로 채운다(MissionRowView 와 같은 규약).")]
    [SerializeField] Image progressFill;

    [Header("보상")]
    [SerializeField] Image rewardIcon;
    [SerializeField] TMP_Text rewardCountText;

    [Header("액션 알약")]
    [SerializeField] Button actionButton;
    [SerializeField] TMP_Text actionLabel;
    [SerializeField] Image actionBackground;
    [Tooltip("진행 중(이동) 알약 배경.")]
    [SerializeField] Sprite goSprite;
    [Tooltip("달성(받기) 알약 배경.")]
    [SerializeField] Sprite claimSprite;
    [SerializeField] CanvasGroup actionGroup;
    [Range(0f, 1f)] [SerializeField] float disabledAlpha = 0.5f;

    const string GO_LABEL = "이동";
    const string CLAIM_LABEL = "받기";
    const string WAIT_LABEL = "확인 중";

    void OnEnable()
    {
        if (this.actionButton != null)
        {
            this.actionButton.onClick.RemoveAllListeners();
            this.actionButton.onClick.AddListener(this.HandleAction);
        }
        MissionManager.OnChanged += this.Rebind;
        this.Rebind();
    }

    void OnDisable()
    {
        MissionManager.OnChanged -= this.Rebind;
    }

    void Rebind()
    {
        MissionDefinition t_definition = GuideMissionTrack.Current;
        if (t_definition == null) return;   // 카드 자체는 패널이 끈다

        if (this.actText != null)
            this.actText.text = GuideMissionTrack.TryGetAct(t_definition, out GuideMissionTrack.GuideAct t_act) ? t_act.Label : string.Empty;
        if (this.titleText != null) this.titleText.text = t_definition.Title ?? string.Empty;

        long t_target = t_definition.Target;
        long t_progress = t_target > 0 ? Math.Min(MissionManager.ProgressOf(t_definition), t_target) : MissionManager.ProgressOf(t_definition);
        bool t_complete = MissionManager.IsComplete(t_definition);
        bool t_inFlight = MissionCommands.IsInFlight(t_definition.Id);
        bool t_canGo = !t_complete && GuideMissionNavigator.CanGo(t_definition);

        if (this.progressText != null) this.progressText.text = $"{t_progress} / {t_target}";
        if (this.progressFill != null)
        {
            float t_ratio = t_target > 0 ? Mathf.Clamp01((float)t_progress / t_target) : (t_complete ? 1f : 0f);
            this.progressFill.rectTransform.anchorMax = new Vector2(t_ratio, 1f);
            this.progressFill.gameObject.SetActive(t_ratio > 0f);
        }

        this.ApplyReward(t_definition);
        this.ApplyNext(t_complete ? GuideMissionTrack.NextOf(t_definition) : null);

        if (this.actionLabel != null) this.actionLabel.text = t_inFlight ? WAIT_LABEL : t_complete ? CLAIM_LABEL : GO_LABEL;
        if (this.actionBackground != null)
        {
            Sprite t_sprite = t_complete ? this.claimSprite : this.goSprite;
            if (t_sprite != null) this.actionBackground.sprite = t_sprite;
        }
        bool t_interactable = !t_inFlight && (t_complete ? MissionManager.CanClaim(t_definition) : t_canGo);
        if (this.actionButton != null) this.actionButton.interactable = t_interactable;
        if (this.actionGroup != null) this.actionGroup.alpha = t_interactable ? 1f : this.disabledAlpha;
    }

    void ApplyReward(MissionDefinition _definition)
    {
        ClaimRewardGain t_gain = MissionRowView.CurrencyAt(_definition, 0);
        ClaimRewardItem t_item = t_gain == null ? MissionRowView.FirstItem(_definition) : null;

        if (this.rewardIcon != null)
        {
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
            this.rewardCountText.text = t_amount > 0 ? "x" + t_amount : string.Empty;
        }
    }

    void ApplyNext(MissionDefinition _next)
    {
        if (this.nextText == null) return;
        if (_next == null)
        {
            this.nextText.gameObject.SetActive(false);
            return;
        }
        ClaimRewardGain t_gain = MissionRowView.CurrencyAt(_next, 0);
        string t_reward = t_gain != null && Enum.TryParse(t_gain.Currency, out ECurrencyType t_type)
            ? $" · {CurrencyLook.NameOf(t_type)} {t_gain.Amount}"
            : string.Empty;
        this.nextText.text = $"다음: {_next.Title}{t_reward}";
        this.nextText.gameObject.SetActive(true);
    }

    void HandleAction()
    {
        // 알약은 패널의 잠금 배선 밖이라 여기서 다시 막는다.
        if (!OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission)) return;
        MissionDefinition t_definition = GuideMissionTrack.Current;
        if (t_definition == null) return;

        if (MissionManager.CanClaim(t_definition)) this.ClaimAsync(t_definition).Forget();
        else if (!MissionManager.IsComplete(t_definition)) GuideMissionNavigator.Go(t_definition);
    }

    async UniTaskVoid ClaimAsync(MissionDefinition _definition)
    {
        // GuideMissionPanel.ClaimAsync 와 같은 계약 — 대기 오버레이는 팝업보다 먼저 걷는다.
        string t_title = GuideMissionTrack.IsActFinale(_definition) && GuideMissionTrack.TryGetAct(_definition, out GuideMissionTrack.GuideAct t_act)
            ? $"{t_act.Number}막 완료!"
            : "가이드 미션 보상";
        ClaimMissionResult t_result = null;
        ServerWaitOverlay.Hold(this);
        try
        {
            t_result = await MissionCommands.ClaimAsync(_definition.Id);
        }
        finally
        {
            ServerWaitOverlay.Release(this);
        }
        if (t_result != null) MissionPanel.ShowClaimedRewards(new[] { t_result }, t_title);
    }
}
