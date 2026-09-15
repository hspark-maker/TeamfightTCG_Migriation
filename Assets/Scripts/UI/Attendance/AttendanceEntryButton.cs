using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>로비 출석 버튼과 수령 가능 알림.</summary>
[RequireComponent(typeof(Button))]
public sealed class AttendanceEntryButton : AlertDotView
{
    void Awake() => GetComponent<Button>().onClick.AddListener(Open);
    void Open()
    {
        if (GuidanceCoordinator.CanNavigateFromLobby(null))
            UIPoolManager.Instance?.RequestUI<AttendancePanel>(this);
    }
    protected override bool ShouldShow => AttendanceCommands.CanClaim;
    protected override void Subscribe(Action _handler) => AttendanceCommands.OnChanged += _handler;
    protected override void Unsubscribe(Action _handler) => AttendanceCommands.OnChanged -= _handler;
}
