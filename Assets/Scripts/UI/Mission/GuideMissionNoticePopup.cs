using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

public sealed class GuideMissionNoticeData : UIData
{
    public string MissionId;
    public Action OnCompleted;
}

/// <summary>온보딩 커서와 독립적인 단일 가이드 달성 안내. 양보는 사용자 닫기로 기록하지 않는다.</summary>
public sealed class GuideMissionNoticePopup : PooledUIBase
{
    [SerializeField] GameObject root;
    [SerializeField] Transform listContent;
    [SerializeField] MissionRowView rowPrefab;
    [SerializeField] Button closeButton;
    [SerializeField] Button dimButton;
    [SerializeField] PopupTransition transition = new PopupTransition();
    [Range(0f, 1f)] [SerializeField] float dimAlpha = 0.72f;

    GuideMissionNoticeData m_notice;
    MissionRowView m_row;
    public bool IsClaiming { get; private set; }

    public override void Initialization(UIData _data)
    {
        if (!this.IsClaiming) this.m_notice = _data as GuideMissionNoticeData;
    }

    public override void Show()
    {
        if (this.IsClaiming) return;
        MissionDefinition t_definition = MissionManager.Find(this.m_notice?.MissionId);
        if (t_definition == null || !MissionManager.IsComplete(t_definition) ||
            MissionManager.IsClaimed(t_definition.Id)) return;
        if (this.m_row == null && this.rowPrefab != null && this.listContent != null)
            this.m_row = Instantiate(this.rowPrefab, this.listContent);
        if (this.m_row == null) return;
        this.m_row.gameObject.SetActive(true);
        this.m_row.Bind(t_definition, this.HandleClaim);
        this.gameObject.SetActive(true);
        this.isShow = true;
        ScreenDim.Show(this, this.dimAlpha, true, this.transition.OpenDuration);
        this.transition.SetVisible(this.root, true);
    }

    public override void Hide() => this.Yield();

    /// <summary>화면 전환·다른 안내에 양보. 진행 요청과 완료 콜백은 그대로 보존한다.</summary>
    public void Yield()
    {
        if (this.IsClaiming) return;
        this.HideImmediately();
    }

    public void Close()
    {
        if (!this.isShow || this.IsClaiming) return;
        this.Complete();
    }

    void Complete()
    {
        Action t_completed = this.m_notice?.OnCompleted;
        this.m_notice = null;
        this.HideImmediately();
        t_completed?.Invoke();
    }

    void HideImmediately()
    {
        this.isShow = false;
        ScreenDim.Hide(this);
        this.transition.HandleDisabled(this.root);
        if (this.root != null) this.root.SetActive(false);
    }

    void OnEnable()
    {
        if (this.closeButton != null) this.closeButton.onClick.AddListener(this.Close);
        if (this.dimButton != null) this.dimButton.onClick.AddListener(this.Close);
        MissionManager.OnChanged += this.Refresh;
    }

    void OnDisable()
    {
        if (this.closeButton != null) this.closeButton.onClick.RemoveListener(this.Close);
        if (this.dimButton != null) this.dimButton.onClick.RemoveListener(this.Close);
        MissionManager.OnChanged -= this.Refresh;
        this.HideImmediately();
    }

    void Refresh()
    {
        if (this.isShow && this.m_row != null) this.m_row.Refresh();
    }

    void HandleClaim(string _missionId)
    {
        if (!this.isShow || this.IsClaiming || !MissionManager.CanClaim(MissionManager.Find(_missionId))) return;
        this.ClaimAsync(_missionId).Forget();
    }

    async UniTask ClaimAsync(string _missionId)
    {
        this.IsClaiming = true;
        ClaimMissionResult t_result = null;
        ServerWaitOverlay.Hold(this);
        try { t_result = await MissionCommands.ClaimAsync(_missionId); }
        finally
        {
            ServerWaitOverlay.Release(this);
            this.IsClaiming = false;
        }
        if (this == null) return;
        if (t_result != null)
        {
            this.Complete();
            MissionPanel.ShowClaimedRewards(new[] { t_result });
            return;
        }
        // 명령 API가 실패 유형을 노출하지 않으므로 네트워크 문제로 단정하지 않는다.
        UIPoolManager.instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = "보상 수령을 확인하지 못했어요.\n잠시 후 다시 시도해 주세요.",
            yesText = "확인",
            noText = "닫기",
        });
        MissionCommands.RefreshAsync().Forget();
    }
}
