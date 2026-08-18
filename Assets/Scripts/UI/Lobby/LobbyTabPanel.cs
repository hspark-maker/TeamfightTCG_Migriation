using System;
using UnityEngine;

/// <summary>Lobby tab content owns its enter/leave lifecycle and leave decision.</summary>
public class LobbyTabPanel : MonoBehaviour
{
    public RectTransform Root => transform as RectTransform;

    public virtual void RequestLeave(Action _proceed) => _proceed?.Invoke();

    public virtual void OnEnter() { }

    public virtual void OnLeave() { }
}
