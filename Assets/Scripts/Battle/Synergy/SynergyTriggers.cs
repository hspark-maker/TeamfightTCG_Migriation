using Cysharp.Threading.Tasks;

// 시너지 트리거 디스패처. 공격 흐름 등 이벤트 시점에 활성 시너지 효과의 훅을 발화한다.
// Active에는 requiredCount를 충족한 시너지만 담기므로(SynergyResolver) 별도 활성 판정 불필요.
// 규칙(회복량 등)은 각 SynergyEffect 서브클래스가 소유 — 여기서 재구현 금지.
public static class SynergyTriggers
{
    // 시너지 효과가 실제 발동한 순간, 발동 주체 self에게 효과 배너 + 카드 배지 pop을 함께 낸다.
    // 순수 UI 피드백(게임상태/RNG 무관, 결정론 무관). 각 효과의 발동 게이트에서 Notify 대신 호출.
    public static void Fire(CardInstance self, SynergyData synergy)
    {
        if (self == null || synergy == null) return;
        CardPassive.Notify(self, synergy.effectDescription);   // 효과 라벨 배너(시너지 설명 통일)
        CardView.GetView(self)?.PopSynergyBadge(synergy);       // 발동 주체 카드의 해당 배지 pop
    }


    // 공격 직후 self(공격자) 소속 활성 시너지 효과의 OnAfterAttack 발화.
    // damageDealt: 주 대상 실제 적용 데미지(AttackResult.damageDealt, Execute 시점 캡처값).
    public static async UniTask OnAfterAttack(CardInstance self, BattleField field, int damageDealt)
    {
        var t_state = field?.Synergy;
        if (t_state == null || self == null || !self.IsAlive) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;
            if (!SynergyApplier.BelongsTo(self, t_active.Synergy)) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                await t_effect.OnAfterAttack(self, damageDealt, t_active.Synergy);
            }
        }
    }

    // 공격 개시 직전 self(공격자) 소속 활성 시너지 효과의 OnBeforeAttack 발화(무리 선피해 등).
    // 데미지 해결(Execute) 전에 완료돼야 하므로 반드시 await. RNG 미소비(net SAFE).
    public static async UniTask OnBeforeAttack(CardInstance self, CardInstance defender, BattleField field)
    {
        var t_state = field?.Synergy;
        if (t_state == null || self == null || !self.IsAlive || defender == null) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;
            if (!SynergyApplier.BelongsTo(self, t_active.Synergy)) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                await t_effect.OnBeforeAttack(self, defender, field, t_active.Synergy);
            }
        }
    }

    // 피격 시 self(방어자) 소속 활성 시너지 효과의 OnAttackedBy 발화(성벽 반격 등).
    // 동기 void — RemoveDead 전 인라인 완료 필수(behavior에서 counter 해결 직후 호출).
    public static void OnAttackedBy(CardInstance self, BattleField field, CardInstance attacker)
    {
        var t_state = field?.Synergy;
        if (t_state == null || self == null || attacker == null) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;
            if (!SynergyApplier.BelongsTo(self, t_active.Synergy)) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                t_effect.OnAttackedBy(self, attacker, t_active.Synergy);
            }
        }
    }

    // 스폰 시 field 활성 시너지 효과의 OnSpawn 발화(돌보미 힐/흐름 스택).
    // OnDeath/OnTurnEnd와 달리 BelongsTo(self) 필터를 걸지 않고 synergy를 넘긴다 → 효과가 소속을 스스로 판정.
    // (흐름은 비소속 신규 카드에도 flowBonus 상속을 걸어야 하므로 여기서 필터하면 안 됨.)
    // 동기 완결 계약: 효과 본문은 await 없이 상태변이를 끝냄 → .Forget이라도 발화 시점에 상태 확정(net divergence 없음).
    public static void OnSpawn(CardInstance self, BattleField field)
    {
        var t_state = field?.Synergy;
        if (t_state == null || self == null) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                t_effect.OnSpawn(self, field, t_active.Synergy).Forget();
            }
        }
    }

    // 사망 시 self(죽는 카드) 소속 활성 시너지 효과의 OnDeath 발화(언데드 부활/유산 회복).
    // 동기 void — RemoveDead가 이 호출 직후 IsAlive로 부활 게이팅. RNG 미소비.
    public static void OnDeath(CardInstance self, BattleField field)
    {
        var t_state = field?.Synergy;
        if (t_state == null || self == null) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;
            if (!SynergyApplier.BelongsTo(self, t_active.Synergy)) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                t_effect.OnDeath(self, field, t_active.Synergy);
            }
        }
    }

    // 턴 종료 시 self 소속 활성 시너지 효과의 OnTurnEnd 발화(유산 스택 적립).
    // 동기 void — TurnRunner가 OnExit 직후 인라인 호출(CheckGameOver 전). RNG 미소비.
    public static void OnTurnEnd(CardInstance self, BattleField field)
    {
        var t_state = field?.Synergy;
        if (t_state == null || self == null) return;

        foreach (var t_active in t_state.Active)
        {
            if (t_active?.Tier?.effects == null) continue;
            if (!SynergyApplier.BelongsTo(self, t_active.Synergy)) continue;

            foreach (var t_effect in t_active.Tier.effects)
            {
                if (t_effect == null) continue;
                t_effect.OnTurnEnd(self, t_active.Synergy);
            }
        }
    }
}
