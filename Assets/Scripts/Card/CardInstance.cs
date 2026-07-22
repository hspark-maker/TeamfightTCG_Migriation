using Cysharp.Threading.Tasks;

public class CardInstance
{
    public CardData data;
    public int hp;
    public int bonusHp;
    public int slotIndex;   // -1 = waiting queue
    public bool isRevealed;
    public bool wasEverRevealed;
    public int ownerIndex;   // 싱글: 0=player, 1=enemy / 멀티: TurnState.LocalOwnerIndex가 아군, 나머지 적
    public CardKeyword runtimeKeywords;
    public int attackCount;

    // 시너지: 덱 확정 시 1회 적용, 전투 중 재계산 없음.
    // runtimeKeywords와 분리(그웬·피즈 무적 토글이 시너지 키워드를 clobber하지 않도록).
    public int synergyAtk;
    public CardKeyword synergyKeywords;

    // 덱 복귀 시 체력 보존용 (-1 = 미저장)
    public int savedHp      = -1;
    public int savedBonusHp = -1;

    // 스폰 직후 OnTurnStart 1회 스킵용 (피즈·그웬 무적 즉시 소멸 방지)
    public bool justSpawned;

    public bool IsAlive => this.hp > 0;
    public bool HasKeyword(CardKeyword _kw) => (this.data.keywords | this.runtimeKeywords | this.synergyKeywords).HasFlag(_kw);

    // ── 전투 규칙 (단일 진실원: 공격 해결부·프리뷰 공용) ──
    /// <summary>이 카드가 가하는 기본 공격력. 도발이면 현재 체력의 절반(최소 1).</summary>
    public int AttackDamage() =>
        (HasKeyword(CardKeyword.Taunt) ? UnityEngine.Mathf.Max(1, UnityEngine.Mathf.FloorToInt(this.hp * 0.5f)) : this.hp)
        + this.synergyAtk;   // 시너지 가산은 도발 반감 뒤에 더함(보너스는 반감 안 받음)

    /// <summary>실제 적용 데미지(현재 체력+보너스로 상한).</summary>
    public int ClampDamage(int _raw) => UnityEngine.Mathf.Min(_raw, this.hp + this.bonusHp);

    /// <summary>_raw 데미지로 이 카드가 죽는가(무적이면 소멸만, 죽지 않음).</summary>
    public bool WouldDieFrom(int _raw) =>
        !HasKeyword(CardKeyword.Invincible) && _raw >= this.hp + this.bonusHp;

    /// <summary>이 카드가 _defender를 공격할 때 반격을 받는가. 원거리면 무반격, 대상이 표식이면 무반격.</summary>
    public bool TakesCounterFrom(CardInstance _defender) =>
        !HasKeyword(CardKeyword.Ranged) && !_defender.HasKeyword(CardKeyword.Mark);

    public CardInstance(CardData _data, int _ownerIndex)
    {
        this.data    = _data;
        this.hp      = _data.maxHp;
        this.bonusHp = _data.bonusHp;
        this.slotIndex   = -1;
        this.isRevealed  = false;
        this.ownerIndex  = _ownerIndex;
    }

    // ── 시너지 적용 (SynergyApplier가 호출하는 계약: 덱 확정 시 1회, 가산/합집합) ──
    public void ApplySynergy(int _atk, CardKeyword _kw) { this.synergyAtk += _atk; this.synergyKeywords |= _kw; }
    public void ClearSynergy() { this.synergyAtk = 0; this.synergyKeywords = CardKeyword.None; }

    /// <summary>_damage 적용 후 (hp, bonusHp)를 부작용 없이 계산. TakeDamage와 동일 규칙(프리뷰 공용).</summary>
    public (int hp, int bonusHp) PreviewAfterDamage(int _damage)
    {
        int t_bonusDrain = UnityEngine.Mathf.Min(this.bonusHp, _damage);
        int t_bonusAfter = this.bonusHp - t_bonusDrain;
        int t_hpAfter    = UnityEngine.Mathf.Max(0, this.hp - (_damage - t_bonusDrain));
        return (t_hpAfter, t_bonusAfter);
    }

    public void TakeDamage(int _damage)
    {
        if (HasKeyword(CardKeyword.Invincible))
        {
            this.runtimeKeywords &= ~CardKeyword.Invincible;
            return;
        }
        (this.hp, this.bonusHp) = PreviewAfterDamage(_damage);
        if (_damage > 0)
            this.data.passive?.OnHit(this, _damage).Forget();
    }
}
