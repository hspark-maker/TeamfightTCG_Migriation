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

    [Header("Attack Peerless (무쌍: 대상 앞으로 → 베기 → 광역 대상 쪽으로 회전 → 베기 → 복귀)")]
    // 박치기와 같은 규약 — 시간 4개만 배속을 먹고, 비율·각도는 배속과 무관하다.
    [SerializeField] float prlApproachDur = 0.22f;   // 주 대상 앞까지 가는 시간
    [SerializeField] float prlTurnDur     = 0.16f;   // 광역 대상 쪽으로 도는 시간
    [SerializeField] float prlReturnDur   = 0.26f;   // 슬롯 복귀 시간
    [SerializeField] float prlHitStop     = 0.1f;    // 벨 때마다 멈칫하는 시간(데미지 표시 직전)
    [SerializeField] float prlAfterHitHold = 0.12f;  // 한 대 때린 뒤 다음 동작까지 머무는 시간(타격 여운)
    [Range(0f, 1f)]  [SerializeField] float prlApproachT = 0.6f;   // 주 대상까지 파고드는 비율(1=완전겹침)
    [Range(0f, 90f)] [SerializeField] float prlMaxTurn   = 70f;    // 최대 회전각(도). 넘기면 카드가 드러눕는다
    [SerializeField] float prlSwingFront = 0.6f;   // 휘두름 이펙트가 공격자 앞으로 나가는 거리(월드)
    [SerializeField] float prlTurnSideStep = 0.5f; // 마무리로 광역 대상 쪽으로 더 미끄러지는 거리(월드)
    [Range(0f, 60f)] [SerializeField] float prlWindupAngle = 25f;  // 베기 전 반대쪽으로 더 트는 각(도)
    [Range(0f, 90f)] [SerializeField] float prlSlashMaxTurn = 30f; // 베기 자국이 수평에서 기울 수 있는 최대 각(도)

    [Header("Cunning Exit (교활: 안개 → 한 바퀴 돌며 뒷면 → 덱으로)")]
    [SerializeField] float cunFogLead = 0.15f;   // 안개가 먼저 깔리는 시간
    [SerializeField] float cunSpinDur = 0.45f;   // 한 바퀴 도는 시간(중간에 뒷면으로 바뀐다)
    [SerializeField] float cunExitDur = 0.35f;   // 덱 쪽으로 빨려 들어가는 시간
    // 축소는 이동보다 **먼저 끝난다**. 같은 길이로 두면 끝까지 커다란 카드가 미끄러지다 사라져
    // "빨려 들어간다"가 아니라 "화면 밖으로 나간다"로 보인다. 이동 시간 대비 비율.
    [Range(0.1f, 1f)] [SerializeField] float cunShrinkRatio = 0.5f;

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

    [Header("Swarm Vfx (무리 선피해 연출 — 프리팹/형태값은 BattleVfxLibrary)")]
    [SerializeField] float swarmLaunchStagger  = 0.07f;   // 무리 카드별 발사 간격(한 프레임에 겹치면 한 덩어리로 보인다)
    [SerializeField] float swarmTravelDuration = 0.26f;   // 투사체 비행 시간. 본 공격 앞에 붙는 시간이라 짧게
    [SerializeField] float swarmImpactHold     = 0.10f;   // 마지막 착탄 후 본 공격까지의 여운

    // 시너지 엠블럼 길이는 여기 없다 — 몸짓/시너지마다 달라서 그 시너지의 연출 에셋
    // (SynergyEmblemSpec.duration, raw 초)이 쥔다. 배속은 Scaled()를 통과해 적용된다.

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

    /// <summary>다른 SO가 들고 있는 raw 초 값에 배속을 먹인다(키워드별 글로우 hold 등).
    /// SpeedFactor 자체는 계속 비공개 — 곱하는 지점이 코드 여기저기로 흩어지면
    /// "배속이 안 먹는 연출"이 조용히 생긴다. 외부 시간값은 반드시 이 출구를 통과할 것.</summary>
    public float Scaled(float _seconds) => _seconds * SpeedFactor;
    public float HealLaunchLead      => healLaunchLead      * SpeedFactor;
    public float HealLaunchStagger   => healLaunchStagger   * SpeedFactor;
    public float HealTravelDuration  => healTravelDuration  * SpeedFactor;
    public float HealTrailLinger     => healTrailLinger     * SpeedFactor;
    public float SwarmLaunchStagger  => swarmLaunchStagger  * SpeedFactor;
    public float SwarmTravelDuration => swarmTravelDuration * SpeedFactor;
    public float SwarmImpactHold     => swarmImpactHold     * SpeedFactor;
    public float CunningFogLead      => cunFogLead          * SpeedFactor;
    public float CunningSpinDuration => cunSpinDur          * SpeedFactor;
    public float CunningExitDuration => cunExitDur          * SpeedFactor;
    // 배속은 CunningExitDuration에서 이미 걸리므로 비율은 raw로 노출한다(두 번 곱하면 축소가 사라진다).
    public float CunningShrinkRatio  => cunShrinkRatio;
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

    /// <summary>무쌍 연출 튜닝 한 벌. NormalAttack과 같은 규약 — 시간 항목만 배속이 적용된 상태로 나간다.
    /// AttackSequence가 공격 시작 시 한 번 읽어 스냅샷으로 쓴다.</summary>
    public AttackSequence.PeerlessTuning PeerlessAttack => new AttackSequence.PeerlessTuning
    {
        approachDur = prlApproachDur * SpeedFactor,
        turnDur     = prlTurnDur     * SpeedFactor,
        returnDur   = prlReturnDur   * SpeedFactor,
        hitStop      = prlHitStop      * SpeedFactor,
        afterHitHold = prlAfterHitHold * SpeedFactor,
        approachT   = prlApproachT,
        maxTurn     = prlMaxTurn,
        swingFront   = prlSwingFront,
        turnSideStep = prlTurnSideStep,
        windupAngle   = prlWindupAngle,
        slashMaxTurn  = prlSlashMaxTurn,
    };
}
