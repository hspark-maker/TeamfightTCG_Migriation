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
    /// <summary>재공격 대상 하나를 무작위로. 유효 대상이 없으면 null(= 재공격 불가, 턴 종료).
    ///
    /// 후보는 <see cref="BattleField.GetValidTargets"/> 그대로다 — 도발 같은 지정 규칙을 여기서 다시 풀지 않는다
    /// (수동 선택과 후보 집합이 갈리면 "고를 수 없던 카드를 처형이 친다"가 된다).</summary>
    public static CardInstance PickRandomTarget(CardInstance _attacker, BattleField _targetField)
    {
        if (_attacker == null || !_attacker.IsAlive || _targetField == null) return null;

        List<CardInstance> t_targets = _targetField.GetValidTargets(_attacker);
        if (t_targets == null || t_targets.Count == 0) return null;

        return t_targets[MatchRandom.Range(t_targets.Count)];
    }
}
