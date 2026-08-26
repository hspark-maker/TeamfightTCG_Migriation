/// <summary>멀티플레이 대기 상한의 <b>단일 진실원</b>.
///
/// <para><b>전부 벽시계 시간이다.</b> 연출 타이밍(<see cref="BattleTimingConfig"/>)에 넣으면 안 된다 —
/// 그쪽은 노출 프로퍼티마다 전역 배속(SpeedFactor)을 곱하는데, 배속은 플레이어가 바꿀 수 있어서
/// 두 클라의 상한이 갈린다. 같은 이유로 생각시간(turnThinkTime)도 그 SO에서 raw로 노출된다.</para>
///
/// <para>대기를 거는 쪽은 반드시 <c>ignoreTimeScale: true</c>로 재라 — 승패 여운이 timeScale을 만진다.</para></summary>
public static class NetTimeouts
{
    /// <summary>초기화 전체(덱 교환 + 시드 commit-reveal)에 걸리는 <b>하나의</b> 데드라인.
    /// 단계마다 따로 걸면 최악 대기가 단계 수만큼 곱해져 잠긴 화면에 몇 분씩 갇힌다.
    /// 소비: MultiplayerTurnRunner.InitSyncTimeoutSec(별칭), GameInitializer의 ownerIndex 확보 대기.</summary>
    public const float InitSyncSec = 20f;

    /// <summary>상대의 공격 결정 RPC를 기다리는 상한. 상대는 생각시간(30초) 초과 시 자동 공격하므로
    /// 정상적으로는 그 안에 온다 — 이 값은 자동 공격조차 못 나가는 경우(유효 대상 없음·프리즈)를 막는 벽이다.
    /// MultiplayerTurnRunner.WaitForOpponentAttack이 사용한다.</summary>
    public const float TurnActionSec = 60f;

    /// <summary>연출 완료 신호(AnimReady) 교환 상한. 양쪽이 서로의 공격 연출이 끝나기를 기다리는 구간이라
    /// 한쪽이 멈추면 상대가 영원히 잠긴다.
    /// NetworkGameController.WaitForOpponentReady가 사용한다.</summary>
    public const float AnimHandshakeSec = 20f;

    /// <summary>후공 플레이어의 멀리건 선택 상한. 초과 시 스킵(-1)을 전송해 양쪽 RNG를 소비하지 않는다.</summary>
    public const float MulliganPickSec = 20f;

    /// <summary>상대 멀리건 선택 패킷 대기 상한. 초과 시 무효 경기로 종료한다.</summary>
    public const float MulliganWaitSec = 30f;

    /// <summary>상대 이탈 후 AI가 인수하기 전 유예. 현재 0은 즉시 인수한다.
    /// Firebase 재접속은 이 창에서만 허용하고, AI 인수 뒤에는 복귀시키지 않는다.</summary>
    public const float OpponentDropGraceSec = 0f;

    /// <summary>씬 전환 전 Runner 종료 대기 상한. 종료가 늦어져도 UI가 잠기지 않게 상한을 두고
    /// 넘기면 그냥 진행한다(BattleCleanup). 대기 자체를 빼면 안 된다 —
    /// 다음 씬에서 Runner.IsRunning이 아직 true라 스테일 러너로 멀티 오진입한다.</summary>
    public const float RunnerShutdownSec = 3f;
}
