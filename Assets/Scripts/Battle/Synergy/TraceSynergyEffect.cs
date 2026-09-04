
// 추적(Trace): [AfterAttack] 하나로 두 단계를 다 처리한다.
//  2단계 — 공격 후 살아남은 적에게 표식(Mark) 부여.
//  4단계 — 공격 **전부터** 표식이 있던 적을 처치하면 공격자에게 bonusHp 부여.
//
// 티어는 누적되지 않는다(SynergyResolver가 만족 티어 중 최고 하나만 Active로 만든다) —
// 4단계 에셋도 grantMarkOnAttack을 켜 둬야 2단계 효과가 유지된다.
//
// 결정론: RNG 미소비, 순수 산술. bonusHp·runtimeKeywords 둘 다 BattleStateHash가 폴딩하므로
// 양쪽 클라가 같은 순서로 같은 값을 써야 한다 — 여기서 보드 순회나 시간 의존을 넣지 말 것.
public class TraceSynergyEffect : SynergyEffect
{
    bool grantMarkOnAttack = true;
    int bonusHpOnMarkedKill;

    public override bool TrySetParam(string _key, string _value)
    {
        switch (_key)
        {
            case nameof(grantMarkOnAttack):     this.grantMarkOnAttack = ParseBool(_value); return true;
            case nameof(bonusHpOnMarkedKill):   this.bonusHpOnMarkedKill = ParseInt(_value); return true;
            default: return false;
        }
    }

    public override void OnAfterAttack(AfterAttackCtx _ctx)
    {
        // self는 트리거가 IsAlive까지 걸러 준다. target은 아무도 안 보므로 여기서 막는다.
        if (_ctx.target == null) return;

        // 공격 **전** 상태를 먼저 캡처한다 — 아래에서 붙인 표식이 같은 공격의 처치 보상을
        // 자기충족시키면 2단계만으로 4단계가 항상 터진다.
        bool t_wasMarked = _ctx.target.HasKeyword(CardKeyword.Mark);

        // 처치 판정은 ctx.defenderKilled(치사 래치) — 언데드 부활은 이 래치 뒤에 일어나므로
        // 부활한 적도 '처치'로 친다. 반격으로 공격자가 죽은 경우는 트리거의 IsAlive 가드가 막는다.
        if (t_wasMarked && _ctx.defenderKilled && this.bonusHpOnMarkedKill > 0)
        {
            _ctx.self.GrantBonusHp(this.bonusHpOnMarkedKill);

            // 표시는 [6] 처치 단계로 미룬다 — 이 보상은 "죽였다"가 조건이라 ④가 아니다.
            // 여기서 그냥 내면 아직 서 있는(사망 연출 전) 적 앞에서 처치 보상 배너가 먼저 뜬다.
            // 규칙(bonusHp)은 위에서 이미 끝났고 담기는 건 순수 표시뿐이다.
            CardInstance t_self = _ctx.self;
            SynergyRuntime t_synergy = _ctx.synergy;
            BattleField t_field = _ctx.ownField;
            BattlePresentationQueue.RunOnKill(() => SynergyTriggers.Fire(t_self, t_synergy, t_field));
        }

        // 이미 표식이 붙은 적은 건너뛴다 — 비트는 어차피 멱등이라 규칙은 안 변하지만,
        // 그냥 두면 같은 적을 때릴 때마다 배너·배지가 다시 튄다(발동하지 않은 발동 표시).
        if (this.grantMarkOnAttack && !t_wasMarked && _ctx.target.IsAlive)
        {
            _ctx.target.runtimeKeywords |= CardKeyword.Mark;
            SynergyTriggers.Fire(_ctx.self, _ctx.synergy, _ctx.ownField);
            PlayMark(_ctx.target, _ctx.synergy);
        }

    }

    /// <summary>표식이 붙은 적 자리에 낙점을 띄운다. 규칙은 이미 위에서 끝났고 여기서부터는 표시뿐이라
    /// 기다리지 않는다 — 수명은 VfxEntry.lifetime이 쥐고 반납은 BattleVfx가 한다.
    /// 미배선이거나 뷰가 없으면 조용히 생략(연출은 선택).</summary>
    static void PlayMark(CardInstance _target, SynergyRuntime _synergy)
    {
        CardCatalog.TryGetSynergyData(_synergy, out SynergyData t_presentation);
        if (!(t_presentation?.vfx is TraceSynergyVfxConfig t_vfx) || t_vfx.mark.prefab == null) return;

        CardView t_view = CardView.GetView(_target);
        if (t_view == null) return;

        BattleVfx.Play(t_vfx.mark, t_view.SlotPosition, t_view.VfxSortingLayerId);
    }
}
