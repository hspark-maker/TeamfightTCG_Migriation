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

    [Tooltip("0~1 로 채우는 게이지(Image.Type = Filled). 비워 두면 그리지 않는다.")]
    [SerializeField] Image progressFill;

    [Tooltip("보상 문구. 재화 목록과 패스 경험치를 한 줄로 적는다.")]
    [SerializeField] TMP_Text rewardText;

    [SerializeField] Button claimButton;

    [Tooltip("수령 버튼 라벨. 상태에 따라 문구가 바뀐다.")]
    [SerializeField] TMP_Text claimLabel;

    [Tooltip("이미 받은 줄에 켜는 표식(체크 등). 비워 두면 그리지 않는다.")]
    [SerializeField] GameObject claimedMark;

    [Tooltip("수령 불가일 때 버튼에 씌울 알파.")]
    [Range(0f, 1f)] [SerializeField] float disabledAlpha = 0.5f;

    [SerializeField] CanvasGroup claimGroup;

    MissionDefinition m_definition;
    System.Action<string> m_onClaim;

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

        if (this.titleText != null) this.titleText.text = _definition?.Title ?? string.Empty;
        if (this.descriptionText != null) this.descriptionText.text = _definition?.Description ?? string.Empty;
        if (this.rewardText != null) this.rewardText.text = BuildRewardText(_definition);

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

        if (this.progressText != null)
        {
            // 목표를 넘겨 쌓여도 표시는 목표에서 멈춘다 — 서버도 수령을 한 번만 허용한다.
            long t_shown = t_target > 0 ? System.Math.Min(t_progress, t_target) : t_progress;
            s_text.Clear();
            s_text.Append(t_shown).Append(" / ").Append(t_target);
            this.progressText.text = s_text.ToString();
        }

        if (this.progressFill != null)
            this.progressFill.fillAmount = t_target > 0
                ? Mathf.Clamp01((float)t_progress / t_target)
                : (t_complete ? 1f : 0f);

        if (this.claimedMark != null) this.claimedMark.SetActive(t_claimed);

        if (this.claimLabel != null)
            this.claimLabel.text = t_claimed ? "완료" : t_complete ? "받기" : "진행 중";

        // 버튼은 끄지 않고 상호작용만 막는다 — 꺼 버리면 레이아웃이 흔들리고 "받은 줄"이 사라진 것처럼 보인다.
        if (this.claimButton != null) this.claimButton.interactable = t_canClaim;
        if (this.claimGroup != null) this.claimGroup.alpha = t_canClaim ? 1f : this.disabledAlpha;
    }

    void HandleClaim()
    {
        if (this.m_definition == null) return;

        // 여기서도 막는다. 버튼 비활성만으로는 부족하다 — 왕복 중 통지로 다시 그려지기 전에
        // 두 번 눌리면 같은 미션이 두 번 나간다(서버 영수증이 막지만 화면이 먼저 막는 게 맞다).
        if (!MissionManager.CanClaim(this.m_definition)) return;

        this.m_onClaim?.Invoke(this.m_definition.Id);
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
        if (_definition.Reward.PassExp > 0)
        {
            if (s_text.Length > 0) s_text.Append("  ");
            s_text.Append("패스 경험치 ").Append(_definition.Reward.PassExp);
        }
        return s_text.ToString();
    }

    // 와이어는 ECurrencyType 이름 문자열이다. 못 읽는 표기는 서버가 그 줄을 버리므로 여기 오지 않지만,
    // 새 재화가 늘면 열거에 없는 이름이 올 수 있어 원문을 그대로 보여 준다(빈칸보다는 낫다).
    static string CurrencyLabel(string _currency)
        => System.Enum.TryParse(_currency, out ECurrencyType t_type) ? CurrencyLook.NameOf(t_type) : _currency;
}
