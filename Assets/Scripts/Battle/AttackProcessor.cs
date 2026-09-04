using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TeamfightTCG.BattleCore;

/// <summary>
/// 공격 해결의 유일한 시퀀스 소유자. 순서는 데이터가 아니라 코드로 고정한다(결정론).
/// 키워드별 확장은 아래 3개 seam으로만 한다: PreResolve / ExtraTargets / PostResolve.
///
/// ── 새 공격 효과를 만들 때 어디에 넣나 (이 파일을 고치기 전에 먼저 읽을 것) ──
/// ① 순수 규칙(피해량·반격 자격·피해 감소가 바뀌는 것)
///    → CardKeyword 추가 + CardInstance에 폴딩. **이 파일 수정 0.**
///      현 키워드 9개 중 6개가 이 방식이고, 유일하게 확장 비용이 안 늘어나는 경로다.
/// ② 트리거 행동(피격 시/처치 시/공격 후에 무언가 한다)
///    → 기존 SynergyEffect 훅. **이 파일 수정 0.** .cs 1 + 시트 행으로 끝난다.
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
        bool? _forceCunningSwap = null,
        bool _derivedCommand = false)
    {
        // ---- Snapshot: 전부 피해 적용 '전' 값. 이후 단계는 읽기만 한다(반격 동시해결 규칙). ----
        int t_commandAttackerSlot = _attacker.slotIndex;
        int t_commandDefenderSlot = _defender.slotIndex;
        int t_atkDmg = _attacker.AttackDamage();
        int t_ctrDmg = _defender.AttackDamage(); // 동시 해결: 공격 전 수치로 반격 (도발 시 50%)
        bool t_takesCounter = _attacker.TakesCounterFrom(_defender); // 반격 자격(단일 진실원): 원거리/표식 무반격
        bool t_markedCounter = _defender.HasKeyword(CardKeyword.Mark);
        bool t_peerless = _attacker.HasKeyword(CardKeyword.Peerless);
        bool t_cunning = _attacker.HasKeyword(CardKeyword.Cunning);
        bool t_ranged = _attacker.HasKeyword(CardKeyword.Ranged);
        int t_splashDmg = t_peerless ? _attacker.SplashDamage() : 0; // 반격 맞기 전 hp 기준

        // ---- seam 1: PreResolve — 피해 적용 전에 확정해야 하는 결정 ----
        // 교활 스왑 여부. 와이어에서 온 값이 항상 우선(멀티 미러 보장).
        bool t_shouldSwap = t_cunning && (_forceCunningSwap ?? _attackerField.CanSwapWithWaiting(_attacker));

        // ---- 고정 시퀀스 (순서 변경 금지) ----
        int t_defenderHpBefore = _defender.hp + _defender.bonusHp;
        _defender.TakeDamage(t_atkDmg);
        int t_actualAtkDmg = t_defenderHpBefore - (_defender.hp + _defender.bonusHp);
        if (t_actualAtkDmg > 0)
            BattleEventStream.Emit(new BattleEvent(BattleEventKind.Damage,
                _defender.ownerIndex, t_commandDefenderSlot, t_actualAtkDmg,
                _attacker.ownerIndex, t_commandAttackerSlot));

        int t_attackerHpBefore = _attacker.hp + _attacker.bonusHp;
        if (t_takesCounter)
            _attacker.TakeDamage(t_ctrDmg);
        int t_actualCtrDmg = t_takesCounter
            ? t_attackerHpBefore - (_attacker.hp + _attacker.bonusHp)
            : 0;
        if (t_actualCtrDmg > 0)
            BattleEventStream.Emit(new BattleEvent(BattleEventKind.Damage,
                _attacker.ownerIndex, t_commandAttackerSlot, t_actualCtrDmg,
                _defender.ownerIndex, t_commandDefenderSlot, BattleEventFlags.Counter));

        // ---- seam 2: ExtraTargets — 추가 대상 피해 ----
        CardInstance t_splash = null;
        bool t_splashHit = false;
        if (t_peerless)
        {
            t_splash = _preSelectedSplash ?? PickSplash(_defender.slotIndex, _defenderField);
            t_splashHit = t_splash != null && t_splashDmg > 0; // 0 데미지로 무적 태우지 않기
            if (t_splashHit)
            {
                int t_splashSlot = t_splash.slotIndex;
                int t_splashBefore = t_splash.hp + t_splash.bonusHp;
                t_splash.TakeDamage(t_splashDmg);
                int t_actualSplashDmg = t_splashBefore - (t_splash.hp + t_splash.bonusHp);
                if (t_actualSplashDmg > 0)
                    BattleEventStream.Emit(new BattleEvent(BattleEventKind.Damage,
                        t_splash.ownerIndex, t_splashSlot, t_actualSplashDmg,
                        _attacker.ownerIndex, t_commandAttackerSlot, BattleEventFlags.Splash));
            }
        }

        AttackFlow.RunAttacked(_defender, _attacker, _defenderField, _attackerField); // 패시브/시너지 Attacked(동기)
        // 광역 피격자도 같은 직격이므로 시너지 [Attacked]를 발화한다(패시브는 제외 — RunSplashAttacked 주석 참조).
        if (t_splashHit)
            AttackFlow.RunSplashAttacked(t_splash, _attacker, _defenderField, _attackerField);
        // [DamageDealt] 패시브 → 시너지 순. ctx가 소속 필드를 들고 있다(디스패처 BelongsTo 판정용).
        if (t_takesCounter)
        {
            var t_ctrCtx = new DamageDealtCtx(_defender, _defenderField, t_actualCtrDmg, true);
            SynergyTriggers.DamageDealt(t_ctrCtx);
        }
        var t_atkCtx = new DamageDealtCtx(_attacker, _attackerField, t_actualAtkDmg, false);
        SynergyTriggers.DamageDealt(t_atkCtx);
        // ---- 강화(일반): "공격한 후, 원래 체력의 50%만큼 추가 피해" ----
        // 자리가 여기인 이유:
        //  · 기본타·반격보다 뒤 — "공격한 후"이고, 반격에 공격자가 죽었으면 발동하지 않아야 한다(AfterAttack과 같은 기준).
        //  · 치사 래치보다 앞 — 이 추가타로 죽으면 그것도 이 공격의 처치다. 뒤로 밀면 처형 재공격·승패 예측이 어긋난다.
        //  · AfterAttack(RunAfterAttack)이 아닌 이유 — 그건 사망 정리·연출이 끝난 뒤라 거기서 피해를 주면
        //    처형/부활/막타 슬로우/전멸 판정이 전부 한 박자 늦게 본다.
        // 기본타로 이미 쓰러진 대상에는 들어가지 않는다(살아 있는 대상에게 한 번 더 치는 효과).
        // 추가 반격도, [Attacked] 트리거도 없다 — 한 번의 공격이 반격을 두 번 부르면 안 된다.
        int t_enhanceDmg = 0;
        if (_attacker.IsAlive && _defender.IsAlive && _attacker.HasVanillaEnhance)
        {
            int t_enhanceRaw = _attacker.VanillaEnhanceDamage();
            // 추가타도 별도 피해 1회라 보호막·비늘 판정을 다시 받는다.
            int t_before = _defender.hp + _defender.bonusHp;
            _defender.TakeDamage(t_enhanceRaw);
            t_enhanceDmg = t_before - (_defender.hp + _defender.bonusHp);
            if (t_enhanceDmg > 0)
                BattleEventStream.Emit(new BattleEvent(BattleEventKind.Damage,
                    _defender.ownerIndex, t_commandDefenderSlot, t_enhanceDmg,
                    _attacker.ownerIndex, t_commandAttackerSlot, BattleEventFlags.Enhanced));
        }

        // ★ 치사 래치는 반드시 RemoveDead 전. 불사 부활이 RemoveDead 안에서 일어나므로
        //   뒤로 미루면 부활한 방어자에 대해 처형 재공격/AfterAttack 처치 판정(defenderKilled)이 사라진다.
        bool t_defKilled = _defender.hp == 0;

        // ---- seam 3: PostResolve — 치사 판정 후 / 사망 정리 전 행동 ----
        CardInstance t_incoming = (t_shouldSwap && _attacker.IsAlive)
            ? _attackerField.SwapWithWaiting(_attacker)
            : null;
        bool t_swapped = t_incoming != null;
        if (t_swapped)
        {
            BattleEventStream.Emit(new BattleEvent(BattleEventKind.Swap,
                _attacker.ownerIndex, t_commandAttackerSlot, _sourceOwnerIndex: t_incoming.ownerIndex,
                _sourceSlotIndex: t_incoming.slotIndex));
            // [SwappedOut] 패시브 → 시너지 순.
            var t_swapCtx = new SwapOutCtx(_attacker, t_incoming, _attackerField);
            SynergyTriggers.SwappedOut(t_swapCtx);
        }

        RemoveDead(_attackerField);
        RemoveDead(_defenderField);

        // 이 공격이 판을 끝냈는가 — **표시 전용** 기록. 규칙도 결과도 바꾸지 않는다(진짜 승패는 TurnRunner).
        // 여기가 유일하게 맞는 시점이다: 부활·사망 정리가 끝났고, 연출(AttackSequence)은 아직 죽는 카드를
        // 화면에 들고 있다. 공격마다 무조건 불러 지난 판정이 다음 타격으로 새지 않게 한다.
        BattleFinisher.Arm(_attackerField, _defenderField);

        // ---- 결과 조립 ----
        var t_result = MakeResult(_attacker, t_defKilled);
        t_result.damageDealt = t_actualAtkDmg; // 주 대상만(splash 합산 안 함 = v1). 트리거용
        t_result.enhanceDamage = t_enhanceDmg; // 강화 추가타는 따로 — 합산은 AttackResult.TotalDamage가 준다
        t_result.splashDefender = t_splash;
        t_result.attackerSwapped = t_swapped;
        if (t_swapped) t_result.attackerKeywords |= CardKeyword.Cunning;
        if (t_ranged) t_result.attackerKeywords |= CardKeyword.Ranged;
        // 표식은 '표식 덕에 반격이 면제됐을 때'만 발동 표시. 원거리는 표식과 무관하게 이미 무반격이므로 제외
        // (TakesCounterFrom = !Ranged && !Mark — 원거리면 Mark가 아무 일도 안 한 것).
        if (t_markedCounter && !t_ranged) t_result.defenderKeywords |= CardKeyword.Mark;
        BattleCommandLog.RecordAttack(_attackerField.OwnerIndex, t_commandAttackerSlot,
            t_commandDefenderSlot, t_result.attackerSwapped, _derivedCommand);
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

    /// <summary>필드의 사망 카드 정리. Lethal(유산 회복 등)이 먼저 돌 기회를 갖고, 그 뒤 불사 키워드가 판정된다.
    ///
    /// public인 이유는 디버그 도구(<c>Test/BattleDebugKill</c>) 하나 때문이다 — 그쪽이 카드를 강제로
    /// 죽인 뒤 **같은 순서**로 정리해야 훅(Lethal/Removed)이 전투와 똑같이 돈다.
    /// 전투 규칙 쪽에서 새로 부르지 마라: 호출 지점은 지금도 Resolve 하나뿐이고,
    /// 치사 래치보다 먼저 부르면 불사 부활 판정이 뒤집힌다.</summary>
    public static void RemoveDead(BattleField _field)
    {
        for (int i = 0; i < BattleField.SLOT_COUNT; i++)
        {
            CardInstance t_c = _field.GetSlot(i);
            if (t_c != null && !t_c.IsAlive)
            {
                // [Lethal] 치사 트리거 먼저(유산 아군 회복 등). 패시브 → 시너지 순.
                // 둘 다 동기 완결 — 부활은 제자리 hp 복구이므로 아래 IsAlive 게이트가 양쪽 결과를 함께 본다.
                var t_deathCtx = new DeathCtx(t_c, _field);
                SynergyTriggers.Lethal(t_deathCtx);
                // [불사] 시너지 Lethal이 먼저 살릴 기회를 갖고, 그래도 죽어 있을 때만 발동한다 —
                // 유산 회복으로 살아난 카드는 부활 횟수를 소비하지 않는다(피닉시아가 유일한 겸용 카드).
                if (!t_c.IsAlive && t_c.HasKeyword(CardKeyword.Immortal) && t_c.ReviveAtHalf())
                {
                    CardInstance t_revived = t_c;   // 연출 큐가 나중에 읽으므로 루프 변수를 캡처하지 않는다
                    // 죽는 그림 → 디졸브 → 되살아나는 그림. 규칙(체력 복구)은 위에서 이미 끝났고 여기는 표시다.
                    BattlePresentationQueue.Run(() => ImmortalVfx.PlayRevive(t_revived).Forget());
                }
                // 부활(불사)했으면 슬롯 유지 → Removed/RemoveCard 스킵(라이프사이클 재진입 없음).
                if (t_c.IsAlive) continue;
                BattleEventStream.Emit(new BattleEvent(BattleEventKind.Death,
                    t_c.ownerIndex, t_c.slotIndex));
                // [Removed] 제거 직전. 취소 불가. 패시브 → 시너지 순.
                SynergyTriggers.Removed(t_deathCtx);
                _field.RemoveCard(i);
            }
        }
    }
}
