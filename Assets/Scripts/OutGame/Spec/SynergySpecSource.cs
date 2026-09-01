using System;
using System.Collections.Generic;

/// <summary>시너지 **규칙**(티어 조건·효과·수치)과 **문구**(이름·설명문)를 스펙시트에서 읽어 <see cref="SynergyData"/>에 꽂는다.
///
/// 규칙·문구는 시트가, 표현(색·아이콘·VFX)은 SO가 갖는다. 그래서 이 클래스는 SO의 표현 필드를 건드리지 않고
/// <see cref="SynergyData.tiers"/>·<see cref="SynergyData.displayName"/>·<see cref="SynergyData.effectDescription"/>만
/// 채운다 — 셋 다 직렬화하지 않으므로 에셋에 남는 값이 없다.
///
/// 표 3개(SynergyDef · SynergyTierDef · SynergyEffectDef)를
/// synergyId → tierIndex → effectOrder 로 조인한다. 파라미터는 별도 표가 아니라
/// SynergyEffectDef.parameters 한 칸(<c>키=값;키=값</c>)이다. 어긋난 참조·모르는 타입·모르는 파라미터 키는
/// **그 자리에서 던진다**. 조용히 넘어가면 시너지가 무증상으로 사라져 밸런스만 이상해진다.</summary>
public static class SynergySpecSource
{
    /// <summary>레지스트리에 등록된 시너지 전부에 시트 규칙을 적용한다. 카탈로그 초기화에서 1회.</summary>
    public static void Apply(SynergyRegistry _registry)
    {
        if (_registry == null) throw new InvalidOperationException("[SynergySpec] SynergyRegistry가 없다.");

        SpecDataManager t_manager = SpecSource.Manager;
        if (t_manager == null)
            throw new InvalidOperationException("[SynergySpec] SpecData를 읽지 못해 시너지 규칙을 만들 수 없다.");

        Dictionary<string, List<SynergyTier>> t_byId = BuildTiers(t_manager);
        Dictionary<string, SynergyDef> t_defs = BuildDefs(t_manager);

        foreach (SynergyData t_synergy in _registry.Entries)
        {
            if (t_synergy == null) continue;
            string t_id = t_synergy.SynergyId;
            if (!t_byId.TryGetValue(t_id, out List<SynergyTier> t_tiers))
                throw new InvalidOperationException(
                    $"[SynergySpec] SynergyTierDef 표에 '{t_id}' 티어가 없다. 시트와 SynergyRegistry가 어긋났다.");
            if (!t_defs.TryGetValue(t_id, out SynergyDef t_def))
                throw new InvalidOperationException(
                    $"[SynergySpec] SynergyDef 표에 '{t_id}' 행이 없다. 시트와 SynergyRegistry가 어긋났다.");

            t_synergy.displayName       = t_def.displayName;
            t_synergy.effectDescription = t_def.effectDescription;
            t_synergy.tiers             = t_tiers.ToArray();
        }
    }

    /// <summary>이름·설명문 행을 synergyId로 색인한다. 중복 id는 어느 쪽이 이겼는지 알 수 없어 던진다.</summary>
    static Dictionary<string, SynergyDef> BuildDefs(SpecDataManager _manager)
    {
        IReadOnlyList<SynergyDef> t_rows = Rows(_manager.SynergyDef?.All, "SynergyDef");

        var t_result = new Dictionary<string, SynergyDef>(StringComparer.Ordinal);
        foreach (SynergyDef t_row in t_rows)
        {
            if (t_result.ContainsKey(t_row.synergyId))
                throw new InvalidOperationException($"[SynergySpec] SynergyDef 행 중복: '{t_row.synergyId}'");
            t_result.Add(t_row.synergyId, t_row);
        }
        return t_result;
    }

    static Dictionary<string, List<SynergyTier>> BuildTiers(SpecDataManager _manager)
    {
        IReadOnlyList<SynergyTierDef>   t_tierRows   = Rows(_manager.SynergyTierDef?.All, "SynergyTierDef");
        IReadOnlyList<SynergyEffectDef> t_effectRows = Rows(_manager.SynergyEffectDef?.All, "SynergyEffectDef");

        // 효과 인스턴스를 만들고 같은 행의 parameters 칸을 그 자리에서 꽂는다.
        // 키는 (synergyId, tierIndex, effectOrder).
        var t_effects = new Dictionary<(string, int, int), SynergyEffect>();
        foreach (SynergyEffectDef t_row in t_effectRows)
        {
            SynergyEffect t_effect = SynergyEffectFactory.Create(t_row.effectType);
            if (t_effect == null)
                throw new InvalidOperationException(
                    $"[SynergySpec] 모르는 효과 타입 '{t_row.effectType}' (id={t_row.id}). " +
                    $"지원: {string.Join("/", SynergyEffectFactory.SupportedTypes)}");
            t_effect.name = $"{t_row.synergyId}_T{t_row.tierIndex}_{t_row.effectType}";
            var t_key = (t_row.synergyId, t_row.tierIndex, t_row.effectOrder);
            if (t_effects.ContainsKey(t_key))
                throw new InvalidOperationException($"[SynergySpec] 효과 중복: {t_key}");

            ApplyParams(t_effect, t_row);
            t_effects.Add(t_key, t_effect);
        }

        // 티어 구성. Resolver가 동률일 때 뒤쪽 인덱스를 고르므로 tierIndex 오름차순 정렬이 결과를 좌우한다.
        var t_ordered = new List<SynergyTierDef>(t_tierRows);
        t_ordered.Sort((a, b) =>
        {
            int t_byName = string.CompareOrdinal(a.synergyId, b.synergyId);
            return t_byName != 0 ? t_byName : a.tierIndex.CompareTo(b.tierIndex);
        });

        var t_result = new Dictionary<string, List<SynergyTier>>(StringComparer.Ordinal);
        foreach (SynergyTierDef t_row in t_ordered)
        {
            if (!t_result.TryGetValue(t_row.synergyId, out List<SynergyTier> t_list))
            {
                t_list = new List<SynergyTier>();
                t_result.Add(t_row.synergyId, t_list);
            }

            var t_tierEffects = new List<SynergyEffect>();
            for (int t_order = 0; ; t_order++)
            {
                if (!t_effects.TryGetValue((t_row.synergyId, t_row.tierIndex, t_order), out SynergyEffect t_effect)) break;
                t_tierEffects.Add(t_effect);
            }

            t_list.Add(new SynergyTier
            {
                requiredCount = t_row.requiredCount,
                label         = t_row.label,
                effects       = t_tierEffects.ToArray(),
            });
        }
        return t_result;
    }

    /// <summary>한 칸에 담긴 <c>키=값;키=값</c>을 효과에 꽂는다. 빈 칸이면 클래스 기본값 그대로.
    ///
    /// 파라미터를 별도 표로 두지 않는 이유: 조인키 3개를 손으로 맞추는 자리가 하나 줄고,
    /// "이 효과가 어떤 값으로 도는가"를 행 하나에서 다 읽을 수 있다. 대신 셀 안 문법이 생겼으므로
    /// **어긋난 토큰은 전부 그 자리에서 던진다** — 조용히 넘기면 기본값으로 돌아 밸런스만 이상해진다.</summary>
    static void ApplyParams(SynergyEffect _effect, SynergyEffectDef _row)
    {
        if (string.IsNullOrWhiteSpace(_row.parameters)) return;

        foreach (string t_token in _row.parameters.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string t_pair = t_token.Trim();
            if (t_pair.Length == 0) continue;

            int t_eq = t_pair.IndexOf('=');
            if (t_eq <= 0 || t_eq == t_pair.Length - 1)
                throw new InvalidOperationException(
                    $"[SynergySpec] 파라미터 문법 오류 '{t_pair}' (id={_row.id}). 형식은 키=값, 여러 개는 ;로 구분한다.");

            string t_key   = t_pair.Substring(0, t_eq).Trim();
            string t_value = t_pair.Substring(t_eq + 1).Trim();
            try
            {
                if (!_effect.TrySetParam(t_key, t_value))
                    throw new InvalidOperationException(
                        $"[SynergySpec] '{_effect.GetType().Name}'이 모르는 파라미터 키 '{t_key}' (id={_row.id})");
            }
            catch (FormatException t_exception)
            {
                throw new InvalidOperationException(
                    $"[SynergySpec] 파라미터 값 오류 {_row.synergyId} T{_row.tierIndex}.{t_key}: {t_exception.Message}");
            }
        }
    }

    static IReadOnlyList<T> Rows<T>(IReadOnlyList<T> _rows, string _table)
    {
        if (_rows == null || _rows.Count == 0)
            throw new InvalidOperationException($"[SynergySpec] {_table} 표가 비었다.");
        return _rows;
    }
}
