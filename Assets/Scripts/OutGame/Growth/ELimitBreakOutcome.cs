// 한계돌파 1회의 결말. 강화와 달리 확률 실패가 없어 "성립하면 반드시 오른다" —
// Success가 아닌 값은 전부 결제 전에 막힌 것이라 간식이 줄지 않는다.
public enum ELimitBreakOutcome
{
    Success,
    NotEnoughSnack,
    MaxStage,
    NotReady,
}
