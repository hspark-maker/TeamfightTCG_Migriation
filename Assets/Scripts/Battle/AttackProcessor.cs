using System.Collections.Generic;
using Cysharp.Threading.Tasks;

/// <summary>
/// 공격 해결의 유일한 시퀀스 소유자. 순서는 데이터가 아니라 코드로 고정한다(결정론).
/// 키워드별 확장은 아래 3개 seam으로만 한다: PreResolve / ExtraTargets / PostResolve.
///
/// ── 새 공격 효과를 만들 때 어디에 넣나 (이 파일을 고치기 전에 먼저 읽을 것) ──
/// ① 순수 규칙(피해량·반격 자격·피해 감소가 바뀌는 것)
///    → CardKeyword 추가 + CardInstance에 폴딩. **이 파일 수정 0.**
///      현 키워드 9개 중 6개가 이 방식이고, 유일하게 확장 비용이 안 늘어나는 경로다.
/// ② 트리거 행동(피격 시/처치 시/공격 후에 무언가 한다)
///    → 기존 CardPassive 또는 SynergyEffect 훅. **이 파일 수정 0.** .cs 1 + .asset 1로 끝난다.
///      이미 열린 훅 지점: Attacked / DamageDealt / SwappedOut / RemoveDead 내 Lethal·Removed.
///      훅 계약: 첫 await 전에 상태변이 완결 + **MatchRandom 미소비**(.Forget/동기 void 훅은 특히).
/// ③ 추가 대상·시퀀스 자체 개입(광역 대상 수 변경, 다단 타격, 대상 대체)
///    → seam 인라인. 단 이건 이 파일만으로 안 끝난다 — AttackFlow.PreSelectSplash(단수 반환),
///      AttackSequence(_splashView 단수 전제), AttackResult.splashDefender(단수), 턴 클래스 4개
///      배관까지 함께 바뀐다. 착수 전에 그 범위를 먼저 확인할 것.
///
/// seam을 "등록된 효과 리스트에 디스패치"로 바꾸는 안은 검토 후 기각.
/// seam2는 RNG 선-소비 계약(연출이 대상을 미리 알아야 함)이 걸려 있어 가장 닫아야 할 지점이다.
/// </summary>
public static class AttackProcessor
{
    public static AttackResult Execute(CardInstance _attacker, CardInstance _defender,
        BattleField _attackerField, BattleField _defenderField,
        CardInstance _preSelectedSplash = null,
        bool? _forceCunningSwap = null)
    {
        // ---- Snapshot: 전부 피해 적용 '전' 값. 이후 단계는 읽기만 한다(반격 동시해결 규칙). ----
        int t_atkDmg = _attacker.AttackDamage();
        int t_ctrDmg = _defender.AttackDamage(); // 동시 해결: 공격 전 수치로 반격 (도발 시 50%)
        bool t_takesCounter = _attacker.TakesCounterFrom(_defender); // 반격 자격(단일 진실원): 원거리/표식 무반격
        bool t_markedCounter = _defender.HasKeyword(CardKeyword.Mark);
        int t_actualAtkDmg = _defender.ClampDamage(t_atkDmg); // 직격(공격): 비늘 감소 반영(기본 true)
        int t_actualCtrDmg = _attacker.ClampDamage(t_ctrDmg, false); // 반격: 비늘 감소 없음(TakeDamage(false)와 일치)

        bool t_peerless = _attacker.HasKeyword(CardKeyword.Peerless);
        bool t_cunning = _attacker.HasKeyword(CardKeyword.Cunning);
        bool t_ranged = _attacker.HasKeyword(CardKeyword.Ranged);
        int t_splashDmg = t_peerless ? _attacker.SplashDamage() : 0; // 반격 맞기 전 hp 기준

        // ---- seam 1: PreResolve — 피해 적용 전에 확정해야 하는 결정 ----
        // 교활 스왑 여부. 와이어에서 온 값이 항상 우선(멀티 미러 보장).
        bool t_shouldSwap = t_cunning && (_forceCunningSwap ?? _attackerField.CanSwapWithWaiting(_attacker));

        // ---- 고정 시퀀스 (순서 변경 금지) ----
        _defender.TakeDamage(t_atkDmg, true); // 직격: 비늘 감소 대상
        if (t_takesCounter)
            _attacker.TakeDamage(t_ctrDmg); // 반격: 비늘 감소 없음(기본 false)

        // ---- seam 2: ExtraTargets — 추가 대상 피해 ----
        CardInstance t_splash = null;
        if (t_peerless)
        {
            t_splash = _preSelectedSplash ?? PickSplash(_defender.slotIndex, _defenderField);
            if (t_splash != null && t_splashDmg > 0) // 0 데미지로 무적 태우지 않기
                t_splash.TakeDamage(t_splashDmg, true); // 스플래시도 공격 직격: 비늘 감소 대상
        }

        AttackFlow.RunAttacked(_defender, _attacker, _defenderField, _attackerField); // 패시브 Attacked + 성벽 반격(동기)
        // [DamageDealt] 패시브 → 시너지 순. ctx가 소속 필드를 들고 있다(디스패처 BelongsTo 판정용).
        if (t_takesCounter)
        {
            var t_ctrCtx = new DamageDealtCtx(_defender, _defenderField, t_actualCtrDmg, true);
            _defender.data.passive?.OnDamageDealt(t_ctrCtx).Forget();
            SynergyTriggers.DamageDealt(t_ctrCtx);
        }
        var t_atkCtx = new DamageDealtCtx(_attacker, _attackerField, t_actualAtkDmg, false);
        _attacker.data.passive?.OnDamageDealt(t_atkCtx).Forget();
        SynergyTriggers.DamageDealt(t_atkCtx);
        if (t_ranged)
            CardPassive.Notify(_attacker, CardKeyword.Ranged);

        // ★ 치사 래치는 반드시 RemoveDead 전. 언데드 부활이 RemoveDead 안에서 일어나므로
        //   뒤로 미루면 부활한 방어자에 대해 처형 재공격/AfterAttack 처치 판정(defenderKilled)이 사라진다.
        bool t_defKilled = _defender.hp == 0;

        // ---- seam 3: PostResolve — 치사 판정 후 / 사망 정리 전 행동 ----
        CardInstance t_incoming = (t_shouldSwap && _attacker.IsAlive)
            ? _attackerField.SwapWithWaiting(_attacker)
            : null;
        bool t_swapped = t_incoming != null;
        if (t_swapped)
        {
            // [SwappedOut] 패시브 → 시너지 순.
            var t_swapCtx = new SwapOutCtx(_attacker, t_incoming, _attackerField);
            _attacker.data.passive?.OnSwappedOut(t_swapCtx).Forget();
            SynergyTriggers.SwappedOut(t_swapCtx);
        }

        RemoveDead(_attackerField);
        RemoveDead(_defenderField);

        // ---- 결과 조립 ----
        var t_result = MakeResult(_attacker, t_defKilled);
        t_result.damageDealt = t_actualAtkDmg; // 주 대상만(splash 합산 안 함 = v1). 트리거용
        t_result.splashDefender = t_splash;
        t_result.attackerSwapped = t_swapped;
        if (t_swapped) t_result.attackerKeywords |= CardKeyword.Cunning;
        if (t_ranged) t_result.attackerKeywords |= CardKeyword.Ranged;
        // 표식은 '표식 덕에 반격이 면제됐을 때'만 발동 표시. 원거리는 표식과 무관하게 이미 무반격이므로 제외
        // (TakesCounterFrom = !Ranged && !Mark — 원거리면 Mark가 아무 일도 안 한 것).
        if (t_markedCounter && !t_ranged) t_result.defenderKeywords |= CardKeyword.Mark;
        return t_result;
    }

    /// <summary>무쌍 광역 대상 사전 선정(연출이 대상을 미리 알아야 함). 게임 RNG 유일 소비 경로.</summary>
    public static CardInstance PreSelectSplash(int _targetSlot, BattleField _field)
        => PickSplash(_targetSlot, _field);

    static CardInstance PickSplash(int _targetSlot, BattleField _field)
    {
        var t_adj = new List<int>();
        if (_targetSlot > 0 && _field.GetSlot(_targetSlot - 1) != null)
            t_adj.Add(_targetSlot - 1);
        if (_targetSlot < BattleField.SLOT_COUNT - 1 && _field.GetSlot(_targetSlot + 1) != null)
            t_adj.Add(_targetSlot + 1);
        if (t_adj.Count == 0) return null;
        return _field.GetSlot(t_adj[MatchRandom.Range(t_adj.Count)]);
    }

    static AttackResult MakeResult(CardInstance _attacker, bool _defKilled)
    {
        bool t_canAttack = _defKilled && _attacker.IsAlive && _attacker.HasKeyword(CardKeyword.Execution);
        return new AttackResult
        {
            defenderKilled = _defKilled,
            canAttackAgain = t_canAttack,
            attackerKeywords = t_canAttack ? CardKeyword.Execution : CardKeyword.None,
        };
    }

    /// <summary>필드의 사망 카드 정리. Lethal(언데드 부활 등)이 먼저 돌 기회를 갖는다.</summary>
    static void RemoveDead(BattleField _field)
    {
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            CardInstance t_c = _field.GetSlot(i);
            if (t_c != null && !t_c.IsAlive)
            {
                // [Lethal] 치사 트리거 먼저(언데드 부활 / 유산 아군 회복). 패시브 → 시너지 순.
                // 둘 다 동기 완결 — 부활은 제자리 hp 복구이므로 아래 IsAlive 게이트가 양쪽 결과를 함께 본다.
                var t_deathCtx = new DeathCtx(t_c, _field);
                t_c.data.passive?.OnLethal(t_deathCtx);
                SynergyTriggers.Lethal(t_deathCtx);
                // 부활(언데드)했으면 슬롯 유지 → Removed/RemoveCard 스킵(라이프사이클 재진입 없음).
                if (t_c.IsAlive) continue;
                // [Removed] 제거 직전. 취소 불가. 패시브 → 시너지 순.
                t_c.data.passive?.OnRemoved(t_deathCtx).Forget();
                SynergyTriggers.Removed(t_deathCtx);
                _field.RemoveCard(i);
            }
        }
    }
}