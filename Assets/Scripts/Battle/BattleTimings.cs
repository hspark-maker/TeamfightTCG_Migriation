/// <summary>
/// 전투 발동 타이밍 택소노미 — 이 프로젝트의 트리거 어휘 단일 진실원.
///
/// **원칙: 이름 1개 = 타이밍 1개.** 하나의 훅 이름이 두 시점을 겸하면 안 된다.
/// (구 `OnSpawn`이 오프닝 배치와 런타임 등장을, 구 `OnDeath`가 취소 가능 시점과 확정 제거를
///  겸하던 것이 이 정리의 발단. 각각 Placed/Entered, Lethal/Removed로 분리했다.)
///
/// 효과를 어느 시스템(CardPassive / SynergyEffect / CardKeyword)에 넣을지는 **활성 조건**으로만 갈린다:
///   카드 고유·상시     → CardPassive
///   덱 조합·임계 티어  → SynergyEffect
///   순수 규칙(수치 변화) → CardKeyword + CardInstance 폴딩 (타이밍 불필요)
/// "어느 타이밍이냐"가 1차 질문이고, 시스템은 그 다음이다.
///
/// ── 타이밍 14 ─────────────────────────────────────────────────────────────
///  #   타이밍         위치                          구독            계약
///  1   DeckResolved   SynergyApplier.ApplyAll       synergy         동기. 멱등(ClearSynergy 선행)
///  2   Placed         BattleField 초기 배치          passive         동기 완결. **등장 아님**
///  3   TurnStarted    TurnRunner (필드 단위)         TurnEvents 구독  훅 아님(HealerEffect/UI)
///  4   TurnBegan      TurnRunner (카드 단위)         passive         await. justSpawned 1회 스킵
///  5   TurnEnded      TurnRunner (OnExit 직후)       synergy         동기 void
///  6   BeforeAttack   AttackFlow (Execute 전)        synergy         await. **RNG 금지**
///  7   Hit            CardInstance.TakeDamage        ※예약(훅 없음) 아래 참조
///  8   Attacked       AttackProcessor 피격 반응       passive+synergy 동기 완결 필수(아래 ★)
///  9   DamageDealt    AttackProcessor 피해 통보       passive         .Forget
/// 10   Lethal         AttackProcessor 치사 확정       synergy         동기 void. **★취소 가능(부활)**
/// 11   Removed        AttackProcessor 제거 직전       passive         .Forget. 취소 불가
/// 12   SwappedOut     AttackProcessor 필드 이탈       passive         .Forget
/// 13   Entered        BattleField 런타임 등장         passive+synergy 동기 완결
/// 14   AfterAttack    AttackFlow (연출 후)           passive+synergy await. defenderKilled 포함
///
/// ※ Hit(7)은 **훅이 없다.** 타임라인상 실재하는 지점(모든 피해원이 통과하는 CardInstance.TakeDamage)이라
///   어휘에는 남겨두지만, 구 CardPassive.OnHit이 구현체 0이라 제거했다. 필요해지면 여기에 되살린다.
///   찾지 마라 — 지금은 선언된 훅이 없다.
///
/// ★ Attacked가 동기 완결이어야 하는 이유: 성벽/가시 반격이 치사 래치(AttackProcessor의 t_defKilled)
///   보다 **먼저** hp에 반영돼야 한다. 여기서 await하면 래치가 반격 전 hp를 읽는다.
///
/// ★ Lethal → Removed 사이에 IsAlive 게이트가 있다. Lethal에서 부활(ReviveAtHalf)하면
///   Removed와 슬롯 제거가 통째로 스킵된다. 두 타이밍을 하나로 합칠 수 없는 이유.
///
/// ── 결정론 규약 (전 타이밍 공통) ──────────────────────────────────────────
/// - **훅에서 MatchRandom 소비 금지.** 게임 RNG 소비는 AttackProcessor.PickSplash 한 곳과
///   OrnnPassive(AfterAttack, await 경로)뿐이다. .Forget/동기 void 훅에서 뽑으면 즉시 divergence.
/// - .Forget으로 발화되는 훅은 **첫 await 전에 상태변이를 완결**해야 한다(양 클라 동형 보장).
/// - 순서는 데이터가 아니라 코드로 고정. 한 타이밍에 passive와 synergy가 둘 다 붙으면
///   디스패치 순서를 호출부에 하드코딩한다(현행: passive → synergy).
/// </summary>
public static class BattleTimings { }


// ── 타이밍별 컨텍스트 ────────────────────────────────────────────────────────
// passive와 synergy가 **같은 컨텍스트 타입**을 쓴다(어휘 통일). synergy 훅만 발동 표시용
// SynergyData를 별도 인자로 더 받는다 — 게임 규칙과 무관한 UI/오디오 태그다.

/// <summary>Placed(오프닝 배치) / Entered(런타임 등장) 공용.</summary>
public readonly struct SpawnCtx
{
    public readonly CardInstance self;
    public readonly BattleField  field;
    public SpawnCtx(CardInstance _self, BattleField _field) { this.self = _self; this.field = _field; }
}

/// <summary>TurnBegan / TurnEnded 공용.</summary>
public readonly struct TurnCtx
{
    public readonly CardInstance self;
    public readonly BattleField  field;
    public TurnCtx(CardInstance _self, BattleField _field) { this.self = _self; this.field = _field; }
}

/// <summary>BeforeAttack — self=공격자. 데미지 해결 전이라 아직 아무 수치도 확정 안 됨.</summary>
public readonly struct BeforeAttackCtx
{
    public readonly CardInstance self;
    public readonly CardInstance defender;
    public readonly BattleField  ownField;
    public readonly BattleField  enemyField;
    public BeforeAttackCtx(CardInstance _self, CardInstance _defender, BattleField _ownField, BattleField _enemyField)
    { this.self = _self; this.defender = _defender; this.ownField = _ownField; this.enemyField = _enemyField; }
}

/// <summary>Attacked — self=방어자. 직격이 이미 적용된 뒤.</summary>
public readonly struct AttackedCtx
{
    public readonly CardInstance self;
    public readonly CardInstance attacker;
    public readonly BattleField  ownField;
    public readonly BattleField  enemyField;
    public AttackedCtx(CardInstance _self, CardInstance _attacker, BattleField _ownField, BattleField _enemyField)
    { this.self = _self; this.attacker = _attacker; this.ownField = _ownField; this.enemyField = _enemyField; }
}

/// <summary>DamageDealt — self가 damage만큼 피해를 입혔다. isRetaliation=반격분.</summary>
public readonly struct DamageDealtCtx
{
    public readonly CardInstance self;
    public readonly int          damage;
    public readonly bool         isRetaliation;
    public DamageDealtCtx(CardInstance _self, int _damage, bool _isRetaliation)
    { this.self = _self; this.damage = _damage; this.isRetaliation = _isRetaliation; }
}

/// <summary>Lethal(취소 가능) / Removed(확정) 공용.</summary>
public readonly struct DeathCtx
{
    public readonly CardInstance self;
    public readonly BattleField  field;
    public DeathCtx(CardInstance _self, BattleField _field) { this.self = _self; this.field = _field; }
}

/// <summary>SwappedOut — self가 필드를 떠나고 incoming이 그 자리에 들어온다.</summary>
public readonly struct SwapOutCtx
{
    public readonly CardInstance self;
    public readonly CardInstance incoming;
    public readonly BattleField  field;
    public SwapOutCtx(CardInstance _self, CardInstance _incoming, BattleField _field)
    { this.self = _self; this.incoming = _incoming; this.field = _field; }
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
    public AfterAttackCtx(CardInstance _self, CardInstance _target, BattleField _ownField, BattleField _enemyField,
                          int _damageDealt, bool _defenderKilled)
    {
        this.self = _self; this.target = _target;
        this.ownField = _ownField; this.enemyField = _enemyField;
        this.damageDealt = _damageDealt; this.defenderKilled = _defenderKilled;
    }
}
