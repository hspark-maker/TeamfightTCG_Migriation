using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class BattleCamera : MonoBehaviour
{
    public static BattleCamera Instance { get; private set; }

    // 시네마 진입 시 카메라가 뒤로 빠지는 거리 = **기준 거리 대비 비율**.
    // 절대값으로 두면 화면 비율에 따라 카메라 거리가 달라졌을 때(BattleCameraFit) 확대감이 기기마다 어긋난다.
    // (구 cinemaZoom = DOOrthoSize 트윈은 제거 — 이 카메라는 퍼스펙티브라 아무 효과가 없었다.)
    [SerializeField, Range(0f, 0.6f)] float cinemaZMoveRatio = 0.18f;   // 기준 거리 11 기준 약 2

    [Header("롱프레스(카드 정보) 카메라 뒤로 빼기")]
    // 정보를 보는 동안 화면이 뒤로 물러난다(카드에서 멀어짐). 어둡기/흐림보다 **먼저** 출발해
    // 천천히 빠지는 게 자연스럽다 — 시작 시점은 CardInputController가 정한다.
    [SerializeField] float longPressPullBackZ    = 0.35f;   // 뒤로 물러나는 거리(양수 = 멀어짐)
    [SerializeField] float longPressLiftDuration = 0.32f;

    [Header("승패 확정 여운(카메라 미세 줌)")]
    // 시네마와 같은 **기준 거리 대비 비율**. 결과 여운은 "다가간다"가 읽힐 정도만 — 시네마의 1/4 수준.
    [SerializeField, Range(0f, 0.2f)] float resultZMoveRatio = 0.045f;

    [Header("피니시 클로즈업(승부를 가른 타격)")]
    // 여운(4.5%)으로는 "결정타"가 안 읽힌다 — 이쪽은 시네마(18%)보다도 훨씬 깊게 들어간다.
    [SerializeField, Range(0f, 0.6f)] float finishZMoveRatio = 0.32f;
    // 타격 지점을 화면 중앙으로 얼마나 끌어오는가(1 = 완전 중앙). 깊게 당길수록 가시 영역이 좁아지므로
    // 이 값도 같이 올려야 한다 — 안 그러면 가장자리 슬롯에서 정작 죽는 카드가 화면 밖으로 밀린다.
    [SerializeField, Range(0f, 1f)] float finishFollowXY = 0.92f;

    [Header("매치포인트 공격 접근 줌")]
    [SerializeField, Range(0f, 0.3f)] float approachZMoveRatio = 0.10f;
    [SerializeField, Range(0f, 1f)]   float approachFollowXY    = 0.50f;

    [Header("화면 흔들림(타격)")]
    // 흔들림은 **XY 오프셋 전용**이다. z는 fit(기준 거리)과 시네마·롱프레스 트윈이 소유하므로 건드리지 않는다 —
    // 같은 축을 두 주인이 쓰면 카메라가 튀거나 복귀점이 오염된다.
    [SerializeField] float shakeDuration  = 0.18f;   // 한 번의 흔들림이 잦아드는 데 걸리는 시간(초)
    [SerializeField] float shakeStrength  = 0.15f;   // 진폭(월드). 기준 거리 11 기준 — 실제로는 거리에 비례해 커진다
    [SerializeField] float shakeFrequency = 26f;     // 초당 진동 수. 낮으면 출렁이고 높으면 지직거린다

    // 시네마 지속시간은 BattleTimingConfig 단일 진실원 (AttackSequence와 공유, 배율 적용)
    float CinemaDuration => GameTiming.Battle.CinemaDuration;

    Camera cam;
    BattleCameraFit fit;
    float fallbackBaseZ;
    bool  liftActive;
    bool  liftOwnsExternalControl;
    Tween liftTween;

    // ── 카메라 XY의 주인은 이 클래스뿐이다(fit은 z만 만진다) ──────────────
    // 화면에 찍히는 XY = home + focus + shake. 매 프레임 이 합을 **절대 좌표로 다시 쓴다** —
    // 오프셋을 더했다 빼는 방식이면 두 연출이 겹칠 때 서로의 복귀점을 오염시킨다.
    Vector2 homeXY;        // 씬 배치 기준 XY. 연출이 끝나면 언제나 여기로 수렴한다
    Vector2 focusOffset;   // 피니시 클로즈업이 미는 XY. 트윈이 이 값을 몰고, 화면 반영은 LateUpdate가 한다
    bool    focusActive;   // 오프셋이 0이 아니거나 복귀 중 — LateUpdate가 XY를 계속 쓸지 판단하는 기준
    Tween   focusTween;

    float shakeLeft;      // 남은 시간(0 = 흔들림 없음)
    float shakeTotal;     // 이번 흔들림의 전체 시간(감쇠 계산 기준)
    float shakeAmp;       // 이번 흔들림의 시작 진폭

    /// <summary>시네마 연출 중인가. BattleCameraFit이 이 동안 카메라 z를 덮지 않게 하는 기준.</summary>
    public bool InCinema { get; private set; }

    /// <summary>연출 기준 카메라 z. fit이 붙어 있으면 화면 비율에 맞춰 계산된 값, 없으면 씬 배치값.</summary>
    float BaseZ => this.fit != null ? this.fit.BaseCameraZ : this.fallbackBaseZ;

    /// <summary>기준 거리 대비 배율. 절대 거리로 잡힌 연출 값(시네마 카드 Z 이동 등)에 곱해
    /// 화면이 좁아 카메라가 멀어져도 같은 화면 비중으로 보이게 한다.</summary>
    public static float DepthScale => Instance != null && Instance.fit != null ? Instance.fit.DistanceScale : 1f;

    void Awake()
    {
        Instance = this;
        this.cam = GetComponent<Camera>();
        this.fit = GetComponent<BattleCameraFit>();
        if (this.cam == null) return;
        this.fallbackBaseZ = transform.position.z;
        this.homeXY = transform.position;
    }

    void OnDestroy()
    {
        ReleaseLiftExternalControl();
        if (Instance == this) Instance = null;
    }

    // 연출 도중에 꺼지면 그 위치에 굳는다. 진행 중일 때만 기준으로 되돌린다
    // (아무것도 안 하고 있을 때 부르면 아직 잡지 않은 기준값을 카메라에 덮어쓴다).
    void OnDisable()
    {
        if (this.shakeLeft <= 0f && !this.focusActive && !InCinema
            && !this.liftActive && !this.liftOwnsExternalControl) return;

        this.shakeLeft   = 0f;
        this.liftActive  = false;
        this.liftTween?.Kill();
        this.liftTween   = null;
        transform.DOKill();
        this.focusTween?.Kill();
        this.focusTween  = null;
        this.focusOffset = Vector2.zero;
        this.focusActive = false;
        WriteXY(Vector2.zero);
        if (this.cam != null)
            transform.position = new Vector3(transform.position.x, transform.position.y, BaseZ);
        InCinema = false;
        ReleaseLiftExternalControl();
    }

    // ── 화면 흔들림 ──────────────────────────────────────────────────────

    /// <summary>타격 순간의 화면 흔들림. <paramref name="_scale"/>로 세기를 조절한다(1 = 기본).
    /// 카메라가 없는 씬(테스터 등)에서도 호출부가 분기하지 않게 정적 진입점을 둔다.
    ///
    /// **기다리지 않는다(void).** await 하면 연출 길이가 늘어나 두 클라의 진행 시간이 갈린다.
    /// 파형은 고정 사인 + 선형 감쇠라 난수를 쓰지 않는다 — MatchRandom은 물론 Unity RNG도 소비하지 않는다.</summary>
    public static void Shake(float _scale = 1f) => Instance?.BeginShake(_scale);

    void BeginShake(float _scale)
    {
        if (this.cam == null || this.shakeDuration <= 0f) return;
        // 끄기 판정은 여기 한 곳. 호출부(AttackSequence)가 옵션을 알면 판정이 흩어진다.
        if (!GameManager.ScreenShakeEnabled) return;

        // 거리 비례: 화면이 좁아 카메라가 멀어져도 화면상 흔들리는 비중이 같아진다(가로/세로 체감 통일).
        float t_amp = Mathf.Abs(this.shakeStrength * _scale) * DepthScale;
        if (t_amp <= 0f) return;

        // 기준 XY를 여기서 잡지 않는다 — home + focus가 이미 "흔들리지 않는 상태의 위치"다.
        // (흔들린 위치를 새 기준으로 삼으면 연쇄 타격마다 카메라가 조금씩 밀려난다.)

        // 연쇄 타격(무쌍·처형)에서 진폭이 누적돼 폭주하지 않게, 합산이 아니라 **큰 쪽으로 재시작**한다.
        float t_remain = this.shakeLeft > 0f && this.shakeTotal > 0f
            ? this.shakeAmp * (this.shakeLeft / this.shakeTotal)
            : 0f;

        this.shakeAmp   = Mathf.Max(t_amp, t_remain);
        this.shakeTotal = GameTiming.Battle.Scaled(this.shakeDuration);   // 전역 배속을 따른다
        this.shakeLeft  = this.shakeTotal;
    }

    // fit(실행순서 -100)이 z를 확정한 **뒤** XY를 얹는다. 트윈은 Update에서 도므로 이 순서면 항상 마지막이다.
    // 오프셋을 더했다 빼는 대신 매 프레임 기준값에서 **절대 좌표로 다시 쓴다** — 부동소수 오차가 쌓이지 않는다.
    void LateUpdate()
    {
        bool t_shaking = this.shakeLeft > 0f;
        if (!t_shaking && !this.focusActive) return;

        Vector2 t_shake = Vector2.zero;
        if (t_shaking)
        {
            // 배속(Time.timeScale)은 피니시·결과 여운이 낮추지만, 흔들림은 표시 전용이라
            // 배속과 무관한 unscaled로 감쇠시킨다(느려진 화면에서 흔들림만 늘어지지 않게).
            this.shakeLeft -= Time.unscaledDeltaTime;
            if (this.shakeLeft > 0f)
            {
                float t_elapsed = this.shakeTotal - this.shakeLeft;
                float t_decay   = this.shakeLeft / this.shakeTotal;          // 1 → 0 선형 감쇠
                float t_amp     = this.shakeAmp * t_decay;
                float t_phase   = t_elapsed * this.shakeFrequency * Mathf.PI * 2f;

                // 가로가 주, 세로는 약하게 + 다른 주기 — 같은 주기면 대각선으로만 흔들려 기계적으로 보인다.
                t_shake = new Vector2(Mathf.Sin(t_phase) * t_amp,
                                      Mathf.Cos(t_phase * 0.85f) * t_amp * 0.6f);
            }
            else this.shakeLeft = 0f;   // 이번 프레임에 오프셋 0을 한 번 써서 정확히 기준으로 안착시킨다
        }

        WriteXY(t_shake);
    }

    // 화면에 찍히는 XY = home + focus + shake. z는 건드리지 않는다(fit·시네마 소유).
    void WriteXY(Vector2 _shake)
    {
        if (this.cam == null) return;
        Vector2 t_xy = this.homeXY + this.focusOffset + _shake;
        transform.position = new Vector3(t_xy.x, t_xy.y, transform.position.z);
    }

    /// <summary>롱프레스로 카드 정보를 보는 동안 카메라를 카드 쪽으로 살짝 당긴다(손 떼면 false로 복귀).
    /// 같은 값으로 두 번 불러도 무시하므로 호출부가 매 프레임 불러도 된다(멱등).
    /// 카메라가 없는 씬(테스터 등)에서도 호출부가 분기하지 않게 정적 진입점을 둔다.</summary>
    public static void SetLongPressLift(bool _on) => Instance?.ApplyLongPressLift(_on);

    void ApplyLongPressLift(bool _on)
    {
        if (this.cam == null || this.liftActive == _on) return;

        if (_on)
        {
            // 시네마가 z를 몰고 있을 때는 상태와 외부 제어권도 잡지 않는다.
            if (InCinema) return;

            // 진행 중인 복귀가 있다면 그쪽 OnKill이 제어권을 먼저 반환한 뒤 새로 획득한다.
            transform.DOKill();
            this.liftTween = null;
            this.liftActive = true;
            AcquireLiftExternalControl();

            this.liftTween = transform.DOMoveZ(BaseZ - this.longPressPullBackZ,
                                                Mathf.Max(0.01f, this.longPressLiftDuration))
                .SetEase(Ease.InOutSine)
                .SetLink(gameObject);
            return;
        }

        bool t_snapBack = this.liftTween != null
                       && this.liftTween.IsActive()
                       && !this.liftTween.IsComplete();
        this.liftActive = false;

        // 시네마 트윈은 건드리지 않고, 실제로 획득했던 제어권만 정확히 반환한다.
        if (InCinema)
        {
            this.liftTween = null;
            ReleaseLiftExternalControl();
            return;
        }

        transform.DOKill();
        this.liftTween = null;

        // 확정 전 잠깐 출발했다 취소된 카메라는 복귀 트윈 없이 기준 위치로 스냅한다.
        if (t_snapBack)
        {
            Vector3 t_pos = transform.position;
            t_pos.z = BaseZ;
            transform.position = t_pos;
            ReleaseLiftExternalControl();
            return;
        }

        this.liftTween = transform.DOMoveZ(BaseZ, Mathf.Max(0.01f, this.longPressLiftDuration))
            .SetEase(Ease.InOutSine)
            .SetLink(gameObject)
            .OnKill(ReleaseLiftExternalControl);
    }

    void AcquireLiftExternalControl()
    {
        if (this.liftOwnsExternalControl) return;
        this.liftOwnsExternalControl = true;
        BattleCameraFit.BeginExternalControl();
    }

    void ReleaseLiftExternalControl()
    {
        if (!this.liftOwnsExternalControl) return;
        this.liftOwnsExternalControl = false;
        BattleCameraFit.EndExternalControl();
    }

    /// <summary>기준 거리에서 콘텐츠 쪽으로 <paramref name="_ratio"/>만큼 <b>다가간</b> z(줌 인).
    ///
    /// <para><b>부호 함정.</b> 이 카메라는 BaseZ가 음수라 "다가간다"는 z를 0 쪽으로 당기는 것이다.
    /// 바로 옆의 시네마·롱프레스는 <c>BaseZ - x</c>를 쓰는데 그건 <b>뒤로 빼는</b> 연출이라 그렇다 —
    /// 식이 비슷해 보여서 그대로 베끼면 줌 인이 줌 아웃이 된다(실제로 그렇게 났다).
    /// 줌 인 계산은 여기 하나만 쓴다.</para></summary>
    float ZoomInZ(float _ratio) => BaseZ * (1f - Mathf.Clamp01(_ratio));

    /// <summary>승패 확정 여운용 미세 줌. <paramref name="_depth01"/>은 0~1(패배는 승리보다 얕게 준다).
    /// 시네마와 같은 z 축을 쓰므로 <see cref="InCinema"/>를 세워 fit이 z를 덮지 않게 한다 —
    /// 전투는 이미 끝났고 다음 행선지는 로비뿐이라 이 상태는 씬이 내려갈 때까지 유지된다.
    ///
    /// **기다리지 않는다(void).** 여운의 길이는 BattleResultBeat이 소유하고, 카메라는 그 위에 얹히기만 한다.
    /// 트윈은 unscaled — 여운이 Time.timeScale을 낮추는 동안에도 줌 속도는 설계값 그대로여야 한다.</summary>
    public static void ResultPush(float _depth01, float _duration) => Instance?.ApplyResultPush(_depth01, _duration);

    void ApplyResultPush(float _depth01, float _duration)
    {
        if (this.cam == null) return;

        float t_ratio = this.resultZMoveRatio * Mathf.Clamp01(_depth01);
        if (t_ratio <= 0f) return;

        InCinema = true;
        transform.DOKill();
        transform.DOMoveZ(ZoomInZ(t_ratio), Mathf.Max(0.01f, _duration))
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)
            .SetLink(gameObject);
    }

    // 첫 박에 들어가는 비율. 나머지는 사망 연출이 도는 내내 천천히 마저 붙는다 —
    // 한 번에 다 붙이면 카메라가 튀고 멈춰 서서, 정작 죽는 그림 위에서는 아무것도 움직이지 않는다.
    const float FinishPunchRatio = 0.8f;

    /// <summary>공격자·방어자 중점으로 들어가는 얕은 와이드 줌.
    /// 실제 결정타가 나면 FinishFocus가 현재 오프셋과 z에서 이어받는다.</summary>
    public static void ApproachFocus(Vector3 _worldPos, float _duration)
        => Instance?.ApplyApproachFocus(_worldPos, _duration);

    void ApplyApproachFocus(Vector3 _worldPos, float _duration)
    {
        if (this.cam == null) return;

        InCinema = true;
        Vector2 t_target = (new Vector2(_worldPos.x, _worldPos.y) - this.homeXY) * this.approachFollowXY;
        float t_z = ZoomInZ(this.approachZMoveRatio);
        float t_duration = Mathf.Max(0.01f, _duration);

        this.focusActive = true;
        this.focusTween?.Kill();
        this.focusTween = DOTween.To(() => this.focusOffset, _v => this.focusOffset = _v,
                                     t_target, t_duration)
            .SetEase(Ease.InOutSine)
            .SetUpdate(true)
            .SetLink(gameObject);

        transform.DOKill();
        transform.DOMoveZ(t_z, t_duration)
            .SetEase(Ease.InOutSine)
            .SetUpdate(true)
            .SetLink(gameObject);
    }

    /// <summary>승부를 가른 타격 지점으로 밀고 들어가는 클로즈업. XY(타격 지점 추적)와 z(당기기)를 함께 쓴다.
    /// <paramref name="_worldPos"/>는 죽는 카드(들)의 월드 좌표.
    /// <paramref name="_punch"/>에 80%까지 확 붙고, <paramref name="_creep"/>에 걸쳐 나머지가 천천히 붙는다.
    ///
    /// **기다리지 않는다(void).** 연출 길이는 BattleFinisher가 소유한다. 트윈은 unscaled —
    /// 이 연출은 Time.timeScale을 깊게 낮춘 상태에서 돌므로, scaled로 두면 줌이 기어간다.
    /// 되돌리는 건 <see cref="RestoreFromFinish"/> 하나. 안 부르면 카메라가 클로즈업 상태로 굳는다.</summary>
    public static void FinishFocus(Vector3 _worldPos, float _punch, float _creep)
        => Instance?.ApplyFinishFocus(_worldPos, _punch, _creep);

    void ApplyFinishFocus(Vector3 _worldPos, float _punch, float _creep)
    {
        if (this.cam == null) return;

        InCinema = true;   // fit이 z를 덮지 않게(시네마와 같은 축을 쓴다)

        Vector2 t_target = (new Vector2(_worldPos.x, _worldPos.y) - this.homeXY) * this.finishFollowXY;
        float   t_z      = ZoomInZ(this.finishZMoveRatio);
        float   t_punch  = Mathf.Max(0.01f, _punch);
        float   t_creep  = Mathf.Max(0.01f, _creep);

        this.focusActive = true;
        this.focusTween?.Kill();
        this.focusTween = DOTween.Sequence()
            .Append(DOTween.To(() => this.focusOffset, _v => this.focusOffset = _v,
                               t_target * FinishPunchRatio, t_punch).SetEase(Ease.OutCubic))
            .Append(DOTween.To(() => this.focusOffset, _v => this.focusOffset = _v,
                               t_target, t_creep).SetEase(Ease.InOutSine))
            .SetUpdate(true)
            .SetLink(gameObject);

        // z도 같은 리듬 — 첫 박에 대부분 당기고 나머지를 천천히 마저 당긴다.
        float t_punchZ = Mathf.Lerp(BaseZ, t_z, FinishPunchRatio);
        transform.DOKill();
        DOTween.Sequence()
            .Append(transform.DOMoveZ(t_punchZ, t_punch).SetEase(Ease.OutCubic))
            .Append(transform.DOMoveZ(t_z, t_creep).SetEase(Ease.InOutSine))
            .SetUpdate(true)
            .SetLink(gameObject);
    }

    /// <summary>접근·피니시 줌을 통째로 되돌린다 — XY·거리·시네마 소유권까지.
    /// <b>줌을 푸는 공개 경로는 이것 하나다.</b> XY만 놓고 거리를 남기는 변형이 있었는데,
    /// 그러면 그 뒤로 z를 되돌릴 지점이 씬 종료밖에 없어 카메라가 당겨진 채로 굳었다.</summary>
    public static void RestoreFromFinish(float _duration) => Instance?.ApplyRestoreFromFinish(_duration);

    void ApplyRestoreFromFinish(float _duration)
    {
        if (this.cam == null) return;

        ApplyReleaseFocus(_duration);

        transform.DOKill();
        transform.DOMoveZ(BaseZ, Mathf.Max(0.01f, _duration))
            .SetEase(Ease.InOutSine)
            .SetUpdate(true)
            .SetLink(gameObject)
            .OnComplete(() => InCinema = false);
    }

    void ApplyReleaseFocus(float _duration)
    {
        if (this.cam == null || !this.focusActive) return;

        this.focusTween?.Kill();
        this.focusTween = DOTween.To(() => this.focusOffset, _v => this.focusOffset = _v, Vector2.zero,
                                     Mathf.Max(0.01f, _duration))
            .SetEase(Ease.InOutSine)
            .SetUpdate(true)
            .SetLink(gameObject)
            .OnComplete(() => this.focusActive = false);
    }

    public UniTask EnterCinema()
    {
        if (this.cam == null) return UniTask.CompletedTask;

        InCinema = true;
        float t_move = Mathf.Abs(BaseZ) * this.cinemaZMoveRatio;   // 거리 비례 — 어느 화면에서나 같은 확대감

        var t_tcs = new UniTaskCompletionSource();
        transform.DOKill();
        transform.DOMoveZ(BaseZ - t_move, CinemaDuration)
            .OnComplete(() => t_tcs.TrySetResult());
        return t_tcs.Task;
    }

    public void ExitCinema()
    {
        if (this.cam == null) return;

        transform.DOKill();
        transform.DOMoveZ(BaseZ, CinemaDuration)
            .OnComplete(() => InCinema = false);
    }
}
