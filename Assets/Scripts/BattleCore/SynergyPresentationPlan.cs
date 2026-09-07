/// <summary>규칙이 캡처하고 Unity 표현 계층이 읽기 전용으로 소비하는 대본의 공통 표식.</summary>
public interface ISynergyPresentationPlan { }

public enum SynergyPresentationTiming
{
    Immediate,
    OnDeath,
    OnKill,
}

public static class SynergyPresentationStream
{
    public static event System.Action<ISynergyPresentationPlan> Published;
    public static void Emit(ISynergyPresentationPlan _plan)
    {
        if (_plan != null) Published?.Invoke(_plan);
    }
}

public interface IBeforeAttackPlanSource
{
    // ★ 규칙 적용 전에 호출된다. 피해 전 체력처럼 표시용 선행 스냅샷만 캡처한다.
    ISynergyPresentationPlan CaptureBeforeAttackPlan(BeforeAttackCtx _ctx);
}

public interface IAfterAttackPlanSource
{
    // ★ 규칙 적용 전에 호출된다. 효과 인스턴스에는 캡처 상태를 저장하지 않는다.
    ISynergyPresentationPlan CaptureAfterAttackPlan(AfterAttackCtx _ctx);
}

public sealed class BrandAttackPlan : ISynergyPresentationPlan
{
    public CardInstance self;
    public SynergyRuntime synergy;
    public BattleFieldState ownField;
    public System.Collections.Generic.List<CardInstance> brandCards;
    public CardInstance defender;
    public int totalDamage;
    public int appliedDamage;
    public int hpBefore;
    public int bonusHpBefore;
}

public sealed class PredatorDrainPlan : ISynergyPresentationPlan
{
    public CardInstance self;
    public CardInstance target;
    public SynergyRuntime synergy;
    public BattleFieldState ownField;
}

public sealed class SynergyFirePlan : ISynergyPresentationPlan
{
    public CardInstance self;
    public SynergyRuntime synergy;
    public BattleFieldState field;
    public SynergyPresentationTiming timing;
}

public sealed class FlowAttackPresentationPlan : ISynergyPresentationPlan
{
    public CardInstance self;
    public SynergyRuntime synergy;
    public BattleFieldState field;
}

public readonly struct SynergyHealTarget
{
    public readonly CardInstance card;
    public readonly int amount;

    public SynergyHealTarget(CardInstance _card, int _amount)
    {
        card = _card;
        amount = _amount;
    }
}

public sealed class CaretakerPresentationPlan : ISynergyPresentationPlan
{
    public CardInstance self;
    public SynergyRuntime synergy;
    public BattleFieldState field;
    public System.Collections.Generic.List<SynergyHealTarget> targets;
}

public sealed class LegacyTurnPresentationPlan : ISynergyPresentationPlan
{
    public CardInstance self;
    public SynergyRuntime synergy;
    public BattleFieldState field;
}

public sealed class LegacyDeathPresentationPlan : ISynergyPresentationPlan
{
    public CardInstance self;
    public SynergyRuntime synergy;
    public BattleFieldState field;
    public System.Collections.Generic.List<CardInstance> healed;
    public int amount;
}

public sealed class TraceMarkPresentationPlan : ISynergyPresentationPlan
{
    public CardInstance self;
    public CardInstance target;
    public SynergyRuntime synergy;
    public BattleFieldState field;
}
