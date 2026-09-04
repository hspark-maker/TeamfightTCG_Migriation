/// <summary>
/// 전투 발동 타이밍 택소노미 — 이 프로젝트의 트리거 어휘 단일 진실원.
///
/// **원칙: 이름 1개 = 타이밍 1개.** 하나의 훅 이름이 두 시점을 겸하면 안 된다.
/// (구 `OnSpawn`이 오프닝 배치와 런타임 등장을, 구 `OnDeath`가 취소 가능 시점과 확정 제거를
///  겸하던 것이 이 정리의 발단. 각각 Placed/Entered, Lethal/Removed로 분리했다.)
///
/// **훅 선언은 <see cref="BattleEffect"/> 한 곳에만 있다.** SynergyEffect는 그 베이스를
/// 상속만 하고 훅을 따로 선언하지 않는다. 순수 수치 규칙은 CardKeyword + CardInstance 폴딩을 쓴다.
///
/// ── 타이밍 15 ─────────────────────────────────────────────────────────────
///  #   타이밍         발화 위치                        계약
///  1   DeckResolved   SynergyApplier.ApplyAll          동기 void. 멱등.
///  2   Placed         BattleField 초기 배치            동기 완결. **등장 아님**
///  3   TurnStarted    TurnRunner (필드 단위)           훅 아님 — TurnEvents 구독(HealerEffect/UI)
///  4   TurnBegan      TurnRunner (카드 단위)           await. justSpawned 1회 스킵
///  5   TurnEnded      TurnRunner (OnExit 직후)         동기 void
///  6   BeforeAttack   AttackFlow (Execute 전)          await. **RNG 금지**
///  7   Hit            CardInstance.TakeDamage          ※예약(훅 없음) 아래 참조
///  8   Attacked       AttackProcessor 피격 반응         **동기 void**(아래 ★)
///  9   DamageDealt    AttackProcessor 피해 통보         동기 완결
/// 10   Lethal         AttackProcessor 치사 확정         동기 void. **★취소 가능(부활)**
/// 11   Removed        AttackProcessor 제거 직전         동기 완결. 취소 불가
/// 12   SwappedOut     AttackProcessor 필드 이탈         동기 완결
/// 13   Entered        BattleField 런타임 등장           동기 완결
/// 14   AfterAttack    AttackFlow (연출 후)             await. defenderKilled 포함
/// 15   BoardChanged   BattleField 보드 구성 변화        동기 void. 아래 ※
///
/// ※ BoardChanged: 필드의 **라이브 카드 구성이 바뀔 때마다** 발화한다(배치 확정 / 등장 / 제거).
///   "필드의 X 수만큼"처럼 보드를 세서 파생 상태를 유지해야 하는 효과가 쓴다.
///   발화점은 전부 BattleField 안이다 — ApplyDeckSynergy / NotifyEntered / RemoveCard.
///
/// ※ Hit(7)은 **훅이 없다.** 타임라인상 실재하는 지점(모든 피해원이 통과하는 CardInstance.TakeDamage)이라
///   어휘에는 남겨두며, 필요해지면 여기에 되살린다.
///
/// ★ Attacked가 **동기 void**인 이유: 가시 등 피격 반응이 치사 래치(AttackProcessor의 t_defKilled)보다
///   **먼저** hp에 반영돼야 하므로 규칙 훅은 동기 void만 허용한다.
///   양쪽 계약을 통일할 때는 **느슨한 쪽이 아니라 엄격한 쪽**으로 맞춘다.
///
/// ★ Lethal → Removed 사이에 IsAlive 게이트가 있다. Lethal에서 부활(ReviveAtHalf)하면
///   Removed와 슬롯 제거가 통째로 스킵된다. 두 타이밍을 하나로 합칠 수 없는 이유.
///
/// ── 결정론 규약 (전 타이밍 공통) ──────────────────────────────────────────
/// - **훅에서 MatchRandom 소비 금지. 예외 없음.** 규칙 훅에서 뽑으면 즉시 divergence.
/// - 규칙 훅은 반환 전에 상태변이를 완결하고, await가 필요한 표시는 별도 presentation 단계에서만 실행한다.
/// - 순서는 데이터가 아니라 코드로 고정한다.
/// </summary>
public static class BattleTimings { }


// ── 타이밍별 컨텍스트 ────────────────────────────────────────────────────────
// ctx.synergy = 이 발화를 일으킨 시너지.
// 시너지 효과는 이 값으로 BelongsTo 자기판정 + SynergyTriggers.Fire 태그를 한다.
// WithSynergy(...)는 디스패처가 효과별로 태그를 갈아끼울 때 쓰는 복사 헬퍼다.

/// <summary>DeckResolved — 덱 확정 시 정적 적용. state는 산출된 시너지 스냅샷.</summary>
public readonly struct DeckCtx
{
    public readonly CardInstance card;
    public readonly SynergyState state;
    public readonly SynergyRuntime synergy;
    public DeckCtx(CardInstance _card, SynergyState _state, SynergyRuntime _synergy = null)
    { this.card = _card; this.state = _state; this.synergy = _synergy; }
    public DeckCtx WithSynergy(SynergyRuntime _s) => new DeckCtx(this.card, this.state, _s);
}

/// <summary>Placed(오프닝 배치) / Entered(런타임 등장) 공용.</summary>
public readonly struct SpawnCtx
{
    public readonly CardInstance self;
    public readonly BattleField  field;
    public readonly SynergyRuntime synergy;
    public SpawnCtx(CardInstance _self, BattleField _field, SynergyRuntime _synergy = null)
    { this.self = _self; this.field = _field; this.synergy = _synergy; }
    public SpawnCtx WithSynergy(SynergyRuntime _s) => new SpawnCtx(this.self, this.field, _s);
}

/// <summary>BoardChanged — 필드의 라이브 카드 구성이 바뀐 직후.
/// self는 null이다(필드 전체를 대상으로 한 번 발화하므로).
/// 시너지 효과는 field를 훑어 자기 소속 카드를 찾는다.</summary>
public readonly struct BoardCtx
{
    public readonly BattleField  field;
    public readonly CardInstance self;
    public readonly SynergyRuntime synergy;
    public BoardCtx(BattleField _field, CardInstance _self = null, SynergyRuntime _synergy = null)
    { this.field = _field; this.self = _self; this.synergy = _synergy; }
    public BoardCtx WithSynergy(SynergyRuntime _s) => new BoardCtx(this.field, this.self, _s);
}

/// <summary>TurnBegan / TurnEnded 공용.</summary>
public readonly struct TurnCtx
{
    public readonly CardInstance self;
    public readonly BattleField  field;
    public readonly SynergyRuntime synergy;
    public TurnCtx(CardInstance _self, BattleField _field, SynergyRuntime _synergy = null)
    { this.self = _self; this.field = _field; this.synergy = _synergy; }
    public TurnCtx WithSynergy(SynergyRuntime _s) => new TurnCtx(this.self, this.field, _s);
}

/// <summary>BeforeAttack — self=공격자. 데미지 해결 전이라 아직 아무 수치도 확정 안 됨.</summary>
public readonly struct BeforeAttackCtx
{
    public readonly CardInstance self;
    public readonly CardInstance defender;
    public readonly BattleField  ownField;
    public readonly BattleField  enemyField;
    public readonly SynergyRuntime synergy;
    public BeforeAttackCtx(CardInstance _self, CardInstance _defender,
                           BattleField _ownField, BattleField _enemyField, SynergyRuntime _synergy = null)
    { this.self = _self; this.defender = _defender; this.ownField = _ownField;
      this.enemyField = _enemyField; this.synergy = _synergy; }
    public BeforeAttackCtx WithSynergy(SynergyRuntime _s)
        => new BeforeAttackCtx(this.self, this.defender, this.ownField, this.enemyField, _s);
}

/// <summary>Attacked — self=방어자. 직격이 이미 적용된 뒤.</summary>
public readonly struct AttackedCtx
{
    public readonly CardInstance self;
    public readonly CardInstance attacker;
    public readonly BattleField  ownField;
    public readonly BattleField  enemyField;
    public readonly SynergyRuntime synergy;
    public AttackedCtx(CardInstance _self, CardInstance _attacker,
                       BattleField _ownField, BattleField _enemyField, SynergyRuntime _synergy = null)
    { this.self = _self; this.attacker = _attacker; this.ownField = _ownField;
      this.enemyField = _enemyField; this.synergy = _synergy; }
    public AttackedCtx WithSynergy(SynergyRuntime _s)
        => new AttackedCtx(this.self, this.attacker, this.ownField, this.enemyField, _s);
}

/// <summary>DamageDealt — self가 damage만큼 피해를 입혔다. isRetaliation=반격분.
/// field는 self의 소속 필드(디스패처가 BelongsTo 판정에 쓴다 — 없으면 시그니처가 2인자로 어긋난다).</summary>
public readonly struct DamageDealtCtx
{
    public readonly CardInstance self;
    public readonly BattleField  field;
    public readonly int          damage;
    public readonly bool         isRetaliation;
    public readonly SynergyRuntime synergy;
    public DamageDealtCtx(CardInstance _self, BattleField _field, int _damage, bool _isRetaliation,
                          SynergyRuntime _synergy = null)
    { this.self = _self; this.field = _field; this.damage = _damage;
      this.isRetaliation = _isRetaliation; this.synergy = _synergy; }
    public DamageDealtCtx WithSynergy(SynergyRuntime _s)
        => new DamageDealtCtx(this.self, this.field, this.damage, this.isRetaliation, _s);
}

/// <summary>Lethal(취소 가능) / Removed(확정) 공용.</summary>
public readonly struct DeathCtx
{
    public readonly CardInstance self;
    public readonly BattleField  field;
    public readonly SynergyRuntime synergy;
    public DeathCtx(CardInstance _self, BattleField _field, SynergyRuntime _synergy = null)
    { this.self = _self; this.field = _field; this.synergy = _synergy; }
    public DeathCtx WithSynergy(SynergyRuntime _s) => new DeathCtx(this.self, this.field, _s);
}

/// <summary>SwappedOut — self가 필드를 떠나고 incoming이 그 자리에 들어온다.</summary>
public readonly struct SwapOutCtx
{
    public readonly CardInstance self;
    public readonly CardInstance incoming;
    public readonly BattleField  field;
    public readonly SynergyRuntime synergy;
    public SwapOutCtx(CardInstance _self, CardInstance _incoming, BattleField _field, SynergyRuntime _synergy = null)
    { this.self = _self; this.incoming = _incoming; this.field = _field; this.synergy = _synergy; }
    public SwapOutCtx WithSynergy(SynergyRuntime _s)
        => new SwapOutCtx(this.self, this.incoming, this.field, _s);
}

/// <summary>AfterAttack — self=공격자. 구 OnKill을 흡수했다(defenderKilled로 판정).
/// damageDealt는 주 대상 실제 적용 데미지(스플래시 미합산).</summary>
public readonly struct AfterAttackCtx
{
    public readonly CardInstance self;
    public readonly CardInstance target;
    public readonly BattleField  ownField;
    public readonly BattleField  enemyField;
    public readonly int          damageDealt;
    public readonly bool         defenderKilled;
    public readonly SynergyRuntime synergy;
    public AfterAttackCtx(CardInstance _self, CardInstance _target, BattleField _ownField, BattleField _enemyField,
                          int _damageDealt, bool _defenderKilled, SynergyRuntime _synergy = null)
    {
        this.self = _self; this.target = _target;
        this.ownField = _ownField; this.enemyField = _enemyField;
        this.damageDealt = _damageDealt; this.defenderKilled = _defenderKilled;
        this.synergy = _synergy;
    }
    public AfterAttackCtx WithSynergy(SynergyRuntime _s)
        => new AfterAttackCtx(this.self, this.target, this.ownField, this.enemyField,
                              this.damageDealt, this.defenderKilled, _s);
}
