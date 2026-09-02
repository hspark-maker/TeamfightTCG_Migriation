/// <summary>안내할 축 하나를 그것을 보여줄 대본으로 바꾼다. 대본이 갈리는 자리는 여기 둘뿐이다.</summary>
public static class UnlockDemoScriptTable
{
    /// <summary>키워드 대본. 표에 없는 키워드는 기본 한 방으로 떨어진다.</summary>
    public static IUnlockDemoScript For(CardKeyword _keyword)
    {
        switch (_keyword)
        {
            case CardKeyword.Taunt:     return new TauntDemoScript(_keyword);
            case CardKeyword.Healer:    return new HealerDemoScript(_keyword);
            case CardKeyword.Execution: return new ExecutionDemoScript(_keyword);
            case CardKeyword.Cunning:   return new CunningDemoScript(_keyword);
            case CardKeyword.Ranged:    return new NoRiposteDemoScript(_keyword);
            case CardKeyword.Mark:      return new NoRiposteDemoScript(_keyword);
            case CardKeyword.Peerless:  return new SwingDemoScript(_keyword, _splashesNeighbor: true);
            default:                    return new SwingDemoScript(_keyword);
        }
    }

    /// <summary>시너지 대본. 대본이 없는 시너지는 기본 안무로 떨어진다.</summary>
    // 키는 SynergyId 하나뿐이다 — 효과 클래스나 연출 에셋 타입으로 가르면 덩치와 비늘이 붙어 버린다(보여줄 순간이 반대인데도).
    public static IUnlockDemoScript For(SynergyData _synergy)
    {
        // null이면 배역 판정이 false를 돌려줘 무대가 서지 않는다.
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
