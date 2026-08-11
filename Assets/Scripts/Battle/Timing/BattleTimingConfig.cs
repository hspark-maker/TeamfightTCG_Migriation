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

    [Header("Attack Ranged (원거리: 제자리 발사 → 투사체 비행 → 착탄)")]
    // 비행 시간의 **바닥값**. 평소엔 카드의 AttackEffect.hitDelay가 정하지만, 그 값이 0이거나
    // AttackEffect 자체가 없는 카드(키워드만 원거리인 경우)는 비행 시간이 0이 되어
    // 투사체가 생성된 프레임에 그대로 파괴된다 — "발사체가 아예 안 나온다"의 정체.
    [SerializeField] float rangedFlightMin = 0.28f;

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
    // 사망 길이의 단일 진실원. Card_Dissolve 클립(0.767초)은 이 값에 맞춰 **배속으로 눌린다** —
    // 이 값이 클립보다 짧으면 디졸브가 그 비율만큼 빨리 감긴다. 클립 길이와 같게 두면 원본 속도.
    [SerializeField] float deathDuration     = 0.7666667f;
    // 사망 연출 내부 박자. 전부 deathDuration 안에서 끝난다 — 이 값들을 늘려 사망을 길게 만들지 말 것
    // (결정타에서 deathDuration에 finishSlow 배율이 곱해져 체감이 4배로 늘어난다).
    [SerializeField] float deathFlash        = 0.05f;  // 사망 순간 흰 플래시(디졸브 클립 쓰면 미사용)
    [SerializeField] float deathLift         = 0.12f;  // 카드가 떠오르는 데 걸리는 시간
    [SerializeField] float deathNovaAt       = 0.28f;  // 바닥 빛 파동이 터지는 시점(사망 시작 기준)
    [SerializeField] float dealAnimDuration  = 0.6f;
    [SerializeField] float dealMidPause      = 0.5f;   // 거래 애니 중간 정지
    [SerializeField] float deathPreviewFlash = 0.55f;  // 죽음 미리보기 점멸

    [Header("Card View")]
    [SerializeField] float longPress          = 0.45f;
    [SerializeField] float fadeViewDuration   = 0.3f;
    [SerializeField] float attackPreviewFlash = 0.55f;
    [SerializeField] float keywordGlowHold    = 1.5f;

    [Header("HP 표기 변동 (아이콘 팝 → 숫자 굴림 → 복귀). 피격·회복 공용")]
    [SerializeField] float hpPopDuration = 0.1f;    // 아이콘이 커지는/작아지는 각 1회 시간
    [SerializeField] float hpRollPerStep = 0.05f;   // 체력 1칸당 굴림 시간(6→3이면 3칸 = 0.15초)
    [SerializeField] float hpRollMax     = 0.45f;   // 큰 피해에도 굴림이 늘어지지 않게 하는 상한

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

    [Header("Approach (매치포인트 공격 과정)")]
    // 결과 미확정 구간이므로 전역 timeScale 대신 이 공격의 이동 시간에만 배율을 적용한다.
    [SerializeField, Range(0.2f, 1f)] float approachSlow = 0.65f;
    [SerializeField] float approachFocusIn  = 0.22f;
    [SerializeField] float approachFocusOut = 0.20f;
    // 결정타에서만 쓰는 돌진 도달 비율. 평소(atkLungeT 0.62)는 방어자 앞에서 멈추는데, 접근 연출은
    // 느리고 카메라까지 붙어서 그 빈틈이 그대로 보인다 — "안 닿았는데 맞았다"가 된다.
    // 시간이 아니라 거리라 접근 배율(SpeedFactor·ApproachDurationFactor)과 무관한 raw 값이다.
    [SerializeField, Range(0f, 1f)] float approachLungeT = 0.88f;

    [Header("Finisher (승부를 가른 타격 강조 — 결과 확정보다 앞선다)")]
    // 여기 시간은 **승패에 관계없이 동일**하다. 멀티는 한쪽이 승리·다른 쪽이 패배인데 길이가 갈리면
    // 다음 Ready 동기 시점이 어긋난다 — 다르게 가져갈 것은 길이가 아니라 색감·깊이다.
    // Result Beat과 같은 이유로 전역 배속(SpeedFactor) 미적용 raw다.
    [SerializeField] float finishHitStop = 0.11f;   // 타격이 터진 직후 완전히 얼어붙는 시간
    [SerializeField] float finishIn      = 0.06f;   // 슬로우·줌으로 빨려 들어가는 시간
    // 유지 구간의 **본편은 사망 연출**이다(느려진 채로 재생된다). 이 값은 그 뒤에 남기는 여운이라
    // 카드가 사라진 빈 클로즈업을 붙잡는 시간이기도 하다 — 길이를 늘릴 땐 여기보다 finishSlow를 먼저 본다.
    [SerializeField] float finishHold    = 0.22f;
    [SerializeField] float finishOut     = 0.22f;   // 배속 복귀 + 카메라 XY 복귀 시간
    // 첫 박(얼어붙기+진입)에 확 붙은 뒤, 사망 연출이 도는 내내 **천천히 더 다가가는** 시간.
    // 여기가 0이면 카메라가 한 번 튀고 멈춰 서서, 정작 죽는 그림 위에서는 움직이지 않는다.
    [SerializeField] float finishCreep   = 1.4f;
    // 사망 연출(약 0.4초)이 이 배속으로 늘어난다 — 0.25면 약 1.6초. 피니시 길이를 조절하는 **주 레버**다.
    // 더 내리면 죽는 그림이 늘어져 "느리다"가 아니라 "멈췄다"로 읽히기 시작한다.
    [SerializeField, Range(0.05f, 1f)] float finishSlow     = 0.25f;
    [SerializeField, Range(0.5f, 1f)]  float finishBgmPitch = 0.82f;
    // 배경 블러는 여기 없다 — 결정타 구간은 **선명해야** 무슨 일이 벌어졌는지 읽힌다.
    // 흐림은 "보드에서 팝업으로 넘어가는" 장치라 Result Beat 쪽이 소유한다.

    [Header("Result Beat (승패 확정 → 결과 팝업 사이의 여운)")]
    // 이 구간의 시간값만은 **배속을 먹지 않는다**(raw 노출). 여기는 전투 연출이 아니라 결과 표시의 리듬이고,
    // 배속을 5로 올려 빠르게 돌리는 사람에게도 승패 확정은 똑같이 한 박자 쉬어야 읽힌다.
    [SerializeField] float resultBeatIn   = 0.08f;   // 슬로우로 빨려 들어가는 시간
    [SerializeField] float resultBeatHold = 0.20f;   // 가장 느린 상태로 머무는 시간
    [SerializeField] float resultBeatOut  = 0.12f;   // 정상 속도로 돌아오는 시간
    [SerializeField, Range(0.05f, 1f)] float resultBeatSlow     = 0.25f;  // 가장 느릴 때의 Time.timeScale(1 = 슬로우 없음)
    [SerializeField, Range(0f, 1f)]    float resultBeatBlur     = 0.45f;  // 여운 동안 차오르는 배경 블러 강도
    [SerializeField, Range(0.5f, 1f)]  float resultBeatBgmPitch = 0.88f;  // 여운 동안 BGM이 끌리는 정도(1 = 그대로)
    // 패배는 승리보다 약하게 — 깊이(슬로우·블러·줌)와 머무는 시간에 함께 곱한다.
    [SerializeField, Range(0f, 1f)]    float resultBeatLoseRatio = 0.7f;
    // 피니시가 이미 돌았으면 여운은 통째로 접고 이만큼만 쉬었다 팝업을 연다 —
    // 슬로우를 두 번 먹이면 "결정타"가 흐려지고 결과까지 늘어진다(블러·클로즈업은 이미 올라와 있다).
    [SerializeField] float resultBeatAfterFinish = 0.22f;

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
    public float DeathFlash         => deathFlash         * SpeedFactor;
    public float DeathLift          => deathLift          * SpeedFactor;
    public float DeathNovaAt        => deathNovaAt        * SpeedFactor;
    public float DealAnimDuration   => dealAnimDuration   * SpeedFactor;
    public float DealMidPause       => dealMidPause       * SpeedFactor;
    public float DeathPreviewFlash  => deathPreviewFlash  * SpeedFactor;
    // 사용자 입력 임계시간은 전투 연출 배속과 무관해야 한다.
    public float LongPress          => longPress;
    public float FadeViewDuration   => fadeViewDuration   * SpeedFactor;
    public float AttackPreviewFlash => attackPreviewFlash * SpeedFactor;
    public float KeywordGlowHold    => keywordGlowHold    * SpeedFactor;
    public float HpPopDuration      => hpPopDuration      * SpeedFactor;
    public float HpRollPerStep      => hpRollPerStep      * SpeedFactor;
    public float HpRollMax          => hpRollMax          * SpeedFactor;

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
    public float RangedFlightMin     => Mathf.Max(0f, rangedFlightMin) * SpeedFactor;
    public float CunningFogLead      => cunFogLead          * SpeedFactor;
    public float CunningSpinDuration => cunSpinDur          * SpeedFactor;
    public float CunningExitDuration => cunExitDur          * SpeedFactor;
    // 배속은 CunningExitDuration에서 이미 걸리므로 비율은 raw로 노출한다(두 번 곱하면 축소가 사라진다).
    public float CunningShrinkRatio  => cunShrinkRatio;
    public float MulliganNoticeHold  => mulliganNoticeHold  * SpeedFactor;
    // 여운은 배속 미적용 raw 노출 — 이유는 위 Header 주석 참조.
    public float ResultBeatIn        => Mathf.Max(0f, resultBeatIn);
    public float ResultBeatHold      => Mathf.Max(0f, resultBeatHold);
    public float ResultBeatOut       => Mathf.Max(0f, resultBeatOut);
    public float ResultBeatSlow      => resultBeatSlow;
    public float ResultBeatBlur      => resultBeatBlur;
    public float ResultBeatBgmPitch  => resultBeatBgmPitch;
    public float ResultBeatLoseRatio => resultBeatLoseRatio;
    public float ResultBeatAfterFinish => Mathf.Max(0f, resultBeatAfterFinish);
    public float ApproachDurationFactor => 1f / Mathf.Clamp(approachSlow, 0.2f, 1f);
    public float ApproachFocusIn        => Mathf.Max(0f, approachFocusIn);
    public float ApproachFocusOut       => Mathf.Max(0f, approachFocusOut);
    public float ApproachLungeT         => Mathf.Clamp01(approachLungeT);
    public float FinishHitStop      => Mathf.Max(0f, finishHitStop);
    public float FinishIn           => Mathf.Max(0f, finishIn);
    public float FinishHold         => Mathf.Max(0f, finishHold);
    public float FinishOut          => Mathf.Max(0f, finishOut);
    public float FinishCreep        => Mathf.Max(0f, finishCreep);
    public float FinishSlow         => finishSlow;
    public float FinishBgmPitch     => finishBgmPitch;
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
