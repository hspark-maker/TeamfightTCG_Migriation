// 전투 보상을 로비 씬까지 실어 나르는 씬 캐리어 (서버가 지급을 확정한 뒤의 표시량만 실린다).
// 싣는 시점이 전투 종료가 아니라 서버 응답 도착이라 로비 진입 뒤에 늦게 채워질 수 있다 — OnGainAdded 가 그 자리를 메운다.
public static class BattleRewardHandoff
{
    static readonly CurrencyGainBucket s_pending = new CurrencyGainBucket();

    /// <summary>표시량이 새로 실렸다. 로비가 이미 열려 있어 진입 연출을 놓친 경우를 위한 신호다.</summary>
    // 싱글(BattleRewardCommand)과 멀티(PayoutInbox) 둘 다 여기로 들어온다 — 멀티도 상대 제출이 늦으면
    // 로비 진입 뒤에 지급이 확정되므로 같은 자리가 필요하다.
    public static event System.Action OnGainAdded;

    // 표시량을 싣는다 — 로비 도달 전 연속 전투도 총합이 남도록 누적
    public static void Set(CurrencyGain _gain)
    {
        if (!_gain.HasAmount) return;

        s_pending.Add(_gain);
        OnGainAdded?.Invoke();
    }

    // 표시량을 _into로 옮기고 홀더를 비운다 (1회 소비)
    public static bool TryConsume(CurrencyGainBucket _into)
    {
        if (s_pending.IsEmpty) return false;

        _into?.Add(s_pending);
        s_pending.Clear();
        return true;
    }
}
