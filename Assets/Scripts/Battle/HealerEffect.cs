using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class HealerEffect
{
    readonly BattleField field;

    public HealerEffect(BattleField _field)
    {
        this.field = _field;
        TurnEvents.TurnStarted += Handle;
    }

    public void Unsubscribe() => TurnEvents.TurnStarted -= Handle;

    void Handle(BattleField _field)
    {
        if (_field != this.field) return;

        // 필드의 힐러 전부(슬롯 오름차순 = 양 클라 동일 순서). **힐러 하나당 1 회복** —
        // 예전엔 첫 힐러만 골라 총 1만 회복해서 2장을 깔아도 효과가 하나였다.
        var t_healers = new List<CardInstance>();
        foreach (var t_c in this.field.GetActiveCards())
            if (t_c.HasKeyword(CardKeyword.Healer)) t_healers.Add(t_c);
        if (t_healers.Count == 0) return;

        // 힐러별로 순차 적용. 회복 적용은 **여기서 전부 즉시**(상태 = 동기), 표시는 투사체가 닿을 때(연출 = 비동기).
        // 상태 변경을 연출에 묶으면 프레임레이트 차이가 그대로 멀티 divergence가 된다 —
        // 힐러가 둘일 때 두 번째 회복을 연출 대기 뒤로 미루면 딱 그 divergence가 된다(연출만 미룬다).
        // 순회 순서(슬롯 순 힐러 × 슬롯 순 대상)가 고정이라 양 클라 결과가 같다. RNG 미소비.
        var t_bursts = new List<(CardInstance healer, List<(CardView view, CardInstance card, int amount)> healed)>();
        foreach (var t_healer in t_healers)
        {
            var t_healed = new List<(CardView view, CardInstance card, int amount)>();
            foreach (var t_c in this.field.GetActiveCards())
            {
                // 제외는 **자기 자신뿐**이다. 예전엔 힐러 전체를 대상에서 뺐는데, 그러면 힐러를 둘 깔았을 때
                // 서로를 못 치유해 힐러만 회복 없이 말라 죽었다("아군 1 회복"이라는 키워드 설명과도 어긋난다).
                if (t_c == t_healer) continue;
                int t_amount = t_c.Heal(1, _showEffect: false, _allowOverheal: true);   // 표기는 HealVfx가 도착 시점에 재생
                if (t_amount <= 0) continue;                      // 유효한 회복량이 없으면 연출 대상 아님
                t_healed.Add((CardView.GetView(t_c), t_c, t_amount));
            }

            // 아무도 회복되지 않았으면 글로우·투사체 모두 생략 — 빈 연출이 번쩍이지 않게.
            if (t_healed.Count == 0) continue;
            t_bursts.Add((t_healer, t_healed));
        }

        if (t_bursts.Count > 0) PlayBurstsSequential(t_bursts).Forget();
    }

    /// <summary>힐러가 여럿이면 한 명씩 차례로 연출한다 — 동시에 터지면 투사체가 한 덩어리로 겹쳐
    /// 누가 몇 회복시켰는지 읽히지 않는다. **순수 연출**이라 회복 수치는 이미 전부 적용된 뒤다.
    /// 간격은 앞 힐러의 연출 길이(HealVfx.BurstDuration) — 앞이 끝나야 다음이 시작한다.</summary>
    static async UniTaskVoid PlayBurstsSequential(
        List<(CardInstance healer, List<(CardView view, CardInstance card, int amount)> healed)> _bursts)
    {
        for (int i = 0; i < _bursts.Count; i++)
        {
            if (i > 0)
            {
                float t_gap = HealVfx.BurstDuration(_bursts[i - 1].healed.Count);
                await UniTask.Delay((int)(t_gap * 1000));
            }

            // 뷰 조회는 재생 직전에 — 대기 중 슬롯이 비거나 카드가 죽었으면 null이 되고,
            // 그 경우 PlayHealBurst가 표기("+N")만 즉시 처리한다(회복 수치는 잃지 않는다).
            CardView t_src = CardView.GetView(_bursts[i].healer);
            t_src?.PlayKeywordGlow(CardKeyword.Healer).Forget();
            HealVfx.PlayHealBurst(t_src, _bursts[i].healed);
        }
    }
}
