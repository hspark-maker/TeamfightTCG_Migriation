#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 서버 없이 돌리는 개발용 추첨. 파일 전체가 #if 안이라 릴리스 빌드에는 타입 자체가 없다 —
// 훗날 누가 아무 데서나 new 해도 런타임 침묵이 아니라 컴파일 에러로 터진다.
public sealed class LocalRouletteSpinSource : IRouletteSpinSource
{
    readonly RouletteConfig m_config;

    public LocalRouletteSpinSource(RouletteConfig _config)
    {
        m_config = _config;
    }

    // 인위적 지연을 넣지 않는다 — 2단계에서 진짜 왕복 지연이 붙었을 때 연출이 견디는지가 그때 드러나야 한다.
    public UniTask<RouletteSpinOutcome> SpinAsync(CancellationToken _ct)
    {
        if (_ct.IsCancellationRequested) return UniTask.FromResult(RouletteSpinOutcome.CreateFailure(ERouletteSpinResult.Canceled));
        if (m_config == null) return UniTask.FromResult(RouletteSpinOutcome.CreateFailure(ERouletteSpinResult.NotConfigured));

        int t_totalWeight = 0;
        int t_slotCount = m_config.SlotCount;
        for (int t_i = 0; t_i < t_slotCount; t_i++)
        {
            if (!m_config.TryGetSlot(t_i, out RouletteSlotDef t_slot) || !t_slot.IsDrawable) continue;

            t_totalWeight += t_slot.EffectiveWeight;
        }

        if (t_totalWeight <= 0) return UniTask.FromResult(RouletteSpinOutcome.CreateFailure(ERouletteSpinResult.EmptyPool));

        // 아웃게임 추첨은 UnityEngine.Random을 쓴다 — 전투의 결정론 축(MatchRandom)과 분리한다.
        int t_roll = Random.Range(0, t_totalWeight);
        for (int t_i = 0; t_i < t_slotCount; t_i++)
        {
            if (!m_config.TryGetSlot(t_i, out RouletteSlotDef t_slot) || !t_slot.IsDrawable) continue;

            t_roll -= t_slot.EffectiveWeight;
            if (t_roll >= 0) continue;

            return UniTask.FromResult(RouletteSpinOutcome.CreateSuccess(t_i, t_slot.currency, t_slot.amount, t_slot.isJackpot));
        }

        return UniTask.FromResult(RouletteSpinOutcome.CreateFailure(ERouletteSpinResult.EmptyPool));
    }
}
#endif
