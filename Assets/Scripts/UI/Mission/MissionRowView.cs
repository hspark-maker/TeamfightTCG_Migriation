using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 미션 한 줄. 제목·설명·진행 게이지·보상·수령 버튼을 그린다.
///
/// <para><b>정의를 스스로 들고 있지 않는다.</b> 표시값은 전부 서버가 준 <see cref="MissionDefinition"/>
/// 에서 읽고, 완료 여부는 <see cref="MissionManager"/> 에 묻는다 — 행이 자기 판정을 갖는 순간
/// 화면에 보이는 조건과 서버가 거절하는 조건이 갈린다.</para>
/// </summary>
public class MissionRowView : MonoBehaviour
{
    [SerializeField] TMP_Text titleText;
    [SerializeField] TMP_Text descriptionText;

    [Tooltip("진행도 표시. 비워 두면 그리지 않는다.")]
    [SerializeField] TMP_Text progressText;

    [Tooltip("채워진 구간에 겹쳐 그릴 숫자. progressText와 같은 서체·배치, 다른 색으로 저작한다.")]
    [SerializeField] TMP_Text filledProgressText;

    [Tooltip("filledProgressText의 RectMask2D 부모. progressText와 같은 부모 아래에 둔다.")]
    [SerializeField] RectTransform progressTextFillMask;

    [Tooltip("Sliced 채움 이미지. 부모는 게이지 최대 영역, 이미지는 부모 전체에 Stretch로 배선한다. 너비로 진행도를 표시한다.")]
    [SerializeField] Image progressFill;

    [Tooltip("보상 문구. 재화·아이템 목록을 한 줄로 적는다. 비워 두면 그리지 않는다.")]
    [SerializeField] TMP_Text rewardText;

    [Tooltip("보상 아이콘. 첫 재화 보상이면 CurrencyLook 아이콘으로 갈고, 아이템 보상이면 저작 그림을 그대로 둔다\n" +
             "(팩·카드 아이콘 조회 축이 아직 없다). 보상이 통째로 없으면 꺼진다. 패스 경험치는 표시 축이 아니다.")]
    [SerializeField] Image rewardIcon;

    [Tooltip("보상 개수(x100 꼴). 비워 두면 그리지 않는다.")]
    [SerializeField] TMP_Text rewardCountText;

    [Tooltip("재화 보상이 둘이면 기존 보상 칸 안에 두 아이콘과 수량을 함께 표시한다.")]
    [SerializeField] GameObject dualRewardRoot;
    [SerializeField] Image dualRewardIcon;
    [SerializeField] TMP_Text dualRewardCountText;
    [SerializeField] Image secondRewardIcon;
    [SerializeField] TMP_Text secondRewardCountText;

    [SerializeField] Button claimButton;
    [SerializeField] GameObject claimAlertDot;

    [Header("보상 수령 버튼 배경")]
    [SerializeField] Image claimBackground;
    [Tooltip("보상 획득 가능 상태의 배경.")]
    [SerializeField] Sprite claimableBackgroundSprite;
    [Tooltip("보상 획득 완료 상태의 배경.")]
    [SerializeField] Sprite claimedBackgroundSprite;
    [Tooltip("미완료 또는 아직 해금되지 않은 미션의 배경.")]
    [SerializeField] Sprite incompleteBackgroundSprite;

    [Tooltip("수령 버튼 라벨. 상태에 따라 문구가 바뀐다.")]
    [SerializeField] TMP_Text claimLabel;

    [Tooltip("이미 받은 줄에 켜는 표식(체크 등). 비워 두면 그리지 않는다.")]
    [SerializeField] GameObject claimedMark;

    [Tooltip("수령 불가일 때 버튼에 씌울 알파.")]
    [Range(0f, 1f)] [SerializeField] float disabledAlpha = 0.5f;

    [SerializeField] CanvasGroup claimGroup;

    MissionDefinition m_definition;
    System.Action<string> m_onClaim;

    readonly Vector3[] m_fillCorners = new Vector3[4];
    readonly Vector3[] m_textCorners = new Vector3[4];

    // 매 프레임 문자열을 새로 만들지 않으려고 재사용한다. 행이 8개까지 늘 수 있고
    // OnChanged 마다 전 행이 다시 그려지므로 여기서 GC 를 만들면 그대로 누적된다.
    static readonly StringBuilder s_text = new StringBuilder(64);

    /// <summary>이 줄이 그릴 미션을 정한다. 수령 콜백은 미션 id 를 그대로 넘긴다.</summary>
    internal void Bind(MissionDefinition _definition, System.Action<string> _onClaim)
    {
        this.m_definition = _definition;
        this.m_onClaim = _onClaim;

        if (this.claimButton != null)
        {
            // 재바인딩마다 중복 등록 방지(RankRewardPanel 의 버튼 규약과 같다).
            this.claimButton.onClick.RemoveAllListeners();
            this.claimButton.onClick.AddListener(this.HandleClaim);
        }

        if (this.titleText != null) this.titleText.text = (_definition?.Period == "guide" ? "가이드 · " : "") + (_definition?.Title ?? string.Empty);
        if (this.descriptionText != null) this.descriptionText.text = _definition?.Description ?? string.Empty;
        if (this.rewardText != null) this.rewardText.text = BuildRewardText(_definition);
        this.ApplyRewardVisual(_definition);

        this.Refresh();
    }

    /// <summary>진행도·수령 상태만 다시 그린다. 정의가 그대로면 Bind 를 다시 부르지 않는다.</summary>
    internal void Refresh()
    {
        if (this.m_definition == null) return;

        long t_progress = MissionManager.ProgressOf(this.m_definition);
        long t_target = this.m_definition.Target;
        bool t_complete = MissionManager.IsComplete(this.m_definition);
        bool t_claimed = MissionManager.IsClaimed(this.m_definition.Id);
        bool t_canClaim = MissionManager.CanClaim(this.m_definition);
        if (this.claimAlertDot != null) this.claimAlertDot.SetActive(t_canClaim);
        // 요청 중 입력 잠금은 표시 상태와 분리해 배경이 미완료로 깜빡이지 않게 한다.
        bool t_rewardAvailable = t_complete && MissionManager.IsGuideUnlocked(this.m_definition);

        if (this.progressText != null)
        {
            // 목표를 넘겨 쌓여도 표시는 목표에서 멈춘다 — 서버도 수령을 한 번만 허용한다.
            long t_shown = t_target > 0 ? System.Math.Min(t_progress, t_target) : t_progress;
            s_text.Clear();
            s_text.Append(t_shown).Append(" / ").Append(t_target);
            this.progressText.text = s_text.ToString();
            if (this.filledProgressText != null) this.filledProgressText.text = this.progressText.text;
        }

        if (this.progressFill != null)
        {
            float t_ratio = t_target > 0
                ? Mathf.Clamp01((float)t_progress / t_target)
                : (t_complete ? 1f : 0f);
            // Sliced의 테두리 두께를 유지하며, 고정된 최대 영역 안에서 너비만 바꾼다.
            this.progressFill.rectTransform.anchorMax = new Vector2(t_ratio, 1f);
            this.progressFill.gameObject.SetActive(t_ratio > 0f);
        }

        if (this.claimedMark != null) this.claimedMark.SetActive(t_claimed);

        if (this.claimBackground != null)
        {
            Sprite t_background = t_claimed ? this.claimedBackgroundSprite
                : t_rewardAvailable ? this.claimableBackgroundSprite : this.incompleteBackgroundSprite;
            if (t_background != null) this.claimBackground.sprite = t_background;
        }

        if (this.claimLabel != null)
            this.claimLabel.text = t_claimed ? "완료" : t_rewardAvailable ? "받기" : "진행 중";

        // 버튼은 끄지 않고 상호작용만 막는다 — 꺼 버리면 레이아웃이 흔들리고 "받은 줄"이 사라진 것처럼 보인다.
        if (this.claimButton != null) this.claimButton.interactable = t_canClaim;
        if (this.claimGroup != null) this.claimGroup.alpha = t_canClaim ? 1f : this.disabledAlpha;
    }

    void LateUpdate()
    {
        if (this.progressText == null || this.filledProgressText == null ||
            this.progressFill == null || this.progressTextFillMask == null) return;

        RectTransform t_source = this.progressText.rectTransform;
        RectTransform t_mask = this.progressTextFillMask;
        Transform t_parent = t_mask.parent;
        this.progressFill.rectTransform.GetWorldCorners(this.m_fillCorners);
        t_source.GetWorldCorners(this.m_textCorners);

        // 글자 위치는 고정하고 채움 경계까지만 다른 색을 노출한다.
        // 부모 좌표계에서 계산하므로 팝업 확대·해상도 변경에도 두 글자가 포개진다.
        Vector3 t_fillLeft = t_parent.InverseTransformPoint(this.m_fillCorners[0]);
        Vector3 t_fillRight = t_parent.InverseTransformPoint(this.m_fillCorners[2]);
        Vector3 t_textBottom = t_parent.InverseTransformPoint(this.m_textCorners[0]);
        Vector3 t_textTop = t_parent.InverseTransformPoint(this.m_textCorners[2]);
        float t_width = Mathf.Max(0f, t_fillRight.x - t_fillLeft.x);
        bool t_visible = this.progressFill.gameObject.activeSelf && t_width > 0f;
        t_mask.gameObject.SetActive(t_visible);
        if (!t_visible) return;

        t_mask.localPosition = new Vector3(t_fillLeft.x, t_textBottom.y, t_textBottom.z);
        t_mask.sizeDelta = new Vector2(t_width, Mathf.Max(0f, t_textTop.y - t_textBottom.y));

        RectTransform t_filledText = this.filledProgressText.rectTransform;
        t_filledText.pivot = t_source.pivot;
        t_filledText.sizeDelta = t_source.rect.size;
        t_filledText.localScale = t_source.localScale;
        t_filledText.SetPositionAndRotation(t_source.position, t_source.rotation);
    }

    void HandleClaim()
    {
        if (this.m_definition == null) return;

        // 여기서도 막는다. 버튼 비활성만으로는 부족하다 — 왕복 중 통지로 다시 그려지기 전에
        // 두 번 눌리면 같은 미션이 두 번 나간다(서버 영수증이 막지만 화면이 먼저 막는 게 맞다).
        if (!MissionManager.CanClaim(this.m_definition)) return;

        this.m_onClaim?.Invoke(this.m_definition.Id);
    }

    /// <summary>대표 보상 하나를 아이콘·개수로 그린다. 재화면 아이콘을 갈아끼우고,
    /// 아이템이면 저작 그림을 신뢰한다. 패스 경험치는 여기서도 문구에서도 그리지 않는다.</summary>
    void ApplyRewardVisual(MissionDefinition _definition)
    {
        ClaimRewardGain t_gain = CurrencyAt(_definition, 0);
        ClaimRewardGain t_secondGain = CurrencyAt(_definition, 1);
        ClaimRewardItem t_item = t_gain == null ? FirstItem(_definition) : null;
        bool t_dual = t_secondGain != null && this.dualRewardRoot != null;

        if (this.dualRewardRoot != null) this.dualRewardRoot.SetActive(t_dual);
        if (t_dual)
        {
            ApplyCurrencyVisual(this.dualRewardIcon, this.dualRewardCountText, t_gain);
            ApplyCurrencyVisual(this.secondRewardIcon, this.secondRewardCountText, t_secondGain);
        }

        if (this.rewardIcon != null)
        {
            if (t_gain != null && System.Enum.TryParse(t_gain.Currency, out ECurrencyType t_type))
            {
                Sprite t_sprite = CurrencyLook.IconOf(t_type);
                if (t_sprite != null) this.rewardIcon.sprite = t_sprite;
            }
            this.rewardIcon.gameObject.SetActive(!t_dual && (t_gain != null || t_item != null));
        }

        if (this.rewardCountText != null)
        {
            this.rewardCountText.gameObject.SetActive(!t_dual);
            long t_amount = t_gain != null ? t_gain.Amount : t_item != null ? t_item.Amount : 0;
            this.rewardCountText.text = t_amount > 0 ? "x" + t_amount : string.Empty;
        }
    }

    static void ApplyCurrencyVisual(Image _icon, TMP_Text _countText, ClaimRewardGain _gain)
    {
        if (_icon != null && System.Enum.TryParse(_gain.Currency, out ECurrencyType t_type))
            _icon.sprite = CurrencyLook.IconOf(t_type);
        if (_countText != null) _countText.text = "x" + _gain.Amount;
    }

    static ClaimRewardGain CurrencyAt(MissionDefinition _definition, int _index)
    {
        List<ClaimRewardGain> t_gains = _definition?.Reward?.Currencies;
        if (t_gains == null) return null;
        for (int i = 0; i < t_gains.Count; i++)
            if (t_gains[i] != null && t_gains[i].Amount > 0 && _index-- == 0) return t_gains[i];
        return null;
    }

    static ClaimRewardItem FirstItem(MissionDefinition _definition)
    {
        List<ClaimRewardItem> t_items = _definition?.Reward?.Items;
        if (t_items == null) return null;
        for (int i = 0; i < t_items.Count; i++)
            if (t_items[i] != null && t_items[i].Amount > 0) return t_items[i];
        return null;
    }

    static string BuildRewardText(MissionDefinition _definition)
    {
        if (_definition?.Reward == null) return string.Empty;

        s_text.Clear();
        List<ClaimRewardGain> t_gains = _definition.Reward.Currencies;
        if (t_gains != null)
        {
            for (int i = 0; i < t_gains.Count; i++)
            {
                ClaimRewardGain t_gain = t_gains[i];
                if (t_gain == null || t_gain.Amount <= 0) continue;
                if (s_text.Length > 0) s_text.Append("  ");
                s_text.Append(CurrencyLabel(t_gain.Currency)).Append(' ').Append(t_gain.Amount);
            }
        }
        RewardItemDisplay.Append(s_text, _definition.Reward.Items);
        if (s_text.Length == 0 && _definition.Reward.PassExp > 0)
            s_text.Append("패스 경험치 +").Append(_definition.Reward.PassExp);
        return s_text.ToString();
    }

    // 와이어는 ECurrencyType 이름 문자열이다. 못 읽는 표기는 서버가 그 줄을 버리므로 여기 오지 않지만,
    // 새 재화가 늘면 열거에 없는 이름이 올 수 있어 원문을 그대로 보여 준다(빈칸보다는 낫다).
    static string CurrencyLabel(string _currency)
        => System.Enum.TryParse(_currency, out ECurrencyType t_type) ? CurrencyLook.NameOf(t_type) : _currency;
}
