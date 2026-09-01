using UnityEngine;

/// <summary>
/// 화면 비율이 어떻든 전투 보드(카드 6장)가 **전부 보이도록** 카메라 거리를 맞춘다.
///
/// 슬롯 좌표는 씬 트랜스폼이 단일 진실원 — 여기선 건드리지 않고 카메라만 뒤로 뺀다.
/// 좁은 세로 화면일수록 가로가 먼저 부족해지므로(세로는 남아돎) 가로/세로 각각 필요한 거리를 구해 **큰 쪽**을 쓴다.
/// 남는 세로 여백은 배경이 채우는 전제.
///
/// 퍼스펙티브: 거리 d에서 세로 반높이 = d * tan(fov/2), 가로 반폭 = 그 값 * aspect → **거리**로 맞춘다.
/// 오쏘그래픽: 세로 반높이 = orthographicSize, 가로 반폭 = 그 값 * aspect. 거리는 담을 영역과 무관하므로
/// **orthographicSize**로 맞추고 z는 손대지 않는다(z는 정렬·클리핑 용도로만 남는다).
///
/// **확인 방법**: ExecuteAlways라 플레이 없이도 게임뷰 해상도만 바꾸면 즉시 반영된다.
/// 인스펙터의 status에 현재 aspect/거리/가시영역이 찍히고, 기즈모로 담을 영역(노랑)과
/// 실제 가시 영역(초록)이 그려진다 — 두 사각형이 겹치면 안 잘린다는 뜻(게임뷰 Gizmos 켜면 플레이 중에도 보임).
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(-100)]   // BattleCamera가 기준 z를 읽기 전에 확정돼야 한다
[RequireComponent(typeof(Camera))]
public class BattleCameraFit : MonoBehaviour
{
    /// <summary>튜닝 기준 거리(카메라 z = -11, fov 60에서 잡은 원래 연출 값).
    /// 시네마 이동량처럼 "거리에 비례해야 하는" 값의 배율 산출에 쓴다.</summary>
    public const float REFERENCE_DISTANCE = 11f;

    /// <summary>위 기준 거리(11, fov 60)에서의 세로 반높이 = 11 * tan(30°). 오쏘 전환 뒤에도
    /// <see cref="DistanceScale"/>이 두 모드에서 같은 뜻(기준 대비 화면에 담기는 월드 크기 배율)이 되도록
    /// 맞춘 값이다 — 이 값을 바꾸면 배율을 곱해 쓰는 연출의 세기가 전부 달라진다.</summary>
    public const float REFERENCE_ORTHO_SIZE = 6.351f;

    [Header("항상 담아야 할 영역 (카드 평면 기준, 반지름)")]
    // 슬롯 끝 x=±2.0 + 카드 반폭 1.16 + 여백. 세로는 적 슬롯 상단 4.43 + 여백.
    [SerializeField] float contentHalfWidth  = 3.25f;
    [SerializeField] float contentHalfHeight = 4.70f;
    [SerializeField] float contentZ          = 0f;     // 카드가 놓인 평면

    [Header("에디터")]
    [SerializeField] bool applyInEditMode = true;      // 끄면 플레이 중에만 카메라를 움직인다(씬 diff 방지)
    [SerializeField] bool drawGizmos      = true;
    [SerializeField, TextArea] string status;          // 읽기 전용 표시 — 현재 계산 결과

    Camera cam;
    float  lastAspect = -1f;
    float  lastFov    = -1f;
    bool   lastOrtho;
    float  orthoBaseZ;      // 오쏘에서는 z가 담을 영역과 무관 — 씬 배치값을 그대로 기준으로 쓴다
    bool   hasOrthoBaseZ;

    /// <summary>콘텐츠 평면까지의 카메라 거리(항상 양수).</summary>
    public float BaseDistance { get; private set; } = REFERENCE_DISTANCE;

    /// <summary>오쏘에서 보드 전체가 들어오는 orthographicSize. 인트로 줌이 여기로 수렴한다.</summary>
    public float BaseOrthoSize { get; private set; } = REFERENCE_ORTHO_SIZE;

    /// <summary>현재 카메라가 오쏘그래픽인가. 연출이 z를 몰지 size를 몰지 가르는 기준.</summary>
    public bool IsOrthographic => this.cam != null && this.cam.orthographic;

    /// <summary>연출이 기준으로 삼을 카메라 z(시네마가 여기서 출발/복귀).
    /// 오쏘에서는 거리로 화각이 안 변하므로 씬 배치 z를 그대로 돌려준다 — 이 모드에서 z를 미는 연출은
    /// 화면에 아무 변화를 못 만든다(크기를 바꾸려면 <see cref="BaseOrthoSize"/>를 몰아야 한다).</summary>
    public float BaseCameraZ => IsOrthographic
        ? (this.hasOrthoBaseZ ? this.orthoBaseZ : transform.position.z)
        : this.contentZ - BaseDistance;

    /// <summary>기준 거리 대비 배율. 절대 거리로 잡힌 연출 값(시네마 이동량 등)에 곱해
    /// 기기 화면이 달라도 같은 화면 비중으로 보이게 한다.</summary>
    public float DistanceScale => IsOrthographic
        ? BaseOrthoSize / REFERENCE_ORTHO_SIZE
        : BaseDistance / REFERENCE_DISTANCE;

    /// <summary>인트로 줌·시네마처럼 **연출이 카메라를 직접 몰 때** 켜 둔다. 켜져 있는 동안 fit은
    /// 거리/표시만 갱신하고 트랜스폼은 건드리지 않는다 — 트윈과 매 프레임 싸우면 카메라가 튄다.
    /// 중첩 호출 대비 카운터. Begin/End 짝을 반드시 맞출 것.</summary>
    static int s_externalControl;

    public static bool ExternalControl => s_externalControl > 0;

    public static void BeginExternalControl() => s_externalControl++;
    public static void EndExternalControl()   => s_externalControl = Mathf.Max(0, s_externalControl - 1);

    /// <summary>씬 전환 등으로 짝이 어긋났을 때 강제 해제.</summary>
    public static void ClearExternalControl() => s_externalControl = 0;

    /// <summary>현재 카메라가 콘텐츠 평면에서 실제로 보여주는 반폭/반높이. 담을 영역보다 크면 안 잘린다.</summary>
    public Vector2 VisibleHalfExtents
    {
        get
        {
            if (this.cam == null) return Vector2.zero;
            float t_h = this.cam.orthographic
                ? BaseOrthoSize
                : BaseDistance * Mathf.Tan(this.cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return new Vector2(t_h * this.cam.aspect, t_h);
        }
    }

    void Awake()
    {
        this.cam = GetComponent<Camera>();
        CaptureOrthoBaseZ();
        Fit();
    }

    void OnEnable()
    {
        this.cam = GetComponent<Camera>();
        CaptureOrthoBaseZ();
        this.lastAspect = -1f;   // 다음 갱신에서 무조건 다시 계산
        ClearExternalControl();  // 이전 씬에서 Begin/End 짝이 어긋난 채 넘어와도 여기서 재설정
        Fit();
    }

    // 값 조정 즉시 반영(플레이 중 인스펙터에서 만져도 바로 보인다).
    void OnValidate()
    {
        this.cam = GetComponent<Camera>();
        this.lastAspect = -1f;
        Fit();
    }

    // 게임뷰 해상도 전환·기기 회전 대응. 값이 바뀔 때만 반영(매 프레임 트랜스폼 쓰기 방지).
    // 오쏘 기준 z는 씬 배치값이다 — 연출이 z를 민 뒤에 잡으면 그 오염된 값이 기준이 되므로
    // 연출이 카메라를 몰고 있는 동안에는 새로 잡지 않는다.
    void CaptureOrthoBaseZ()
    {
        if (this.hasOrthoBaseZ && ExternalControl) return;
        this.orthoBaseZ = transform.position.z;
        this.hasOrthoBaseZ = true;
    }

    void LateUpdate()
    {
        if (this.cam == null) return;
        if (Mathf.Approximately(this.cam.aspect, this.lastAspect)
         && Mathf.Approximately(this.cam.fieldOfView, this.lastFov)
         && this.cam.orthographic == this.lastOrtho) return;
        Fit();
    }

    void Fit()
    {
        if (this.cam == null) return;

        this.lastAspect = this.cam.aspect;
        this.lastFov    = this.cam.fieldOfView;
        this.lastOrtho  = this.cam.orthographic;

        float t_aspect = Mathf.Max(0.01f, this.cam.aspect);
        if (this.cam.orthographic)
        {
            // 좁은 세로 화면일수록 가로가 먼저 부족해진다 — 세로/가로에 필요한 size 중 큰 쪽.
            BaseOrthoSize = Mathf.Max(this.contentHalfHeight, this.contentHalfWidth / t_aspect);
            // 오쏘에서 거리는 화각과 무관하다. 배율 산출은 BaseOrthoSize가 맡으므로 기준값으로 고정한다.
            BaseDistance  = REFERENCE_DISTANCE;
        }
        else
        {
            float t_tan   = Mathf.Tan(this.cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float t_distV = this.contentHalfHeight / t_tan;
            float t_distH = this.contentHalfWidth  / (t_tan * t_aspect);

            BaseDistance  = Mathf.Max(t_distV, t_distH);
            BaseOrthoSize = BaseDistance * t_tan;
        }

        Vector2 t_vis = VisibleHalfExtents;
        this.status = string.Format(
            "aspect {0:F3}  거리 {1:F2}  배율 {2:F2}\n가시 ±{3:F2} x ±{4:F2}  (담을 영역 ±{5:F2} x ±{6:F2})\n{7}",
            this.cam.aspect, BaseDistance, DistanceScale, t_vis.x, t_vis.y,
            this.contentHalfWidth, this.contentHalfHeight,
            t_vis.x + 1e-3f >= this.contentHalfWidth && t_vis.y + 1e-3f >= this.contentHalfHeight ? "OK — 전부 보임" : "잘림!");

        if (!Application.isPlaying && !this.applyInEditMode) return;

        // 인트로 줌/시네마 등 연출이 카메라를 몰고 있으면 z를 덮지 않는다 — 트윈과 싸워 카메라가 튄다.
        // (해상도가 그 중에 바뀌는 건 비정상 케이스라 연출이 끝나며 기준 z로 복귀할 때 자연히 반영된다.)
        if (ExternalControl) return;
        if (BattleCamera.Instance != null && BattleCamera.Instance.InCinema) return;

        if (this.cam.orthographic)
        {
            // 오쏘는 size가 화각의 주인이다. z는 씬 배치값 그대로 둔다 — 여기서 z를 덮으면
            // 정렬·클리핑만 흔들고 화면은 그대로라 원인을 못 찾는 버그가 된다.
            if (!Mathf.Approximately(this.cam.orthographicSize, BaseOrthoSize))
                this.cam.orthographicSize = BaseOrthoSize;
            return;
        }

        Vector3 t_pos = transform.position;
        if (!Mathf.Approximately(t_pos.z, BaseCameraZ))
            transform.position = new Vector3(t_pos.x, t_pos.y, BaseCameraZ);
    }

    // 노랑 = 항상 담아야 할 영역 / 초록 = 지금 실제로 보이는 영역. 초록이 노랑을 감싸면 안 잘린다.
    void OnDrawGizmos()
    {
        if (!this.drawGizmos || this.cam == null) return;

        Vector3 t_center = new Vector3(transform.position.x, transform.position.y, this.contentZ);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(t_center, new Vector3(this.contentHalfWidth * 2f, this.contentHalfHeight * 2f, 0f));

        Vector2 t_vis = VisibleHalfExtents;
        Gizmos.color = (t_vis.x >= this.contentHalfWidth && t_vis.y >= this.contentHalfHeight) ? Color.green : Color.red;
        Gizmos.DrawWireCube(t_center, new Vector3(t_vis.x * 2f, t_vis.y * 2f, 0f));
    }
}
