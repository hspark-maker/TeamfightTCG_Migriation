using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

// 룰렛 판 하나. 가속·등속·감속 정지·원위치 복귀만 하고 각도식의 유일한 소유자다.
// 무엇이 나왔는지는 모른다 — 받는 것은 멈출 칸 번호 하나뿐이라, 화면이 칸을 스스로 고르는 경로가 생기지 않는다.
public class RouletteWheelView : MonoBehaviour
{
    [Tooltip("돌아갈 판입니다. 칸 8개를 자식으로 둔 노드를 배선하세요.")]
    [SerializeField] RectTransform board;

    [Tooltip("바늘이 가리키는 시계각입니다. 0이면 12시입니다.\n\n" +
             "판을 세워 보고 0번 칸이 바늘 아래로 오지 않으면 그 어긋난 각을 여기 넣으세요 — " +
             "각도식은 코드에 있고, 이 한 칸이 판마다 다른 바늘 위치를 흡수합니다.")]
    [SerializeField] float pointerOffsetDeg = 0f;

    [Header("가속·등속")]
    [Tooltip("멈춰 있던 판이 최고 속도에 오르기까지 걸리는 시간입니다. 짧게 두세요 — " +
             "룰렛은 누르는 순간 이미 빠르게 돌고 있어야 하고, 긴 가속은 판이 굼떠 보입니다.")]
    [SerializeField] float accelSeconds = 0.22f;

    [Tooltip("최고 회전 속도(초당 도)입니다. 1080이면 1초에 세 바퀴입니다.\n\n" +
             "감속 시간이 이 값에서 파생됩니다 — 빠르게 둘수록 멈추는 데 걸리는 시간도 짧아집니다.")]
    [SerializeField] float cruiseSpeedDegPerSec = 1080f;

    [Header("감속 정지")]
    [Tooltip("결과가 온 뒤 멈추기까지 도는 바퀴 수입니다. 감속에 걸리는 시간은 저작하지 않습니다 — " +
             "최고 속도에서 매끄럽게 이어지려면 시간이 거리에서 정해져야 하기 때문입니다(코드 참조).")]
    [SerializeField] int settleTurns = 3;

    [Tooltip("칸 정중앙에서 좌우로 흔들 최대 각도입니다. 칸 하나가 45도라 절반인 22.5도에 가까워지면 " +
             "옆 칸에 선 것으로 읽힙니다 — 그래서 코드가 16도로 상한을 겁니다.")]
    [SerializeField] float slotJitterDeg = 8f;

    [Header("원위치 복귀")]
    [Tooltip("거절·실패로 되돌아올 때 도는 바퀴 수입니다. 급정지는 결함으로 읽히므로 0으로 두지 마세요.")]
    [SerializeField] int returnTurns = 1;

    // 코드가 거는 지터 상한. 45도의 절반(22.5)에 가까워지면 옆 칸으로 읽힌다.
    const float MAX_JITTER_DEG = 16f;

    // 계산된 감속 시간의 안전 범위. 저작을 극단으로 밀어도 한 프레임 급정지나 하염없는 표류가 되지 않게.
    const float MIN_SETTLE_SECONDS = 0.8f;
    const float MAX_SETTLE_SECONDS = 4.5f;

    // 이번 회전을 시작하기 직전의 각도. 거절·실패면 정확히 여기로 돌아온다("아무 일도 없었다").
    // 판을 저작 각도로 되돌리지는 않는다 — 판이 어디에 서 있든 다음 회전은 그 자리에서 이어진다.
    float m_homeAngle;

    Tween m_spin;

    void OnDisable() => this.KillSpin();

    /// <summary>회전을 시작한다. 결과가 오기 전까지 등속으로 계속 돈다.</summary>
    public void BeginSpin()
    {
        if (this.board == null) return;

        this.KillSpin();

        // 복귀 목적지는 누른 순간의 각도다 — 거절이 "아무 일도 없었다"로 읽히려면 그 자리로 정확히 돌아와야 한다.
        this.m_homeAngle = this.board.localEulerAngles.z;

        // 등가속의 평균 속도 = 최고 속도의 절반. 이 거리를 돌고 나면 등속과 이어진다.
        float t_accelDeg = this.cruiseSpeedDegPerSec * Mathf.Max(0f, this.accelSeconds) * 0.5f;

        this.m_spin = this.board
            .DOLocalRotate(new Vector3(0f, 0f, -t_accelDeg), Mathf.Max(0.01f, this.accelSeconds), RotateMode.LocalAxisAdd)
            .SetEase(Ease.InQuad)
            .SetUpdate(true)
            .SetLink(this.board.gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(this.BeginCruise);
    }

    /// <summary>결과가 정한 칸에 감속 정지한다. 멈출 자리의 유일한 출처는 이 인자다.</summary>
    public UniTask SettleAtAsync(int _slotIndex, CancellationToken _ct)
    {
        float t_jitterLimit = Mathf.Clamp(this.slotJitterDeg, 0f, MAX_JITTER_DEG);
        float t_jitter = t_jitterLimit > 0f ? Random.Range(-t_jitterLimit, t_jitterLimit) : 0f;

        return this.SettleToAsync(45f * _slotIndex - this.pointerOffsetDeg + t_jitter, this.settleTurns, _ct);
    }

    /// <summary>회전 직전의 자리로 감속 복귀한다. 거절·실패에서 쓴다.</summary>
    public UniTask ReturnHomeAsync(CancellationToken _ct)
        => this.SettleToAsync(this.m_homeAngle, this.returnTurns, _ct);

    /// <summary>회전을 걷는다. 판은 지금 각도에 그대로 남는다.</summary>
    public void Stop() => this.KillSpin();

    async UniTask SettleToAsync(float _restAngle, int _turns, CancellationToken _ct)
    {
        this.KillSpin();

        if (this.board == null) return;

        float t_rest = Mathf.Repeat(_restAngle, 360f);
        float t_from = this.board.localEulerAngles.z;

        // 판은 시계방향(각도 감소)으로만 돈다 — 감속이 방향을 뒤집으면 되감기는 것으로 읽힌다.
        float t_delta = Mathf.Repeat(t_from - t_rest, 360f);
        float t_distance = t_delta + 360f * Mathf.Max(0, _turns);
        float t_target = t_from - t_distance;

        // 감속 시간은 저작이 아니라 거리에서 나온다. 등감속(OutQuad)의 시작 속도는 평균의 2배라
        // T = 2D/v 여야 최고 속도에서 정확히 이어진다 — 이 식을 안 맞추면 감속으로 넘어가는 순간
        // 판이 오히려 빨라진다(그 전에 쓰던 OutQuart는 시작 속도가 평균의 4배였다).
        float t_speed = Mathf.Max(1f, this.cruiseSpeedDegPerSec);
        float t_seconds = Mathf.Clamp(2f * t_distance / t_speed, MIN_SETTLE_SECONDS, MAX_SETTLE_SECONDS);

        // FastBeyond360이 아니면 최단호로 질러가 여러 바퀴가 통째로 사라진다.
        Tween t_settle = this.board
            .DOLocalRotate(new Vector3(0f, 0f, t_target), t_seconds, RotateMode.FastBeyond360)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)
            .SetLink(this.board.gameObject, LinkBehaviour.KillOnDisable);

        this.m_spin = t_settle;

        await t_settle.ToUniTask(cancellationToken: _ct);
    }

    // 가속이 끝난 뒤의 등속 구간. 결과가 올 때까지 무한히 돈다(무한 루프 트윈은 Sequence에 담을 수 없어 따로 세운다).
    void BeginCruise()
    {
        if (this.board == null) return;

        float t_turnSeconds = 360f / Mathf.Max(1f, this.cruiseSpeedDegPerSec);

        this.m_spin = this.board
            .DOLocalRotate(new Vector3(0f, 0f, -360f), t_turnSeconds, RotateMode.LocalAxisAdd)
            .SetEase(Ease.Linear)
            .SetLoops(-1, LoopType.Incremental)
            .SetUpdate(true)
            .SetLink(this.board.gameObject, LinkBehaviour.KillOnDisable);
    }

    void KillSpin()
    {
        if (this.m_spin != null && this.m_spin.IsActive()) this.m_spin.Kill();
        this.m_spin = null;
    }

}
