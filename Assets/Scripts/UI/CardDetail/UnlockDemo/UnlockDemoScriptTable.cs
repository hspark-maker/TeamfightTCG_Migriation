/// <summary>안내할 축 하나를 그것을 보여줄 대본으로 바꾼다. 대본이 갈리는 자리는 여기 둘뿐이다.</summary>
public static class UnlockDemoScriptTable
{
    /// <summary>키워드 대본. 표에 없는 키워드는 기본 한 방으로 떨어진다 —
    /// 원거리처럼 "안 오는 것"이 본체인 축만 따로 대본을 갖는다.</summary>
    public static IUnlockDemoScript For(CardKeyword _keyword)
    {
        switch (_keyword)
        {
            // 도발만 **공격 방향이 반대**다(적이 이 카드를 치러 온다).
            case CardKeyword.Taunt:     return new TauntDemoScript(_keyword);
            case CardKeyword.Healer:    return new HealerDemoScript(_keyword);
            case CardKeyword.Execution: return new ExecutionDemoScript(_keyword);
            case CardKeyword.Cunning:   return new CunningDemoScript(_keyword);
            case CardKeyword.Ranged:    return new NoRiposteDemoScript(_keyword);
            case CardKeyword.Mark:      return new NoRiposteDemoScript(_keyword);

            // 무쌍은 기본 안무에 윗줄 곁자리를 광역 대상으로 얹은 것이다.
            case CardKeyword.Peerless:  return new SwingDemoScript(_keyword, _splashesNeighbor: true);

            default:                    return new SwingDemoScript(_keyword);
        }
    }

    /// <summary>시너지 대본. 키는 SynergyId 하나뿐이다 — 효과 클래스나 연출 에셋 타입으로 가르면
    /// 덩치와 비늘이 붙어 버린다(둘 다 StatSynergyEffect + 엠블럼만 있는 설정이지만 보여줄 순간이 반대다).</summary>
    public static IUnlockDemoScript For(SynergyData _synergy)
    {
        // null이면 배역 판정에서 걸러진다(TryBuildCast가 false) — 무대는 서지 않고 글자만 남는다.
        if (_synergy == null) return new AnySynergyDemoScript(null);

        switch (_synergy.SynergyId)
        {
            case "Bulk":      return new BulkDemoScript(_synergy);
            case "Scale":     return new ScaleDemoScript(_synergy);
            case "Guardian":  return new GuardianDemoScript(_synergy);
            case "Caretaker": return new CaretakerDemoScript(_synergy);
            case "Flow":      return new FlowDemoScript(_synergy);
            case "Brand":     return new BrandDemoScript(_synergy);
            case "Predator":  return new PredatorDemoScript(_synergy);
            case "Trace":     return new TraceDemoScript(_synergy);
            case "Legacy":    return new LegacyDemoScript(_synergy);
            default:          return new AnySynergyDemoScript(_synergy);
        }
    }
}
