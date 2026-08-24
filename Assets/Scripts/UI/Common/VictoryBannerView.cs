using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reusable runtime controller for the isolated Victory banner prefab.
/// The authored motion lives in Builder-generated Animator clips; this class
/// only owns playback, interruption safety, particles, and terminal states.
/// </summary>
public sealed class VictoryBannerView : MonoBehaviour
{
    public enum BannerState
    {
        Hidden,
        Showing,
        Shown,
        Hiding
    }

    public const string HiddenStateName = "VictoryBanner_Hidden";
    public const string ShowStateName = "VictoryBanner_Show";
    public const string ShownStateName = "VictoryBanner_Shown";
    public const string HideStateName = "VictoryBanner_Hide";

    private const string BaseLayerPrefix = "Base Layer.";
    private static readonly int HiddenStateHash = Animator.StringToHash(BaseLayerPrefix + HiddenStateName);
    private static readonly int ShowStateHash = Animator.StringToHash(BaseLayerPrefix + ShowStateName);
    private static readonly int ShownStateHash = Animator.StringToHash(BaseLayerPrefix + ShownStateName);
    private static readonly int HideStateHash = Animator.StringToHash(BaseLayerPrefix + HideStateName);

    [Header("Animator motion")]
    [SerializeField] private GameObject visualRoot;
    [SerializeField] private Animator animator;
    [SerializeField, Min(0.01f)] private float showDuration = 2f;
    [SerializeField, Min(0.01f)] private float hideDuration = 0.75f;
    [SerializeField, Min(0f)] private float reversalBlendDuration = 0.06f;

    [Header("Reusable runtime effects")]
    [SerializeField] private ParticleSystem[] rearBurstParticles;
    [SerializeField] private Image[] shineBands;

    private Coroutine completionRoutine;
    private uint playbackGeneration;
    private uint rearBurstPlaybackGeneration;
    private bool hideQueuedWhileShowing;

    public float ShowDuration => showDuration;
    public float HideDuration => hideDuration;
    public BannerState State { get; private set; } = BannerState.Hidden;

    private void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();

        if (animator != null)
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;

        Sprite sharedShineSprite = ShineBandSprite.Get();
        if (shineBands != null)
        {
            foreach (Image band in shineBands)
            {
                if (band != null)
                    band.sprite = sharedShineSprite;
            }
        }

        HideImmediate();
    }

    public void Show()
    {
        if (State == BannerState.Showing)
        {
            hideQueuedWhileShowing = false;
            return;
        }

        if (State == BannerState.Shown)
            return;

        hideQueuedWhileShowing = false;
        bool startFromHidden = State == BannerState.Hidden || visualRoot == null || !visualRoot.activeSelf;
        BeginPlayback(BannerState.Showing);

        if (visualRoot != null)
            visualRoot.SetActive(true);

        StopRearBurst();
        PlayState(ShowStateHash, startFromHidden);
        PlayRearBurst();
        completionRoutine = StartCoroutine(CompleteAfter(showDuration, playbackGeneration, BannerState.Shown, ShownStateHash, false));
    }

    public void Hide()
    {
        if (State == BannerState.Hidden || State == BannerState.Hiding)
            return;

        // Closing during the reveal is queued until the authored Show pose is
        // stable. This prevents late actors from being blended into visibility
        // by a Show-to-Hide crossfade.
        if (State == BannerState.Showing)
        {
            hideQueuedWhileShowing = true;
            return;
        }

        StartHide();
    }

    private void StartHide()
    {
        hideQueuedWhileShowing = false;
        BeginPlayback(BannerState.Hiding);
        StopRearBurst();
        PlayState(HideStateHash, true);
        completionRoutine = StartCoroutine(CompleteAfter(hideDuration, playbackGeneration, BannerState.Hidden, HiddenStateHash, true));
    }

    public void ShowImmediate()
    {
        hideQueuedWhileShowing = false;
        BeginPlayback(BannerState.Shown);
        StopRearBurst();

        if (visualRoot != null)
            visualRoot.SetActive(true);

        EvaluateState(ShownStateHash, 0f);
    }

    public void HideImmediate()
    {
        hideQueuedWhileShowing = false;
        BeginPlayback(BannerState.Hidden);
        StopRearBurst();

        if (visualRoot != null)
            visualRoot.SetActive(true);

        EvaluateState(HiddenStateHash, 0f);

        if (visualRoot != null)
            visualRoot.SetActive(false);
    }

    /// <summary>Starts the deterministic rear-firework schedule once per Show generation.</summary>
    public void PlayRearBurst()
    {
        if (State != BannerState.Showing || rearBurstParticles == null)
            return;
        if (rearBurstPlaybackGeneration == playbackGeneration)
            return;

        rearBurstPlaybackGeneration = playbackGeneration;

        foreach (ParticleSystem particles in rearBurstParticles)
        {
            if (particles == null)
                continue;

            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.Play(true);
        }
    }

    /// <summary>AnimationEvent, terminal-state, and interruption cleanup entry point.</summary>
    public void StopRearBurst()
    {
        if (rearBurstParticles == null)
            return;

        foreach (ParticleSystem particles in rearBurstParticles)
        {
            if (particles != null)
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void BeginPlayback(BannerState nextState)
    {
        playbackGeneration++;
        State = nextState;

        if (completionRoutine == null)
            return;

        StopCoroutine(completionRoutine);
        completionRoutine = null;
    }

    private void PlayState(int stateHash, bool playFromStart)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        if (playFromStart || reversalBlendDuration <= 0f)
            animator.Play(stateHash, 0, 0f);
        else
            animator.CrossFadeInFixedTime(stateHash, reversalBlendDuration, 0, 0f);

        animator.Update(0f);
    }

    private void EvaluateState(int stateHash, float normalizedTime)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        animator.Play(stateHash, 0, normalizedTime);
        animator.Update(0f);
    }

    private IEnumerator CompleteAfter(
        float duration,
        uint expectedGeneration,
        BannerState completedState,
        int terminalStateHash,
        bool deactivateVisual)
    {
        yield return new WaitForSecondsRealtime(duration);

        if (expectedGeneration != playbackGeneration)
            yield break;

        if (completedState == BannerState.Shown)
            StopRearBurst();

        EvaluateState(terminalStateHash, 0f);
        State = completedState;
        completionRoutine = null;

        if (completedState == BannerState.Shown && hideQueuedWhileShowing)
        {
            StartHide();
            yield break;
        }

        if (deactivateVisual && visualRoot != null)
            visualRoot.SetActive(false);
    }

    private void OnDisable()
    {
        playbackGeneration++;
        if (completionRoutine != null)
        {
            StopCoroutine(completionRoutine);
            completionRoutine = null;
        }

        StopRearBurst();
        hideQueuedWhileShowing = false;
        State = BannerState.Hidden;

        if (visualRoot != null)
            visualRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        playbackGeneration++;
        completionRoutine = null;
        hideQueuedWhileShowing = false;
        StopRearBurst();
    }
}
