public class CardInstance
{
    public CardData data;
    public int hp;
    // 이 인스턴스의 최대 체력(= data.maxHp + 영구 강화분). 회복 상한·부활 기준은 전부 이 값이다 —
    // data.maxHp는 공유 에셋이라 강화값을 담을 수 없다. 강화분은 bonusHp(시너지 임시 채널)에 섞지 않는다.
    public int maxHp;
    public int bonusHp;
    public int slotIndex;   // -1 = waiting queue
    public bool isRevealed;
    public bool wasEverRevealed;
    public int ownerIndex;   // 싱글: 0=player, 1=enemy / 멀티: TurnState.LocalOwnerIndex가 아군, 나머지 적
    public CardKeyword runtimeKeywords;
    // 이 카드가 **지금 실제로 가진** 고유 키워드. 강화 해금 전이면 비어 있다.
    // data.keywords를 직접 읽지 않는 이유가 여기다 — 마스터 데이터는 "해금되면 무엇이 열리는가"이고
    // 켜졌는지는 성장 레벨이 정한다. runtimeKeywords와 분리한 것은 그웬·피즈의 `&= ~Invincible` 같은
    // 해제가 영구 해금분까지 걷어가면 안 되기 때문(시너지 키워드를 따로 둔 것과 같은 이유).
    public CardKeyword unlockedKeywords;
    // 성장값이 주입된 카드는 1차 진화(Lv5)부터 시너지에 참여한다.
    // 성장 미주입(default) 경로는 기존 AI/멀티 규칙을 보존하기 위해 항상 활성으로 본다.
    public bool synergyEnabled;
    public int attackCount;

    // 시너지: 덱 확정 시 1회 적용, 전투 중 재계산 없음.
    // runtimeKeywords와 분리(그웬·피즈 무적 토글이 시너지 키워드를 clobber하지 않도록).
    public CardKeyword synergyKeywords;
    public int synergyDmgReduction;   // 비늘: 받는 피해 상시 -N. 정적(drain 안 됨) → ClearSynergy 리셋 대상.

    // 라이프사이클 시너지 파생 상태(stateful): 와이어 전송 금지, ClearSynergy 리셋 제외.
    // 양 클라가 동일 스폰/사망/턴종료 경로에서 순수 산술로 동일하게 파생 → 동기화 불필요.
    public int  flowBonus;     // 흐름: AttackDamage에 가산되는 흐름 스택(스택 1당 공격력 +1). 등장마다 성장(무제한).
    public int  legacyStack;   // 유산: 내 턴 종료마다 +1, 사망 시 아군 회복량.
    public bool reviveUsed;    // 언데드: 게임당 1회 부활 소진 플래그.
    public bool hasShield;      // 보호막: 다음 양수 피해 1회 무시. bool이라 재부여해도 중첩되지 않는다.

    // 교활 효과로 필드에서 물러난 뒤 재등장하는 카드인지 표시.
    public bool returnedFromField;

    // 스폰 직후 TurnBegan 1회 스킵용 (피즈·그웬 무적 즉시 소멸 방지)
    public bool justSpawned;

    // 런타임 진화 단계. 0=미진화, 1~CardData.MaxEvolutionStage. 주입원은 마스터 데이터(defaultEvolutionStage).
    public int evolutionStage;
    // 등장 컷씬 1회성 래치. 스왑으로 대기열에 갔다가 다시 필드로 돌아오는 등 같은 인스턴스가
    // 여러 번 "등장"할 수 있어, 매 등장마다 컷씬이 다시 뜨는 것을 막는다.
    // 세팅/조회는 CardCinematicRules에서만 한다(호출부에 판정이 흩어지지 않게).
    public bool cinematicShown;
    // 시네마 공격 1회성 래치. 3단계 카드가 **처음 공격할 때**만 클로즈업 연출을 받고 이후엔 일반 연출로 돌아간다.
    // 세팅/조회는 CardCinematicRules.TryConsumeCinemaAttack에서만 한다.
    public bool cinemaAttackUsed;

    public bool IsAlive => this.hp > 0;
    public bool HasKeyword(CardKeyword _kw) =>
        (this.unlockedKeywords | this.runtimeKeywords | this.synergyKeywords).HasFlag(_kw);

    // ── 전투 규칙 (단일 진실원: 공격 해결부·프리뷰 공용) ──
    /// <summary>이 카드가 가하는 기본 공격력. 도발이면 현재 체력의 절반(최소 1).</summary>
    public int AttackDamage() =>
        (HasKeyword(CardKeyword.Taunt) ? UnityEngine.Mathf.Max(1, UnityEngine.Mathf.FloorToInt(this.hp * 0.5f)) : this.hp)
        + this.flowBonus;   // 가산(흐름 스택)은 도발 반감 뒤에 더함(보너스는 반감 안 받음)

    /// <summary>무쌍 광역 피해량. AttackDamage()와 별개 규칙 — 도발/시너지 보정 없는 순수 hp 기준(v1 시맨틱 보존).</summary>
    public int SplashDamage() => UnityEngine.Mathf.FloorToInt(this.hp * 0.5f);

    // ── 강화 키워드(2차 진화 = Lv10 해금) ──
    /// <summary>강화 효과가 열린 단계. 성장 곡선의 <c>secondEvolutionLevel</c>(기본 Lv10)에 도달하면
    /// <see cref="evolutionStage"/>가 이 값이 된다 — 레벨 숫자를 전투 쪽에 들이지 않기 위해 단계로만 본다.</summary>
    public const int EnhanceStage = 2;

    /// <summary>이 카드가 <b>일반(키워드 없는) 카드의 강화</b> 자격을 갖는가.
    ///
    /// 판정을 <see cref="HasKeyword"/>가 아니라 <c>data.keywords</c>로 하는 이유: 시너지가 얹어 준 키워드나
    /// 전투 중 걸린 표식·무적 때문에 "일반 카드"라는 정체성이 중간에 바뀌면 안 된다. 강화는 카드 원본의 속성이다.</summary>
    public bool HasVanillaEnhance =>
        this.evolutionStage >= EnhanceStage && this.data != null && this.data.keywords == CardKeyword.None;

    /// <summary>일반 강화 추가 피해 = <b>원래 체력</b>의 절반(버림).
    ///
    /// 기준이 현재 hp도, 강화로 늘어난 maxHp도 아니라 <c>data.maxHp</c>인 이유: 문구가 "원래 체력"이고,
    /// 현재 hp 기준이면 맞을수록 약해지는 도발과 같은 축이 되어 "강화"로 읽히지 않는다.
    /// 성장 HP·덩치(bonusHp)·시너지 공격력도 섞지 않는다 — 카드마다 고정된 값이라야 예측이 선다.</summary>
    public int VanillaEnhanceDamage() =>
        this.data != null ? UnityEngine.Mathf.FloorToInt(this.data.maxHp * 0.5f) : 0;

    /// <summary>같은 공격 안에서 연달아 들어오는 두 직격(기본타 → 강화 추가타)의 누적 결과를 <b>부작용 없이</b> 계산.
    /// 돌려주는 값 = (실제 들어갈 총 피해, 이 공격으로 죽는가).
    ///
    /// 폴딩을 여기 한 곳에 두는 이유: 프리뷰(-N 표시)와 전투 종료 예측이 각자 hp를 빼 보면
    /// 감소·무적·덩치(bonusHp) 소진 순서가 조용히 갈라진다. 실제 적용(<see cref="TakeDamage"/>)과
    /// 같은 규칙을 한 번만 적는다.
    ///
    /// 무적은 <b>첫 타가 소진</b>하고 그 다음 타부터 정상으로 들어간다(TakeDamage와 같은 순서).
    /// 첫 타로 쓰러지면 둘째 타는 아예 없다 — "살아 있는 대상에게 한 번 더"가 강화의 계약이다.</summary>
    public (int applied, bool dies) PreviewAttackChain(int _firstRaw, int _secondRaw) =>
        PreviewDamageChain(_firstRaw, _secondRaw);

    /// <summary>같은 해결 순서 안에서 연달아 들어오는 두 피해의 결과를 부작용 없이 계산한다.</summary>
    public (int applied, bool dies) PreviewDamageChain(int _firstRaw, int _secondRaw)
    {
        int  t_hp          = this.hp;
        int  t_bonus       = this.bonusHp;
        bool t_shield      = this.hasShield;
        bool t_invincible  = HasKeyword(CardKeyword.Invincible);
        int  t_applied     = 0;

        for (int t_i = 0; t_i < 2; t_i++)
        {
            int t_raw = t_i == 0 ? _firstRaw : _secondRaw;
            DamageResolution t_result = ResolveDamage(t_raw, t_hp, t_bonus, t_shield, t_invincible);
            t_hp          = t_result.hp;
            t_bonus       = t_result.bonusHp;
            t_shield      = t_result.hasShield;
            t_invincible  = t_result.hasInvincible;
            t_applied    += t_result.applied;
            if (t_hp <= 0) break;
        }

        return (t_applied, t_hp <= 0);
    }

    /// <summary>피해 감소(비늘)가 깎아낼 수 있는 하한. 감소가 걸려도 **최소 1은 들어간다** —
    /// 0까지 떨어지면 그 카드는 그 공격에 대해 완전 무효가 되어 전투가 멈춘다(무적과 구분이 사라진다).
    /// 원래 0인 피해에는 적용하지 않는다(없던 피해를 만들지 않는다).</summary>
    const int MinReducedDamage = 1;

    /// <summary>받는 피해 감소 지점. 비늘 감소는 모든 피해에 적용하며 하한은
    /// <see cref="MinReducedDamage"/>.
    /// Clamp/사망판정/프리뷰/실적용이 모두 이 식을 통과해야 프리뷰↔실제·양클라 일치.</summary>
    int EffectiveDamage(int _raw)
    {
        if (_raw <= 0) return 0;

        int t_cut = this.synergyDmgReduction;
        return t_cut > 0 ? UnityEngine.Mathf.Max(MinReducedDamage, _raw - t_cut) : _raw;
    }

    readonly struct DamageResolution
    {
        public readonly int  hp;
        public readonly int  bonusHp;
        public readonly bool hasShield;
        public readonly bool hasInvincible;
        public readonly int  applied;
        public bool Dies => this.hp <= 0;

        public DamageResolution(int _hp, int _bonusHp, bool _hasShield, bool _hasInvincible, int _applied)
        {
            this.hp             = _hp;
            this.bonusHp        = _bonusHp;
            this.hasShield      = _hasShield;
            this.hasInvincible  = _hasInvincible;
            this.applied        = _applied;
        }
    }

    /// <summary>피해 해석의 단일 진실원. 무적 → 보호막 → 비늘 → 추가 생명력 → 생명력 순서로
    /// 부작용 없이 계산한다. 실제 상태 반영과 프리뷰가 모두 이 결과를 사용한다.</summary>
    DamageResolution ResolveDamage(int _raw, int _hp, int _bonusHp, bool _hasShield, bool _hasInvincible)
    {
        if (_raw <= 0 || _hp <= 0)
            return new DamageResolution(_hp, _bonusHp, _hasShield, _hasInvincible, 0);
        if (_hasInvincible)
            return new DamageResolution(_hp, _bonusHp, _hasShield, false, 0);
        if (_hasShield)
            return new DamageResolution(_hp, _bonusHp, false, _hasInvincible, 0);

        int t_effective  = EffectiveDamage(_raw);
        int t_applied    = UnityEngine.Mathf.Min(t_effective, _hp + _bonusHp);
        int t_bonusDrain = UnityEngine.Mathf.Min(_bonusHp, t_effective);
        int t_bonusAfter = _bonusHp - t_bonusDrain;
        int t_hpAfter    = UnityEngine.Mathf.Max(0, _hp - (t_effective - t_bonusDrain));
        return new DamageResolution(t_hpAfter, t_bonusAfter, _hasShield, _hasInvincible, t_applied);
    }

    /// <summary>보호막·피해 감소·현재 체력 상한을 반영한 실제 적용 예상 피해.</summary>
    public int ClampDamage(int _raw) =>
        ResolveDamage(_raw, this.hp, this.bonusHp, this.hasShield, HasKeyword(CardKeyword.Invincible)).applied;

    /// <summary>_raw 피해 1회로 이 카드가 죽는가. 보호막·무적 소모까지 실제 규칙과 같이 계산한다.</summary>
    public bool WouldDieFrom(int _raw) =>
        ResolveDamage(_raw, this.hp, this.bonusHp, this.hasShield, HasKeyword(CardKeyword.Invincible)).Dies;

    /// <summary>이 카드가 _defender를 공격할 때 반격을 받는가. 원거리면 무반격, 대상이 표식이면 무반격.
    ///
    /// **이미 죽어 있는 방어자는 반격하지 않는다.** 공격 개시 전에 죽는 경로가 실재한다 —
    /// 낙인 선피해([BeforeAttack])가 hp를 0으로 만들어도 시체 정리(RemoveDead)는 Execute 안에서야 돌아서,
    /// 그 사이 방어자는 hp 0인 채 슬롯에 남아 있다. 그 상태로 AttackDamage()를 읽으면 도발 하한(최소 1) 때문에
    /// **hp 0짜리가 1을 반격**한다. 직격으로 죽는 정상 경로는 이 판정이 피해 적용 **전** 스냅샷이라 영향 없다
    /// (동시 해결 = 공격 전 수치로 반격).</summary>
    public bool TakesCounterFrom(CardInstance _defender) =>
        _defender != null && _defender.IsAlive
        && !HasKeyword(CardKeyword.Ranged) && !_defender.HasKeyword(CardKeyword.Mark);

    /// <summary>_growth = 카드 영구 성장값(강화 체력). 기본값(default)이면 성장 미적용 —
    /// 성장을 태우지 않는 경로(AI 적 필드·멀티 원격 미러)는 인자를 생략한다.</summary>
    public CardInstance(CardData _data, int _ownerIndex, CardGrowth _growth = default)
    {
        this.data    = _data;
        // 강화분은 최대 체력에 흡수(bonusHp는 데미지로 소진되는 시너지 채널이라 영구값을 담으면 안 된다).
        this.maxHp   = _data.maxHp + _growth.HpBonus;
        this.hp      = this.maxHp;
        this.bonusHp = _data.bonusHp;
        this.slotIndex   = -1;
        this.isRevealed  = false;
        this.ownerIndex  = _ownerIndex;
        // 성장값 주입은 이 한 지점뿐(모든 생성 경로가 이 ctor를 통과).
        // 진화 단계는 마스터 데이터(임시 입력)와 강화 해금 중 높은 쪽 — 성장 미주입이면 후자가 0이라 기존 동작 그대로.
        this.evolutionStage = UnityEngine.Mathf.Max(_data.defaultEvolutionStage, _growth.EvolutionStage);
        // 성장을 태우는 경로만 해금 게이트를 받는다. 미주입(AI 적 필드·멀티 원격 미러)은 마스터 데이터 그대로 —
        // 한쪽만 키워드가 사라지면 밸런스 기준선이 무너지고 멀티는 즉시 divergence다.
        this.unlockedKeywords = _growth.Applied ? _growth.UnlockedKeywords : _data.keywords;
        this.synergyEnabled   = !_growth.Applied || _growth.SynergyUnlocked;
    }

    // ── 시너지 적용 (SynergyApplier가 호출하는 계약: 덱 확정 시 1회, 가산/합집합) ──
    // _bonusHp(덩치): stateful(데미지로 소진)이라 ClearSynergy가 리셋하지 않음 → ApplyAll 이중호출 금지(1회 전제 유지).
    // 시너지는 공격력을 올리지 않는다 — 스탯 항목이 생명력(bonusHp)뿐이고, 공격력은 hp에서 파생되므로
    // 별도 가산 항을 두면 진실원이 둘로 갈린다.
    public void ApplySynergy(int _bonusHp, CardKeyword _kw, int _dmgReduction)
    {
        this.bonusHp             += _bonusHp;
        this.synergyKeywords     |= _kw;
        this.synergyDmgReduction += _dmgReduction;
    }
    // 정적 스탯(keywords/dmgReduction)만 리셋 → ApplyAll 멱등. bonusHp(덩치)는 stateful이라 제외.
    public void ClearSynergy() { this.synergyKeywords = CardKeyword.None; this.synergyDmgReduction = 0; }

    /// <summary>_damage 적용 후 (hp, bonusHp)를 부작용 없이 계산. TakeDamage와 동일 규칙(프리뷰 공용).</summary>
    public (int hp, int bonusHp) PreviewAfterDamage(int _damage)
    {
        DamageResolution t_result = ResolveDamage(
            _damage, this.hp, this.bonusHp, this.hasShield, HasKeyword(CardKeyword.Invincible));
        return (t_result.hp, t_result.bonusHp);
    }

    /// <summary>체력 회복(단일 진실원). hp만 회복하며 기본적으로 maxHp 상한(보너스HP는 회복 대상 아님).
    /// _allowOverheal=true면 maxHp를 초과해 회복한다. 반환값 = **실제 회복량**(상한에 걸리면 0).
    /// _showEffect=false면 연출을 생략한다 —
    /// 힐러 투사체처럼 회복 표기를 **도착 시점으로 미루는** 호출부 전용(상태 변경 시점은 그대로).</summary>
    public int Heal(int _amount, bool _showEffect = true, bool _allowOverheal = false)
    {
        if (_amount <= 0) return 0;
        if (!_allowOverheal && this.hp >= this.maxHp) return 0;
        int t_before = this.hp;
        this.hp = _allowOverheal
            ? this.hp + _amount
            : UnityEngine.Mathf.Min(this.hp + _amount, this.maxHp);
        int t_healed = this.hp - t_before;
        // 실제 회복량으로 연출 1회(힐러/돌보미/유산/포식자 모두 이 경로). 순수 연출 — RNG/게임상태 무관.
        // 표기를 미루는 호출부(_showEffect:false)는 **미룬다는 사실 자체를 뷰에 알린다** — 그러지 않으면
        // 그 사이 화면 갱신(Render)이 최신 hp를 먼저 찍어, 투사체는 나중에 오는데 숫자는 이미 올라가 있다.
        if (t_healed > 0)
        {
            if (_showEffect) CardView.GetView(this)?.PlayHealEffect(t_healed);
            else             CardView.GetView(this)?.DeferHpDisplay(t_healed);
        }
        return t_healed;
    }

    /// <summary>돌보미: bonusHp 부여(양수만). stateful(데미지로 소진) → ClearSynergy 리셋 제외.</summary>
    public void GrantBonusHp(int _amount)
    {
        if (_amount > 0) this.bonusHp += _amount;
    }

    public void GrantShield() => this.hasShield = true;
    public void ClearShield() => this.hasShield = false;

    /// <summary>언데드: 파괴 순간 체력 50%(최소 1)로 게임당 1회 부활. 성공 시 true(제자리 hp 복구).
    /// 무적 등과 무관한 순수 산술 — RemoveDead가 이 결과로 RemoveCard 게이팅.</summary>
    public bool ReviveAtHalf()
    {
        if (this.reviveUsed || IsAlive) return false;
        this.reviveUsed = true;
        this.hp = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.FloorToInt(this.maxHp * 0.5f));
        CardView.GetView(this)?.PlayHealEffect(this.hp);   // 언데드 부활도 회복 연출(복구된 hp만큼). 순수 연출.
        return true;
    }

    /// <summary>피해 적용. 무적·보호막 소모와 비늘·추가 생명력·생명력 폴딩을 한 번에 반영한다.</summary>
    public void TakeDamage(int _damage)
    {
        bool t_hadInvincible = HasKeyword(CardKeyword.Invincible);
        DamageResolution t_result = ResolveDamage(
            _damage, this.hp, this.bonusHp, this.hasShield, t_hadInvincible);
        if (t_hadInvincible && !t_result.hasInvincible)
            this.runtimeKeywords &= ~CardKeyword.Invincible;
        this.hasShield = t_result.hasShield;
        this.hp        = t_result.hp;
        this.bonusHp   = t_result.bonusHp;
    }
}
