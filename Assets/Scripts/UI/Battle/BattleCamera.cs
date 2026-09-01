using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class BattleCamera : MonoBehaviour
{
    public static BattleCamera Instance { get; private set; }

    // 시네마 진입 시 카메라가 뒤로 빠지는 양 = **기준 대비 비율**.
    // 절대값으로 두면 화면 비율에 따라 기준이 달라졌을 때(BattleCameraFit) 확대감이 기기마다 어긋난다.
    // 이 비율이 실제로 무엇을 움직이는지는 투영이 정한다 — 퍼스펙티브는 z, 오쏘는 orthographicSize.
    // 그 변환은 ZoomValue/TweenZoom 한 곳이 소유한다(아래 참조).
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
    float fallbackBaseOrthoSize = 6.351f;
    bool  liftActive;
    bool  liftOwnsExternalControl;
    Tween liftTween;

    // ── 카메라 XY의 주인은 이 클래스뿐이다(fit은 z만 만진다) ──────────────
    // 화면에 찍히는 XY = home + focus + shake. 매 프레임 이 합을 **절대 좌표로 다시 쓴다** —
    // 오프셋을 더했다 빼는 방식이면 두 연출이 겹칠 때 서로의 복귀점을 오염시킨다.
    Vector2 homeXY;        // 씬 배치 기준 XY. 연출이 끝나면 언제나 여기로 수렴한다
    Vector2 focusOffset;   // 지금 화면에 적용 중인 XY 밀기. LateUpdate가 매 프레임 다시 계산한다
    bool    focusActive;   // 오프셋이 0이 아니거나 복귀 중 — LateUpdate가 XY를 계속 쓸지 판단하는 기준
    Tween   focusTween;    // focusWeight를 모는 트윈(오프셋을 직접 몰지 않는다)

    // 줌 대상은 **좌표가 아니라 카드**다. 결정타를 맞고 죽는 카드는 그 자리에 서 있지 않다 —
    // 반격사면 공격자가 돌진 지점에서 제 슬롯으로 밀려 돌아가는 중이고, 좌표를 한 번만 찍어두면
    // 카메라는 카드가 떠난 자리(=상대 카드 근처)를 비춘다. 그래서 매 프레임 다시 읽는다.
    Transform focusA;      // 따라갈 카드. 둘 다 null이면 focusAnchor 고정 좌표를 쓴다
    Transform focusB;      // 둘이면 중점(공격자·방어자 와이드 / 주 대상·광역 동시 처치)
    Vector2   focusAnchor; // 따라갈 대상이 없을 때의 목표. 복귀 시작 시점의 좌표를 여기 굳힌다
    float     focusRatio;  // 이번 연출의 추적 비율(접근은 얕게, 피니시는 깊게)
    float     focusWeight; // 0 = 기준 위치, 1 = 목표까지 완전히 이동

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

    // ── 줌 축 ────────────────────────────────────────────────────────────
    // 이 클래스의 모든 확대/축소는 **화면에 담기는 세로 범위 배율**(extentScale) 하나로 표현한다.
    //   1 = 기준, <1 = 다가감(줌 인), >1 = 물러남(줌 아웃).
    // 그 배율을 실제 카메라 값으로 옮기는 곳은 ZoomValue 하나다 — 퍼스펙티브는 z(거리에 비례),
    // 오쏘는 orthographicSize. 예전에는 각 연출이 z를 직접 계산해서, 투영을 오쏘로 바꾸자
    // 컴파일 에러 없이 다섯 연출이 통째로 무효가 됐다. 축을 하나로 모아 그 재발을 막는다.

    /// <summary>줌을 orthographicSize로 거는가(오쏘) z로 거는가(퍼스펙티브).</summary>
    bool UsesOrthoZoom => this.cam != null && this.cam.orthographic;

    /// <summary>오쏘 기준 size. fit이 있으면 화면 비율에 맞춰 계산된 값, 없으면 씬 배치값.</summary>
    float BaseOrthoSize => this.fit != null ? this.fit.BaseOrthoSize : this.fallbackBaseOrthoSize;

    /// <summary>비율을 절대 거리로 환산할 때 쓰는 기준 거리. 오쏘에서도 fit이 기준값을 돌려주므로
    /// 절대 단위로 저작된 값(롱프레스 뒤로 빼기 등)이 두 모드에서 같은 화면 비중이 된다.</summary>
    float ZoomReferenceDistance => this.fit != null
        ? Mathf.Max(0.01f, this.fit.BaseDistance)
        : Mathf.Max(0.01f, Mathf.Abs(this.fallbackBaseZ));

    /// <summary>지금 투영에서 "기준 상태"에 해당하는 줌 값.</summary>
    float ZoomBase => UsesOrthoZoom ? BaseOrthoSize : BaseZ;

    /// <summary>가시 범위 배율 → 카메라 값. BaseZ가 음수라 퍼스펙티브도 그냥 곱하면 맞는다
    /// (거리 = |BaseZ| * scale). 부호를 손으로 다루지 않는 게 이 함수의 존재 이유다 —
    /// 예전엔 <c>BaseZ - x</c>(뒤로)와 <c>BaseZ * (1-r)</c>(앞으로)가 섞여 줌 인이 줌 아웃으로 뒤집힌 적이 있다.</summary>
    float ZoomValue(float _extentScale) => ZoomBase * Mathf.Max(0.01f, _extentScale);

    /// <summary>절대 월드 단위로 저작된 "뒤로 빼기"를 가시 범위 배율로 바꾼다.</summary>
    float PullBackScale(float _worldPullBack) => 1f + _worldPullBack / ZoomReferenceDistance;

    /// <summary>줌 트윈을 건다. 대상이 모드마다 다르므로(Transform vs Camera) 생성도 여기 한 곳에서만 한다.</summary>
    Tween TweenZoom(float _extentScale, float _duration, Ease _ease, bool _unscaled)
    {
        float t_to       = ZoomValue(_extentScale);
        float t_duration = Mathf.Max(0.01f, _duration);
        Tween t_tween    = UsesOrthoZoom
            ? this.cam.DOOrthoSize(t_to, t_duration)
            : transform.DOMoveZ(t_to, t_duration);
        t_tween.SetEase(_ease).SetLink(gameObject);
        if (_unscaled) t_tween.SetUpdate(true);
        return t_tween;
    }

    /// <summary>진행 중인 줌 트윈을 끊는다. <b>transform.DOKill() 하나로는 부족하다</b> —
    /// 오쏘 줌은 Camera를 대상으로 돌기 때문에 트랜스폼만 죽이면 트윈이 살아남아 다음 연출과 싸운다.</summary>
    void KillZoomTweens()
    {
        transform.DOKill();
        if (this.cam != null) this.cam.DOKill();
    }

    /// <summary>줌을 기준 상태로 즉시 되돌린다(트윈 없음).</summary>
    void SnapZoomToBase()
    {
        if (this.cam == null) return;
        if (UsesOrthoZoom) { this.cam.orthographicSize = BaseOrthoSize; return; }
        transform.position = new Vector3(transform.position.x, transform.position.y, BaseZ);
    }

    void Awake()
    {
        Instance = this;
        this.cam = GetComponent<Camera>();
        this.fit = GetComponent<BattleCameraFit>();
        if (this.cam == null) return;
        this.fallbackBaseZ = transform.position.z;
        this.fallbackBaseOrthoSize = Mathf.Max(0.01f, this.cam.orthographicSize);
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
        KillZoomTweens();
        this.focusTween?.Kill();
        this.focusTween  = null;
        this.focusA      = null;
        this.focusB      = null;
        this.focusWeight = 0f;
        this.focusOffset = Vector2.zero;
        this.focusActive = false;
        WriteXY(Vector2.zero);
        SnapZoomToBase();
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

        if (this.focusActive) this.focusOffset = ComputeFocusOffset();

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

    // 지금 따라갈 목표(카드 위치 / 없으면 굳혀둔 좌표)에서 이번 프레임의 XY 밀기를 낸다.
    Vector2 CurrentFocusTarget()
    {
        Vector2 t_sum = Vector2.zero;
        int     t_n   = 0;
        if (this.focusA != null) { t_sum += (Vector2)this.focusA.position; t_n++; }
        if (this.focusB != null) { t_sum += (Vector2)this.focusB.position; t_n++; }
        return t_n > 0 ? t_sum / t_n : this.focusAnchor;
    }

    Vector2 ComputeFocusOffset()
        => (CurrentFocusTarget() - this.homeXY) * this.focusRatio * this.focusWeight;

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
            // 시네마가 줌 축을 몰고 있을 때는 상태와 외부 제어권도 잡지 않는다.
            if (InCinema) return;

            // 진행 중인 복귀가 있다면 그쪽 OnKill이 제어권을 먼저 반환한 뒤 새로 획득한다.
            KillZoomTweens();
            this.liftTween = null;
            this.liftActive = true;
            AcquireLiftExternalControl();

            this.liftTween = TweenZoom(PullBackScale(this.longPressPullBackZ),
                                       this.longPressLiftDuration, Ease.InOutSine, false);
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

        KillZoomTweens();
        this.liftTween = null;

        // 확정 전 잠깐 출발했다 취소된 카메라는 복귀 트윈 없이 기준 상태로 스냅한다.
        if (t_snapBack)
        {
            SnapZoomToBase();
            ReleaseLiftExternalControl();
            return;
        }

        this.liftTween = TweenZoom(1f, this.longPressLiftDuration, Ease.InOutSine, false)
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

    /// <summary>비율만큼 <b>다가간</b> 가시 범위 배율(줌 인). 0.32면 화면에 담기는 범위가 68%로 줄어든다.
    /// 물러나는 연출은 <see cref="PullBackScale"/>이나 <c>1 + ratio</c>를 쓴다 — 두 방향을 섞어
    /// 손으로 부호를 다루면 줌 인이 줌 아웃으로 뒤집힌다(실제로 그렇게 났다).</summary>
    static float ZoomInScale(float _ratio) => 1f - Mathf.Clamp01(_ratio);

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
        KillZoomTweens();
        TweenZoom(ZoomInScale(t_ratio), _duration, Ease.OutCubic, true);
    }

    // 첫 박에 들어가는 비율. 나머지는 사망 연출이 도는 내내 천천히 마저 붙는다 —
    // 한 번에 다 붙이면 카메라가 튀고 멈춰 서서, 정작 죽는 그림 위에서는 아무것도 움직이지 않는다.
    const float FinishPunchRatio = 0.8f;

    /// <summary>공격자·방어자 <b>중점을 따라가는</b> 얕은 와이드 줌. 둘 다 연출 중에 움직이므로
    /// 좌표가 아니라 트랜스폼을 받는다. 실제 결정타가 나면 FinishFocus가 현재 상태에서 이어받는다.</summary>
    public static void ApproachFocus(Transform _attacker, Transform _defender, float _duration)
        => Instance?.ApplyApproachFocus(_attacker, _defender, _duration);

    void ApplyApproachFocus(Transform _attacker, Transform _defender, float _duration)
    {
        if (this.cam == null) return;

        InCinema = true;
        BeginFocus(_attacker, _defender, this.approachFollowXY);

        float t_duration = Mathf.Max(0.01f, _duration);

        this.focusTween?.Kill();
        this.focusTween = TweenFocusWeight(1f, t_duration, Ease.InOutSine);

        KillZoomTweens();
        TweenZoom(ZoomInScale(this.approachZMoveRatio), t_duration, Ease.InOutSine, true);
    }

    // 추적 대상 교체. 진행 중인 밀기(focusWeight)는 그대로 두고 목표만 바꾼다 —
    // 접근 줌에서 피니시 줌으로 넘어갈 때 카메라가 튀지 않게.
    void BeginFocus(Transform _a, Transform _b, float _ratio)
    {
        this.focusActive = true;
        this.focusA      = _a;
        this.focusB      = _b;
        this.focusRatio  = _ratio;
        if (_a == null && _b == null) this.focusAnchor = this.homeXY;   // 대상이 없으면 밀지 않는다
    }

    Tween TweenFocusWeight(float _to, float _duration, Ease _ease)
        => DOTween.To(() => this.focusWeight, _v => this.focusWeight = _v, _to, Mathf.Max(0.01f, _duration))
            .SetEase(_ease)
            .SetUpdate(true)
            .SetLink(gameObject);

    /// <summary>승부를 가른 <b>죽는 카드를 따라가는</b> 클로즈업. XY 추적과 z 당기기를 함께 쓴다.
    /// <paramref name="_victimA"/>/<paramref name="_victimB"/>는 이번 타격에 쓰러지는 카드(둘이면 중점).
    /// <paramref name="_punch"/>에 80%까지 확 붙고, <paramref name="_creep"/>에 걸쳐 나머지가 천천히 붙는다.
    ///
    /// <para>좌표가 아니라 트랜스폼인 이유 — 반격사에서 죽는 쪽은 <b>공격자</b>이고, 그 카드는
    /// 돌진 지점에서 제 슬롯으로 밀려 돌아가는 중이다. 좌표를 한 번만 찍으면 카메라가 충돌 지점에
    /// 굳어 "맞은 카드를 비추는" 그림이 된다.</para>
    ///
    /// **기다리지 않는다(void).** 연출 길이는 BattleFinisher가 소유한다. 트윈은 unscaled —
    /// 이 연출은 Time.timeScale을 깊게 낮춘 상태에서 돌므로, scaled로 두면 줌이 기어간다.
    /// 되돌리는 건 <see cref="RestoreFromFinish"/> 하나. 안 부르면 카메라가 클로즈업 상태로 굳는다.</summary>
    public static void FinishFocus(Transform _victimA, Transform _victimB, float _punch, float _creep)
        => Instance?.ApplyFinishFocus(_victimA, _victimB, _punch, _creep);

    void ApplyFinishFocus(Transform _victimA, Transform _victimB, float _punch, float _creep)
    {
        if (this.cam == null) return;

        InCinema = true;   // fit이 줌 축을 덮지 않게(시네마와 같은 축을 쓴다)

        // 접근 줌에서 넘어온 경우 focusWeight는 이미 1 근처다 — 대상만 죽는 카드로 갈아타고
        // 비율(얕게 → 깊게)이 올라가면서 자연스럽게 더 파고든다.
        BeginFocus(_victimA, _victimB, this.finishFollowXY);

        float t_scale = ZoomInScale(this.finishZMoveRatio);
        float t_punch = Mathf.Max(0.01f, _punch);
        float t_creep = Mathf.Max(0.01f, _creep);

        // focusWeight는 **리셋하지 않는다** — 0으로 되돌리면 접근 줌이 붙어 있던 화면이 기준 위치로
        // 한 번 튕겼다 다시 들어온다. 접근에서 이어받으면 실효 밀기는 0.5 → 0.74 → 0.92로 계속 안쪽이다
        // (비율이 0.5에서 0.92로 올라가므로 weight가 1 → 0.8로 내려가도 화면은 파고든다).
        this.focusTween?.Kill();
        this.focusTween = DOTween.Sequence()
            .Append(DOTween.To(() => this.focusWeight, _v => this.focusWeight = _v,
                               FinishPunchRatio, t_punch).SetEase(Ease.OutCubic))
            .Append(DOTween.To(() => this.focusWeight, _v => this.focusWeight = _v,
                               1f, t_creep).SetEase(Ease.InOutSine))
            .SetUpdate(true)
            .SetLink(gameObject);

        // 줌도 같은 리듬 — 첫 박에 대부분 당기고 나머지를 천천히 마저 당긴다.
        // 중간값을 배율에서 보간하므로 두 투영에서 같은 리듬이 나온다(z를 보간하면 오쏘에서 무효였다).
        float t_punchScale = Mathf.Lerp(1f, t_scale, FinishPunchRatio);
        KillZoomTweens();
        DOTween.Sequence()
            .Append(TweenZoom(t_punchScale, t_punch, Ease.OutCubic, false))
            .Append(TweenZoom(t_scale, t_creep, Ease.InOutSine, false))
            .SetUpdate(true)
            .SetLink(gameObject);
    }

    /// <summary>접근·피니시 줌을 통째로 되돌린다 — XY·줌·시네마 소유권까지.
    /// <b>줌을 푸는 공개 경로는 이것 하나다.</b> XY만 놓고 줌을 남기는 변형이 있었는데,
    /// 그러면 그 뒤로 줌을 되돌릴 지점이 씬 종료밖에 없어 카메라가 당겨진 채로 굳었다.</summary>
    public static void RestoreFromFinish(float _duration) => Instance?.ApplyRestoreFromFinish(_duration);

    void ApplyRestoreFromFinish(float _duration)
    {
        if (this.cam == null) return;

        ApplyReleaseFocus(_duration);

        KillZoomTweens();
        TweenZoom(1f, _duration, Ease.InOutSine, true)
            .OnComplete(() => InCinema = false);
    }

    void ApplyReleaseFocus(float _duration)
    {
        if (this.cam == null || !this.focusActive) return;

        // 추적을 먼저 끊고 마지막 목표를 좌표로 굳힌다 — 물러나는 동안에도 카드를 계속 따라가면
        // 죽어서 사라지거나(파괴) 다음 카드에 재바인딩된 View를 쫓아 카메라가 엉뚱한 데로 끌려간다.
        this.focusAnchor = CurrentFocusTarget();
        this.focusA      = null;
        this.focusB      = null;

        this.focusTween?.Kill();
        this.focusTween = TweenFocusWeight(0f, _duration, Ease.InOutSine)
            .OnComplete(() => this.focusActive = false);
    }

    public UniTask EnterCinema()
    {
        if (this.cam == null) return UniTask.CompletedTask;

        InCinema = true;

        var t_tcs = new UniTaskCompletionSource();
        KillZoomTweens();
        // 뒤로 빠지는 연출 — 담기는 범위가 (1 + 비율)배로 넓어진다. 기준 대비 비율이라 어느 화면에서나 같다.
        TweenZoom(1f + this.cinemaZMoveRatio, CinemaDuration, Ease.OutQuad, false)
            .OnComplete(() => t_tcs.TrySetResult());
        return t_tcs.Task;
    }

    public void ExitCinema()
    {
        if (this.cam == null) return;

        KillZoomTweens();
        TweenZoom(1f, CinemaDuration, Ease.OutQuad, false)
            .OnComplete(() => InCinema = false);
    }
}
