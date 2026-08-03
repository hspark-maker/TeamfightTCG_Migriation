using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;

public class CardAnimator : MonoBehaviour
{
    [SerializeField] SpriteRenderer hitOverlay;
    [SerializeField] SpriteRenderer dieOverlay;
    [SerializeField] HitEffectView  hitEffect;   // 피격 붐(카드 자식). 없으면 무시.
    [SerializeField] HitEffectView  healEffect;  // 회복 붐(카드 자식, isHeal=1). 없으면 무시.

    // 타이밍은 BattleTimingConfig 단일 진실원(배율 적용).
    float moveDuration => GameTiming.Battle.CardMoveDuration;

    // 시네마에서 여러 장을 세울 때 좌우 간격(월드). 슬롯 간격(2.0)보다 좁게 모아 붙인다.
    [SerializeField] float cinemaSpacing = 1.6f;

    [Header("Hit Twitch (피격 순간 떨림)")]
    // 흔들 대상은 카드 **비주얼 자식**(예: "Card"). 루트를 흔들면 이동/박치기 트윈과 충돌한다.
    [SerializeField] Transform twitchTarget;
    [SerializeField] float twitchDistance = 0.12f;   // 흔들림 폭(월드)
    [SerializeField] float twitchDuration = 0.16f;   // 길이(초, 전역 배속 적용)
    [SerializeField] float twitchAngle    = 5f;      // 함께 흔들리는 각도(0이면 회전 없음)

    Vector3    twitchHome;      // 떨림 기준 자세 — 연타/중단 시 여기로 되돌린다
    Quaternion twitchHomeRot;

    CardInstance boundCard;
    BattleFieldView fieldView;   // 이 카드가 속한 필드(시네마 집결 좌표의 기준). 없으면 폴백.
    Vector3 slotPosition;
    readonly System.Collections.Generic.HashSet<SpriteRenderer> fadeExcludes
        = new System.Collections.Generic.HashSet<SpriteRenderer>();

    SpriteRenderer[] cachedRenderers;
    TMP_Text[]       cachedTexts;

    public void ExcludeFromFade(SpriteRenderer _sr) { if (_sr != null) this.fadeExcludes.Add(_sr); }

    /// <summary>이 카드가 **최종적으로 도달할** 알파. 페이드는 트윈이라 진행 중엔 렌더러의 현재 알파가
    /// 목표와 다르다 — 그 사이에 태어난 자식(키워드 아이콘/시너지 배지)을 현재 알파로 맞추면
    /// 진행 중인 트윈에는 못 끼고 중간값에 그대로 굳는다(공격 후 아이콘만 흐린 채 남던 원인).
    /// 새로 만든 자식은 이 목표값으로 맞춘다. 알파를 바꾸는 경로는 전부 이 값을 같이 갱신할 것.</summary>
    public float FadeTarget { get; private set; } = 1f;

    /// <summary>피격/회복 연출(HitEffect·HealEffect) 하위인가. 자체 페이드 시퀀스를 가지므로 카드 페이드 대상에서 제외 —
    /// 안 그러면 fade의 DOKill이 붐/숫자 트윈을 죽이고 dim alpha로 덮어써 연출이 튄다.</summary>
    bool IsHitEffectPart(Component _c)
    {
        if (_c == null) return false;
        if (this.hitEffect  != null && _c.transform.IsChildOf(this.hitEffect.transform))  return true;
        if (this.healEffect != null && _c.transform.IsChildOf(this.healEffect.transform)) return true;
        return false;
    }

    public Vector3 SlotPosition => this.slotPosition;

    void Awake()
    {
        this.slotPosition = transform.position;
        this.fieldView    = GetComponentInParent<BattleFieldView>();

        // 떨림 대상 미배선이면 첫 자식(카드 비주얼 루트)을 쓴다 — 프리팹 배선 없이도 동작하게.
        if (this.twitchTarget == null && transform.childCount > 0) this.twitchTarget = transform.GetChild(0);
        if (this.twitchTarget != null)
        {
            this.twitchHome    = this.twitchTarget.localPosition;
            this.twitchHomeRot = this.twitchTarget.localRotation;
        }
    }

    /// <summary>이 카드가 속한 필드의 가운데 자리(월드). 시네마 집결 지점 —
    /// 카메라(화면 중앙)가 아니라 필드 격자가 기준이다. 필드를 못 찾으면 자기 슬롯 y/z에 x만 0.</summary>
    Vector3 FieldCenter => this.fieldView != null
        ? this.fieldView.FieldCenter
        : new Vector3(0f, this.slotPosition.y, this.slotPosition.z);

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

    /// <summary>페이드 대상 캐시 갱신. <b>비활성 오브젝트까지 포함</b>한다(_includeInactive: true).
    ///
    /// 조건부로 켜지는 장식(키워드 프레임 등)은 평소 비활성이라, 제외하면 페이드에 참여하지 못하고
    /// 알파 1로 남는다. 그 상태에서 슬롯이 재사용되면 — 죽은 카드가 알파 0으로 사라진 자리에
    /// 새 카드의 프레임만 `SetActive(true)` 되며 **몸통 없이 프레임만 먼저 보인다**.
    /// 카드의 알파는 카드에 속한 모든 렌더러에 걸린다는 불변식을 여기서 지킨다.</summary>
    void RefreshVisualCache()
    {
        this.cachedRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        this.cachedTexts     = GetComponentsInChildren<TMP_Text>(true);
    }

    public void SetBoundCard(CardInstance _card) => this.boundCard = _card;

    /// <summary>진행 중인 피격/회복 연출(붐/숫자) 즉시 제거. 슬롯에 새 카드가 들어오기 전 호출 → 잔여 연출 이월 방지.</summary>
    public void ResetHitEffect()
    {
        this.hitEffect?.Stop();
        this.healEffect?.Stop();
    }

    /// <summary>회복 연출(붐 + "+N"). 순수 연출 — 게임상태/RNG 무관, 활성 클라 시각 표시만.</summary>
    public void PlayHealEffect(int _amount) => this.healEffect?.Play(_amount);

    // ── Move ─────────────────────────────────────────────────────────────

    /// <summary>내 필드의 가운데로. 공격자·피격자가 각자 자기 필드 중앙에 선다(세로 줄은 그대로 유지).
    /// z는 슬롯 z 그대로 — 카메라 쪽으로 띄우는 z 이동은 AttackSequence가 따로 트윈한다.</summary>
    public async UniTask MoveToCenter()
    {
        Vector3 t_wc = new Vector3(FieldCenter.x, this.slotPosition.y, this.slotPosition.z);
        FadeSpriteRenderers(1f);
        await MoveTo(t_wc);
    }

    public async UniTask MoveToCinemaSlot()
    {
        int t_slot = Mathf.Clamp(this.boundCard?.slotIndex ?? 1, 0, BattleField.SLOT_COUNT - 1);
        float t_offset = (t_slot - (BattleField.SLOT_COUNT - 1) * 0.5f) * this.cinemaSpacing;
        Vector3 t_wc = new Vector3(FieldCenter.x + t_offset, this.slotPosition.y, this.slotPosition.z);
        FadeSpriteRenderers(1f);
        await MoveTo(t_wc);
    }

    public UniTask MoveToSlot() => MoveTo(this.slotPosition);

    /// <summary>여러 장(무쌍 스플래시 등)을 자기 필드 중앙 기준 좌우 대칭으로 세운다.</summary>
    public async UniTask MoveToCinemaPosition(int _posIndex, int _totalCount)
    {
        float t_offset = (_posIndex - (_totalCount - 1) * 0.5f) * this.cinemaSpacing;
        Vector3 t_wc = new Vector3(FieldCenter.x + t_offset, this.slotPosition.y, this.slotPosition.z);
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
        this.FadeTarget = _alpha;
        RefreshVisualCache();

        foreach (SpriteRenderer t_sr in this.cachedRenderers)
        {
            if (t_sr == this.hitOverlay) continue;
            if (t_sr == this.dieOverlay) continue;
            if (IsHitEffectPart(t_sr)) continue;
            if (this.fadeExcludes.Contains(t_sr)) continue;
            t_sr.DOKill();
            t_sr.DOFade(_alpha, _duration).SetLink(gameObject);
        }
        foreach (TMP_Text t_tmp in this.cachedTexts)
        {
            if (IsHitEffectPart(t_tmp)) continue;
            t_tmp.DOKill();
            t_tmp.DOFade(_alpha, _duration).SetLink(gameObject);
        }
    }

    void FadeSpriteRenderers(float _alpha)
    {
        this.FadeTarget = _alpha;
        RefreshVisualCache();

        foreach (SpriteRenderer t_sr in this.cachedRenderers)
        {
            if (t_sr == this.hitOverlay) continue;
            if (t_sr == this.dieOverlay) continue;
            if (IsHitEffectPart(t_sr)) continue;
            t_sr.DOKill();
            t_sr.DOFade(_alpha, this.moveDuration).SetLink(gameObject);
        }
        foreach (TMP_Text t_tmp in this.cachedTexts)
        {
            if (IsHitEffectPart(t_tmp)) continue;
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

    /// <summary>피격 순간의 짧은 떨림. **카드 루트가 아니라 비주얼 자식을 흔든다** —
    /// 루트 position/rotation은 이동·박치기·시네마 트윈이 쓰고 있어서 거기에 흔들기를 얹으면
    /// 서로 덮어쓰거나 DOKill에 잘려 카드가 엉뚱한 자리에 굳는다.
    /// 미배선이면(twitchTarget=null) Awake에서 첫 자식을 잡고, 그것도 없으면 조용히 생략한다.</summary>
    void PlayHitTwitch()
    {
        Transform t_t = this.twitchTarget;
        if (t_t == null || this.twitchDistance <= 0f || this.twitchDuration <= 0f) return;

        float t_dur = this.twitchDuration * GameTiming.Factor;   // 전역 배속 반영(다른 연출과 같은 기준)

        // 직전 떨림이 남아 있으면 끊고 기준 자세로 되돌린 뒤 다시 — 연타 피격에서 누적되어 밀리지 않게.
        t_t.DOKill();
        t_t.localPosition = this.twitchHome;
        t_t.localRotation = this.twitchHomeRot;

        t_t.DOShakePosition(t_dur, this.twitchDistance, vibrato: 18, randomness: 40f, fadeOut: true)
           .SetLink(gameObject)
           .OnComplete(() => { t_t.localPosition = this.twitchHome; });

        if (this.twitchAngle > 0f)
            t_t.DOPunchRotation(new Vector3(0f, 0f, this.twitchAngle), t_dur, vibrato: 8, elasticity: 0.6f)
               .SetLink(gameObject)
               .OnComplete(() => { t_t.localRotation = this.twitchHomeRot; });
    }

    public async UniTask PlayHitAnim(float _duration = -1f, int _damage = 0)
    {
        if (_duration < 0f) _duration = GameTiming.Battle.HitDuration;
        SoundManager.Instance?.PlayHit();
        this.hitEffect?.Play(_damage);   // 피격 붐 + 데미지 숫자(있으면). 위치=이 카드.
        PlayHitTwitch();                 // 맞은 순간 잠깐 떨림(붐과 같은 프레임에 시작)
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
        this.FadeTarget = 0f;   // 사망은 알파 0으로 끝난다(아래 주석 참조) — 그 사이 태어난 자식도 0으로
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

        await PlayDealToMid(_from, _mid, _dest, _duration);
        if (this == null) return;

        bool t_cancelled = await UniTask.Delay((int)(GameTiming.Battle.DealMidPause * 1000),
                cancellationToken: this.GetCancellationTokenOnDestroy())
            .SuppressCancellationThrow();
        if (t_cancelled) return;

        await PlayDealToSlot(_dest, _duration);
    }

    /// <summary>배치 연출 전반부: 화면 밖 → 중앙에서 확대. **중앙에 멈춘 채로 끝난다** —
    /// 등장 컷씬이 있는 카드는 이 상태로 컷씬을 보여주고, 끝난 뒤 PlayDealToSlot으로 이어 붙인다.
    /// 컷씬이 없으면 PlayDealAnim이 중간 정지만 두고 곧바로 이어 붙인다(예전과 같은 흐름).</summary>
    public async UniTask PlayDealToMid(Vector3 _from, Vector3 _mid, Vector3 _dest, float _duration = -1f)
    {
        if (_duration < 0f) _duration = GameTiming.Battle.DealAnimDuration;
        this.FadeTarget = 1f;   // 배치는 알파 1로 리셋하고 시작(아래 색 리셋과 짝)
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

        var t_seq = DOTween.Sequence()
            .Join(transform.DOMove(_mid, t_half).SetEase(Ease.OutCubic))
            .Join(transform.DOScale(1.5f, t_half).SetEase(Ease.OutCubic));
        await t_seq.ToUniTask(cancellationToken: t_ct).SuppressCancellationThrow();
    }

    /// <summary>배치 연출 후반부: 중앙 → 슬롯(축소 + 한 바퀴). 중앙에 서 있는 상태에서만 의미가 있다.</summary>
    public async UniTask PlayDealToSlot(Vector3 _dest, float _duration = -1f)
    {
        if (_duration < 0f) _duration = GameTiming.Battle.DealAnimDuration;

        float t_half = _duration * 0.5f;
        var   t_ct   = this.GetCancellationTokenOnDestroy();

        var t_seq = DOTween.Sequence()
            .Join(transform.DOMove(_dest, t_half).SetEase(Ease.InCubic))
            .Join(transform.DOScale(1f, t_half).SetEase(Ease.InCubic))
            .Join(transform.DORotate(new Vector3(0f, 360f, 0f), t_half, RotateMode.FastBeyond360));
        bool t_cancelled = await t_seq.ToUniTask(cancellationToken: t_ct).SuppressCancellationThrow();
        if (t_cancelled || this == null) return;

        transform.localRotation = Quaternion.identity;
        transform.localScale    = Vector3.one;
    }
}
