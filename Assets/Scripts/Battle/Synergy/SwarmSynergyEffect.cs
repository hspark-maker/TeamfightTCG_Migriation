using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 무리 시너지(덱 4장↑ 활성). 순수 트리거형 — 정적 스탯 없음.
// 공격 개시 직전, 공격자 필드의 라이브 아군 무리 카드 수(공격자 포함)만큼 방어자에게 선피해.
// 값 규칙은 CardInstance.TakeDamage에 전량 위임(인라인 데미지 공식 금지). 결정론: RNG 미소비.
// 무리(Swarm) 효과 — OnBeforeAttack 선피해. 덱 4장↑ 활성.
[CreateAssetMenu(fileName = "SwarmSynergyEffect", menuName = "Card Battle/Synergy Effect/Swarm")]
public class SwarmSynergyEffect : SynergyEffect
{
    public override async UniTask OnBeforeAttack(BeforeAttackCtx _ctx)
    {
        if (_ctx.defender == null || !_ctx.defender.IsAlive || _ctx.ownField == null) return;

        // 무리 카드 수집. 슬롯 오름차순(GetActiveCards)이라 발사 순서도 양 클라 동일하다.
        var t_swarm = new List<CardInstance>();
        foreach (var t_card in _ctx.ownField.GetActiveCards())   // 슬롯 라이브 카드(공격자 포함)
        {
            if (t_card == null || !t_card.IsAlive) continue;
            if (SynergyApplier.BelongsTo(t_card, _ctx.synergy)) t_swarm.Add(t_card);
        }

        int t_count = Mathf.Min(t_swarm.Count, BattleField.SLOT_COUNT);   // 방어적 상한(아군 슬롯 ≤3 → 자연히 만족, 회귀 가드)
        if (t_count <= 0) return;

        // 표시용 스냅샷은 피해 적용 **전**에. 착탄마다 숫자를 나눠 띄우려면 실제 적용량과 시작 체력이 필요하다.
        // 실제 적용량은 ClampDamage가 유일 진실원 — 비늘/성벽 감소와 체력 상한이 여기서 이미 반영된다.
        // (발당 1씩 띄우면 감소가 걸렸을 때 숫자 합이 실제 피해와 어긋난다.)
        int t_applied   = _ctx.defender.ClampDamage(t_count, true);
        int t_hpBefore  = _ctx.defender.hp;
        int t_bonusBefore = _ctx.defender.bonusHp;

        // ── 상태 변경은 여기까지. 아래는 전부 표시(await 가능) ──
        // 훅 계약: 첫 await 전에 상태변이 완결 + MatchRandom 미소비.
        _ctx.defender.TakeDamage(t_count, true);   // 선피해도 공격 직격 취급: 비늘 감소 대상. 규칙 전량 TakeDamage 위임.
        // 선피해 발동 시에만 배너+배지 pop(스팸 방지). ownField를 넘기는 이유: 무리 엠블럼은
        // AllMembers 범위라 "쏘는 무리 전원"에게 떠야 한다 — 범위 해석에 필드가 필요하다.
        bool t_emblem = SynergyTriggers.Fire(_ctx.self, _ctx.synergy, _ctx.ownField);

        // 엠블럼이 다 뜨고 나서 볼리 → 본 공격. 겹쳐 돌리면 "무리가 모였다"는 신호와 발사가 뭉쳐
        // 둘 다 안 읽힌다. 여기 대기는 표시 전용이다 — 상태는 위에서 이미 확정됐고 RNG도 안 쓴다.
        float t_wait = t_emblem
            ? SynergyEmblemVfx.DurationOf(_ctx.synergy, SynergyEmblemTiming.Triggered) : 0f;
        if (t_wait > 0f)
            await UniTask.Delay((int)(t_wait * 1000)).SuppressCancellationThrow();

        // 무리 카드들이 하나씩 투사체를 쏘고, 다 맞은 뒤에 본 공격 연출이 이어진다.
        // 이 await가 곧 "선피해 먼저, 공격 나중"의 표시 순서다 — 호출부(AttackFlow.RunBeforeAttack)가
        // AttackSequence.Play 앞에서 await 하므로 별도 배관이 필요 없다.
        var t_views = new List<CardView>(t_count);
        for (int i = 0; i < t_count; i++)
        {
            CardView t_view = CardView.GetView(t_swarm[i]);
            if (t_view != null) t_views.Add(t_view);
        }
        // 연출 스펙은 그 시너지의 연출 에셋이 소유한다. 타입이 안 맞게 꽂혔으면 null → 볼리만 생략된다
        // (피해는 이미 적용된 뒤라 안전하다). 이 캐스트가 "무리 데이터 ↔ 무리 연출"의 유일한 접점이다.
        await SwarmVfx.PlayVolley(t_views, CardView.GetView(_ctx.defender),
                                  SplitDamage(t_applied, t_views.Count), t_hpBefore, t_bonusBefore,
                                  _ctx.synergy?.vfx as SwarmSynergyVfxConfig);
    }

    /// <summary>실제 적용된 총 피해를 발수만큼 정수로 쪼갠다. 나머지는 <b>앞쪽 발</b>에 얹는다 —
    /// 합이 총량과 정확히 같아야 숫자와 체력 감소가 맞물린다(발당 1 고정으로 두면 감소 적용 시 어긋난다).
    /// 총량이 발수보다 적으면 뒤쪽 발은 0 → 숫자 없이 파티클만 뜬다.</summary>
    static int[] SplitDamage(int _total, int _shots)
    {
        var t_out = new int[Mathf.Max(0, _shots)];
        if (t_out.Length == 0) return t_out;

        int t_base = _total / t_out.Length;
        int t_rem  = _total % t_out.Length;
        for (int i = 0; i < t_out.Length; i++)
            t_out[i] = t_base + (i < t_rem ? 1 : 0);
        return t_out;
    }
}
