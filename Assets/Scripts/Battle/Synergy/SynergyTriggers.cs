using Cysharp.Threading.Tasks;

// 시너지 트리거 디스패처. 전투 타이밍마다 활성 시너지 효과의 훅을 발화한다.
// 메서드명·선언 순서는 BattleTimings의 타이밍 표와 1:1 — 여기서 새 어휘를 만들지 마라.
// Active에는 requiredCount를 충족한 시너지만 담기므로(SynergyResolver) 별도 활성 판정 불필요.
// 규칙(회복량 등)은 각 SynergyEffect 서브클래스가 소유 — 여기서 재구현 금지.
//
// 공통 형태: state.Active × Tier.effects 이중 순회 → effect.OnX(ctx.WithSynergy(active.Synergy)).
// **이 순회 순서는 결정론 계약이다(양 클라 동형 발화). 바꾸지 마라.**
// 반환 계약(동기 void / .Forget / await)은 BattleEffect 선언을 그대로 따른다 — BattleTimings 참조.
//
// BelongsTo(self) 필터: 대부분의 타이밍은 self 소속 시너지만 발화한다.
// 예외는 Entered / BoardChanged 둘뿐 — 비소속 카드까지 훑어야 하는 효과(흐름 상속, 성벽 라이브 카운트)가
// 있어 필터를 걸지 않고 효과가 ctx.synergy로 스스로 소속 판정한다(각 메서드 주석 참조).
//
// [DeckResolved]는 여기 없다 — 덱 확정 적용은 SynergyApplier.ApplyAll이 소유한다(시너지×카드 순회 형태가
// 다르고 SynergyState 스냅샷을 직접 들고 있음). 여기에 중복 디스패처를 만들지 마라.
public static class SynergyTriggers
{
    // 시너지 효과가 실제 발동한 순간, 발동 주체 self에게 효과 배너 + 카드 배지 pop을 함께 낸다.
    // 순수 UI 피드백(게임상태/RNG 무관, 결정론 무관). 각 효과의 발동 게이트에서 Notify 대신 호출.
    public static void Fire(CardInstance self, SynergyData synergy)
    {
        if (self == null || synergy == null) return;
        // 배너 그림은 카드 초상화가 아니라 시너지 아이콘 — 어느 시너지가 터졌는지가 핵심 정보다.
        // (icon 미배정이면 null 전달 = 기존대로 카드 초상화 폴백)
        CardPassive.Notify(self, synergy.effectDescription, synergy.activeIcon);
        CardView.GetView(self)?.PopSynergyBadge(synergy);       // 발동 주체 카드의 해당 배지 pop
    }


    // [Placed] 오프닝 배치 확정 직후 self 소속 활성 시너지 효과 발화. 런타임 등장(Entered)과 별개 타이밍이다.
    // .Forget — 효과 본문은 첫 await 전에 상태변이를 완결해야 한다(동기 완결 계약).
    public static void Placed(SpawnCtx ctx)
    {
        var t_state = ctx.field?.Synergy;
        if (t_state == null || ctx.self == null) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;
            if (!SynergyApplier.BelongsTo(ctx.self, t_active.Synergy)) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                t_effect.OnPlaced(ctx.WithSynergy(t_active.Synergy)).Forget();
            }
        }
    }

    // [TurnBegan] 카드 단위 턴 시작 시 self 소속 활성 시너지 효과 발화.
    // await — TurnRunner가 카드별로 순차 대기(연출 허용). justSpawned 스킵 판정은 호출부(TurnRunner) 소유.
    public static async UniTask TurnBegan(TurnCtx ctx)
    {
        var t_state = ctx.field?.Synergy;
        if (t_state == null || ctx.self == null) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;
            if (!SynergyApplier.BelongsTo(ctx.self, t_active.Synergy)) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                await t_effect.OnTurnBegan(ctx.WithSynergy(t_active.Synergy));
            }
        }
    }

    // [TurnEnded] 턴 종료 시 self 소속 활성 시너지 효과 발화(유산 스택 적립).
    // 동기 void — TurnRunner가 OnExit 직후 인라인 호출(CheckGameOver 전). RNG 미소비.
    public static void TurnEnded(TurnCtx ctx)
    {
        var t_state = ctx.field?.Synergy;
        if (t_state == null || ctx.self == null) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;
            if (!SynergyApplier.BelongsTo(ctx.self, t_active.Synergy)) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                t_effect.OnTurnEnded(ctx.WithSynergy(t_active.Synergy));
            }
        }
    }

    // [BeforeAttack] 공격 개시 직전 self(공격자) 소속 활성 시너지 효과 발화(무리 선피해 등).
    // 데미지 해결(Execute) 전에 완료돼야 하므로 반드시 await. RNG 미소비(net SAFE).
    public static async UniTask BeforeAttack(BeforeAttackCtx ctx)
    {
        var t_state = ctx.ownField?.Synergy;
        if (t_state == null || ctx.self == null || !ctx.self.IsAlive || ctx.defender == null) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;
            if (!SynergyApplier.BelongsTo(ctx.self, t_active.Synergy)) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                await t_effect.OnBeforeAttack(ctx.WithSynergy(t_active.Synergy));
            }
        }
    }

    // [Attacked] 피격 시 self(방어자) 소속 활성 시너지 효과 발화(성벽 반격 등).
    // 동기 void — 치사 래치 전 인라인 완료 필수(AttackProcessor에서 counter 해결 직후 호출).
    public static void Attacked(AttackedCtx ctx)
    {
        var t_state = ctx.ownField?.Synergy;
        if (t_state == null || ctx.self == null || ctx.attacker == null) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;
            if (!SynergyApplier.BelongsTo(ctx.self, t_active.Synergy)) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                t_effect.OnAttacked(ctx.WithSynergy(t_active.Synergy));
            }
        }
    }

    // [DamageDealt] self가 피해를 입힌 직후 self 소속 활성 시너지 효과 발화. .Forget(동기 완결 계약).
    public static void DamageDealt(DamageDealtCtx ctx)
    {
        var t_state = ctx.field?.Synergy;
        if (t_state == null || ctx.self == null) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;
            if (!SynergyApplier.BelongsTo(ctx.self, t_active.Synergy)) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                t_effect.OnDamageDealt(ctx.WithSynergy(t_active.Synergy)).Forget();
            }
        }
    }

    // [Lethal] 치사 확정 시 self(죽는 카드) 소속 활성 시너지 효과 발화(언데드 부활/유산 회복).
    // 동기 void — RemoveDead가 이 호출 직후 IsAlive로 부활 게이팅. RNG 미소비.
    public static void Lethal(DeathCtx ctx)
    {
        var t_state = ctx.field?.Synergy;
        if (t_state == null || ctx.self == null) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;
            if (!SynergyApplier.BelongsTo(ctx.self, t_active.Synergy)) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                t_effect.OnLethal(ctx.WithSynergy(t_active.Synergy));
            }
        }
    }

    // [Removed] 제거 직전(취소 불가) self 소속 활성 시너지 효과 발화. IsAlive 게이트를 이미 통과한 뒤다.
    // .Forget — 슬롯 제거(RemoveCard)가 바로 뒤라 상태변이는 첫 await 전에 완결해야 한다.
    public static void Removed(DeathCtx ctx)
    {
        var t_state = ctx.field?.Synergy;
        if (t_state == null || ctx.self == null) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;
            if (!SynergyApplier.BelongsTo(ctx.self, t_active.Synergy)) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                t_effect.OnRemoved(ctx.WithSynergy(t_active.Synergy)).Forget();
            }
        }
    }

    // [SwappedOut] self가 필드를 떠나고 incoming이 그 자리에 들어온 직후. self 소속 활성 시너지 효과 발화.
    // .Forget(동기 완결 계약). ctx.field는 이탈이 일어난 필드.
    public static void SwappedOut(SwapOutCtx ctx)
    {
        var t_state = ctx.field?.Synergy;
        if (t_state == null || ctx.self == null) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;
            if (!SynergyApplier.BelongsTo(ctx.self, t_active.Synergy)) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                t_effect.OnSwappedOut(ctx.WithSynergy(t_active.Synergy)).Forget();
            }
        }
    }

    // [Entered] 런타임 등장 시 field 활성 시너지 효과 발화(돌보미 힐/흐름 스택). 오프닝 배치(Placed)와 별개.
    // Lethal/TurnEnded와 달리 BelongsTo(self) 필터를 걸지 않는다 → 효과가 ctx.synergy로 소속을 스스로 판정.
    // (흐름은 비소속 신규 카드에도 flowBonus 상속을 걸어야 하므로 여기서 필터하면 안 됨.)
    // 동기 완결 계약: 효과 본문은 await 없이 상태변이를 끝냄 → .Forget이라도 발화 시점에 상태 확정(net divergence 없음).
    public static void Entered(SpawnCtx ctx)
    {
        var t_state = ctx.field?.Synergy;
        if (t_state == null || ctx.self == null) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                t_effect.OnEntered(ctx.WithSynergy(t_active.Synergy)).Forget();
            }
        }
    }

    // [AfterAttack] 공격 직후 self(공격자) 소속 활성 시너지 효과 발화.
    // ctx.damageDealt: 주 대상 실제 적용 데미지(AttackResult.damageDealt, Execute 시점 캡처값).
    public static async UniTask AfterAttack(AfterAttackCtx ctx)
    {
        var t_state = ctx.ownField?.Synergy;
        if (t_state == null || ctx.self == null || !ctx.self.IsAlive) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;
            if (!SynergyApplier.BelongsTo(ctx.self, t_active.Synergy)) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                await t_effect.OnAfterAttack(ctx.WithSynergy(t_active.Synergy));
            }
        }
    }

    // [BoardChanged] 필드 라이브 구성 변화 직후. 발화점은 BattleField 3곳(ApplyDeckSynergy/NotifyEntered/RemoveCard).
    // Entered와 마찬가지로 BelongsTo 필터를 걸지 않는다 — 보드 전체를 세는 효과라 소속 판정은 효과가 한다.
    // 동기 void, RNG 미소비. 시너지 발화는 필드당 1회(ctx.self = null).
    public static void BoardChanged(BoardCtx ctx)
    {
        var t_state = ctx.field?.Synergy;
        if (t_state == null) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;
            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                t_effect.OnBoardChanged(ctx.WithSynergy(t_active.Synergy));
            }
        }
    }
}
