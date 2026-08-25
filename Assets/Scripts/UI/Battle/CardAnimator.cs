using Cysharp.Threading.Tasks;
using DG.Tweening;
using TMPro;
using UnityEngine;

public class CardAnimator : MonoBehaviour
{
    [SerializeField] SpriteRenderer hitOverlay;
    [SerializeField] HitEffectView hitEffect;

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

    [Header("Hit Knockback (피해에 비례해 뒤로 밀림)")]
    // 세기는 HitImpact.Strength01 하나가 정한다 — 흔들림과 같은 곡선을 써야 "센 공격"이 화면·카드에서 같이 읽힌다.
    [SerializeField] float knockbackMax      = 0.3f;    // 최대 세기일 때 밀리는 거리(월드). 0이면 반동 없음
    [SerializeField] float knockbackOutTime  = 0.06f;   // 밀려나는 시간(초, 전역 배속 적용)
    [SerializeField] float knockbackBackTime = 0.14f;   // 제자리로 돌아오는 시간(밀림보다 길어야 '되밀린다'로 읽힌다)

    // 밀림+떨림+복귀를 한 줄로 묶은 시퀀스. 필드로 들고 있는 이유 — 시퀀스 **안**의 트윈은
    // 대상 DOKill로 끊으면 안 된다(DOTween에서 시퀀스 내부 트윈 개별 Kill은 정의되지 않은 동작).
    // 끊을 땐 항상 KillHitTwitch()로 시퀀스부터 죽인다.
    Sequence hitTwitchSeq;

    Vector3    twitchHome;      // 떨림 기준 자세 — 연타/중단 시 여기로 되돌린다
    Quaternion twitchHomeRot;
    Vector3    twitchHomeScale;   // 리프트가 곱해질 기준 크기(프리팹에서 1이 아닐 수 있다)
    Vector3    twitchBaseHome;    // 리프트를 얹기 전의 원래 자세. twitchHome을 오르내리며 누적시키지 않으려고 따로 둔다

    [Header("롱프레스 리프트 (정보창 볼 때 살짝 떠오름)")]
    // 등장 컷씬(PlayDealToMid)은 화면 중앙에서 1.5배까지 키운다 — 이건 그보다 훨씬 얕은 '살짝'이다.
    [SerializeField] float longPressLiftY     = 0.2f;    // 위로 뜨는 거리(월드)
    [SerializeField] float longPressLiftScale = 1.06f;   // 뜬 동안의 크기 배율
    [SerializeField] float longPressLiftTime  = 0.12f;   // 뜨고/내려오는 시간(초)

    bool liftActive;

    CardInstance boundCard;
    BattleFieldView fieldView;   // 이 카드가 속한 필드(시네마 집결 좌표의 기준). 없으면 폴백.
    Vector3 slotPosition;
    readonly System.Collections.Generic.HashSet<SpriteRenderer> fadeExcludes
        = new System.Collections.Generic.HashSet<SpriteRenderer>();

    SpriteRenderer[] cachedRenderers;
    TMP_Text[]       cachedTexts;

    // 각 렌더러가 "완전히 보일 때" 가질 알파(CardFadeAlpha). 페이드는 절대값이 아니라 이 값과의 곱으로 건다 —
    // 안 그러면 반투명이어야 할 배경판(이름·키워드 뒤)까지 알파 1로 올라간다. 없는 렌더러는 1.
    float[] rendererBaseAlpha;
    float[] textBaseAlpha;

    public void ExcludeFromFade(SpriteRenderer _sr) { if (_sr != null) this.fadeExcludes.Add(_sr); }

    /// <summary>이 카드가 **최종적으로 도달할** 알파. 페이드는 트윈이라 진행 중엔 렌더러의 현재 알파가
    /// 목표와 다르다 — 그 사이에 태어난 자식(키워드 아이콘/시너지 배지)을 현재 알파로 맞추면
    /// 진행 중인 트윈에는 못 끼고 중간값에 그대로 굳는다(공격 후 아이콘만 흐린 채 남던 원인).
    /// 새로 만든 자식은 이 목표값으로 맞춘다. 알파를 바꾸는 경로는 전부 이 값을 같이 갱신할 것.</summary>
    public float FadeTarget { get; private set; } = 1f;

    bool IsHitEffectPart(Component _component)
        => _component != null && this.hitEffect != null
            && _component.transform.IsChildOf(this.hitEffect.transform);

    public Vector3 SlotPosition => this.slotPosition;

    void Awake()
    {
        this.slotPosition = transform.position;
        this.fieldView    = GetComponentInParent<BattleFieldView>();

        // 떨림 대상 미배선이면 첫 자식(카드 비주얼 루트)을 쓴다 — 프리팹 배선 없이도 동작하게.
        if (this.twitchTarget == null && transform.childCount > 0) this.twitchTarget = transform.GetChild(0);
        if (this.twitchTarget != null)
        {
            this.twitchHome      = this.twitchTarget.localPosition;
            this.twitchBaseHome  = this.twitchHome;
            this.twitchHomeRot   = this.twitchTarget.localRotation;
            this.twitchHomeScale = this.twitchTarget.localScale;
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

        // 기준 알파는 **현재 색이 아니라** 태그(CardFadeAlpha)에서 읽는다 — 페이드 도중 캐시를 다시 만들어도
        // 중간값이 기준으로 굳지 않는다.
        this.rendererBaseAlpha = new float[this.cachedRenderers.Length];
        for (int t_i = 0; t_i < this.cachedRenderers.Length; t_i++)
            this.rendererBaseAlpha[t_i] = CardFadeAlpha.Of(this.cachedRenderers[t_i]);

        this.textBaseAlpha = new float[this.cachedTexts.Length];
        for (int t_i = 0; t_i < this.cachedTexts.Length; t_i++)
            this.textBaseAlpha[t_i] = CardFadeAlpha.Of(this.cachedTexts[t_i]);
    }

    public void SetBoundCard(CardInstance _card) => this.boundCard = _card;

    public void ResetHitEffect() => this.hitEffect?.Stop();

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

        for (int t_i = 0; t_i < this.cachedRenderers.Length; t_i++)
        {
            SpriteRenderer t_sr = this.cachedRenderers[t_i];
            if (t_sr == this.hitOverlay) continue;
            if (IsHitEffectPart(t_sr)) continue;
            if (this.fadeExcludes.Contains(t_sr)) continue;
            t_sr.DOKill();
            t_sr.DOFade(_alpha * this.rendererBaseAlpha[t_i], _duration).SetLink(gameObject);
        }
        for (int t_i = 0; t_i < this.cachedTexts.Length; t_i++)
        {
            TMP_Text t_tmp = this.cachedTexts[t_i];
            if (IsHitEffectPart(t_tmp)) continue;
            t_tmp.DOKill();
            t_tmp.DOFade(_alpha * this.textBaseAlpha[t_i], _duration).SetLink(gameObject);
        }
    }

    void FadeSpriteRenderers(float _alpha)
    {
        this.FadeTarget = _alpha;
        RefreshVisualCache();

        for (int t_i = 0; t_i < this.cachedRenderers.Length; t_i++)
        {
            SpriteRenderer t_sr = this.cachedRenderers[t_i];
            if (t_sr == this.hitOverlay) continue;
            if (IsHitEffectPart(t_sr)) continue;
            t_sr.DOKill();
            t_sr.DOFade(_alpha * this.rendererBaseAlpha[t_i], this.moveDuration).SetLink(gameObject);
        }
        for (int t_i = 0; t_i < this.cachedTexts.Length; t_i++)
        {
            TMP_Text t_tmp = this.cachedTexts[t_i];
            if (IsHitEffectPart(t_tmp)) continue;
            t_tmp.DOKill();
            t_tmp.DOFade(_alpha * this.textBaseAlpha[t_i], this.moveDuration).SetLink(gameObject);
        }
    }

    // ── Hit / Death / Deal ───────────────────────────────────────────────

    /// <summary>피격 순간의 짧은 떨림. **카드 루트가 아니라 비주얼 자식을 흔든다** —
    /// 루트 position/rotation은 이동·박치기·시네마 트윈이 쓰고 있어서 거기에 흔들기를 얹으면
    /// 서로 덮어쓰거나 DOKill에 잘려 카드가 엉뚱한 자리에 굳는다.
    /// 미배선이면(twitchTarget=null) Awake에서 첫 자식을 잡고, 그것도 없으면 조용히 생략한다.</summary>
    /// <param name="_damage">이번에 받은 피해. 반동 거리·회전 폭이 여기에 비례한다(0이면 반동 없이 예전 떨림 그대로).</param>
    /// <param name="_awayDir">"때린 쪽의 반대" 월드 방향. 없으면(환경 피해 등) 밀리지 않고 떨기만 한다.</param>
    void PlayHitTwitch(int _damage, Vector3 _awayDir)
    {
        Transform t_t = this.twitchTarget;
        if (t_t == null || this.twitchDuration <= 0f) return;

        float t_dur  = this.twitchDuration * GameTiming.Factor;   // 전역 배속 반영(다른 연출과 같은 기준)
        // 세기 판정은 HitImpact 단독(화면 흔들림과 공용). 기준은 이 카드의 최대 체력 —
        // 데미지는 이미 적용된 뒤지만 maxHp는 피해로 변하지 않아 그대로 써도 된다.
        float t_s01  = HitImpact.Strength01(_damage, this.boundCard);

        // 직전 떨림이 남아 있으면 끊고 기준 자세로 되돌린 뒤 다시 — 연타 피격에서 누적되어 밀리지 않게.
        KillHitTwitch();
        t_t.localPosition = this.twitchHome;
        t_t.localRotation = this.twitchHomeRot;

        // 밀림 방향은 부모 기준으로 변환한다 — twitchTarget은 localPosition으로 움직이는데
        // 카드 루트는 조준/돌진 중 기울어 있어서 월드 방향을 그대로 넣으면 엉뚱한 쪽으로 밀린다.
        Vector3 t_back = Vector3.zero;
        if (this.knockbackMax > 0f && t_s01 > 0f && _awayDir.sqrMagnitude > 1e-6f)
        {
            Vector3 t_dir = _awayDir.normalized;
            if (t_t.parent != null) t_dir = t_t.parent.InverseTransformDirection(t_dir);
            t_dir.z = 0f;   // 화면 평면 안에서만 밀린다(시네마 중 z가 벌어져 있어도 카메라 쪽으로 안 튄다)
            if (t_dir.sqrMagnitude > 1e-6f)
                t_back = t_dir.normalized * (this.knockbackMax * t_s01);
        }

        // 순서: 밀려나고 → **밀린 자리에서** 부들부들 → 제자리. 밀림과 떨림을 겹치면 같은 localPosition을
        // 두 트윈이 한 프레임에 서로 덮어써서 카드가 지직거린다 — 그래서 겹치지 않고 잇는다.
        this.hitTwitchSeq = DOTween.Sequence().SetLink(gameObject);

        if (t_back != Vector3.zero)
            this.hitTwitchSeq.Append(t_t.DOLocalMove(this.twitchHome + t_back,
                                                     this.knockbackOutTime * GameTiming.Factor)
                                        .SetEase(Ease.OutQuad));

        if (this.twitchDistance > 0f)
            this.hitTwitchSeq.Append(t_t.DOShakePosition(t_dur, this.twitchDistance,
                                                         vibrato: 18, randomness: 40f, fadeOut: true));

        if (t_back != Vector3.zero)
            this.hitTwitchSeq.Append(t_t.DOLocalMove(this.twitchHome,
                                                     this.knockbackBackTime * GameTiming.Factor)
                                        .SetEase(Ease.OutBack));

        this.hitTwitchSeq.OnComplete(() => { if (t_t != null) t_t.localPosition = this.twitchHome; });

        // 회전 펀치는 위치와 다른 축이라 겹쳐도 안전 — 세기만 피해에 따라 얕게/깊게.
        if (this.twitchAngle > 0f)
            t_t.DOPunchRotation(new Vector3(0f, 0f, this.twitchAngle * Mathf.Lerp(0.7f, 1.5f, t_s01)),
                                t_dur, vibrato: 8, elasticity: 0.6f)
               .SetLink(gameObject)
               .OnComplete(() => { t_t.localRotation = this.twitchHomeRot; });
    }

    /// <summary>진행 중인 밀림/떨림을 끊는다. 시퀀스를 **먼저** 죽인 뒤 대상 트윈을 정리해야 한다 —
    /// 순서를 뒤집으면 살아 있는 시퀀스가 이미 죽은 내부 트윈을 계속 돌린다.</summary>
    void KillHitTwitch()
    {
        this.hitTwitchSeq?.Kill();
        this.hitTwitchSeq = null;
        if (this.twitchTarget != null) this.twitchTarget.DOKill();
    }

    /// <summary>롱프레스로 카드 정보를 보는 동안 카드가 살짝 떠오른다. 손을 떼면 원래 자세로.
    ///
    /// 떨림과 같은 **비주얼 자식**을 쓰되, 떨림의 기준 자세(twitchHome)까지 함께 올린다 —
    /// 그래야 뜬 상태에서 맞아도 떨림이 끝난 뒤 슬롯이 아니라 '뜬 위치'로 되돌아간다.
    /// 루트가 아닌 이유는 PlayHitTwitch 주석과 같다(이동·박치기 트윈과 충돌).</summary>
    public void SetLongPressLift(bool _on)
    {
        Transform t_t = this.twitchTarget;
        if (t_t == null || this.liftActive == _on) return;
        this.liftActive = _on;

        this.twitchHome = this.twitchBaseHome + (_on ? new Vector3(0f, this.longPressLiftY, 0f) : Vector3.zero);

        float t_dur = Mathf.Max(0.01f, this.longPressLiftTime) * GameTiming.Factor;

        // 진행 중인 떨림/밀림은 끊고 회전만 기준으로 되돌린다 — 위치/크기는 아래 트윈이 바로 이어받는다.
        KillHitTwitch();
        t_t.localRotation = this.twitchHomeRot;

        t_t.DOLocalMove(this.twitchHome, t_dur)
           .SetEase(_on ? Ease.OutCubic : Ease.InCubic)
           .SetLink(gameObject);

        t_t.DOScale(_on ? this.twitchHomeScale * this.longPressLiftScale : this.twitchHomeScale, t_dur)
           .SetEase(_on ? Ease.OutCubic : Ease.InCubic)
           .SetLink(gameObject);
    }

    /// <summary>리프트를 트윈 없이 즉시 없앤다. 슬롯이 재사용될 때(배치 연출 시작) 부르는 안전망 —
    /// 손을 떼기 전에 카드가 죽거나 갈려나가면 뜬 자세가 다음 카드에 그대로 남는다.</summary>
    void ResetLongPressLift()
    {
        if (!this.liftActive) return;
        this.liftActive = false;
        this.twitchHome = this.twitchBaseHome;
        if (this.twitchTarget == null) return;

        KillHitTwitch();
        this.twitchTarget.localPosition = this.twitchBaseHome;
        this.twitchTarget.localScale    = this.twitchHomeScale;
        this.twitchTarget.localRotation = this.twitchHomeRot;
    }

    public async UniTask PlayHitAnim(float _duration = -1f, int _damage = 0, Vector3 _awayDir = default)
    {
        if (_duration < 0f) _duration = GameTiming.Battle.HitDuration;
        SoundManager.Instance?.PlayHit();
        this.hitEffect?.Play(_damage);
        PlayHitTwitch(_damage, _awayDir);
        if (this.hitOverlay == null) return;
        this.hitOverlay.DOKill();
        Color t_c = this.hitOverlay.color;
        t_c.a = 0f;
        this.hitOverlay.color = t_c;
        await this.hitOverlay.DOFade(0.5f, _duration).SetLink(gameObject).ToUniTask();
        await this.hitOverlay.DOFade(0f, _duration).SetLink(gameObject).ToUniTask();
    }

    // 사망 연출 형태값(거리·배율이라 시간이 아니다 → BattleTimingConfig가 아니라 여기).
    const float DEATH_LIFT_DISTANCE = 0.18f;   // 사라지며 떠오르는 거리(월드)
    const float DEATH_POP_SCALE     = 1.18f;   // 바닥 파동이 터지는 순간의 최대 배율(부풀었다 터지는 그림)
    const float DEATH_SHRINK        = 0.45f;   // 팝 뒤 급격히 줄어드는 끝 배율
    // 팝 이후 남은 시간 중 **실제로 줄어드는** 비율. 나머지는 정지 — "빠르게" 줄어야 터진 것으로 읽힌다.
    const float DEATH_SHRINK_RATIO  = 0.45f;

    /// <summary>사망 연출. 살짝 떠오르며 <b>한 번 부풀었다가</b> 급격히 축소 + 페이드아웃.
    /// 흰 플래시는 없다 — 카드 실루엣을 덮는 판이라 한 프레임 번쩍임으로만 읽혀 걷어냈다(DieOverlay 배선째 삭제).
    /// 부푸는 정점은 바닥 파동(DeathNova)이 터지는 시각(<c>DeathNovaAt</c>)에 맞춘다 — 파동과 카드가
    /// 같은 순간에 터져야 "여기서 터져 사라졌다"로 읽힌다. 파동을 쏘는 쪽은 <see cref="CardView"/>이고,
    /// 두 곳이 같은 <c>GameTiming</c> 값을 공유해 박자를 맞춘다.
    /// 별가루·바닥 파동 파티클은 여기 없다 — 스폰 좌표와 정렬 레이어를 아는 <see cref="CardView"/>가 쏜다.
    /// 여기는 트윈만 소유한다.</summary>
    public async UniTask PlayDeathAnim(float _duration = -1f)
    {
        if (_duration < 0f) _duration = GameTiming.Battle.DeathDuration;
        // 부유의 출발점은 지금 있는 자리지만, **되돌릴 자리는 슬롯**이다(아래 finally).
        // 진입 시점 좌표를 복원 기준으로 삼으면 어긋난 좌표가 새 기준이 되고, 슬롯은 재사용되므로
        // 그 어긋남이 다음 카드에 그대로 이월된다(카드가 조금씩 밀리던 원인).
        Vector3 t_liftFrom = transform.localPosition;
        this.FadeTarget = 0f;   // 사망은 알파 0으로 끝난다(아래 주석 참조) — 그 사이 태어난 자식도 0으로
        RefreshVisualCache();

        // 직전 피격 흰 판(HitOverlay)이 남은 채로 죽으면 축소·페이드에서 제외된 그 판만 남아
        // 흰 잔상으로 보인다. 사망 진입점에서 트윈과 알파를 확실히 정리한다.
        if (this.hitOverlay != null)
        {
            this.hitOverlay.DOKill();
            Color t_hitColor = this.hitOverlay.color;
            t_hitColor.a = 0f;
            this.hitOverlay.color = t_hitColor;
        }

        SoundManager.Instance?.PlayDeath();
        SoundManager.Instance?.PlayDeathVoice(this.boundCard?.data?.deathVoices);

        // 팝의 정점 = 바닥 파동이 터지는 시각. 그 뒤 남은 시간의 일부만 써서 확 줄인다.
        float t_popDur    = Mathf.Clamp(GameTiming.Battle.DeathNovaAt, 0.01f, _duration);
        float t_shrinkDur = Mathf.Max(0.01f, (_duration - t_popDur) * DEATH_SHRINK_RATIO);

        var t_seq = DOTween.Sequence()
            .SetLink(gameObject)
            .Join(transform.DOLocalMoveY(t_liftFrom.y + DEATH_LIFT_DISTANCE,
                                         Mathf.Min(GameTiming.Battle.DeathLift, _duration))
                           .SetEase(Ease.OutCubic))
            // 두 스케일 트윈은 시간이 겹치지 않는다(두 번째가 첫 번째가 끝나는 지점에서 시작).
            .Insert(0f, transform.DOScale(Vector3.one * DEATH_POP_SCALE, t_popDur).SetEase(Ease.OutQuad))
            .Insert(t_popDur, transform.DOScale(Vector3.one * DEATH_SHRINK, t_shrinkDur)
                                       .SetEase(Ease.OutQuint));
        // 페이드는 **줄어드는 구간에 붙인다** — 부푸는 동안은 또렷하게 보이고 터지면서 함께 사라져야
        // "부풀었다 터졌다"로 읽힌다. 부푸는 동안 이미 흐려지면 그냥 흐지부지 사라진 것이 된다.
        foreach (SpriteRenderer t_sr in this.cachedRenderers)
        {
            if (t_sr == this.hitOverlay) continue;
            _ = t_seq.Insert(t_popDur, t_sr.DOFade(0f, t_shrinkDur).SetEase(Ease.InQuad));
        }
        foreach (TMP_Text t_tmp in this.cachedTexts)
            _ = t_seq.Insert(t_popDur, t_tmp.DOFade(0f, t_shrinkDur).SetEase(Ease.InQuad));

        // 연출 길이의 주인은 여전히 _duration이다. 팝+축소가 그보다 먼저 끝나도 시퀀스를 끝까지 붙잡는다 —
        // 호출부는 시퀀스 완료를 사망 완료로 본다.
        _ = t_seq.InsertCallback(_duration, () => { });

        try
        {
            await t_seq.ToUniTask();
        }
        finally
        {
            if (this != null)
            {
                // 슬롯(=Awake에서 잡은 원래 자리)과 크기 1로 되돌린다. 진입 시점 값이 아니라 **알려진 정상값**이라
                // 어떤 이유로 어긋난 채 죽어도 그 어긋남이 슬롯에 남지 않는다.
                // 알파는 0으로 남긴다 — 1로 되돌리면 죽은 카드가 잠깐 되살아나 보인 뒤 HideSlot이 다시 숨긴다(플래시).
                // 슬롯 재사용 시 알파=1 복원은 PlayDealAnim(시작 시 리셋)이 담당한다.
                transform.position   = this.slotPosition;
                transform.localScale = Vector3.one;
            }
        }
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
        // 호출부가 await 사이에 멈춘 동안 카드가 풀에 반납·파괴됐을 수 있다(씬 전환 정리).
        // RefreshVisualCache의 GetComponentsInChildren는 파괴된 컴포넌트에서 예외를 던지므로 여기서 끊는다.
        if (this == null) return;

        if (_duration < 0f) _duration = GameTiming.Battle.DealAnimDuration;
        this.FadeTarget = 1f;   // 배치는 알파 1로 리셋하고 시작(아래 색 리셋과 짝)
        ResetLongPressLift();   // 이전 카드가 뜬 채로 사라졌을 수 있다
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
        for (int t_i = 0; t_i < this.cachedRenderers.Length; t_i++)
        {
            SpriteRenderer t_sr = this.cachedRenderers[t_i];
            if (t_sr == this.hitOverlay || this.fadeExcludes.Contains(t_sr)) continue;
            t_sr.DOKill();
            Color t_c = t_sr.color; t_c.a = this.rendererBaseAlpha[t_i]; t_sr.color = t_c;
        }
        for (int t_i = 0; t_i < this.cachedTexts.Length; t_i++)
        {
            TMP_Text t_tmp = this.cachedTexts[t_i];
            t_tmp.DOKill();
            Color t_c = t_tmp.color; t_c.a = this.textBaseAlpha[t_i]; t_tmp.color = t_c;
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
