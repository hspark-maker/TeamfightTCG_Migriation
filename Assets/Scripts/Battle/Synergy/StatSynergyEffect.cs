
// 배선 데모용 선언형 효과: 생명력 가산 + 키워드 부여 + 피해 감소.
// "효과=데이터로 추가"의 증명 에셋. 규칙은 CardInstance.ApplySynergy에 위임.
public class StatSynergyEffect : SynergyEffect
{
    private int         bonusHp;   // 덩치: 생애 1회 bonusHp 가산 (ApplyDeckSynergy 1회 경로 전용, stateful)
    private CardKeyword grantedKeywords;
    private int         dmgReduction;   // 비늘: 받는 피해 상시 -N (정적, 멱등)

    public override bool TrySetParam(string _key, string _value)
    {
        switch (_key)
        {
            case nameof(bonusHp):         this.bonusHp = ParseInt(_value); return true;
            case nameof(grantedKeywords): this.grantedKeywords = ParseKeywords(_value); return true;
            case nameof(dmgReduction):    this.dmgReduction = ParseInt(_value); return true;
            default: return false;
        }
    }

    public override bool TryGetParam(string _key, out int _value)
    {
        switch (_key)
        {
            case nameof(bonusHp):      _value = this.bonusHp;      return true;
            case nameof(dmgReduction): _value = this.dmgReduction; return true;
            default:                   _value = 0;                 return false;
        }
    }

    public override void OnDeckResolved(DeckCtx _ctx)
    {
        if (_ctx.card == null) return;
        _ctx.card.ApplySynergy(bonusHp, grantedKeywords, dmgReduction);
    }

    // 정적 스탯이라 "발동 순간"이 없어 배너를 띄울 지점이 없었다(덩치·비늘이 한 판 내내 안 보이던 이유).
    // 스탯 종류마다 값이 일하는 시점이 다르므로 표시 지점도 나눈다:
    //   덩치(bonusHp)      → 필드에 배치될 때 1회. 추가 생명력은 배치 시점에 이미 붙어 있는 값이다.
    //   비늘(dmgReduction) → 피격 시. 받는 피해를 깎는 순간이 체감 지점이다.
    // 전부 상태변이 없는 순수 표시다.

    // [Placed] 오프닝 배치. 디스패처가 self 소속만 발화하므로 소속 재판정 불필요.
    public override void OnPlaced(SpawnCtx _ctx)
    {
        FireIfBonusHp(_ctx.self, _ctx.synergy, _ctx.field);
    }

    // [Entered] 런타임 등장. **이 디스패처는 BelongsTo 필터를 안 걸므로** 소속을 직접 판정해야 한다.
    public override void OnEntered(SpawnCtx _ctx)
    {
        if (_ctx.self == null || !SynergyApplier.BelongsTo(_ctx.self, _ctx.synergy)) return;
        FireIfBonusHp(_ctx.self, _ctx.synergy, _ctx.field);
    }

    void FireIfBonusHp(CardInstance _self, SynergyRuntime _synergy, BattleField _field)
    {
        if (_self == null || this.bonusHp <= 0) return;
        SynergyTriggers.Fire(_self, _synergy, _field);
    }

    // [Attacked] 피격. 디스패처가 self 소속만 발화하므로 소속 재판정 불필요.
    public override void OnAttacked(AttackedCtx _ctx)
    {
        if (_ctx.self == null || this.dmgReduction <= 0) return;   // 피해 감소가 없으면 피격과 무관한 스탯
        SynergyTriggers.Fire(_ctx.self, _ctx.synergy, _ctx.ownField);
    }
}
