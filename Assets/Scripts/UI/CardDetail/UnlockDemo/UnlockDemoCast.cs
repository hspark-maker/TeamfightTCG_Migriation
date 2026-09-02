/// <summary>이번 대본이 무대에 요구하는 배역. 진영을 따로 들지 않는다 — 자리가 곧 진영이다(윗줄 적·아랫줄 아군).</summary>
// 만드는 문을 둘로 나눈 것은 키워드 축과 시너지 축이 동시에 찬 배역을 애초에 못 만들게 하려는 것이다.
public readonly struct UnlockDemoCast
{
    /// <summary>윗줄 맞은편 — 언제나 적.</summary>
    public readonly int OpponentId;

    /// <summary>윗줄 곁자리 — 언제나 적. 0이면 그 자리를 비운다.</summary>
    public readonly int NeighborId;

    /// <summary>아랫줄 곁자리 — 언제나 아군. 0이면 그 자리를 비운다.</summary>
    public readonly int CompanionId;

    /// <summary>카드에 띄울 키워드 배지. 카드가 가진 것이 아니라 지금 열린 그것이다.</summary>
    public readonly CardKeyword ShowKeyword;

    /// <summary>카드에 띄울 시너지 배지. 규칙을 안 돌리므로 "켜져 있다"는 사실만 담긴다.</summary>
    public readonly SynergyState ShowSynergy;

    public bool UsesNeighbor => this.NeighborId  > 0;
    public bool UsesAlly     => this.CompanionId > 0;

    UnlockDemoCast(int _opponent, int _neighbor, int _companion,
                   CardKeyword _showKeyword, SynergyState _showSynergy)
    {
        this.OpponentId  = _opponent;
        this.NeighborId  = _neighbor;
        this.CompanionId = _companion;
        this.ShowKeyword = _showKeyword;
        this.ShowSynergy = _showSynergy;
    }

    public static UnlockDemoCast OfKeyword(int _opponent, int _neighbor, int _companion, CardKeyword _show)
        => new UnlockDemoCast(_opponent, _neighbor, _companion, _show, null);

    public static UnlockDemoCast OfSynergy(int _opponent, int _companion, SynergyState _show)
        => new UnlockDemoCast(_opponent, 0, _companion, CardKeyword.None, _show);
}

/// <summary>저작이 주는 곁자리 카드 한 장을 어느 줄에 세울지.</summary>
// 회복은 적에게 쏘지 않고 도발은 아군을 대신 맞는 것이라, 곁자리가 적 줄에 서면 둘 다 성립하지 않는다.
public enum EDemoExtraSlot
{
    None,
    EnemyNeighbor,
    AllyCompanion,
}
