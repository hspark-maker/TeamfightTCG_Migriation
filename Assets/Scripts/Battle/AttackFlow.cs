using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 공격 해결 흐름의 공유 조각들. PlayerTurn / EnemyTurn / Multiplayer* 4개가
/// 복붙하던 부분을 추출. 각 턴 클래스는 방향(내/상대 field)과 네트워크 훅만 다름.
/// </summary>
public static class AttackFlow
{
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

    /// <summary>[BeforeAttack] 공격 개시 직전 공격자 트리거 발동(패시브 → 시너지 낙인 선피해 등).
    /// AttackSequence.Play 직전(PreSelectSplash 이후)에 호출 → Execute 전 원자 완료. RNG 미소비(splash 스트림 미교란).</summary>
    public static async UniTask RunBeforeAttack(
        CardInstance _attacker, CardInstance _defender,
        BattleField _attackerField, BattleField _defenderField,
        CardInstance _preSelectedSplash = null)
    {
        BattleFinisher.ArmApproach(null, null, null, null);   // 이전 공격의 미소비 래치 제거
        if (!_attacker.IsAlive) return;
        var t_ctx = new BeforeAttackCtx(_attacker, _defender, _attackerField, _defenderField);
        await SynergyTriggers.BeforeAttack(t_ctx);
        // 낙인 선피해가 반영된 **최신 보드**에서 전투 종료를 예측한다 — 선피해로 이미 hp 0이 된 카드가
        // 슬롯에 남아 있는 상태라, 여기보다 앞에서 계산하면 그 피해를 빼먹는다.
        BattleFinisher.ArmApproach(_attacker, _defender, _attackerField, _defenderField, _preSelectedSplash);
    }

    /// <summary>[Attacked] 피격 시 방어자 트리거 발동(패시브 OnAttacked + 시너지 OnAttacked).
    /// 양쪽 다 동기 void — AttackProcessor의 counter 해결 직후 치사 래치 전에 인라인 완결(hp 기준시점 통일).</summary>
    public static void RunAttacked(
        CardInstance _defender, CardInstance _attacker,
        BattleField _defenderField, BattleField _attackerField)
    {
        var t_ctx = new AttackedCtx(_defender, _attacker, _defenderField, _attackerField);
        SynergyTriggers.Attacked(t_ctx);
    }

    /// <summary>[Attacked] 무쌍 광역 피해를 맞은 카드의 **시너지** 트리거만 발동.
    /// 광역도 공격 직격(비늘 감소 대상)이라 주 대상과 같은 시너지가 일하는데,
    /// 그 발동이 화면에 안 뜨면 "무쌍만 시너지가 없는" 그림이 된다.
    ///
    /// **패시브(가시 반격 등)는 일부러 뺐다** — 반격은 주 대상 1장 규칙이고, 여기서 같이 돌리면
    /// 무쌍이 반격을 두 번 맞는 규칙 변경이 된다(연출 수정이 밸런스를 건드리는 자리).
    /// 주 대상 RunAttacked와 같은 지점에서 동기 인라인 완결 — 치사 래치 전(hp 기준시점 통일).</summary>
    public static void RunSplashAttacked(
        CardInstance _splash, CardInstance _attacker,
        BattleField _defenderField, BattleField _attackerField)
    {
        if (_splash == null) return;
        SynergyTriggers.Attacked(new AttackedCtx(_splash, _attacker, _defenderField, _attackerField));
    }

    /// <summary>[AfterAttack] 공격 직후 공격자 패시브 + 시너지 발동. 처치 판정은 ctx.defenderKilled(구 OnKill 게이트와 동일 소스).</summary>
    public static async UniTask RunAfterAttack(
        CardInstance _attacker, CardInstance _defender,
        BattleField _attackerField, BattleField _defenderField, AttackResult _result)
    {
        if (!_attacker.IsAlive) return;
        var t_ctx = new AfterAttackCtx(_attacker, _defender, _attackerField, _defenderField,
                                       _result.damageDealt, _result.defenderKilled);
        // 시너지 공격-후 트리거(포식자 회복 등). 패시브 발화 직후, 생존 가드 이후.
        await SynergyTriggers.AfterAttack(t_ctx);
    }

    /// <summary>[4] 공격 후 단계 묶음 — 규칙 트리거(RunAfterAttack)를 돌리고
    /// 마지막에 ⑥ 처치 연출을 **예약만** 하고 끝난다(EnqueueKillFlourish).
    ///
    /// **AttackSequence 의 _afterHit 로 넘겨** 사망 연출보다 앞에서 돌린다. 6단계 고정 순서
    /// (공격 전 → 공격 중 → 피격 → 공격 후 → 사망 → 처치, 표는 AttackSequence.PlayCore)에서 이 묶음이 ④다.
    ///
    /// 키워드 글로우(발동 키워드·표식)는 폐기됐다 — 이 자리에 있던 결과 연출 PlayResultFlourish도 함께 사라졌다.
    /// 남은 결과 연출은 처형 하나이고 그건 ⑥이다.</summary>
    public static async UniTask RunAfterAttackPhase(
        CardView _attackerView, CardInstance _attacker, CardInstance _defender,
        BattleField _attackerField, BattleField _defenderField, AttackResult _result)
    {
        await RunAfterAttack(_attacker, _defender, _attackerField, _defenderField, _result);
        EnqueueKillFlourish(_attacker, _result);
    }

    /// <summary>[6] 처치 연출 예약 — 처형 발동 그림. **여기서 재생하지 않는다.**
    ///
    /// 처형은 "죽였다"가 조건이라 ④가 아니라 ⑥이다. 쓰러지는 그림(⑤)보다 앞서 뜨면
    /// 아직 서 있는 적 위에서 처형이 터지고, 같은 박에 뜨면 둘이 뭉갠다.
    /// 큐에 담아 AttackSequence가 사망 연출 뒤에 푼다(BattlePresentationQueue.DrainKillsAsync).
    ///
    /// 판정은 AttackProcessor가 세운 attackerKeywords 그대로 — 여기서 처치/키워드를 다시 보지 않는다.</summary>
    static void EnqueueKillFlourish(CardInstance _attacker, AttackResult _result)
    {
        if (!_result.attackerKeywords.HasFlag(CardKeyword.Execution)) return;

        CardInstance t_attacker = _attacker;
        // 뷰는 그때 다시 찾는다 — 예약과 재생 사이에 슬롯이 갈릴 수 있다.
        BattlePresentationQueue.RunOnKill(() => ExecutionVfx.Play(CardView.GetView(t_attacker)));
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

}
