using UnityEngine;

/// <summary>
/// 전투 연출 공통 타이밍 단일 진실원. 카드 무관 공통 기본값.
/// (카드별 오버라이드는 AttackEffect SO가 담당 — 축이 다름.)
/// raw 값은 private, 노출은 GameTiming.Factor 적용된 프로퍼티만 → 배율 우회 차단.
/// </summary>
[CreateAssetMenu(fileName = "BattleTimingConfig", menuName = "Card Battle/Battle Timing Config")]
public class BattleTimingConfig : ScriptableObject
{
    [Header("전역 배수 (2 = 2배 빠름 / 0.5 = 절반 속도)")]
    [SerializeField, Range(0.2f, 5f)] float globalSpeed = 1f;

    /// <summary>인스펙터 globalSpeed + 런타임 GameTiming.Speed 합성 계수. 지속시간에 곱함.</summary>
    float SpeedFactor => GameTiming.Factor / Mathf.Clamp(globalSpeed, 0.2f, 5f);

    [Header("Cinema / Attack")]
    [SerializeField] float cinemaDuration = 0.25f;   // 시네마 진입/카드 Z 이동

    [Header("Attack Headbutt (일반 공격: 뒤로 → 박치기 → 반동 → 복귀)")]
    // 시간 4개만 배속(SpeedFactor)을 먹는다. 거리·비율·각도는 시간이 아니라 배속과 무관해야 한다
    // — 여기에 배율을 곱하면 빠르게 돌릴 때 이동 거리까지 줄어 연출이 뭉개진다.
    [SerializeField] float atkWindDur    = 0.07f;   // 윈드업(뒤로 살짝) 시간
    [SerializeField] float atkWindDist   = 0.22f;   // 윈드업 거리(적 반대 방향)
    [SerializeField] float atkInDur      = 0.09f;   // 돌진(접촉까지) 시간
    [SerializeField] float atkRecoilDur  = 0.09f;   // 접촉 후 반동 시간
    [SerializeField] float atkRecoilDist = 0.35f;   // 반동 거리
    [SerializeField] float atkOutDur     = 0.16f;   // 반동 → 슬롯 복귀 시간
    [Range(0f, 1f)]  [SerializeField] float atkLungeT  = 0.62f;   // 방어자까지 이동 비율(1=완전겹침)
    [Range(0f, 80f)] [SerializeField] float atkMaxLean = 40f;     // 적 방향 최대 lean 각(도)

    [Header("Intro")]
    [SerializeField] float cameraIntroDuration = 0.8f;
    [SerializeField] float cardDealDelay       = 0.15f;
    [SerializeField] float cardDealDuration     = 0.6f;
    [SerializeField] float deckDealDelay        = 0.12f;
    [SerializeField] float deckDealDuration     = 0.35f;

    [Header("Card Animator")]
    [SerializeField] float cardMoveDuration  = 0.3f;
    [SerializeField] float hitDuration       = 0.15f;
    [SerializeField] float deathDuration     = 0.4f;
    [SerializeField] float dealAnimDuration  = 0.6f;
    [SerializeField] float dealMidPause      = 0.5f;   // 거래 애니 중간 정지
    [SerializeField] float deathPreviewFlash = 0.55f;  // 죽음 미리보기 점멸

    [Header("Card View")]
    [SerializeField] float longPress          = 0.45f;
    [SerializeField] float fadeViewDuration   = 0.3f;
    [SerializeField] float attackPreviewFlash = 0.55f;
    [SerializeField] float keywordGlowHold    = 1.5f;

    [Header("Heal Vfx (힐러 회복 연출 — 프리팹/형태값은 BattleVfxLibrary)")]
    [SerializeField] float healLaunchLead     = 0.15f;   // 발동 이펙트 → 첫 투사체 사이 간격
    [SerializeField] float healLaunchStagger  = 0.06f;   // 대상별 발사 간격
    [SerializeField] float healTravelDuration = 0.45f;   // 투사체 비행 시간
    [SerializeField] float healTrailLinger    = 0.25f;   // 도착 후 트레일 잔상 유지

    [Header("Mulligan")]
    [SerializeField] float mulliganNoticeHold = 1.2f;   // "상대가 교환 중" 안내를 띄워두는 시간

    [Header("Effect Notify (우측 컷인 배너)")]
    [SerializeField] float effectNotifyDisplay = 1.8f;   // 배너가 떠 있는 유지 시간
    [SerializeField] float effectNotifySlide   = 0.25f;  // 슬라이드 인/아웃 각 1회 시간

    [Header("Turn Pacing (AI 사고/상대 턴 지연 — 연출과 개념축 다름)")]
    [SerializeField] float enemyTurnStartDelay     = 0.8f;
    [SerializeField] float enemyExtraAttackDelay   = 0.4f;
    [SerializeField] float opponentTurnStartDelay  = 0.5f;
    [SerializeField] float opponentExtraAttackDelay = 0.4f;
    // 내 턴 생각시간(초). 초과 시 자동 공격. 배속과 무관해야 공정 → SpeedFactor 미적용(raw 그대로).
    [SerializeField] float turnThinkTime           = 30f;

    // ── 배율 적용 노출 (초 단위) ─────────────────────────────
    public float CinemaDuration     => cinemaDuration     * SpeedFactor;
    public float CameraIntroDuration => cameraIntroDuration * SpeedFactor;
    public float CardDealDelay      => cardDealDelay      * SpeedFactor;
    public float CardDealDuration   => cardDealDuration   * SpeedFactor;
    public float DeckDealDelay      => deckDealDelay      * SpeedFactor;
    public float DeckDealDuration   => deckDealDuration   * SpeedFactor;
    public float CardMoveDuration   => cardMoveDuration   * SpeedFactor;
    public float HitDuration        => hitDuration        * SpeedFactor;
    public float DeathDuration      => deathDuration      * SpeedFactor;
    public float DealAnimDuration   => dealAnimDuration   * SpeedFactor;
    public float DealMidPause       => dealMidPause       * SpeedFactor;
    public float DeathPreviewFlash  => deathPreviewFlash  * SpeedFactor;
    public float LongPress          => longPress          * SpeedFactor;
    public float FadeViewDuration   => fadeViewDuration   * SpeedFactor;
    public float AttackPreviewFlash => attackPreviewFlash * SpeedFactor;
    public float KeywordGlowHold    => keywordGlowHold    * SpeedFactor;
    public float HealLaunchLead      => healLaunchLead      * SpeedFactor;
    public float HealLaunchStagger   => healLaunchStagger   * SpeedFactor;
    public float HealTravelDuration  => healTravelDuration  * SpeedFactor;
    public float HealTrailLinger     => healTrailLinger     * SpeedFactor;
    public float MulliganNoticeHold  => mulliganNoticeHold  * SpeedFactor;
    public float EffectNotifyDisplay => effectNotifyDisplay * SpeedFactor;
    public float EffectNotifySlide   => effectNotifySlide   * SpeedFactor;
    public float EnemyTurnStartDelay      => enemyTurnStartDelay      * SpeedFactor;
    public float EnemyExtraAttackDelay    => enemyExtraAttackDelay    * SpeedFactor;
    public float OpponentTurnStartDelay   => opponentTurnStartDelay   * SpeedFactor;
    public float OpponentExtraAttackDelay => opponentExtraAttackDelay * SpeedFactor;
    // 생각시간만은 배율 미적용 raw 노출 (배속 켜도 안 줄어듦 = 공정성).
    public float TurnThinkTime            => turnThinkTime;

    /// <summary>일반 공격(박치기) 연출 튜닝 한 벌. 시간 항목만 배속이 적용된 상태로 나간다.
    /// AttackSequence가 공격 시작 시 한 번 읽어 스냅샷으로 쓴다.</summary>
    public AttackSequence.NormalTuning NormalAttack => new AttackSequence.NormalTuning
    {
        windDur    = atkWindDur   * SpeedFactor,
        inDur      = atkInDur     * SpeedFactor,
        recoilDur  = atkRecoilDur * SpeedFactor,
        outDur     = atkOutDur    * SpeedFactor,
        windDist   = atkWindDist,
        recoilDist = atkRecoilDist,
        lungeT     = atkLungeT,
        maxLean    = atkMaxLean,
    };
}
