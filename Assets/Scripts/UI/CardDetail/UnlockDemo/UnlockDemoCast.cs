/// <summary>이번 대본이 무대에 세워 달라고 요구하는 배역.
///
/// 진영을 따로 들지 않는다 — **자리가 곧 진영이다**(윗줄은 적, 아랫줄은 아군).
/// 진영만 뒤집고 자리를 그대로 두면 아군이 적 줄에 서서, 힐러가 적을 살리는 그림이 된다.
///
/// 만드는 문을 키워드용·시너지용 둘로 나눠 둔 것은 "두 축이 동시에 찬" 배역을 애초에
/// 만들 수 없게 하기 위해서다 — Render가 좁히는 키워드와 배지가 읽는 시너지는 서로 배타적이다.</summary>
public readonly struct UnlockDemoCast
{
    /// <summary>윗줄 맞은편 — 언제나 적.</summary>
    public readonly int OpponentId;

    /// <summary>윗줄 곁자리 — 언제나 적(무쌍의 광역 대상). 0이면 그 자리를 비운다.</summary>
    public readonly int NeighborId;

    /// <summary>아랫줄 곁자리 — 언제나 아군(도발이 지키는 쪽·힐러가 살리는 쪽·시너지 동료).
    /// 0이면 그 자리를 비운다.</summary>
    public readonly int CompanionId;

    /// <summary>카드 위에 띄울 키워드 배지. 카드가 원래 가진 것이 아니라 **지금 열린 그것**이다.</summary>
    public readonly CardKeyword ShowKeyword;

    /// <summary>카드 위에 띄울 시너지 배지. 규칙을 돌리지 않으므로 "켜져 있다"는 사실만 담긴다.</summary>
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

/// <summary>저작이 주는 곁자리 카드 한 장을 어느 줄에 세울지.
///
/// 회복은 적에게 쏘지 않고, 도발은 아군을 대신 맞아주는 것이라 곁자리가 적 줄에 서면
/// "누구를 지켰나"도 "누구를 살렸나"도 성립하지 않는다. 반대로 무쌍의 광역 대상은 적이어야 한다.</summary>
public enum EDemoExtraSlot
{
    /// <summary>곁자리를 아예 쓰지 않는다 — 세워두면 화면만 복잡해지고 시선이 갈린다.</summary>
    None,

    /// <summary>윗줄(적)에 세운다.</summary>
    EnemyNeighbor,

    /// <summary>아랫줄(아군)에 세운다.</summary>
    AllyCompanion,
}
