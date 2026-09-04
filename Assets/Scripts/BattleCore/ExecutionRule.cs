using System.Collections.Generic;

/// <summary>처형(Execution) 재공격의 대상 선택 규칙 — <b>단일 진실원</b>.
///
/// 처형은 "처치했으니 한 번 더"다. 예전엔 그 한 번을 플레이어가 다시 골랐지만
/// (<see cref="BattleUxFlags.ExecutionRandomTarget"/> = false로 되돌리면 그 경로가 그대로 산다),
/// 지금은 무작위 대상으로 자동 발사한다.
///
/// <para><b>결정론 주의.</b> 여기가 <see cref="MatchRandom"/> 소비 지점이다 — 멀티에서는
/// 공격한 쪽(MultiplayerPlayerTurn)과 그걸 재생하는 쪽(MultiplayerOpponentTurn)이
/// <b>같은 순간에 같은 횟수</b>로 뽑아야 스트림이 어긋나지 않는다. 원격 쪽은 실제 대상을 RPC로 받으므로
/// 뽑은 값을 쓰지 않지만, 소비 자체는 반드시 같이 한다(그쪽 호출부 주석 참조).</para></summary>
public static class ExecutionRule
{
    /// <summary>재공격 대상 하나를 무작위로. 칠 카드가 없으면 null(= 재공격 불가, 턴 종료).
    ///
    /// <b>도발을 무시한다.</b> 후보는 살아 있는 적 전부(<see cref="BattleFieldState.GetActiveCards"/>)다 —
    /// 처형 재공격은 플레이어가 고르는 공격이 아니라
    /// "죽인 기세로 한 번 더" 나가는 것이라, 도발이 걸어 놓은 지정을 따르지 않는다.
    ///
    /// 후보 순서는 슬롯 오름차순으로 고정이라 양 클라가 같은 목록에서 같은 인덱스를 뽑는다(결정론).</summary>
    public static CardInstance PickRandomTarget(CardInstance _attacker, BattleFieldState _targetField)
    {
        if (_attacker == null || !_attacker.IsAlive || _targetField == null) return null;

        List<CardInstance> t_all = _targetField.GetActiveCards();
        if (t_all == null || t_all.Count == 0) return null;

        // 슬롯에 남아 있어도 이미 죽은 카드는 후보에서 뺀다(제거 정리 전 타이밍 방어).
        var t_targets = new List<CardInstance>();
        foreach (CardInstance t_card in t_all)
            if (t_card != null && t_card.IsAlive) t_targets.Add(t_card);

        if (t_targets.Count == 0) return null;
        return t_targets[MatchRandom.Range(t_targets.Count)];
    }
}
