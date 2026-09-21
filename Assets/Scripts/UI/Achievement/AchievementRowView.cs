using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>공용 미션 아트를 사용하는 업적 단계 표시. 판정은 AchievementManager가 소유한다.</summary>
public sealed class AchievementRowView : MonoBehaviour, IUIInitializable
{
    [SerializeField] TMP_Text titleText;
    [SerializeField] TMP_Text descriptionText;
    [SerializeField] TMP_Text progressText;
    [SerializeField] TMP_Text filledProgressText;
    [SerializeField] RectTransform progressTextFillMask;
    [SerializeField] Image progressFill;
    [SerializeField] MissionRewardStrip rewardStrip;
    [SerializeField] Button claimButton;
    [SerializeField] GameObject claimAlertDot;
    [SerializeField] Image claimBackground;
    [SerializeField] Sprite claimableBackgroundSprite;
    [SerializeField] Sprite incompleteBackgroundSprite;
    [SerializeField] TMP_Text claimLabel;
    [SerializeField] GameObject claimedMark;
    [SerializeField] CanvasGroup claimGroup;
    [SerializeField] float disabledAlpha = 0.5f;
    [SerializeField] Image categoryIcon;
    [SerializeField] Sprite battleIcon;
    [SerializeField] Sprite collectionIcon;
    [SerializeField] Image[] stageMarks;
    [SerializeField] Sprite stageCompleteSprite;
    [SerializeField] Sprite stageIncompleteSprite;

    AchievementDefinition m_definition;
    Action<AchievementDefinition> m_onClaim;
    bool m_initialized;
    readonly Vector3[] m_fillCorners = new Vector3[4];
    readonly Vector3[] m_textCorners = new Vector3[4];

    public void InitializeUI()
    {
        if (m_initialized) return;
        m_initialized = true;
        claimButton.onClick.RemoveAllListeners();
        claimButton.onClick.AddListener(HandleClaim);
    }

    internal void Bind(AchievementDefinition _definition, Action<AchievementDefinition> _onClaim)
    {
        InitializeUI();
        m_definition = _definition;
        m_onClaim = _onClaim;
        titleText.text = _definition.Title;
        if (descriptionText != null) descriptionText.text = _definition.Description;
        rewardStrip.BindCurrencies(_definition.Reward?.Currencies);
        long t_progress = AchievementManager.ProgressOf(_definition);
        bool t_claimed = AchievementManager.IsClaimed(_definition.Id);
        bool t_canClaim = AchievementManager.CanClaim(_definition);
        bool t_complete = t_progress >= _definition.Target;
        if (categoryIcon != null)
            categoryIcon.sprite = _definition.Event == "OpenPack" || _definition.Event == "CompleteAlbum" ? collectionIcon : battleIcon;
        int t_stages = 0;
        int t_claimedStages = 0;
        foreach (var t_stage in AchievementManager.Definitions)
        {
            if (t_stage.GroupId != _definition.GroupId) continue;
            t_stages++;
            if (AchievementManager.IsClaimed(t_stage.Id)) t_claimedStages++;
        }
        for (int i = 0; i < (stageMarks?.Length ?? 0); i++)
        {
            stageMarks[i].gameObject.SetActive(i < t_stages);
            stageMarks[i].sprite = i < t_claimedStages ? stageCompleteSprite : stageIncompleteSprite;
        }
        progressText.text = $"{Math.Min(t_progress, _definition.Target):N0} / {_definition.Target:N0}";
        filledProgressText.text = progressText.text;
        float t_ratio = _definition.Target > 0 ? Mathf.Clamp01((float)t_progress / _definition.Target) : 0f;
        progressFill.rectTransform.anchorMax = new Vector2(t_ratio, 1f);
        progressFill.gameObject.SetActive(t_ratio > 0f);
        claimButton.interactable = t_canClaim;
        claimAlertDot.SetActive(t_canClaim);
        if (claimedMark != null) claimedMark.SetActive(t_claimed);
        claimBackground.enabled = !t_claimed;
        claimBackground.sprite = t_complete ? claimableBackgroundSprite : incompleteBackgroundSprite;
        claimLabel.text = t_claimed ? "수령 완료" : t_complete ? "보상 받기" : "진행 중";
        if (claimGroup != null) claimGroup.alpha = t_canClaim ? 1f : disabledAlpha;
    }

    void HandleClaim()
    {
        if (m_definition != null && AchievementManager.CanClaim(m_definition)) m_onClaim?.Invoke(m_definition);
    }

    void LateUpdate()
    {
        // 미션 행과 같은 이중 색 진행 숫자. 팝업 확대 중에도 글자 위치는 고정한다.
        RectTransform t_source = progressText.rectTransform;
        Transform t_parent = progressTextFillMask.parent;
        progressFill.rectTransform.GetWorldCorners(m_fillCorners);
        t_source.GetWorldCorners(m_textCorners);
        Vector3 t_left = t_parent.InverseTransformPoint(m_fillCorners[0]);
        Vector3 t_right = t_parent.InverseTransformPoint(m_fillCorners[2]);
        Vector3 t_bottom = t_parent.InverseTransformPoint(m_textCorners[0]);
        Vector3 t_top = t_parent.InverseTransformPoint(m_textCorners[2]);
        float t_width = Mathf.Max(0f, t_right.x - t_left.x);
        bool t_visible = progressFill.gameObject.activeSelf && t_width > 0f;
        progressTextFillMask.gameObject.SetActive(t_visible);
        if (!t_visible) return;
        progressTextFillMask.localPosition = new Vector3(t_left.x, t_bottom.y, t_bottom.z);
        progressTextFillMask.sizeDelta = new Vector2(t_width, Mathf.Max(0f, t_top.y - t_bottom.y));
        RectTransform t_filledText = filledProgressText.rectTransform;
        t_filledText.pivot = t_source.pivot;
        t_filledText.sizeDelta = t_source.rect.size;
        t_filledText.localScale = t_source.localScale;
        t_filledText.SetPositionAndRotation(t_source.position, t_source.rotation);
    }
}
