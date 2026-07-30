using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 공격 해결 흐름의 공유 조각들. PlayerTurn / EnemyTurn / Multiplayer* 4개가
/// 복붙하던 부분을 추출. 각 턴 클래스는 방향(내/상대 field)과 네트워크 훅만 다름.
/// </summary>
public static class AttackFlow
{
    /// <summary>공격자 키워드로 연출용 pre/at 키워드 산출.
    /// 무쌍/원거리 모두 히트 후(at) 글로우 — 다른 공격과 동일하게 히트 앞에 대기(딜레이) 없음.</summary>
    public static (CardKeyword pre, CardKeyword at) Keywords(CardInstance _attacker)
    {
        CardKeyword t_pre = CardKeyword.None;
        CardKeyword t_at  = CardKeyword.None;
        if (_attacker.HasKeyword(CardKeyword.Ranged))   t_at |= CardKeyword.Ranged;
        if (_attacker.HasKeyword(CardKeyword.Peerless)) t_at |= CardKeyword.Peerless;
        return (t_pre, t_at);
    }

    /// <summary>무쌍(Peerless) 광역 대상 사전 선정. 비무쌍이면 (null, null).</summary>
    public static (CardInstance splash, CardView splashView) PreSelectSplash(
        CardInstance _attacker, CardInstance _defender,
        BattleField _defenderField, BattleFieldView _defenderFieldView)
    {
        if (!_attacker.HasKeyword(CardKeyword.Peerless)) return (null, null);
        CardInstance t_splash = AttackProcessor.PreSelectSplash(_defender.slotIndex, _defenderField);
        CardView t_view = t_splash != null ? _defenderFieldView.GetSlotView(t_splash.slotIndex) : null;
        return (t_splash, t_view);
    }

    /// <summary>[BeforeAttack] 공격 개시 직전 공격자 트리거 발동(패시브 → 시너지 무리 선피해 등).
    /// AttackSequence.Play 직전(PreSelectSplash 이후)에 호출 → Execute 전 원자 완료. RNG 미소비(splash 스트림 미교란).</summary>
    public static async UniTask RunBeforeAttack(
        CardInstance _attacker, CardInstance _defender,
        BattleField _attackerField, BattleField _defenderField)
    {
        if (!_attacker.IsAlive) return;
        var t_ctx = new BeforeAttackCtx(_attacker, _defender, _attackerField, _defenderField);
        await (_attacker.data.passive?.OnBeforeAttack(t_ctx) ?? UniTask.CompletedTask);
        await SynergyTriggers.BeforeAttack(t_ctx);
    }

    /// <summary>[Attacked] 피격 시 방어자 트리거 발동(패시브 OnAttacked + 시너지 성벽 반격 등).
    /// 양쪽 다 동기 void — AttackProcessor의 counter 해결 직후 치사 래치 전에 인라인 완결(hp 기준시점 통일).</summary>
    public static void RunAttacked(
        CardInstance _defender, CardInstance _attacker,
        BattleField _defenderField, BattleField _attackerField)
    {
        var t_ctx = new AttackedCtx(_defender, _attacker, _defenderField, _attackerField);
        _defender.data.passive?.OnAttacked(t_ctx);
        SynergyTriggers.Attacked(t_ctx);
    }

    /// <summary>[AfterAttack] 공격 직후 공격자 패시브 + 시너지 발동. 처치 판정은 ctx.defenderKilled(구 OnKill 게이트와 동일 소스).</summary>
    public static async UniTask RunAfterAttack(
        CardInstance _attacker, CardInstance _defender,
        BattleField _attackerField, BattleField _defenderField, AttackResult _result)
    {
        if (!_attacker.IsAlive) return;
        var t_ctx = new AfterAttackCtx(_attacker, _defender, _attackerField, _defenderField,
                                       _result.damageDealt, _result.defenderKilled);
        await (_attacker.data.passive?.OnAfterAttack(t_ctx) ?? UniTask.CompletedTask);
        // 시너지 공격-후 트리거(청소부 회복 등). 패시브 발화 직후, 생존 가드 이후.
        await SynergyTriggers.AfterAttack(t_ctx);
    }

    /// <summary>교활 스왑 교대 연출: 물러나는 카드 퇴장 → 슬롯 재렌더 → 들어온 카드가 덱에서 등장.
    ///
    /// **보드 보충(FillEmptySlots/Refresh) 직전에** 부를 것 — 그 전에는 슬롯 뷰가 아직 물러나는 카드를
    /// 그리고 있고, 그 창을 놓치면 엉뚱한 카드가 나가는 그림이 된다.
    /// 스왑된 슬롯은 비어 있지 않아 보충 연출(PlayFillAnim) 대상이 아니므로 등장도 여기서 책임진다.
    /// 스왑 여부는 AttackProcessor가 세운 결과 그대로 읽는다.</summary>
    public static async UniTask PlayCunningSwap(
        BattleFieldView _attackerFieldView, CardView _attackerView, AttackResult _result)
    {
        if (!_result.attackerSwapped) return;

        await CunningVfx.PlayExit(_attackerView);

        // 들어온 카드를 슬롯에 그린 뒤 등장 — 재렌더 전에 등장을 돌리면 나간 카드가 되돌아온다.
        _attackerFieldView?.Refresh();
        await CunningVfx.PlayEnter(_attackerView);
    }

    /// <summary>발동 키워드 글로우 + 처형 연출. 교활 등장은 PlayCunningSwap 담당(덱에서 나오는 배치 연출).</summary>
    public static async UniTask PlayResultFlourish(
        CardView _attackerView, CardInstance _attacker, CardInstance _defender, AttackResult _result)
    {
        // 처형 발동(처치 + 재공격 권한)이면 전용 연출을 글로우와 같은 프레임에 얹는다.
        // 판정은 AttackProcessor가 세운 attackerKeywords 그대로 — 여기서 처치/키워드를 다시 보지 않는다.
        if (_result.attackerKeywords.HasFlag(CardKeyword.Execution))
            ExecutionVfx.Play(CardView.GetView(_attacker));

        await UniTask.WhenAll(
            CardView.GetView(_attacker)?.PlayKeywordGlow(_result.attackerKeywords) ?? UniTask.CompletedTask,
            CardView.GetView(_defender)?.PlayKeywordGlow(_result.defenderKeywords) ?? UniTask.CompletedTask);
    }
}
