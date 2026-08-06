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

    // 흔들리지 않는 상태의 카메라 XY. 흔들림이 시작될 때 한 번 잡고, 끝나면 정확히 이 값으로 되돌린다.
    float shakeBaseX;
    float shakeBaseY;
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
    }

    void OnDestroy()
    {
        ReleaseLiftExternalControl();
        if (Instance == this) Instance = null;
    }

    // 흔들리는 도중에 꺼지면 그 위치에 굳는다. 진행 중일 때만 기준으로 되돌린다
    // (흔들림이 없을 때 부르면 아직 잡지 않은 기준값을 카메라에 덮어쓴다).
    void OnDisable()
    {
        if (this.shakeLeft > 0f) RestoreShakeBase();
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

        // 기준 XY는 **흔들리지 않는 상태의 위치**. 이미 흔들리는 중이면 그때 잡아둔 값을 그대로 쓴다
        // (흔들린 위치를 새 기준으로 삼으면 연쇄 타격마다 카메라가 조금씩 밀려난다).
        if (this.shakeLeft <= 0f)
        {
            this.shakeBaseX = transform.position.x;
            this.shakeBaseY = transform.position.y;
        }

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
        if (this.shakeLeft <= 0f) return;

        // timeScale은 이 프로젝트가 건드리지 않지만(멈칫도 timeScale을 안 쓴다), 흔들림은 표시 전용이라
        // 배속과 무관한 unscaled로 감쇠시킨다.
        this.shakeLeft -= Time.unscaledDeltaTime;
        if (this.shakeLeft <= 0f) { RestoreShakeBase(); return; }

        float t_elapsed = this.shakeTotal - this.shakeLeft;
        float t_decay   = this.shakeLeft / this.shakeTotal;          // 1 → 0 선형 감쇠
        float t_amp     = this.shakeAmp * t_decay;
        float t_phase   = t_elapsed * this.shakeFrequency * Mathf.PI * 2f;

        // 가로가 주, 세로는 약하게 + 다른 주기 — 같은 주기면 대각선으로만 흔들려 기계적으로 보인다.
        float t_x = this.shakeBaseX + Mathf.Sin(t_phase) * t_amp;
        float t_y = this.shakeBaseY + Mathf.Cos(t_phase * 0.85f) * t_amp * 0.6f;

        transform.position = new Vector3(t_x, t_y, transform.position.z);   // z는 건드리지 않는다
    }

    /// <summary>흔들림을 끝내고 카메라 XY를 기준값으로 정확히 되돌린다(끝날 때 / 꺼질 때 공용).</summary>
    void RestoreShakeBase()
    {
        this.shakeLeft = 0f;
        if (this.cam == null) return;
        transform.position = new Vector3(this.shakeBaseX, this.shakeBaseY, transform.position.z);
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
