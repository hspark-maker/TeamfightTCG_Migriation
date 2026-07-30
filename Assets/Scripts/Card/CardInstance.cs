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
    public int synergyDmgReduction;   // 비늘: 받는 피해 상시 -N. 정적(drain 안 됨) → ClearSynergy 리셋 대상.

    // 라이프사이클 시너지 파생 상태(stateful): 와이어 전송 금지, ClearSynergy 리셋 제외.
    // 양 클라가 동일 스폰/사망/턴종료 경로에서 순수 산술로 동일하게 파생 → 동기화 불필요.
    public int  flowBonus;     // 흐름: AttackDamage에 가산되는 흐름 스택(스택 1당 공격력 +1). 등장마다 성장(무제한).
    public int  legacyStack;   // 유산: 내 턴 종료마다 +1, 사망 시 아군 회복량.
    // 성벽: 필드의 라이브 성벽 아군 수. EffectiveDamage에 synergyDmgReduction과 함께 가산(공격 직격만).
    // BoardChanged 타이밍마다 RampartSynergyEffect가 재동기 — 여기 직접 쓰지 말 것.
    public int  rampartReduction;
    public bool reviveUsed;    // 언데드: 게임당 1회 부활 소진 플래그.

    // 덱 복귀 시 체력 보존용 (-1 = 미저장)
    public int savedHp      = -1;
    public int savedBonusHp = -1;

    // 스폰 직후 TurnBegan 1회 스킵용 (피즈·그웬 무적 즉시 소멸 방지)
    public bool justSpawned;

    // 런타임 진화 단계. 0=미진화, 1~CardData.MaxEvolutionStage.
    // 등급/진화 획득 시스템 미구현 — 지금은 CardData의 임시 기본값(defaultEvolutionStage)에서 주입,
    // 세이브 연동 시 이 필드에 주입하는 지점만 바뀐다(필드 위치·소비측은 그대로).
    public int evolutionStage;
    // 등장 컷씬 1회성 래치. 스왑으로 대기열에 갔다가 다시 필드로 돌아오는 등 같은 인스턴스가
    // 여러 번 "등장"할 수 있어, 매 등장마다 컷씬이 다시 뜨는 것을 막는다.
    // 세팅/조회는 CardCinematicRules에서만 한다(호출부에 판정이 흩어지지 않게).
    public bool cinematicShown;

    public bool IsAlive => this.hp > 0;
    public bool HasKeyword(CardKeyword _kw) => (this.data.keywords | this.runtimeKeywords | this.synergyKeywords).HasFlag(_kw);

    // ── 전투 규칙 (단일 진실원: 공격 해결부·프리뷰 공용) ──
    /// <summary>이 카드가 가하는 기본 공격력. 도발이면 현재 체력의 절반(최소 1).</summary>
    public int AttackDamage() =>
        (HasKeyword(CardKeyword.Taunt) ? UnityEngine.Mathf.Max(1, UnityEngine.Mathf.FloorToInt(this.hp * 0.5f)) : this.hp)
        + this.synergyAtk + this.flowBonus;   // 가산(synergyAtk/흐름 스택)은 도발 반감 뒤에 더함(보너스는 반감 안 받음)

    /// <summary>무쌍 광역 피해량. AttackDamage()와 별개 규칙 — 도발/시너지 보정 없는 순수 hp 기준(v1 시맨틱 보존).</summary>
    public int SplashDamage() => UnityEngine.Mathf.FloorToInt(this.hp * 0.5f);

    /// <summary>받는 피해 단일 폴딩 지점. 공격 직격(_isAttackHit=true)일 때만 비늘 감소(-synergyDmgReduction) 적용, 하한 0.
    /// 반격/가시/기타 패시브 피해는 감소 없음(소스를 호출부가 정적 결정 → 양클라 대칭).
    /// Clamp/사망판정/프리뷰/실적용이 모두 이 식을 통과해야 프리뷰↔실제·양클라 일치.</summary>
    int EffectiveDamage(int _raw, bool _isAttackHit) =>
        UnityEngine.Mathf.Max(0, _raw - (_isAttackHit ? this.synergyDmgReduction + this.rampartReduction : 0));

    /// <summary>실제 적용 데미지(현재 체력+보너스로 상한). _isAttackHit=공격 직격 소스(비늘 감소 대상, 기본 true).
    /// 반격 맥락 호출부는 false 전달 → 실제 TakeDamage(false)와 프리뷰/계산 일치.</summary>
    public int ClampDamage(int _raw, bool _isAttackHit = true) =>
        UnityEngine.Mathf.Min(EffectiveDamage(_raw, _isAttackHit), this.hp + this.bonusHp);

    /// <summary>_raw 데미지로 이 카드가 죽는가(무적이면 소멸만, 죽지 않음). _isAttackHit=공격 직격 소스(기본 true).
    /// 반격 맥락 호출부는 false 전달 → 실제 반격 TakeDamage(false)와 사망 프리뷰 일치.</summary>
    public bool WouldDieFrom(int _raw, bool _isAttackHit = true) =>
        !HasKeyword(CardKeyword.Invincible) && EffectiveDamage(_raw, _isAttackHit) >= this.hp + this.bonusHp;

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
        // 진화 단계 주입은 이 한 지점뿐(모든 생성 경로가 이 ctor를 통과) — 세이브가 들어와도 여기만 교체한다.
        this.evolutionStage = _data.defaultEvolutionStage;
    }

    // ── 시너지 적용 (SynergyApplier가 호출하는 계약: 덱 확정 시 1회, 가산/합집합) ──
    // _bonusHp(덩치): stateful(데미지로 소진)이라 ClearSynergy가 리셋하지 않음 → ApplyAll 이중호출 금지(1회 전제 유지).
    public void ApplySynergy(int _atk, int _bonusHp, CardKeyword _kw, int _dmgReduction)
    {
        this.synergyAtk          += _atk;
        this.bonusHp             += _bonusHp;
        this.synergyKeywords     |= _kw;
        this.synergyDmgReduction += _dmgReduction;
    }
    // 정적 스탯(synergyAtk/keywords/dmgReduction)만 리셋 → ApplyAll 멱등. bonusHp(덩치)는 stateful이라 제외.
    public void ClearSynergy() { this.synergyAtk = 0; this.synergyKeywords = CardKeyword.None; this.synergyDmgReduction = 0; }

    /// <summary>_damage 적용 후 (hp, bonusHp)를 부작용 없이 계산. TakeDamage와 동일 규칙(프리뷰 공용).
    /// _isAttackHit=공격 직격 소스(비늘 감소 대상). 반격/가시 등은 false.</summary>
    public (int hp, int bonusHp) PreviewAfterDamage(int _damage, bool _isAttackHit)
    {
        int t_eff        = EffectiveDamage(_damage, _isAttackHit);
        int t_bonusDrain = UnityEngine.Mathf.Min(this.bonusHp, t_eff);
        int t_bonusAfter = this.bonusHp - t_bonusDrain;
        int t_hpAfter    = UnityEngine.Mathf.Max(0, this.hp - (t_eff - t_bonusDrain));
        return (t_hpAfter, t_bonusAfter);
    }

    /// <summary>체력 회복(단일 진실원). hp만 회복하며 maxHp 상한(보너스HP는 회복 대상 아님).
    /// 반환값 = **실제 회복량**(상한에 걸리면 0). _showEffect=false면 연출을 생략한다 —
    /// 힐러 투사체처럼 회복 표기를 **도착 시점으로 미루는** 호출부 전용(상태 변경 시점은 그대로).</summary>
    public int Heal(int _amount, bool _showEffect = true)
    {
        if (_amount <= 0) return 0;
        int t_before = this.hp;
        this.hp = UnityEngine.Mathf.Min(this.hp + _amount, this.data.maxHp);
        int t_healed = this.hp - t_before;
        // 실제 회복량으로 연출 1회(힐러/돌보미/유산/청소부 모두 이 경로). 순수 연출 — RNG/게임상태 무관.
        if (_showEffect && t_healed > 0) CardView.GetView(this)?.PlayHealEffect(t_healed);
        return t_healed;
    }

    /// <summary>돌보미: bonusHp 부여(양수만). stateful(데미지로 소진) → ClearSynergy 리셋 제외.</summary>
    public void GrantBonusHp(int _amount)
    {
        if (_amount > 0) this.bonusHp += _amount;
    }

    /// <summary>언데드: 파괴 순간 체력 50%(최소 1)로 게임당 1회 부활. 성공 시 true(제자리 hp 복구).
    /// 무적 등과 무관한 순수 산술 — RemoveDead가 이 결과로 RemoveCard 게이팅.</summary>
    public bool ReviveAtHalf()
    {
        if (this.reviveUsed || IsAlive) return false;
        this.reviveUsed = true;
        this.hp = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.FloorToInt(this.data.maxHp * 0.5f));
        CardView.GetView(this)?.PlayHealEffect(this.hp);   // 언데드 부활도 회복 연출(복구된 hp만큼). 순수 연출.
        return true;
    }

    /// <summary>피해 적용. _isAttackHit=공격 직격(defender 직격/스플래시/무리 선피해)일 때만 비늘 감소 대상.
    /// 반격/가시/기타 패시브 피해는 기본값 false로 호출 → 감소 없음. 소스는 호출부가 정적 결정(양클라 대칭).</summary>
    public void TakeDamage(int _damage, bool _isAttackHit = false)
    {
        if (HasKeyword(CardKeyword.Invincible))
        {
            this.runtimeKeywords &= ~CardKeyword.Invincible;
            return;
        }
        (this.hp, this.bonusHp) = PreviewAfterDamage(_damage, _isAttackHit);
    }
}
