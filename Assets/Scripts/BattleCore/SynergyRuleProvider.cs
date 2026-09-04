using System;
using System.Collections.Generic;
using System.Threading;

public interface ISynergyRuleProvider
{
    bool ContainsCard(int _cardId);
    CardSpec SpecOf(int _cardId);
    IReadOnlyList<string> SynergyIdsOf(int _cardId);
    IReadOnlyList<SynergyTier> TiersOf(string _synergyId);
}

/// <summary>규칙 계층이 아웃게임 카탈로그를 직접 참조하지 않게 하는 필수 주입 경계.</summary>
public static class SynergyRuleProvider
{
    static readonly AsyncLocal<ISynergyRuleProvider> s_current = new AsyncLocal<ISynergyRuleProvider>();

    public static ISynergyRuleProvider Current
        => s_current.Value ?? throw new InvalidOperationException("[SynergyRuleProvider] Provider가 주입되지 않았습니다.");

    public static void Install(ISynergyRuleProvider _provider)
        => s_current.Value = _provider ?? throw new ArgumentNullException(nameof(_provider));

    public static bool TryGetCurrent(out ISynergyRuleProvider _provider)
    {
        _provider = s_current.Value;
        return _provider != null;
    }

    public static void Reset() => s_current.Value = null;
}
