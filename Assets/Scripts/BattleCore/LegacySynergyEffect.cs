using System.Collections.Generic;

// 유산 시너지(덱 2장↑ 활성). 턴시작/사망 트리거형 — 정적 스탯 없음.
// 내 턴이 시작될 때마다 legacyStack+1. 파괴 시 legacyStack만큼 살아있는 아군(자신 제외) 전원 회복.
// 스택 적립/회복 규칙은 CardInstance(legacyStack/Heal)에 위임(단일 진실원). RNG 미소비, 순수 산술.
// 디스패처가 self 소속에 대해서만 발화하므로 여기서 소속 재판정 불필요.
//
// 연출(LegacyCrownVfx)의 발화점도 여기다 — 턴 시작(스택 수만큼 왕관이 떴다 사라짐)과
// 파괴(회복받을 아군에게 궤적이 날아감)는 밖에서 구분할 수 없다. 둘 다 [Triggered] 하나로 나가기 때문이다.
// "언제 무엇을 띄우고 언제 날려 보내는가"를 아는 곳은 이 효과뿐이라 호출도 여기서 한다.
// 연출은 상태/RNG를 건드리지 않으므로 결정론 계약과 무관하다.
public class LegacySynergyEffect : SynergyEffect
{
    int amount = 1;

    public override bool TrySetParam(string _key, string _value)
    {
        if (_key != nameof(amount)) return false;
        this.amount = ParseInt(_value);
        return true;
    }

    public override bool TryGetParam(string _key, out int _value)
    {
        _value = this.amount;
        return _key == nameof(amount);
    }

    // 한 카드가 죽으며 아군을 회복시키면, RemoveDead의 같은 루프에서 hp 0으로 대기 중이던 다른 아군이
    // 되살아나 제거를 면할 수 있다 — 즉 전멸 예측이 틀릴 수 있다.
    public override bool CanAlterLethalOutcome => true;

    // 턴 시작: 스택을 먼저 올리고, 오른 값만큼 왕관을 띄웠다 거둔다(1개 → 2개 → 3개…, 많아질수록 작게).
    //
    // 적립 시점이 턴 **종료**가 아니라 여기인 이유는 둘이다:
    //  ① 첫 턴부터 왕관이 보여야 한다 — 종료에 올리면 첫 턴 시작 시점의 스택이 0이라 아무것도 안 뜬다.
    //  ② 적립과 표시가 같은 순간이면 "이번 턴에 하나 더 쌓였다"가 한 박으로 읽힌다.
    //     종료에 올리면 화면은 이미 다음 카드로 넘어가는 중이라 눈이 안 간다.
    // 총 적립량은 그대로다(자기 차례마다 1). 바뀌는 건 첫 턴에 죽었을 때 0이 아니라 1을 남긴다는 것뿐이다.
    //
    // 동기 완결 계약: 상태변이(stack++)는 반환 전에 끝난다. 연출은 기다리지 않는다 —
    // 스택이 쌓일수록 턴 시작이 그만큼 느려지면 안 된다.
    // ⚠ 무적/교활 복귀 카드는 그 턴 TurnBegan을 통째로 건너뛴다(TurnRunner의 justSpawned 스킵) — 그 턴은 안 쌓인다.
    public override void OnTurnBegan(TurnCtx _ctx)
    {
        if (_ctx.self == null || !_ctx.self.IsAlive) return;

        _ctx.self.legacyStack += this.amount;
        SynergyPresentationStream.Emit(new LegacyTurnPresentationPlan
        {
            self = _ctx.self,
            synergy = _ctx.synergy,
            field = _ctx.field,
        });
    }

    // 사망: 축적한 스택만큼 아군(자신 제외 라이브) 전원 회복. 스택 0이면 no-op.
    public override void OnLethal(DeathCtx _ctx)
    {
        if (_ctx.self == null || _ctx.field == null || _ctx.self.legacyStack <= 0) return;

        // 회복 대상을 모아 두는 이유는 연출뿐이다 — 궤적이 "누구에게" 날아가는지가 이 목록이다.
        // 규칙(회복량·대상 판정)은 아래 루프가 이미 끝낸 뒤라, 목록이 비어도 회복 결과는 바뀌지 않는다.
        int t_amount = _ctx.self.legacyStack;   // 대상마다 같은 값(오버힐이라 잘리지 않는다)
        var t_healed = new List<CardInstance>();
        foreach (var t_card in _ctx.field.GetActiveCards())   // ownField 아군
        {
            if (t_card == null || t_card == _ctx.self || !t_card.IsAlive) continue;
            // 대상은 **아군 전원**이다(유산 소속만이 아니다) — 발동 조건이 "유산 카드가 죽는 것"일 뿐,
            // 유산은 죽으며 남기는 것이라 받는 쪽을 가리지 않는다.
            // 오버힐을 허용한다: 힐러와 같은 규칙으로 최대 체력을 넘겨 채운다(Heal의 _allowOverheal).
            // 안 그러면 체력이 꽉 찬 아군에게는 스택이 아무리 높아도 0이라, 판이 길어질수록 효과가 사라진다.
            //
            // _showEffect:false — **표기는 궤적이 도착할 때** 낸다(힐러 투사체와 같은 규약).
            // 여기는 RemoveDead 안, 즉 공격 연출이 시작되기도 전이라 지금 회복 연출을 내면
            // 뒤따르는 돌진·피격·사망 연출과 뷰 갱신에 그대로 덮여 아무도 못 본다.
            // 수치는 지금 확정되고(결정론) 숫자만 도착까지 유예된다.
            t_card.Heal(t_amount, _showEffect: false, _allowOverheal: true);
            t_healed.Add(t_card);
        }

        if (t_healed.Count > 0)
        {
            // 표시는 [5] 사망 단계로 미룬다. 여기(RemoveDead 안)는 공격 모션이 시작되기도 전이라
            // 그대로 재생하면 궤적이 **죽는 그림보다 먼저** 날아간다 — 6단계 순서
            // (공격 전 → 공격 중 → 피격 → 공격 후 → 사망 → 처치)에서 사망 표시는 다섯 번째다.
            // 접촉 프레임(Drain)이 아니라 사망 배치(DrainDeaths)인 이유도 같다: 이건 이 카드가
            // 쓰러지며 남기는 것이라, 아직 멀쩡히 서 있는 카드에서 나가면 인과가 안 읽힌다.
            // 규칙(회복량·대상)은 위에서 이미 확정됐다 — 여기 담기는 건 순수 표시뿐이다.
            SynergyPresentationStream.Emit(new LegacyDeathPresentationPlan
            {
                self = _ctx.self,
                synergy = _ctx.synergy,
                field = _ctx.field,
                healed = t_healed,
                amount = t_amount,
            });
        }
    }
}
