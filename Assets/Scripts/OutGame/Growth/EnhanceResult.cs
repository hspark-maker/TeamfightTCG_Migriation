/// <summary>
/// 강화 1회 시도의 결과. out 파라미터로 흩뿌리지 않고 한 값으로 묶는다 —
/// UI는 결말과 결과 레벨을 함께 봐야 연출과 표시를 고를 수 있다.
/// </summary>
public readonly struct EnhanceResult
{
    public readonly EEnhanceOutcome Outcome;

    /// <summary>시도 후 강화 레벨. 실패·차단이면 기존 레벨 그대로(강화는 하락하지 않는다).</summary>
    public readonly int Level;

    public EnhanceResult(EEnhanceOutcome _outcome, int _level)
    {
        Outcome = _outcome;
        Level   = _level;
    }
}

/// <summary>강화 시도의 결말. Failed는 "결제까지 끝났으나 확률에서 떨어진" 것이라 나머지 실패들과 성격이 다르다.</summary>
public enum EEnhanceOutcome
{
    Success,             // 레벨 +1
    Failed,              // 골드만 소모, 레벨 유지
    BlockedByEvolution,  // 진화 게이트에 막힘 — 진화 전까지 강화 불가
    NotAffordable,       // 골드 부족 — 소모 없음
    MaxLevel,            // 이미 상한 레벨
    NotReady,            // 성장 캐시 미초기화 — 결제 전에 거부(결과를 저장할 수 없는 상태)
}
