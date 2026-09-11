using UnityEngine;

/// <summary>Scene-local presentation of the independently saved adventure introduction.</summary>
public sealed class AdventureTutorialBridge : MonoBehaviour
{
    OutgameTutorialGateUI m_prefab;
    TutorialStepDef m_step;
    bool m_applying;
    bool m_deferred;
    bool m_gateArmed;

    static bool StageBlocked => OutgameTutorialRunner.IsRunning || OutgameTutorialRunner.IsGuidedRunning
        || CurtainView.IsBusy || LoadingCoverView.IsCovering || LobbyRankEffectDirector.Playing
        || LobbyGainEffectDirector.Playing || AdventureRewardFlow.IsClaiming
        || RewardClaimPopup.IsOpen || CardRewardOverlay.IsOpen || CardSetRewardOverlay.IsOpen
        || PackRewardOverlay.IsOpen || PackOpenOverlay.IsOpen
        || (UIPoolManager.Instance != null && UIPoolManager.Instance.HasVisibleUIExcept());

    public static void Install(GameObject owner, OutgameTutorialGateUI prefab)
    {
        var bridge = owner.GetComponent<AdventureTutorialBridge>();
        if (bridge == null) bridge = owner.AddComponent<AdventureTutorialBridge>();
        bridge.m_prefab = prefab;
    }

    void OnEnable()
    {
        AdventureTutorialRunner.OnChanged += Apply;
        TutorialAnchorRegistry.OnRegistered += OnAnchorRegistered;
        OutgameTutorialRunner.OnGuidedChanged += Apply;
    }
    void Start() => Apply();
    void Update()
    {
        if (!AdventureTutorialRunner.IsRunning) return;
        if (StageBlocked)
        {
            if (!m_deferred) Clear();
            m_deferred = true;
        }
        else if (m_deferred) Apply();
    }
    void OnDisable()
    {
        AdventureTutorialRunner.OnChanged -= Apply;
        TutorialAnchorRegistry.OnRegistered -= OnAnchorRegistered;
        OutgameTutorialRunner.OnGuidedChanged -= Apply;
        Clear();
    }
    void OnAnchorRegistered(EOutgameTutorialAnchor key)
    {
        // A click can register the next screen's anchors before the gate's click callback runs.
        // Never replace an armed gate from that notification: its completion listener must survive.
        if (m_gateArmed || m_step == null) return;
        if (key != m_step.Anchor && key != m_step.Spotlight) return;
        Apply();
    }

    void Apply()
    {
        if (m_applying) return;
        m_applying = true;
        try
        {
            Clear();
            if (StageBlocked) { m_deferred = true; return; }
            m_deferred = false;
            // The detached chapter begins with closing card detail, an automatic local action.
            for (int i = 0; i < 16 && AdventureTutorialRunner.TryGetCurrentStep(out m_step); i++)
            {
                if (m_step.Action == EOutgameTutorialAction.CloseCardDetail)
                {
                    CardDetailOverlayView.Close();
                    AdventureTutorialRunner.Advance();
                    continue;
                }
                RectTransform rect = null;
                UnityEngine.UI.Button button = null;
                if (m_step.Anchor != EOutgameTutorialAnchor.None &&
                    !TutorialAnchorRegistry.TryGet(m_step.Anchor, out rect, out button)) return;
                RectTransform spotlight = null;
                if (m_step.Spotlight != EOutgameTutorialAnchor.None)
                    TutorialAnchorRegistry.TryGet(m_step.Spotlight, out spotlight, out _);
                if (m_step.Completion != EOutgameTutorialCompletion.Confirm && button == null) return;
                m_gateArmed = true;
                if (m_step.Completion == EOutgameTutorialCompletion.Confirm)
                    OutgameTutorialGateUI.Ensure(m_prefab).ShowMessageGate(this, rect, m_step.GuideMessage,
                        Satisfied, m_step.MessageAtBottom, m_step.UseDim, spotlight);
                else
                    OutgameTutorialGateUI.Ensure(m_prefab).ShowGate(this, rect, button, m_step.GuideMessage,
                        Satisfied, m_step.UseDim, spotlight);
                return;
            }
        }
        finally { m_applying = false; }
    }

    void Satisfied()
    {
        // Advance invokes OnChanged synchronously, which applies the next gate once.
        // Applying again reuses its Canvas pending Destroy and loses the highlight at frame end.
        AdventureTutorialRunner.Advance();
    }
    void Clear()
    {
        m_gateArmed = false;
        m_step = null;
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear(this);
    }
}
