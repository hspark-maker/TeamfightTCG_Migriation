// 전투에서 받은 보상 골드를 로비 씬까지 실어 나르는 씬 캐리어.
// 지급·영속은 RewardService.GrantBattleReward가 이미 끝냈다 — 여기 실린 값은 "이번에 얼마 벌었는지"의 연출 표시량뿐이다.
// F-20에서 폐기했던 캐리어를 로비 진입 획득 연출 요구로 되살렸다(RankUpHandoff와 같은 성격·같은 1회 소비 규약).
public static class BattleRewardHandoff
{
    // 대기 중인 표시량. 0이면 연출거리가 없다는 뜻이라 "pending 여부"와 값이 갈라질 수 없다.
    static long s_pendingGold;

    /// <summary>로비에서 연출할 획득 골드가 실려 있는지.</summary>
    public static bool HasPending => s_pendingGold > 0;

    /// <summary>표시량을 싣는다. 로비 도달 전 연속 전투가 나도 총합이 남도록 누적한다.</summary>
    public static void Set(long _gold)
    {
        if (_gold <= 0) return;      // 0 이하는 연출거리가 없다(보상 공식에 하한이 있어 정상 경로에선 나오지 않는다).

        s_pendingGold += _gold;
    }

    /// <summary>표시량을 꺼내고 홀더를 비운다(1회 소비). 없으면 0 + false.</summary>
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
