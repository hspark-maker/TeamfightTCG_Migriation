using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>서버가 내려준 7일 출석 상태와 보상을 표시한다.</summary>
public sealed class AttendancePanel : ContentsPooledUI
{
    [SerializeField] AttendanceDayView[] dayViews;
    [SerializeField] TMP_Text resetText;
    [SerializeField] TMP_Text statusText;
    [SerializeField] Button claimButton;
    [SerializeField] TMP_Text claimLabel;
    [SerializeField] Button closeButton;
    [SerializeField] Button dimButton;
    bool m_claiming;
    float m_nextTick;

    public override void Initialization(UIData _data) { InitializeUI(); this.data = _data; }
    public override void Show()
    {
        SetContentsVisible(true);
        AttendanceCommands.MarkPrompted();
        RefreshAsync().Forget();
    }
    public override void Hide() { if (!m_claiming) SetContentsVisible(false); }

    protected override void OnInitializeUI()
    {
        closeButton?.onClick.AddListener(Hide);
        dimButton?.onClick.AddListener(Hide);
        claimButton?.onClick.AddListener(ClaimOrRetry);
    }

    protected override void OnViewShown()
    {
        AttendanceCommands.OnChanged += Render;
        Render();
    }
    protected override void OnViewHidden()
    {
        AttendanceCommands.OnChanged -= Render;
        ServerWaitOverlay.Release(this);
    }

    async UniTaskVoid RefreshAsync()
    {
        await AttendanceCommands.RefreshAsync(true);
        if (this == null || !this.isShow) return;
        AttendanceCommands.MarkPrompted();
        Render();
    }

    void Update()
    {
        if (!this.isShow || Time.unscaledTime < m_nextTick) return;
        m_nextTick = Time.unscaledTime + 1;
        Render();
    }

    void Render()
    {
        bool t_ready = AttendanceCommands.IsReady;
        var t_state = AttendanceCommands.State;
        if (dayViews != null)
            for (int i = 0; i < dayViews.Length; i++)
                dayViews[i]?.Bind(i + 1, AttendanceCommands.RewardLines(i + 1),
                    t_ready && i < t_state.ClaimedDays,
                    t_ready && t_state.CanClaim && i + 1 == t_state.ClaimDay,
                    AttendanceCommands.CanClaim);
        bool t_retry = !t_ready || AttendanceCommands.NeedsRefresh || AttendanceCommands.Error != null;
        if (claimButton != null) claimButton.interactable = !m_claiming && !AttendanceCommands.IsReading
            && (AttendanceCommands.CanClaim || t_retry);
        if (claimLabel != null) claimLabel.text = m_claiming ? "받는 중…"
            : AttendanceCommands.HasPendingReceipt ? "수령 확인"
            : AttendanceCommands.IsReading ? "확인 중…"
            : t_retry ? "다시 확인" : AttendanceCommands.CanClaim ? "보상 받기" : "오늘 출석 완료";
        if (closeButton != null) closeButton.interactable = !m_claiming;
        if (dimButton != null) dimButton.interactable = !m_claiming;
        if (statusText != null) statusText.text = AttendanceCommands.Error
            ?? (!t_ready ? "출석 정보를 확인하고 있어요."
                : t_state.CanClaim ? $"{t_state.ClaimDay}일차 보상을 받을 수 있어요!"
                : t_state.ClaimedDays == 7 ? "7일 출석 완료! 다음 출석부터 새롭게 시작해요."
                : "오늘도 출석 완료! 다음 보상을 기대해 주세요.");
        if (resetText != null)
        {
            long t_left = t_ready ? Math.Max(0, t_state.NextResetAtMs - AttendanceCommands.NowMs) : 0;
            var t_time = TimeSpan.FromMilliseconds(t_left);
            resetText.text = t_ready ? $"다음 출석까지 {(int)t_time.TotalHours:00}:{t_time.Minutes:00}:{t_time.Seconds:00} · 매일 오전 5시"
                : "매일 오전 5시 갱신 · 접속하지 않아도 출석 일수 유지";
        }
    }

    void ClaimOrRetry()
    {
        if (m_claiming) return;
        if (AttendanceCommands.CanClaim) ClaimAsync().Forget();
        else RefreshAsync().Forget();
    }

    async UniTaskVoid ClaimAsync()
    {
        m_claiming = true;
        ServerWaitOverlay.Hold(this);
        Render();
        ClaimAttendanceResult t_result;
        try { t_result = await AttendanceCommands.ClaimAsync(); }
        finally
        {
            m_claiming = false;
            ServerWaitOverlay.Release(this);
        }
        if (this == null || !this.isShow) return;
        Render();
        if (t_result == null) return;

        var t_gains = new List<CurrencyGain>();
        var t_lines = new List<RewardLine>();
        if (t_result.Granted != null)
            foreach (var t_gain in t_result.Granted)
                if (CurrencyCode.TryParse(t_gain.Currency, out var t_type))
                {
                    var t_value = new CurrencyGain(t_type, t_gain.Amount);
                    t_gains.Add(t_value);
                    t_lines.Add(new RewardLine(t_value));
                }
        var t_outcome = RewardItemDisplay.ToOutcome(t_gains, t_result.Cards, t_result.Packs, t_result.Cosmetics, t_result.Titles);
        if (t_outcome.Packs != null)
            foreach (var t_pack in t_outcome.Packs)
                t_lines.Add(new RewardLine(new AlbumRewardDef { rewardType = ERewardType.Pack,
                    rewardId = t_pack.PackId, amount = 1 }));
        if (t_outcome.Cards != null)
            foreach (var t_card in t_outcome.Cards)
                t_lines.Add(new RewardLine(new AlbumRewardDef { rewardType = ERewardType.Card,
                    rewardId = t_card.CardId.ToString(), amount = 1 }));
        Hide();
        if (t_lines.Count == 0) RewardPackPresentation.Show(t_outcome);
        else if (RewardClaimPopup.TryGet(out var t_popup))
            t_popup.Show("출석 보상", t_lines, () => UniTask.FromResult(t_outcome), _claimOnDim: true);
        else RewardClaimPopup.ClaimWithoutPopup(() => UniTask.FromResult(t_outcome)).Forget();
        // 이전 날짜의 영수증을 복원했을 수도 있으므로 현재 날짜를 다시 조회한다.
        AttendanceCommands.RefreshAsync(true).Forget();
    }
}
