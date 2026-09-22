using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>로비 우편함 진입과 전체 우편함의 수령 가능 알림.</summary>
[RequireComponent(typeof(Button))]
public sealed class MailboxEntryButton : AlertDotView
{
    Button m_button;
    double m_nextRefresh;

    protected override bool ShouldShow => MailboxCommands.HasClaimable || MailboxCommands.HasPendingReceipt;

    protected override void Subscribe(Action _handler)
    {
        MailboxCommands.OnChanged += _handler;
        m_nextRefresh = 0;
    }

    protected override void Unsubscribe(Action _handler) => MailboxCommands.OnChanged -= _handler;

    void Awake()
    {
        m_button = GetComponent<Button>();
        m_button.onClick.AddListener(Open);
    }

    void OnDestroy()
    {
        if (m_button != null) m_button.onClick.RemoveListener(Open);
    }

    void OnApplicationFocus(bool _focused)
    {
        if (_focused) m_nextRefresh = 0;
    }

    void Update()
    {
        if (!GameInitialization.IsReady || !PlayerSaveCloud.IsGateComplete
            || MailboxCommands.IsReading || MailboxCommands.IsClaiming
            || Time.realtimeSinceStartupAsDouble < m_nextRefresh) return;
        // 우편함에서 더 읽어 둔 페이지를 로비 알림 조회가 첫 페이지로 되돌리지 않는다.
        if (UIPoolManager.instance != null
            && UIPoolManager.instance.TryGetUI<MailboxPanel>(out var panel) && panel.isShow) return;
        m_nextRefresh = Time.realtimeSinceStartupAsDouble + 60;
        MailboxCommands.RefreshAsync().Forget();
    }

    void Open()
    {
        if (GuidanceCoordinator.CanNavigateFromLobby(null))
            UIPoolManager.Instance?.RequestUI<MailboxPanel>(this);
    }
}
