// 전투 보상 골드를 로비 씬까지 실어 나르는 씬 캐리어 (지급·영속은 이미 끝난 표시량)
public static class BattleRewardHandoff
{
    static long s_pendingGold;

    // 로비에서 연출할 획득 골드가 실려 있는지
    public static bool HasPending => s_pendingGold > 0;

    // 표시량을 싣는다 — 로비 도달 전 연속 전투도 총합이 남도록 누적
    public static void Set(long _gold)
    {
        if (_gold <= 0) return;

        s_pendingGold += _gold;
    }

    // 표시량을 꺼내고 홀더를 비운다 (1회 소비)
    public static bool TryConsume(out long _gold)
    {
        if (s_pendingGold <= 0)
        {
            _gold = 0;
            return false;
        }

        _gold = s_pendingGold;
        s_pendingGold = 0;
        return true;
    }
}
