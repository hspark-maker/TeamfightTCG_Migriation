using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;

public class CardAnimator : MonoBehaviour
{
    [SerializeField] SpriteRenderer hitOverlay;
    [SerializeField] SpriteRenderer dieOverlay;
    [SerializeField] HitEffectView  hitEffect;   // 피격 붐(카드 자식). 없으면 무시.

    // 타이밍은 BattleTimingConfig 단일 진실원(배율 적용).
    float moveDuration => GameTiming.Battle.CardMoveDuration;

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

    void OnDestroy()
    {
        transform.DOKill();
        if (this.hitOverlay != null) this.hitOverlay.DOKill();
        if (this.dieOverlay != null) this.dieOverlay.DOKill();
        if (this.cachedRenderers != null)
            foreach (SpriteRenderer t_sr in this.cachedRenderers)
                if (t_sr != null) t_sr.DOKill();
        if (this.cachedTexts != null)
            foreach (TMP_Text t_tmp in this.cachedTexts)
                if (t_tmp != null) t_tmp.DOKill();
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

    /// <summary>진행 중인 피격 연출(붐/데미지 숫자) 즉시 제거. 슬롯에 새 카드가 들어오기 전 호출 → 잔여 연출 이월 방지.</summary>
    public void ResetHitEffect() => this.hitEffect?.Stop();

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
            t_sr.DOFade(_alpha, _duration).SetLink(gameObject);
        }
        foreach (TMP_Text t_tmp in this.cachedTexts)
        {
            t_tmp.DOKill();
            t_tmp.DOFade(_alpha, _duration).SetLink(gameObject);
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
            t_sr.DOFade(_alpha, this.moveDuration).SetLink(gameObject);
        }
        foreach (TMP_Text t_tmp in this.cachedTexts)
        {
            t_tmp.DOKill();
            t_tmp.DOFade(_alpha, this.moveDuration).SetLink(gameObject);
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
        this.dieOverlay.DOFade(0.7f, GameTiming.Battle.DeathPreviewFlash).SetLoops(-1, LoopType.Yoyo).SetLink(gameObject);
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

    public async UniTask PlayHitAnim(float _duration = -1f, int _damage = 0)
    {
        if (_duration < 0f) _duration = GameTiming.Battle.HitDuration;
        SoundManager.Instance?.PlayHit();
        this.hitEffect?.Play(_damage);   // 피격 붐 + 데미지 숫자(있으면). 위치=이 카드.
        if (this.hitOverlay == null) return;
        this.hitOverlay.DOKill();
        Color t_c = this.hitOverlay.color;
        t_c.a = 0f;
        this.hitOverlay.color = t_c;
        await this.hitOverlay.DOFade(0.5f, _duration).SetLink(gameObject).ToUniTask();
        await this.hitOverlay.DOFade(0f, _duration).SetLink(gameObject).ToUniTask();
    }

    public async UniTask PlayDeathAnim(float _duration = -1f)
    {
        if (_duration < 0f) _duration = GameTiming.Battle.DeathDuration;
        RefreshVisualCache();

        SoundManager.Instance?.PlayDeath();
        SoundManager.Instance?.PlayDeathVoice(this.boundCard?.data?.deathVoices);
        var t_seq = DOTween.Sequence()
            .SetLink(gameObject)
            .Join(transform.DOScale(0f, _duration).SetEase(Ease.InBack));
        foreach (SpriteRenderer t_sr in this.cachedRenderers)
        {
            if (t_sr == this.hitOverlay || t_sr == this.dieOverlay) continue;
            _ = t_seq.Join(t_sr.DOFade(0f, _duration));
        }
        foreach (TMP_Text t_tmp in this.cachedTexts)
            _ = t_seq.Join(t_tmp.DOFade(0f, _duration));
        await t_seq.ToUniTask();

        if (this == null) return;
        // 스케일만 재사용 대비 복원. 알파는 페이드아웃(0) 상태로 남긴다 —
        // 여기서 알파를 1로 되돌리면 죽은 카드가 잠깐 되살아나 보인 뒤 HideSlot이 다시 숨김(플래시).
        // 슬롯 재사용 시 알파=1 복원은 PlayDealAnim(시작 시 리셋)이 담당하므로 중복 불필요.
        transform.localScale = Vector3.one;
    }

    public async UniTask PlayDealAnim(Vector3 _from, Vector3 _mid, Vector3 _dest, float _duration = -1f)
    {
        if (_duration < 0f) _duration = GameTiming.Battle.DealAnimDuration;
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

        var t_seq1 = DOTween.Sequence()
            .Join(transform.DOMove(_mid, t_half).SetEase(Ease.OutCubic))
            .Join(transform.DOScale(1.5f, t_half).SetEase(Ease.OutCubic));
        bool t_cancelled = await t_seq1.ToUniTask(cancellationToken: t_ct).SuppressCancellationThrow();
        if (t_cancelled) return;

        t_cancelled = await UniTask.Delay((int)(GameTiming.Battle.DealMidPause * 1000), cancellationToken: t_ct).SuppressCancellationThrow();
        if (t_cancelled) return;

        var t_seq2 = DOTween.Sequence()
            .Join(transform.DOMove(_dest, t_half).SetEase(Ease.InCubic))
            .Join(transform.DOScale(1f, t_half).SetEase(Ease.InCubic))
            .Join(transform.DORotate(new Vector3(0f, 360f, 0f), t_half, RotateMode.FastBeyond360));
        t_cancelled = await t_seq2.ToUniTask(cancellationToken: t_ct).SuppressCancellationThrow();
        if (t_cancelled) return;

        transform.localRotation = Quaternion.identity;
        transform.localScale    = Vector3.one;
    }
}
