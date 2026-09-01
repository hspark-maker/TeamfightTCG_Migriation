using System.Collections.Generic;

/// <summary>
/// 서버가 아직 확정하지 않은 재화 변동을 화면에만 미리 세워두는 한 장. 발행하는 순간 표시 잔액이 오르고(내리고),
/// <see cref="Settle"/> 이 자기가 건 만큼만 되돌린다 — 서버 잔액 채택 직전에 걷어야 이중 계상도 역주행도 없다.
/// </summary>
public sealed class CurrencyPendingTicket
{
    readonly long[] m_held = new long[(int)ECurrencyType.Count];

    // 발행 당시의 세대. 그 뒤에 낙관분이 통째로 버려졌다면 이 표가 걷을 몫은 이미 없다.
    readonly int m_epoch = CurrencyManager.PendingEpoch;

    bool m_settled;

    /// <summary>한 종류만 건다. 차감은 음수로 넘긴다.</summary>
    public static CurrencyPendingTicket Hold(ECurrencyType _type, long _delta)
    {
        var t_ticket = new CurrencyPendingTicket();
        t_ticket.Add(_type, _delta);

        return t_ticket;
    }

    /// <summary>보상 예고 목록을 건다.</summary>
    public static CurrencyPendingTicket Hold(IReadOnlyList<CurrencyGain> _rewards)
    {
        var t_ticket = new CurrencyPendingTicket();
        if (_rewards == null) return t_ticket;

        for (int t_i = 0; t_i < _rewards.Count; t_i++)
            t_ticket.Add(_rewards[t_i].Type, _rewards[t_i].Amount);

        return t_ticket;
    }

    /// <summary>표시용으로 정규화된 보상 줄을 건다(랭크·앨범·모험가 쥐고 있는 모양).</summary>
    public static CurrencyPendingTicket Hold(IReadOnlyList<RewardLine> _rewards)
    {
        var t_ticket = new CurrencyPendingTicket();
        if (_rewards == null) return t_ticket;

        for (int t_i = 0; t_i < _rewards.Count; t_i++)
            t_ticket.Add(_rewards[t_i].Gain.Type, _rewards[t_i].Gain.Amount);

        return t_ticket;
    }

    /// <summary>걸어둔 만큼을 되돌린다. 멱등이라 성공·거절·예외 어느 갈래에서 여러 번 불러도 한 번만 걷힌다.
    /// 서버 잔액 채택이 바로 뒤따르는 자리에서는 <paramref name="_notify"/> 를 꺼서 그 채택 하나만 화면에 닿게 한다.</summary>
    public void Settle(bool _notify = true)
    {
        if (m_settled) return;
        m_settled = true;

        // 세대가 갈렸으면 누계는 이미 0으로 밀렸다 — 여기서 또 빼면 그만큼 음수로 굳는다.
        if (m_epoch != CurrencyManager.PendingEpoch) return;

        for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++)
            CurrencyManager.ReleasePending((ECurrencyType)t_i, m_held[t_i], _notify);
    }

    void Add(ECurrencyType _type, long _delta)
    {
        if (_delta == 0) return;

        m_held[(int)_type] += _delta;
        CurrencyManager.HoldPending(_type, _delta);
    }
}
