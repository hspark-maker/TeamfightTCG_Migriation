// 전투 보상을 로비 씬까지 실어 나르는 씬 캐리어 (지급·영속은 이미 끝난 표시량)
public static class BattleRewardHandoff
{
    static readonly CurrencyGainBucket s_pending = new CurrencyGainBucket();

    // 표시량을 싣는다 — 로비 도달 전 연속 전투도 총합이 남도록 누적
    public static void Set(CurrencyGain _gain)
    {
        s_pending.Add(_gain);
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
