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

    // 시네마 지속시간은 BattleTimingConfig 단일 진실원 (AttackSequence와 공유, 배율 적용)
    float CinemaDuration => GameTiming.Battle.CinemaDuration;

    Camera cam;
    BattleCameraFit fit;
    float fallbackBaseZ;
    bool  liftActive;
    bool  liftOwnsExternalControl;
    Tween liftTween;

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
