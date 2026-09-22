using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>우편 상태와 남은 시간을 표시한다. 수령 판정과 지급은 서버가 맡는다.</summary>
public sealed class MailboxRowView : MonoBehaviour
{
    [SerializeField] Button openButton;
    [SerializeField] TMP_Text titleText;
    [SerializeField] TMP_Text timeText;
    [SerializeField] TMP_Text stateText;
    [SerializeField] TMP_Text rewardText;
    [SerializeField] Image mailIcon;
    [SerializeField] Sprite closedMail;
    [SerializeField] Sprite openedMail;
    [SerializeField] GameObject claimDot;

    MailboxEntry m_mail;
    Action<MailboxEntry> m_open;
    bool m_initialized;

    internal void Bind(MailboxEntry _mail, Action<MailboxEntry> _open)
    {
        if (!m_initialized)
        {
            m_initialized = true;
            openButton.onClick.AddListener(() => m_open?.Invoke(m_mail));
            titleText.richText = false;
        }
        m_mail = _mail;
        m_open = _open;
        titleText.text = _mail.Title;
        int count = (_mail.Rewards?.Currencies?.Count ?? 0) + (_mail.Rewards?.Items?.Count ?? 0);
        rewardText.text = $"첨부 보상 {count}개";
        RefreshTime();
    }

    internal void RefreshTime()
    {
        if (m_mail == null) return;
        bool claimed = m_mail.State == "Claimed";
        bool claimable = IsClaimable(m_mail);
        mailIcon.sprite = claimed ? openedMail : closedMail;
        mailIcon.color = claimable ? Color.white : new Color(0.68f, 0.68f, 0.68f, 1f);
        claimDot.SetActive(claimable);
        stateText.text = claimed ? "수령 완료" : claimable ? "" : "기간 만료";
        stateText.color = claimed ? new Color32(116, 132, 102, 255) : new Color32(143, 132, 124, 255);
        timeText.text = TimeLabel(m_mail);
    }

    internal static bool IsClaimable(MailboxEntry _mail) => _mail != null
        && _mail.State == "Claimable" && _mail.ExpiresAtMs > MailboxCommands.NowMs;

    internal static string TimeLabel(MailboxEntry _mail)
    {
        if (_mail.State == "Claimed") return "받은 우편";
        long remaining = _mail.ExpiresAtMs - MailboxCommands.NowMs;
        if (_mail.State == "Expired" || remaining <= 0) return "보관 기간이 지났어요";
        var time = TimeSpan.FromMilliseconds(remaining);
        if (time.TotalDays >= 1) return $"{(int)time.TotalDays}일 남음";
        if (time.TotalHours >= 1) return $"{(int)time.TotalHours}시간 남음";
        if (time.TotalMinutes >= 1) return $"{(int)time.TotalMinutes}분 남음";
        return "곧 만료";
    }
}
