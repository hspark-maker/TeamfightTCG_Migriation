/// <summary>
/// 전투 발동 타이밍 택소노미 — 이 프로젝트의 트리거 어휘 단일 진실원.
///
/// **원칙: 이름 1개 = 타이밍 1개.** 하나의 훅 이름이 두 시점을 겸하면 안 된다.
/// (구 `OnSpawn`이 오프닝 배치와 런타임 등장을, 구 `OnDeath`가 취소 가능 시점과 확정 제거를
///  겸하던 것이 이 정리의 발단. 각각 Placed/Entered, Lethal/Removed로 분리했다.)
///
/// **훅 선언은 <see cref="BattleEffect"/> 한 곳에만 있다.** CardPassive와 SynergyEffect는
/// 그 베이스를 상속만 하고 훅을 따로 선언하지 않는다 — 둘의 차이는 훅 목록이 아니라 **활성 조건**이다:
///   카드 고유·상시     → CardPassive   (카드당 1개, 항상 켜짐)
///   덱 조합·임계 티어  → SynergyEffect (덱 확정 시 활성 판정, BelongsTo 그룹)
///   순수 규칙(수치 변화) → CardKeyword + CardInstance 폴딩 (타이밍 불필요)
/// "어느 타이밍이냐"가 1차 질문이고, 시스템은 그 다음이다.
///
/// **모든 타이밍은 양쪽(passive/synergy)에 발화된다.** 예전엔 타이밍마다 한쪽만 구독 가능했는데,
/// 그 분리는 원칙이 아니라 이력이었다(TurnBegan은 passive만, TurnEnded는 synergy만인 식).
/// 지금은 아무 효과나 아무 타이밍이나 구독할 수 있다.
///
/// ── 타이밍 15 ─────────────────────────────────────────────────────────────
///  #   타이밍         발화 위치                        계약
///  1   DeckResolved   synergy=SynergyApplier.ApplyAll / passive=BattleField.ApplyDeckSynergy
///                                                     동기 void. 멱등. **유일하게 synergy→passive 순**(아래 ◆)
///  2   Placed         BattleField 초기 배치            .Forget(동기 완결). **등장 아님**
///  3   TurnStarted    TurnRunner (필드 단위)           훅 아님 — TurnEvents 구독(HealerEffect/UI)
///  4   TurnBegan      TurnRunner (카드 단위)           await. justSpawned 1회 스킵
///  5   TurnEnded      TurnRunner (OnExit 직후)         동기 void
///  6   BeforeAttack   AttackFlow (Execute 전)          await. **RNG 금지**
///  7   Hit            CardInstance.TakeDamage          ※예약(훅 없음) 아래 참조
///  8   Attacked       AttackProcessor 피격 반응         **동기 void**(아래 ★)
///  9   DamageDealt    AttackProcessor 피해 통보         .Forget
/// 10   Lethal         AttackProcessor 치사 확정         동기 void. **★취소 가능(부활)**
/// 11   Removed        AttackProcessor 제거 직전         .Forget. 취소 불가
/// 12   SwappedOut     AttackProcessor 필드 이탈         .Forget
/// 13   Entered        BattleField 런타임 등장           .Forget(동기 완결)
/// 14   AfterAttack    AttackFlow (연출 후)             await. defenderKilled 포함
/// 15   BoardChanged   BattleField 보드 구성 변화        동기 void. 아래 ※
///
/// ※ BoardChanged: 필드의 **라이브 카드 구성이 바뀔 때마다** 발화한다(배치 확정 / 등장 / 제거).
///   "필드의 X 수만큼"처럼 보드를 세서 파생 상태를 유지해야 하는 효과(성벽 피해감소)가 쓴다.
///   발화점은 전부 BattleField 안이다 — ApplyDeckSynergy / NotifyEntered / RemoveCard.
///
/// ※ Hit(7)은 **훅이 없다.** 타임라인상 실재하는 지점(모든 피해원이 통과하는 CardInstance.TakeDamage)이라
///   어휘에는 남겨두지만, 구 CardPassive.OnHit이 구현체 0이라 제거했다. 필요해지면 여기에 되살린다.
///
/// ★ Attacked가 **동기 void**인 이유: 성벽/가시 반격이 치사 래치(AttackProcessor의 t_defKilled)보다
///   **먼저** hp에 반영돼야 한다. UniTask면 await 지점에서 래치가 반격 전 hp를 읽을 수 있다.
///   양쪽 계약을 통일할 때는 **느슨한 쪽이 아니라 엄격한 쪽**으로 맞춘다.
///
/// ★ Lethal → Removed 사이에 IsAlive 게이트가 있다. Lethal에서 부활(ReviveAtHalf)하면
///   Removed와 슬롯 제거가 통째로 스킵된다. 두 타이밍을 하나로 합칠 수 없는 이유.
///
/// ── 결정론 규약 (전 타이밍 공통) ──────────────────────────────────────────
/// - **훅에서 MatchRandom 소비 금지.** 게임 RNG 소비는 AttackProcessor.PickSplash 한 곳과
///   OrnnPassive(AfterAttack, await 경로)뿐이다. .Forget/동기 void 훅에서 뽑으면 즉시 divergence.
/// - .Forget으로 발화되는 훅은 **첫 await 전에 상태변이를 완결**해야 한다(양 클라 동형 보장).
/// - 순서는 데이터가 아니라 코드로 고정. 한 타이밍에서 **passive → synergy** 순으로 발화한다.
///
/// ◆ DeckResolved만 **synergy → passive** 역순이다. 고칠 수 있는 순서 문제가 아니라 구조적 제약이다:
///   `SynergyApplier.ApplyAll` **안에** `ClearSynergy()`가 있어서, passive를 먼저 돌리면 passive가 넣은
///   synergyAtk/keywords/dmgReduction이 그 Clear에 통째로 지워진다. 순서를 뒤집으려면 ClearSynergy 소유권을
///   ApplyAll 밖으로 빼야 한다. 지금은 결과 영향 0(passive DeckResolved 구현체 없음).
/// </summary>
public static class BattleTimings { }


// ── 타이밍별 컨텍스트 ────────────────────────────────────────────────────────
// passive와 synergy가 **같은 컨텍스트 타입·같은 시그니처**를 쓴다.
// ctx.synergy = 이 발화를 일으킨 시너지. passive 발화면 null("시너지가 부른 게 아님").
// 시너지 효과는 이 값으로 BelongsTo 자기판정 + SynergyTriggers.Fire 배너 태그를 한다.
// WithSynergy(...)는 디스패처가 효과별로 태그를 갈아끼울 때 쓰는 복사 헬퍼다.

/// <summary>DeckResolved — 덱 확정 시 정적 적용. state는 산출된 시너지 스냅샷.</summary>
public readonly struct DeckCtx
{
    public readonly CardInstance card;
    public readonly SynergyState state;
    public readonly SynergyData  synergy;
    public DeckCtx(CardInstance _card, SynergyState _state, SynergyData _synergy = null)
    { this.card = _card; this.state = _state; this.synergy = _synergy; }
    public DeckCtx WithSynergy(SynergyData _s) => new DeckCtx(this.card, this.state, _s);
}

/// <summary>Placed(오프닝 배치) / Entered(런타임 등장) 공용.</summary>
public readonly struct SpawnCtx
{
    public readonly CardInstance self;
    public readonly BattleField  field;
    public readonly SynergyData  synergy;
    public SpawnCtx(CardInstance _self, BattleField _field, SynergyData _synergy = null)
    { this.self = _self; this.field = _field; this.synergy = _synergy; }
    public SpawnCtx WithSynergy(SynergyData _s) => new SpawnCtx(this.self, this.field, _s);
}

/// <summary>BoardChanged — 필드의 라이브 카드 구성이 바뀐 직후.
/// self: passive 발화면 그 카드, synergy 발화면 **null**(필드 전체를 대상으로 한 번 발화하므로).
/// 시너지 효과는 field를 훑어 자기 소속 카드를 찾는다.</summary>
public readonly struct BoardCtx
{
    public readonly BattleField  field;
    public readonly CardInstance self;
    public readonly SynergyData  synergy;
    public BoardCtx(BattleField _field, CardInstance _self = null, SynergyData _synergy = null)
    { this.field = _field; this.self = _self; this.synergy = _synergy; }
    public BoardCtx WithSynergy(SynergyData _s) => new BoardCtx(this.field, this.self, _s);
}

/// <summary>TurnBegan / TurnEnded 공용.</summary>
public readonly struct TurnCtx
{
    public readonly CardInstance self;
    public readonly BattleField  field;
    public readonly SynergyData  synergy;
    public TurnCtx(CardInstance _self, BattleField _field, SynergyData _synergy = null)
    { this.self = _self; this.field = _field; this.synergy = _synergy; }
    public TurnCtx WithSynergy(SynergyData _s) => new TurnCtx(this.self, this.field, _s);
}

/// <summary>BeforeAttack — self=공격자. 데미지 해결 전이라 아직 아무 수치도 확정 안 됨.</summary>
public readonly struct BeforeAttackCtx
{
    public readonly CardInstance self;
    public readonly CardInstance defender;
    public readonly BattleField  ownField;
    public readonly BattleField  enemyField;
    public readonly SynergyData  synergy;
    public BeforeAttackCtx(CardInstance _self, CardInstance _defender,
                           BattleField _ownField, BattleField _enemyField, SynergyData _synergy = null)
    { this.self = _self; this.defender = _defender; this.ownField = _ownField;
      this.enemyField = _enemyField; this.synergy = _synergy; }
    public BeforeAttackCtx WithSynergy(SynergyData _s)
        => new BeforeAttackCtx(this.self, this.defender, this.ownField, this.enemyField, _s);
}

/// <summary>Attacked — self=방어자. 직격이 이미 적용된 뒤.</summary>
public readonly struct AttackedCtx
{
    public readonly CardInstance self;
    public readonly CardInstance attacker;
    public readonly BattleField  ownField;
    public readonly BattleField  enemyField;
    public readonly SynergyData  synergy;
    public AttackedCtx(CardInstance _self, CardInstance _attacker,
                       BattleField _ownField, BattleField _enemyField, SynergyData _synergy = null)
    { this.self = _self; this.attacker = _attacker; this.ownField = _ownField;
      this.enemyField = _enemyField; this.synergy = _synergy; }
    public AttackedCtx WithSynergy(SynergyData _s)
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
    public readonly SynergyData  synergy;
    public DamageDealtCtx(CardInstance _self, BattleField _field, int _damage, bool _isRetaliation,
                          SynergyData _synergy = null)
    { this.self = _self; this.field = _field; this.damage = _damage;
      this.isRetaliation = _isRetaliation; this.synergy = _synergy; }
    public DamageDealtCtx WithSynergy(SynergyData _s)
        => new DamageDealtCtx(this.self, this.field, this.damage, this.isRetaliation, _s);
}

/// <summary>Lethal(취소 가능) / Removed(확정) 공용.</summary>
public readonly struct DeathCtx
{
    public readonly CardInstance self;
    public readonly BattleField  field;
    public readonly SynergyData  synergy;
    public DeathCtx(CardInstance _self, BattleField _field, SynergyData _synergy = null)
    { this.self = _self; this.field = _field; this.synergy = _synergy; }
    public DeathCtx WithSynergy(SynergyData _s) => new DeathCtx(this.self, this.field, _s);
}

/// <summary>SwappedOut — self가 필드를 떠나고 incoming이 그 자리에 들어온다.</summary>
public readonly struct SwapOutCtx
{
    public readonly CardInstance self;
    public readonly CardInstance incoming;
    public readonly BattleField  field;
    public readonly SynergyData  synergy;
    public SwapOutCtx(CardInstance _self, CardInstance _incoming, BattleField _field, SynergyData _synergy = null)
    { this.self = _self; this.incoming = _incoming; this.field = _field; this.synergy = _synergy; }
    public SwapOutCtx WithSynergy(SynergyData _s)
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
    public readonly SynergyData  synergy;
    public AfterAttackCtx(CardInstance _self, CardInstance _target, BattleField _ownField, BattleField _enemyField,
                          int _damageDealt, bool _defenderKilled, SynergyData _synergy = null)
    {
        this.self = _self; this.target = _target;
        this.ownField = _ownField; this.enemyField = _enemyField;
        this.damageDealt = _damageDealt; this.defenderKilled = _defenderKilled;
        this.synergy = _synergy;
    }
    public AfterAttackCtx WithSynergy(SynergyData _s)
        => new AfterAttackCtx(this.self, this.target, this.ownField, this.enemyField,
                              this.damageDealt, this.defenderKilled, _s);
}
