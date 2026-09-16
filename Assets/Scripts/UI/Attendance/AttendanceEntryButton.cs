using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>로비 출석 버튼과 수령 가능 알림.</summary>
[RequireComponent(typeof(Button))]
public sealed class AttendanceEntryButton : AlertDotView
{
    Button m_button;

    protected override bool ShouldShow => OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission)
        && AttendanceCommands.CanClaim;

    protected override void Subscribe(Action _handler)
    {
        AttendanceCommands.OnChanged += _handler;
        OutgameFeatureLock.OnChanged += _handler;
        OutgameFeatureLock.OnChanged += ApplyFeatureLock;
        ApplyFeatureLock();
    }

    protected override void Unsubscribe(Action _handler)
    {
        AttendanceCommands.OnChanged -= _handler;
        OutgameFeatureLock.OnChanged -= _handler;
        OutgameFeatureLock.OnChanged -= ApplyFeatureLock;
    }

    void Awake()
    {
        m_button = GetComponent<Button>();
        m_button.onClick.AddListener(Open);
        FeatureLockView.Attach(gameObject, EOutgameFeature.Mission);
    }

    void OnDestroy()
    {
        if (m_button != null) m_button.onClick.RemoveListener(Open);
    }

    void ApplyFeatureLock()
    {
        if (m_button != null) m_button.interactable = OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission);
    }

    void Open()
    {
        if (OutgameFeatureLock.IsUnlocked(EOutgameFeature.Mission)
            && GuidanceCoordinator.CanNavigateFromMatchTab(null))
            UIPoolManager.Instance?.RequestUI<AttendancePanel>(this);
    }
}
