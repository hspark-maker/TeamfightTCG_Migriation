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

/// <summary>
/// 규칙 계층이 아웃게임 카탈로그를 직접 참조하지 않게 하는 필수 주입 경계.
///
/// <para>두 가지 수명이 필요해서 슬롯이 둘이다.</para>
///
/// <list type="bullet">
/// <item><b>전역</b>(<see cref="Install"/>) — Unity 런타임. 초기화가 한 번 심으면 이후 모든 씬·프레임이
/// 같은 규칙을 본다. <b>AsyncLocal 로는 이걸 못 한다</b> — 초기화 스텝이 심은 값은 그 실행 컨텍스트
/// 아래로만 흐르고, 전투 씬의 <c>GameInitializer.Start()</c> 는 빈 컨텍스트에서 시작해 값을 못 본다.</item>
/// <item><b>흐름 한정</b>(<see cref="InstallScoped"/>) — Cloud Run 재생 서비스. 요청마다 specPins 가
/// 달라 규칙 객체가 다르고 요청은 동시에 돈다. 전역 슬롯을 쓰면 서로 덮어써서 다른 판의 규칙으로
/// 재생한다. 이쪽만 <see cref="AsyncLocal{T}"/> 여야 한다.</item>
/// </list>
///
/// <para>조회는 흐름 한정이 전역보다 우선한다. 서버가 흐름 설치를 빠뜨리면 전역이 비어 있어 그대로
/// 던지므로, 조용히 남의 규칙으로 도는 일은 없다.</para>
/// </summary>
public static class SynergyRuleProvider
{
    /// <summary>프로세스 전역. Unity 런타임이 초기화에서 한 번 심는다.</summary>
    static ISynergyRuleProvider s_global;

    /// <summary>호출 흐름 한정. 동시 요청을 받는 서버 전용이다.</summary>
    static readonly AsyncLocal<ISynergyRuleProvider> s_scoped = new AsyncLocal<ISynergyRuleProvider>();

    public static ISynergyRuleProvider Current
        => s_scoped.Value ?? s_global
           ?? throw new InvalidOperationException("[SynergyRuleProvider] Provider가 주입되지 않았습니다.");

    /// <summary>프로세스 전역으로 심는다. 단일 규칙 세대를 오래 쓰는 쪽(Unity·오프라인 도구)이 쓴다.</summary>
    public static void Install(ISynergyRuleProvider _provider)
        => s_global = _provider ?? throw new ArgumentNullException(nameof(_provider));

    /// <summary>지금 호출 흐름에만 심는다. 같은 프로세스가 서로 다른 규칙으로 동시에 도는 쪽이 쓴다.</summary>
    public static void InstallScoped(ISynergyRuleProvider _provider)
        => s_scoped.Value = _provider ?? throw new ArgumentNullException(nameof(_provider));

    public static bool TryGetCurrent(out ISynergyRuleProvider _provider)
    {
        _provider = s_scoped.Value ?? s_global;
        return _provider != null;
    }

    /// <summary>전역·흐름 슬롯을 모두 비운다.</summary>
    public static void Reset()
    {
        s_global = null;
        s_scoped.Value = null;
    }

    /// <summary>흐름 슬롯만 비운다. 요청 종료 시 서버가 쓴다 — 전역을 건드리면 다른 요청이 죽는다.</summary>
    public static void ResetScoped() => s_scoped.Value = null;
}
