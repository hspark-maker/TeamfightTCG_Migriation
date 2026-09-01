using Cysharp.Threading.Tasks;
using UnityEngine;

// 추적: [AfterAttack] 하나로 두 단계를 다 처리한다.
//  2단계 — 공격 후 살아남은 적에게 표식(Mark) 부여.
//  4단계 — 공격 **전부터** 표식이 있던 적을 처치하면 공격자에게 bonusHp 부여.
//
// 티어는 누적되지 않는다(SynergyResolver가 만족 티어 중 최고 하나만 Active로 만든다) —
// 4단계 에셋도 grantMarkOnAttack을 켜 둬야 2단계 효과가 유지된다.
//
// 결정론: RNG 미소비, 순수 산술. bonusHp·runtimeKeywords 둘 다 BattleStateHash가 폴딩하므로
// 양쪽 클라가 같은 순서로 같은 값을 써야 한다 — 여기서 보드 순회나 시간 의존을 넣지 말 것.
[CreateAssetMenu(fileName = "TrackingSynergyEffect", menuName = "Card Battle/Synergy Effect/Tracking")]
public class TrackingSynergyEffect : SynergyEffect
{
    [SerializeField] bool grantMarkOnAttack = true;
    [SerializeField, Min(0)] int bonusHpOnMarkedKill;

    public override bool TrySetParam(string _key, string _value)
    {
        switch (_key)
        {
            case nameof(grantMarkOnAttack):     this.grantMarkOnAttack = ParseBool(_value); return true;
            case nameof(bonusHpOnMarkedKill):   this.bonusHpOnMarkedKill = ParseInt(_value); return true;
            default: return false;
        }
    }


    public override UniTask OnAfterAttack(AfterAttackCtx _ctx)
    {
        // self는 트리거가 IsAlive까지 걸러 준다. target은 아무도 안 보므로 여기서 막는다.
        if (_ctx.target == null) return UniTask.CompletedTask;

        // 공격 **전** 상태를 먼저 캡처한다 — 아래에서 붙인 표식이 같은 공격의 처치 보상을
        // 자기충족시키면 2단계만으로 4단계가 항상 터진다.
        bool t_wasMarked = _ctx.target.HasKeyword(CardKeyword.Mark);

        // 처치 판정은 ctx.defenderKilled(치사 래치) — 언데드 부활은 이 래치 뒤에 일어나므로
        // 부활한 적도 '처치'로 친다. 반격으로 공격자가 죽은 경우는 트리거의 IsAlive 가드가 막는다.
        if (t_wasMarked && _ctx.defenderKilled && this.bonusHpOnMarkedKill > 0)
        {
            _ctx.self.GrantBonusHp(this.bonusHpOnMarkedKill);
            SynergyTriggers.Fire(_ctx.self, _ctx.synergy, _ctx.ownField);
        }

        // 이미 표식이 붙은 적은 건너뛴다 — 비트는 어차피 멱등이라 규칙은 안 변하지만,
        // 그냥 두면 같은 적을 때릴 때마다 배너·배지가 다시 튄다(발동하지 않은 발동 표시).
        if (this.grantMarkOnAttack && !t_wasMarked && _ctx.target.IsAlive)
        {
            _ctx.target.runtimeKeywords |= CardKeyword.Mark;
            SynergyTriggers.Fire(_ctx.self, _ctx.synergy, _ctx.ownField);
        }

        return UniTask.CompletedTask;
    }
}
