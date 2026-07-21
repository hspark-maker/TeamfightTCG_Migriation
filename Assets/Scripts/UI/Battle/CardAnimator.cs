using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;

public class CardAnimator : MonoBehaviour
{
    [SerializeField] SpriteRenderer hitOverlay;
    [SerializeField] SpriteRenderer dieOverlay;
    [SerializeField] float moveDuration = 0.3f;

    static readonly float[] CINEMA_X_FRACTIONS = { 0.25f, 0.5f, 0.75f };

    CardInstance boundCard;
    Vector3 slotPosition;
    readonly System.Collections.Generic.HashSet<SpriteRenderer> fadeExcludes
        = new System.Collections.Generic.HashSet<SpriteRenderer>();

    SpriteRenderer[] cachedRenderers;
    TMP_Text[]       cachedTexts;

    public void ExcludeFromFade(SpriteRenderer _sr) { if (_sr != null) this.fadeExcludes.Add(_sr); }

    public Vector3 SlotPosition => this.slotPosition;

    void Awake()
    {
        this.slotPosition = transform.position;
    }

    public void Initialize()
    {
        RefreshVisualCache();
    }

    void RefreshVisualCache()
    {
        this.cachedRenderers = GetComponentsInChildren<SpriteRenderer>();
        this.cachedTexts     = GetComponentsInChildren<TMP_Text>();
    }

    public void SetBoundCard(CardInstance _card) => this.boundCard = _card;

    // ── Move ─────────────────────────────────────────────────────────────

    public async UniTask MoveToCenter()
    {
        Vector3 t_wc = CameraUtil.ScreenFractionToWorld(0.5f, 0.5f, this.slotPosition.z);
        t_wc.y = this.slotPosition.y;
        FadeSpriteRenderers(1f);
        await MoveTo(t_wc);
    }

    public async UniTask MoveToCinemaSlot()
    {
        int t_slot = Mathf.Clamp(this.boundCard?.slotIndex ?? 1, 0, BattleField.SLOT_COUNT - 1);
        Vector3 t_wc = CameraUtil.ScreenFractionToWorld(CINEMA_X_FRACTIONS[t_slot], 0.5f, this.slotPosition.z);
        t_wc.y = this.slotPosition.y;
        FadeSpriteRenderers(1f);
        await MoveTo(t_wc);
    }

    public UniTask MoveToSlot() => MoveTo(this.slotPosition);

    public async UniTask MoveToCinemaPosition(int _posIndex, int _totalCount)
    {
        const float t_spacing = 0.25f;
        float t_x = 0.5f - (_totalCount - 1) * t_spacing * 0.5f + _posIndex * t_spacing;
        Vector3 t_wc = CameraUtil.ScreenFractionToWorld(t_x, 0.5f, this.slotPosition.z);
        t_wc.y = this.slotPosition.y;
        FadeSpriteRenderers(1f);
        await MoveTo(t_wc);
    }

    public async UniTask MoveTo(Vector3 _pos)
    {
        transform.DOKill();
        bool t_cancelled = await transform.DOMove(_pos, this.moveDuration)
            .ToUniTask(cancellationToken: this.GetCancellationTokenOnDestroy())
            .SuppressCancellationThrow();
        if (t_cancelled) return;
    }

    // ── Fade ─────────────────────────────────────────────────────────────

    public void FadeView(float _alpha, float _duration)
    {
        RefreshVisualCache();

        foreach (SpriteRenderer t_sr in this.cachedRenderers)
        {
            if (t_sr == this.hitOverlay) continue;
            if (t_sr == this.dieOverlay) continue;
            if (this.fadeExcludes.Contains(t_sr)) continue;
            t_sr.DOKill();
            t_sr.DOFade(_alpha, _duration);
        }
        foreach (TMP_Text t_tmp in this.cachedTexts)
        {
            t_tmp.DOKill();
            t_tmp.DOFade(_alpha, _duration);
        }
    }

    void FadeSpriteRenderers(float _alpha)
    {
        RefreshVisualCache();

        foreach (SpriteRenderer t_sr in this.cachedRenderers)
        {
            if (t_sr == this.hitOverlay) continue;
            if (t_sr == this.dieOverlay) continue;
            t_sr.DOKill();
            t_sr.DOFade(_alpha, this.moveDuration);
        }
        foreach (TMP_Text t_tmp in this.cachedTexts)
        {
            t_tmp.DOKill();
            t_tmp.DOFade(_alpha, this.moveDuration);
        }
    }

    // ── Death preview ────────────────────────────────────────────────────

    public void ShowDeathPreview()
    {
        if (this.dieOverlay == null) return;
        this.dieOverlay.gameObject.SetActive(true);
        this.dieOverlay.DOKill();
        Color t_c = this.dieOverlay.color;
        t_c.a = 0f;
        this.dieOverlay.color = t_c;
        this.dieOverlay.DOFade(0.7f, 0.55f).SetLoops(-1, LoopType.Yoyo);
    }

    public void HideDeathPreview()
    {
        if (this.dieOverlay == null) return;
        this.dieOverlay.DOKill();
        Color t_c = this.dieOverlay.color;
        t_c.a = 0f;
        this.dieOverlay.color = t_c;
        this.dieOverlay.gameObject.SetActive(false);
    }

    // ── Hit / Death / Deal ───────────────────────────────────────────────

    public async UniTask PlayHitAnim(float _duration = 0.15f)
    {
        SoundManager.Instance?.PlayHit();
        if (this.hitOverlay == null) return;
        this.hitOverlay.DOKill();
        Color t_c = this.hitOverlay.color;
        t_c.a = 0f;
        this.hitOverlay.color = t_c;
        await this.hitOverlay.DOFade(0.5f, _duration).ToUniTask();
        await this.hitOverlay.DOFade(0f, _duration).ToUniTask();
    }

    public async UniTask PlayDeathAnim(float _duration = 0.4f)
    {
        RefreshVisualCache();

        SoundManager.Instance?.PlayDeath();
        SoundManager.Instance?.PlayDeathVoice(this.boundCard?.data?.deathVoices);
        var t_seq = DOTween.Sequence();
        t_seq.Join(transform.DOScale(0f, _duration).SetEase(Ease.InBack));
        foreach (SpriteRenderer t_sr in this.cachedRenderers)
        {
            if (t_sr == this.hitOverlay || t_sr == this.dieOverlay) continue;
            t_seq.Join(t_sr.DOFade(0f, _duration));
        }
        foreach (TMP_Text t_tmp in this.cachedTexts)
            t_seq.Join(t_tmp.DOFade(0f, _duration));
        await t_seq.ToUniTask();

        if (this == null) return;
        transform.localScale = Vector3.one;
        foreach (SpriteRenderer t_sr in this.cachedRenderers)
        {
            if (t_sr == null) continue;
            Color t_c = t_sr.color;
            t_c.a = (t_sr == this.hitOverlay || t_sr == this.dieOverlay) ? 0f : 1f;
            t_sr.color = t_c;
        }
        foreach (TMP_Text t_tmp in this.cachedTexts)
        {
            if (t_tmp == null) continue;
            Color t_c = t_tmp.color;
            t_c.a = 1f;
            t_tmp.color = t_c;
        }
    }

    public async UniTask PlayDealAnim(Vector3 _from, Vector3 _mid, Vector3 _dest, float _duration = 0.6f)
    {
        RefreshVisualCache();

        SoundManager.Instance?.PlayDealCard();
        SoundManager.Instance?.PlaySpawnVoice(this.boundCard?.data?.spawnVoices);
        var t_ct = this.GetCancellationTokenOnDestroy();

        float t_half   = _duration * 0.5f;
        float t_frontZ = _dest.z - 0.1f;
        _from.z = t_frontZ;
        _mid.z  = t_frontZ;

        transform.position      = _from;
        transform.localRotation = Quaternion.identity;
        transform.localScale    = Vector3.one;
        foreach (SpriteRenderer t_sr in this.cachedRenderers)
        {
            if (t_sr == this.hitOverlay || t_sr == this.dieOverlay || this.fadeExcludes.Contains(t_sr)) continue;
            t_sr.DOKill();
            Color t_c = t_sr.color; t_c.a = 1f; t_sr.color = t_c;
        }
        foreach (TMP_Text t_tmp in this.cachedTexts)
        {
            t_tmp.DOKill();
            Color t_c = t_tmp.color; t_c.a = 1f; t_tmp.color = t_c;
        }

        var t_seq1 = DOTween.Sequence();
        t_seq1.Join(transform.DOMove(_mid, t_half).SetEase(Ease.OutCubic));
        t_seq1.Join(transform.DOScale(1.5f, t_half).SetEase(Ease.OutCubic));
        bool t_cancelled = await t_seq1.ToUniTask(cancellationToken: t_ct).SuppressCancellationThrow();
        if (t_cancelled) return;

        t_cancelled = await UniTask.Delay(500, cancellationToken: t_ct).SuppressCancellationThrow();
        if (t_cancelled) return;

        var t_seq2 = DOTween.Sequence();
        t_seq2.Join(transform.DOMove(_dest, t_half).SetEase(Ease.InCubic));
        t_seq2.Join(transform.DOScale(1f, t_half).SetEase(Ease.InCubic));
        t_seq2.Join(transform.DORotate(new Vector3(0f, 360f, 0f), t_half, RotateMode.FastBeyond360));
        t_cancelled = await t_seq2.ToUniTask(cancellationToken: t_ct).SuppressCancellationThrow();
        if (t_cancelled) return;

        transform.localRotation = Quaternion.identity;
        transform.localScale    = Vector3.one;
    }
}
