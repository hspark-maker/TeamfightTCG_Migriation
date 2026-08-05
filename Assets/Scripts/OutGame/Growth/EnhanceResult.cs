// 강화 1회 시도의 결과
public readonly struct EnhanceResult
{
    public readonly EEnhanceOutcome Outcome;

    // 시도 후 강화 레벨(실패·차단이면 기존 레벨 그대로)
    public readonly int Level;

    public EnhanceResult(EEnhanceOutcome _outcome, int _level)
    {
        Outcome = _outcome;
        Level   = _level;
    }
}

// 강화 시도의 결말(Failed만 결제 후 확률 실패, 나머지는 결제 전 차단)
public enum EEnhanceOutcome
{
    Success,
    Failed,
    NotAffordable,
    MaxLevel,
    NotReady,
}
