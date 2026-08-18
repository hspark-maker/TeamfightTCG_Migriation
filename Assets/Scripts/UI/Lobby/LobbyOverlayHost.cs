using UnityEngine;

/// <summary>Root contract for lobby overlays and their deterministic draw order.</summary>
public sealed class LobbyOverlayHost : MonoBehaviour
{
    [SerializeField] MatchDeckShell matchDeckShell;

    public MatchDeckShell MatchDeckShell => matchDeckShell;
}
