using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Owns the match tab's play-button wiring.</summary>
public sealed class LobbyMatchTabPanel : LobbyTabPanel
{
    [SerializeField] Button playButton;

    public event Action PlayRequested;

    void Awake()
    {
        if (playButton != null) playButton.onClick.AddListener(HandlePlayRequested);
    }

    void OnDestroy()
    {
        if (playButton != null) playButton.onClick.RemoveListener(HandlePlayRequested);
    }

    public void SetPlayInteractable(bool _interactable)
    {
        if (playButton != null) playButton.interactable = _interactable;
    }

    void HandlePlayRequested() => PlayRequested?.Invoke();
}
