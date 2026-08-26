internal enum EGameBootState
{
    Booting,
    Syncing,
    Ready,
    UpdateRequired,
    RecoveryRequired,

    // 재시도로 풀릴 수 있는 대기 상태. 위 두 종료 상태와 달리 부트가 계속 기다린다 —
    // 소비자가 이걸 종료로 취급하면 재시도해도 세이브 의존 설치가 붙지 않는다.
    BlockedRetryable,
}
