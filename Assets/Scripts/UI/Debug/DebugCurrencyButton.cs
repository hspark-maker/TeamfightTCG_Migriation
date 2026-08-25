using UnityEngine;

// Adapter for debug-currency buttons. Bind the matching Grant method to a Button OnClick event.
public class DebugCurrencyButton : MonoBehaviour
{
    // Button OnClick entry point: grants the standard debug amount and persists it immediately.
    public void GrantGold()
    {
        OutgameDebugActions.GrantGold();
    }

    // Button OnClick entry point: grants the standard debug amount and persists it immediately.
    public void GrantDiamond()
    {
        OutgameDebugActions.GrantDiamond();
    }

    // Button OnClick entry point: grants the standard debug amount and persists it immediately.
    public void GrantEnergy()
    {
        OutgameDebugActions.GrantEnergy();
    }

    // Button OnClick entry point: grants the standard debug amount and persists it immediately.
    public void GrantShard()
    {
        OutgameDebugActions.GrantShard();
    }
}
