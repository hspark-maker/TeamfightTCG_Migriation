/// <summary>
/// 서버가 아직 확정하지 않은 한계돌파 한 방을 화면에만 미리 세워두는 한 장. 발행하는 순간 그 카드의 간식이 줄고
/// 단계가 올라 체력까지 따라 오르며, <see cref="Settle"/> 이 자기가 건 만큼만 되돌린다.
/// 성공 갈래는 걷지 않고 <see cref="Discard"/> 로 버린다 — 서버가 이미 올린 단계이기 때문이다.
/// 진행 중인 약속을 값이 아니라 표로 나르는 방식은 <see cref="CurrencyPendingTicket"/> 과 같다.
/// </summary>
internal sealed class LimitBreakPendingTicket
{
    readonly int m_cardId;
    readonly int m_snackCost;

    // 발행 당시의 세대. 그 뒤에 낙관분이 통째로 버려졌다면 이 표가 걷을 몫은 이미 없다.
    readonly int m_epoch = CardGrowthManager.PendingLimitBreakEpoch;

    bool m_settled;

    LimitBreakPendingTicket(int _cardId, int _snackCost)
    {
        m_cardId    = _cardId;
        m_snackCost = _snackCost;
    }

    /// <summary>카드 한 장의 한계돌파 한 단계를 건다.</summary>
    internal static LimitBreakPendingTicket Hold(int _cardId, int _snackCost)
    {
        var t_ticket = new LimitBreakPendingTicket(_cardId, _snackCost);
        CardGrowthManager.HoldPendingLimitBreak(_cardId, _snackCost);

        return t_ticket;
    }

    /// <summary>서버가 확정했으니 걷지 않고 표만 버린다.
    /// 채택(<see cref="CardGrowthManager.Init"/>)이 이미 낙관분을 버렸으면 이 줄은 표식일 뿐이고,
    /// 아직 닿지 않았으면 그 낙관분이 서버가 올린 단계를 대신 서 있다가 채택이 오는 순간 함께 버려진다 —
    /// 여기서 걷으면 방금 오른 단계와 줄어든 간식이 화면에서 원상복귀한다.</summary>
    internal void Discard()
    {
        m_settled = true;
    }

    /// <summary>걸어둔 만큼을 되돌린다. 멱등이라 성공·거절·예외 어느 갈래에서 여러 번 불러도 한 번만 걷힌다.</summary>
    internal void Settle()
    {
        if (m_settled) return;
        m_settled = true;

        // 세대가 갈렸으면 낙관분은 이미 버려졌다 — 여기서 또 되돌리면 그만큼 간식이 모자라고 단계가 내려앉는다.
        if (m_epoch != CardGrowthManager.PendingLimitBreakEpoch) return;

        CardGrowthManager.ReleasePendingLimitBreak(m_cardId, m_snackCost);
    }
}
