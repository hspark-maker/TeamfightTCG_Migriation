using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 공격 해결 흐름의 공유 조각들. PlayerTurn / EnemyTurn / Multiplayer* 4개가
/// 복붙하던 부분을 추출. 각 턴 클래스는 방향(내/상대 field)과 네트워크 훅만 다름.
/// </summary>
public static class AttackFlow
{
    /// <summary>공격자 키워드로 연출용 pre/at 키워드 산출.</summary>
    public static (CardKeyword pre, CardKeyword at) Keywords(CardInstance _attacker)
    {
        CardKeyword t_pre = _attacker.HasKeyword(CardKeyword.Peerless) ? CardKeyword.Peerless : CardKeyword.None;
        CardKeyword t_at  = _attacker.HasKeyword(CardKeyword.Ranged)   ? CardKeyword.Ranged   : CardKeyword.None;
        return (t_pre, t_at);
    }

    /// <summary>무쌍(Peerless) 광역 대상 사전 선정. 비무쌍이면 (null, null).</summary>
    public static (CardInstance splash, CardView splashView) PreSelectSplash(
        CardInstance _attacker, CardInstance _defender,
        BattleField _defenderField, BattleFieldView _defenderFieldView)
    {
        if (!_attacker.HasKeyword(CardKeyword.Peerless)) return (null, null);
        CardInstance t_splash = PeerlessAttack.PreSelect(_defender.slotIndex, _defenderField);
        CardView t_view = t_splash != null ? _defenderFieldView.GetSlotView(t_splash.slotIndex) : null;
        return (t_splash, t_view);
    }

    /// <summary>공격 직후 공격자 패시브 발동 (OnAfterAttack, 처치 시 OnKill).</summary>
    public static async UniTask RunAfterAttackPassives(
        CardInstance _attacker, CardInstance _defender, BattleField _attackerField, AttackResult _result)
    {
        if (!_attacker.IsAlive) return;
        await (_attacker.data.passive?.OnAfterAttack(_attacker, _defender, _attackerField) ?? UniTask.CompletedTask);
        if (_result.defenderKilled)
            await (_attacker.data.passive?.OnKill(_attacker, _defender) ?? UniTask.CompletedTask);
    }

    /// <summary>발동 키워드 글로우 + 교활(swap) 등장 스케일 연출.</summary>
    public static async UniTask PlayResultFlourish(
        CardView _attackerView, CardInstance _attacker, CardInstance _defender, AttackResult _result)
    {
        await UniTask.WhenAll(
            CardView.GetView(_attacker)?.PlayKeywordGlow(_result.attackerKeywords) ?? UniTask.CompletedTask,
            CardView.GetView(_defender)?.PlayKeywordGlow(_result.defenderKeywords) ?? UniTask.CompletedTask);

        if (_result.attackerSwapped)
        {
            _attackerView.transform.localScale = Vector3.zero;
            await _attackerView.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack).ToUniTask();
        }
    }
}
