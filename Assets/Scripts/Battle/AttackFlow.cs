using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 공격 해결 흐름의 공유 조각들. PlayerTurn / EnemyTurn / Multiplayer* 4개가
/// 복붙하던 부분을 추출. 각 턴 클래스는 방향(내/상대 field)과 네트워크 훅만 다름.
/// </summary>
public static class AttackFlow
{
    /// <summary>히트 시점(at)에 글로우를 이미 재생하는 키워드. <see cref="Keywords"/>와
    /// <see cref="PlayResultFlourish"/>의 단일 진실원이다.
    ///
    /// 양쪽이 각자 나열하면 같은 글로우를 두 번 await 해 공격 뒤 대기가 KeywordGlowHold만큼 더 붙는다
    /// (원거리·무쌍 후딜이 일반 공격의 두 배가 되던 원인). 여기 넣은 키워드는 PlayResultFlourish에서 빠진다.</summary>
    public const CardKeyword AtHitGlowKeywords = CardKeyword.Ranged | CardKeyword.Peerless;

    /// <summary>공격자 키워드로 연출용 pre/at 키워드 산출.
    /// 무쌍/원거리 모두 히트 후(at) 글로우 — 다른 공격과 동일하게 히트 앞에 대기(딜레이) 없음.</summary>
    public static (CardKeyword pre, CardKeyword at) Keywords(CardInstance _attacker)
    {
        CardKeyword t_pre = CardKeyword.None;
        CardKeyword t_at  = CardKeyword.None;
        if (_attacker.HasKeyword(CardKeyword.Ranged))   t_at |= CardKeyword.Ranged;
        if (_attacker.HasKeyword(CardKeyword.Peerless)) t_at |= CardKeyword.Peerless;
        return (t_pre & AtHitGlowKeywords, t_at & AtHitGlowKeywords);
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

        // 히트 시점에 이미 재생한 키워드(원거리/무쌍)는 제외 — 안 빼면 같은 글로우를 두 번 기다린다.
        // attackerKeywords 자체는 그대로 둔다(결과 소비처가 "무슨 키워드가 발동했나"를 읽는 값이라).
        CardKeyword t_attackerGlow = _result.attackerKeywords & ~AtHitGlowKeywords;

        await UniTask.WhenAll(
            CardView.GetView(_attacker)?.PlayKeywordGlow(t_attackerGlow) ?? UniTask.CompletedTask,
            CardView.GetView(_defender)?.PlayKeywordGlow(_result.defenderKeywords) ?? UniTask.CompletedTask);
    }
}
