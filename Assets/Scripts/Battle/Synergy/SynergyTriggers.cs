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
// 예외는 Entered / BoardChanged 둘뿐 — 비소속 카드까지 훑어야 하는 효과(흐름 상속, 보드 파생 상태)가
// 있어 필터를 걸지 않고 효과가 ctx.synergy로 스스로 소속 판정한다(각 메서드 주석 참조).
//
// [DeckResolved]는 여기 없다 — 덱 확정 적용은 SynergyApplier.ApplyAll이 소유한다(시너지×카드 순회 형태가
// 다르고 SynergyState 스냅샷을 직접 들고 있음). 여기에 중복 디스패처를 만들지 마라.
public static class SynergyTriggers
{
    // 시너지 효과가 실제 발동한 순간, 발동 주체 self에게 효과 배너 + 카드 배지 pop을 함께 낸다.
    // 순수 UI 피드백(게임상태/RNG 무관, 결정론 무관). 각 효과의 발동 게이트에서 Notify 대신 호출.
    //
    // 엠블럼은 여기서 무조건 뜨지 않는다 — 시너지마다 "보여줄 순간"이 달라서 SynergyData가 고른다
    // ([Triggered] 플래그). 배너/배지는 발동 주체 1장 고정이고(스팸 방지), 엠블럼만 범위를 갖는다.
    // field는 AllMembers 범위 해석에만 쓴다. null이면 그 범위를 못 푸니 self 1장으로 떨어진다.
    //
    // 반환값 = 엠블럼을 띄웠는가. 연출이 끝나길 기다렸다가 다음 연출을 잇는 호출부(낙인 선피해 → 볼리)가
    // 이걸 보고 대기 여부를 정한다. 대부분의 호출부는 무시한다(기다릴 게 없는 표시성 발동).
    public static bool Fire(CardInstance self, SynergyData synergy, BattleField field = null)
    {
        if (self == null || synergy == null) return false;

        // resolve/present 뒤집기 이후 이 메서드는 Execute 안(= 공격 모션 시작 전)에서 불릴 수 있다.
        // 그대로 재생하면 소리·배너·배지·엠블럼이 부딪히기도 전에 터진다. 캡처 중이면 접촉 프레임까지 미룬다.
        // 엠블럼 반환값은 이때 false로 떨어지는데, 반환을 보는 유일한 호출부(낙인 선피해)는
        // [BeforeAttack] = 캡처 밖이라 영향이 없다.
        if (BattlePresentationQueue.IsDeferring)
        {
            CardInstance t_self = self;
            SynergyData t_synergy = synergy;
            BattleField t_field = field;
            BattlePresentationQueue.Run(() => Present(t_self, t_synergy, t_field));
            return false;
        }

        return Present(self, synergy, field);
    }

    // 순수 표시. 캡처 중이면 지연 재생되고, 아니면 즉시 재생된다.
    static bool Present(CardInstance self, SynergyData synergy, BattleField field)
    {
        if (self == null || synergy == null) return false;
        // 배너 그림은 카드 초상화가 아니라 시너지 아이콘 — 어느 시너지가 터졌는지가 핵심 정보다.
        // (icon 미배정이면 null 전달 = 기존대로 카드 초상화 폴백)
        CardPassive.Notify(self, synergy.effectDescription, synergy.activeIcon);
        CardView.GetView(self)?.PopSynergyBadge(synergy);       // 발동 주체 카드의 해당 배지 pop

        // 화면 가장자리 시너지 줄에서도 그 아이콘만 튄다 — 카드 배지는 카드를 보고 있을 때만 눈에 들어오고,
        // 어느 진영의 시너지가 일했는지는 그 줄이 답한다. 발동 주체의 소유자로 진영을 가른다.
        FieldSynergyPanel.Pop(self.ownerIndex == TurnState.LocalOwnerIndex, synergy);

        // 엠블럼 배선(타이밍·범위·몸짓)은 그 시너지의 연출 에셋이 쥔다 — 여기선 "터졌다"만 알린다.
        return SynergyEmblemVfx.PlayTriggered(self, synergy, field);
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
        // [Placed] 배치 엠블럼은 여기서 띄우지 않는다 — 이 디스패처는 ApplyDeckSynergy에서 도는데
        // 그건 InitializeViews보다 앞이라 CardView가 아직 없다. 발화점은 배치 연출이 끝나는 뷰 쪽
        // (CardView.PlayDealAnim/PlayDealToSlot → SynergyEmblemVfx.PlayPlaced) 하나뿐이다.
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

    // [BeforeAttack] 공격 개시 직전 self(공격자) 소속 활성 시너지 효과 발화(낙인 선피해 등).
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

    // [Attacked] 피격 시 self(방어자) 소속 활성 시너지 효과 발화.
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

    // [Lethal] 치사 확정 시 self(죽는 카드) 소속 활성 시너지 효과 발화(유산 회복 등).
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
        // 런타임 등장도 "놓이는 순간"이라 [Placed]와 같은 플래그를 쓰고, 발화점도 같은 뷰 쪽 하나다
        // (등장 카드도 CardAppearSequence로 배치 연출을 탄다). 여기서 또 띄우면 이중이 된다.
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
