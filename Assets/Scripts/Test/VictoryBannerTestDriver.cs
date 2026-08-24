using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class VictoryBannerTestDriver : MonoBehaviour
{
    [Header("Core Preview")]
    [SerializeField] private VictoryBannerView banner;
    [SerializeField] private Text statusLabel;
    [SerializeField] private GameObject referenceOverlay;

    [Header("Test-only Backdrop")]
    [SerializeField] private CanvasGroup dimGroup;

    [Header("Playback")]
    [SerializeField, Min(0f)] private float holdDuration = 0.8f;
    [SerializeField] private bool autoPlayOnStart = true;

    private Coroutine cycleRoutine;
    private Coroutine effectsRoutine;

    private void Awake()
    {
        ResetEffectsImmediate();
    }

    private void OnEnable()
    {
        UpdateStatus();
    }

    private IEnumerator Start()
    {
        if (!autoPlayOnStart || banner == null)
            yield break;

        yield return null;
        Cycle();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            Cycle();
        else if (Input.GetKeyDown(KeyCode.S))
            Show();
        else if (Input.GetKeyDown(KeyCode.H))
            Hide();
        else if (Input.GetKeyDown(KeyCode.R))
            ResetBanner();
        else if (Input.GetKeyDown(KeyCode.O))
            ToggleReferenceOverlay();

        UpdateStatus();
    }

    private void OnDisable()
    {
        StopCycle();
        ResetEffectsImmediate();
    }

    public void Show()
    {
        StopCycle();
        if (banner == null || banner.State == VictoryBannerView.BannerState.Showing || banner.State == VictoryBannerView.BannerState.Shown)
            return;

        PlayShowEffects();
        banner.Show();
    }

    public void Hide()
    {
        StopCycle();
        if (banner == null || banner.State == VictoryBannerView.BannerState.Hiding || banner.State == VictoryBannerView.BannerState.Hidden)
            return;

        PlayHideEffects();
        banner.Hide();
    }

    public void Cycle()
    {
        StopCycle();
        if (banner == null)
            return;

        cycleRoutine = StartCoroutine(PlayCycle());
    }

    public void ResetBanner()
    {
        StopCycle();
        ResetEffectsImmediate();
        banner?.HideImmediate();
        UpdateStatus();
    }

    public void ToggleReferenceOverlay()
    {
        if (referenceOverlay != null)
            referenceOverlay.SetActive(!referenceOverlay.activeSelf);
    }

    private IEnumerator PlayCycle()
    {
        banner.HideImmediate();
        ResetEffectsImmediate();
        PlayShowEffects();
        banner.Show();

        yield return new WaitForSecondsRealtime(banner.ShowDuration + holdDuration);

        PlayHideEffects();
        banner.Hide();
        cycleRoutine = null;
    }

    private void PlayShowEffects()
    {
        StartDimFade(0.68f, 0.06f);
    }

    private void PlayHideEffects()
    {
        StartDimFade(0f, 0.2f);
    }

    private void StartDimFade(float targetAlpha, float duration)
    {
        StopEffects();
        if (dimGroup != null)
            effectsRoutine = StartCoroutine(FadeDim(targetAlpha, duration));
    }

    private IEnumerator FadeDim(float targetAlpha, float duration)
    {
        float startAlpha = dimGroup.alpha;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            dimGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t * t * (3f - 2f * t));
            yield return null;
        }

        dimGroup.alpha = targetAlpha;
        effectsRoutine = null;
    }

    private void ResetEffectsImmediate()
    {
        StopEffects();
        if (dimGroup != null)
            dimGroup.alpha = 0f;
    }

    private void StopCycle()
    {
        if (cycleRoutine == null)
            return;

        StopCoroutine(cycleRoutine);
        cycleRoutine = null;
    }

    private void StopEffects()
    {
        if (effectsRoutine == null)
            return;

        StopCoroutine(effectsRoutine);
        effectsRoutine = null;
    }

    private void UpdateStatus()
    {
        if (statusLabel == null)
            return;

        if (banner == null)
        {
            statusLabel.text = "STATE  NO BANNER ASSIGNED";
            return;
        }

        statusLabel.text = $"STATE  {banner.State.ToString().ToUpperInvariant()}    SHOW  {banner.ShowDuration:0.00}s    HIDE  {banner.HideDuration:0.00}s";
    }
}
