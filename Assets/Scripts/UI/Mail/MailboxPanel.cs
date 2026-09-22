using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>우편 목록·본문·첨부 미리보기. 서버 응답이 채택된 뒤 공용 보상 연출로 인계한다.</summary>
public sealed class MailboxPanel : ContentsPooledUI
{
    [SerializeField] Transform listContent;
    [SerializeField] MailboxRowView rowTemplate;
    [SerializeField] ScrollRect listScroll;
    [SerializeField] GameObject listPage;
    [SerializeField] GameObject detailPage;
    [SerializeField] GameObject emptyRoot;
    [SerializeField] TMP_Text statusText;
    [SerializeField] Button refreshButton;
    [SerializeField] Button nextButton;
    [SerializeField] TMP_Text nextLabel;
    [SerializeField] Button claimAllButton;
    [SerializeField] TMP_Text claimAllLabel;
    [SerializeField] Button closeButton;
    [SerializeField] Button dimButton;
    [SerializeField] Button backButton;
    [SerializeField] TMP_Text detailTitle;
    [SerializeField] TMP_Text detailBody;
    [SerializeField] TMP_Text detailTime;
    [SerializeField] CurrencyRewardSlotView[] rewardSlots;
    [SerializeField] ScrollRect detailScroll;
    [SerializeField] Button claimButton;
    [SerializeField] TMP_Text claimLabel;

    readonly List<MailboxRowView> m_rows = new List<MailboxRowView>();
    MailboxEntry m_selected;
    bool m_claiming;
    float m_nextTick;
    string m_notice;

    bool Busy => m_claiming || MailboxCommands.IsClaiming;

    public override void Initialization(UIData _data) { InitializeUI(); data = _data; }

    public override void Show()
    {
        InitializeUI();
        m_selected = null;
        m_notice = null;
        SetContentsVisible(true);
        Render();
        listScroll.verticalNormalizedPosition = 1f;
        MailboxCommands.RefreshAsync(true).Forget();
    }

    public override void Hide()
    {
        if (!Busy) SetContentsVisible(false);
    }

    protected override void OnInitializeUI()
    {
        rowTemplate.gameObject.SetActive(false);
        detailTitle.richText = false;
        detailBody.richText = false;
        closeButton.onClick.AddListener(Hide);
        dimButton.onClick.AddListener(Hide);
        backButton.onClick.AddListener(Back);
        refreshButton.onClick.AddListener(Refresh);
        nextButton.onClick.AddListener(LoadMore);
        claimAllButton.onClick.AddListener(() => Claim(true));
        claimButton.onClick.AddListener(() => Claim(false));
    }

    protected override void OnViewShown() => MailboxCommands.OnChanged += Render;

    protected override void OnViewHidden()
    {
        MailboxCommands.OnChanged -= Render;
        ServerWaitOverlay.Release(this);
    }

    void Refresh()
    {
        if (Busy || MailboxCommands.IsReading) return;
        m_notice = null;
        MailboxCommands.RefreshAsync(true).Forget();
    }

    void LoadMore()
    {
        if (!Busy && !MailboxCommands.IsReading) MailboxCommands.LoadMoreAsync().Forget();
    }

    void Back()
    {
        if (Busy) return;
        m_selected = null;
        Render();
    }

    void OpenMail(MailboxEntry _mail)
    {
        if (Busy) return;
        m_selected = _mail;
        Render();
        Canvas.ForceUpdateCanvases();
        detailScroll.verticalNormalizedPosition = 1f;
    }

    void Render()
    {
        if (!isShow) return;
        if (!MailboxCommands.IsReady) { m_selected = null; m_notice = null; }
        var mails = MailboxCommands.Mails;
        bool selectedFound = false;
        for (int i = 0; i < mails.Count; i++)
        {
            if (i == m_rows.Count) m_rows.Add(Instantiate(rowTemplate, listContent));
            m_rows[i].gameObject.SetActive(true);
            m_rows[i].Bind(mails[i], OpenMail);
            if (m_selected != null && m_selected.MailId == mails[i].MailId)
            {
                m_selected = mails[i];
                selectedFound = true;
            }
        }
        if (!selectedFound) m_selected = null;
        for (int i = mails.Count; i < m_rows.Count; i++) m_rows[i].gameObject.SetActive(false);

        bool detail = m_selected != null;
        listPage.SetActive(!detail);
        detailPage.SetActive(detail);
        backButton.gameObject.SetActive(detail);
        emptyRoot.SetActive(!detail && MailboxCommands.IsReady && mails.Count == 0
            && !MailboxCommands.IsReading && MailboxCommands.Error == null);
        nextButton.gameObject.SetActive(!detail && MailboxCommands.HasMore);
        nextLabel.text = MailboxCommands.IsReading ? "불러오는 중…" : "이전 우편 더 보기";
        if (detail)
        {
            detailTitle.text = m_selected.Title;
            detailBody.text = m_selected.Body;
            var rewards = MailboxCommands.RewardLines(m_selected);
            for (int i = 0; i < rewardSlots.Length; i++)
                if (i < rewards.Count) rewardSlots[i].Bind(rewards[i]);
                else rewardSlots[i].Hide();
        }
        RefreshState();
    }

    void Update()
    {
        if (!isShow || Time.unscaledTime < m_nextTick) return;
        m_nextTick = Time.unscaledTime + 1f;
        for (int i = 0; i < m_rows.Count; i++)
            if (m_rows[i].gameObject.activeSelf) m_rows[i].RefreshTime();
        RefreshState();
    }

    void RefreshState()
    {
        bool pending = MailboxCommands.HasPendingReceipt;
        bool ready = !Busy && !MailboxCommands.IsReading;
        closeButton.interactable = !Busy;
        dimButton.interactable = !Busy;
        backButton.interactable = !Busy;
        refreshButton.interactable = ready;
        nextButton.interactable = ready;
        claimAllButton.interactable = ready && (pending || MailboxCommands.IsReady && MailboxCommands.HasClaimable);
        claimAllLabel.text = Busy ? "받는 중…" : pending ? "수령 확인" : "모두 받기";
        bool selectedPending = pending && (MailboxCommands.PendingIsAll
            || MailboxCommands.PendingMailId == m_selected?.MailId);
        claimButton.interactable = ready && (selectedPending
            || !pending && MailboxCommands.IsReady && MailboxRowView.IsClaimable(m_selected));
        claimLabel.text = Busy ? "받는 중…" : selectedPending ? "수령 확인"
            : m_selected?.State == "Claimed" ? "수령 완료"
            : MailboxRowView.IsClaimable(m_selected) ? "보상 받기" : "기간 만료";
        if (m_selected != null) detailTime.text = MailboxRowView.TimeLabel(m_selected);
        statusText.text = Busy ? "보상을 받고 있어요."
            : MailboxCommands.Error ?? (pending ? "수령 결과를 확인해 주세요."
                : MailboxCommands.IsReading ? "우편을 불러오고 있어요."
                : m_notice ?? "보관 기간이 지나기 전에 보상을 받아 주세요.");
    }

    void Claim(bool _all)
    {
        if (Busy || MailboxCommands.IsReading) return;
        if (!MailboxCommands.IsReady && !MailboxCommands.HasPendingReceipt) return;
        if (!MailboxCommands.HasPendingReceipt
            && (_all ? !MailboxCommands.HasClaimable : !MailboxRowView.IsClaimable(m_selected))) return;
        ClaimAsync(_all).Forget();
    }

    async UniTaskVoid ClaimAsync(bool _all)
    {
        m_claiming = true;
        int version = VisibilityVersion;
        ServerWaitOverlay.Hold(this);
        RefreshState();
        ClaimMailResult result = null;
        try
        {
            result = MailboxCommands.HasPendingReceipt ? await MailboxCommands.RetryPendingAsync()
                : _all ? await MailboxCommands.ClaimAllAsync()
                : await MailboxCommands.ClaimAsync(m_selected.MailId);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            m_notice = "수령 결과를 확인하지 못했어요. 다시 시도해 주세요.";
        }
        finally
        {
            m_claiming = false;
            ServerWaitOverlay.Release(this);
        }
        if (this == null || !isShow || version != VisibilityVersion) return;
        if (result == null) { Render(); return; }
        m_notice = result.HasMore ? "보상을 받았어요. 남은 우편도 받아 주세요." : "우편 보상을 받았어요.";
        Render();
        // 공용 보상 표시기가 여러 페이지와 팩·카드 연출을 이미 처리한다. 확인에서 재지급하지 않는다.
        if ((result.Cards?.Count ?? 0) > 0 || (result.Packs?.Count ?? 0) > 0) Hide();
        MissionPanel.ShowClaimedRewards(new[] { new ClaimMissionResult
        {
            Granted = result.Granted,
            Cards = result.Cards,
            Packs = result.Packs,
        } }, "우편 보상");
        MailboxCommands.RefreshAsync(true).Forget();
    }
}
