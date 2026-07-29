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

    // 시네마 지속시간은 BattleTimingConfig 단일 진실원 (AttackSequence와 공유, 배율 적용)
    float CinemaDuration => GameTiming.Battle.CinemaDuration;

    Camera cam;
    BattleCameraFit fit;
    float fallbackBaseZ;

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
        if (Instance == this) Instance = null;
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
