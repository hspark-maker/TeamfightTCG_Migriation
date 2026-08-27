using System.Collections.Generic;
using UnityEngine;

/// <summary>강화 한 레벨의 비용·성공률. 시트 한 줄이 그대로 이 값이 된다.</summary>
public readonly struct EnhanceStepSpec
{
    public readonly ECurrencyType Currency;
    public readonly long Cost;
    public readonly float SuccessRate;

    public EnhanceStepSpec(ECurrencyType _currency, long _cost, float _successRate)
    {
        Currency = _currency;
        Cost = _cost;
        SuccessRate = _successRate;
    }
}

// 강화 스펙시트(CardEnhance) 런타임 조회 창구.
// 시트를 못 읽거나 그 레벨 행이 없으면 조회가 실패로 떨어지고 CardGrowthConfig의 저작값으로 폴백한다.
public static class EnhanceSpec
{
    const int PERMILLE_FULL = 1000;

    static bool s_loaded;
    static readonly Dictionary<int, EnhanceStepSpec> s_steps = new Dictionary<int, EnhanceStepSpec>();

    /// <summary>레벨 _level로 올리는 한 스텝. 상한 판정은 하지 않는다 — 그건 CardGrowthConfig가 소유한다.</summary>
    public static bool TryGetStep(int _level, out EnhanceStepSpec _step)
    {
        EnsureLoaded();
        return s_steps.TryGetValue(_level, out _step);
    }

    // 부트에서 1회. 파싱은 SpecSource가 이미 했으므로 여기서 드는 비용은 레벨 색인뿐이다.
    public static void Init() => EnsureLoaded();

    static void EnsureLoaded()
    {
        if (s_loaded) return;
        s_loaded = true;   // 실패해도 매 조회마다 재파싱하지 않는다(폴백으로 계속 돈다).

        SpecDataManager t_manager = SpecSource.Manager;
        if (t_manager == null) return;   // 못 읽은 경고는 SpecSource가 이미 냈다

        IReadOnlyList<CardEnhance> t_rows = t_manager.CardEnhance?.All;
        if (t_rows == null) return;

        foreach (CardEnhance t_row in t_rows)
        {
            if (t_row == null || t_row.level <= CardGrowth.BaseLevel) continue;

            // 재화 표기가 틀리면 그 줄을 버린다 — 조용히 Gold로 떨어지면 시트 오타가 오과금이 된다.
            if (!CurrencyCode.TryParse(t_row.costCurrency, out ECurrencyType t_currency))
            {
                Debug.LogWarning($"[EnhanceSpec] CardEnhance id {t_row.id}: 알 수 없는 재화 '{t_row.costCurrency}' — 이 줄을 버린다.");
                continue;
            }

            if (s_steps.ContainsKey(t_row.level))
            {
                Debug.LogWarning($"[EnhanceSpec] 레벨 {t_row.level} 행이 둘 이상이다(id {t_row.id}) — 먼저 읽은 행을 남긴다.");
                continue;
            }

            float t_rate = Mathf.Clamp01((float)t_row.successPermille / PERMILLE_FULL);
            s_steps[t_row.level] = new EnhanceStepSpec(t_currency, t_row.cost > 0 ? t_row.cost : 0, t_rate);
        }
    }
}
