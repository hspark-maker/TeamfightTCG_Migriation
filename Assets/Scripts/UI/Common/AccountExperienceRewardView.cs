using System;
using DG.Tweening;
using TMPro;
using UnityEngine;

// 전달받은 경험치의 표시만 담당한다. 전투 결과는 예상치, 보상 팝업은 서버 지급치를 넘긴다.
[Serializable]
public sealed class AccountExperienceRewardView
{
    [SerializeField] RectTransform root;
    [SerializeField] TMP_Text gainText;
    [SerializeField] TMP_Text levelText;
    [SerializeField] TMP_Text progressText;
    [SerializeField] RectTransform fill;

    public bool IsWired => this.root != null;

    public Sequence Build(long _gained, long _total, bool _hasItems)
    {
        if (this.root != null)
            this.root.anchoredPosition = new Vector2(0f, _hasItems ? -330f : 0f);
        return Build(_gained, _total);
    }

    // 전투 결과에서는 프리팹에 저작한 위치를 유지한다.
    public Sequence Build(long _gained, long _total)
    {
        if (this.root == null) return null;
        bool t_visible = _gained > 0 && AccountLevelManager.IsConfigured;
        this.root.gameObject.SetActive(t_visible);
        if (!t_visible) return null;

        if (this.gainText != null) this.gainText.text = $"계정 경험치 <color=#A36213>+{_gained:N0}</color>";
        long t_start = Math.Max(0, _total - _gained);
        AccountLevelInfo t_first = AccountLevelManager.GetInfoAt(t_start);
        AccountLevelInfo t_last = AccountLevelManager.GetInfoAt(_total);
        Render(t_first, t_start);

        int t_steps = Math.Max(1, t_last.Level - t_first.Level + 1);
        float t_duration = Mathf.Clamp(1.2f / t_steps, 0.035f, 0.7f);
        var t_sequence = DOTween.Sequence().Pause();
        for (int t_level = t_first.Level; t_level <= t_last.Level; t_level++)
        {
            AccountLevelInfo t_info = AccountLevelManager.GetInfoAt(t_start);
            long t_from = t_start;
            long t_to = t_info.IsMaxLevel ? _total : Math.Min(_total, t_info.NextRequiredExp);
            if (t_to > t_from)
            {
                float t_progress = 0f;
                t_sequence.Append(DOTween.To(() => t_progress, _value =>
                {
                    t_progress = _value;
                    Render(t_info, t_from + (long)((t_to - t_from) * (double)_value));
                }, 1f, t_duration).SetEase(Ease.OutQuad));
            }
            if (!t_info.IsMaxLevel && t_to == t_info.NextRequiredExp)
            {
                t_sequence.AppendInterval(Mathf.Min(0.12f, 0.6f / t_steps));
                t_sequence.AppendCallback(() =>
                {
                    Render(AccountLevelManager.GetInfoAt(t_to), t_to);
                    if (this.levelText != null) this.levelText.text += " <color=#A36213>레벨업!</color>";
                });
            }
            t_start = t_to;
        }
        t_sequence.AppendCallback(() =>
        {
            Render(t_last, _total);
            if (t_last.Level > t_first.Level && this.levelText != null)
                this.levelText.text = $"Lv.{t_first.Level} → Lv.{t_last.Level} <color=#A36213>레벨업!</color>";
        });
        return t_sequence;
    }

    void Render(AccountLevelInfo _info, long _exp)
    {
        long t_current = Math.Max(0, _exp - _info.LevelRequiredExp);
        long t_span = _info.NextRequiredExp - _info.LevelRequiredExp;
        if (this.levelText != null) this.levelText.text = $"Lv.{_info.Level}";
        if (this.progressText != null)
            this.progressText.text = _info.IsMaxLevel ? "MAX" : $"{t_current:N0} / {t_span:N0}";
        if (this.fill != null)
            this.fill.anchorMax = new Vector2(_info.IsMaxLevel || t_span <= 0 ? 1f : Mathf.Clamp01((float)t_current / t_span), 1f);
    }
}
