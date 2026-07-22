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

    [Header("Turn Pacing (AI 사고/상대 턴 지연 — 연출과 개념축 다름)")]
    [SerializeField] float enemyTurnStartDelay     = 0.8f;
    [SerializeField] float enemyExtraAttackDelay   = 0.4f;
    [SerializeField] float opponentTurnStartDelay  = 0.5f;
    [SerializeField] float opponentExtraAttackDelay = 0.4f;

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
    public float EnemyTurnStartDelay      => enemyTurnStartDelay      * SpeedFactor;
    public float EnemyExtraAttackDelay    => enemyExtraAttackDelay    * SpeedFactor;
    public float OpponentTurnStartDelay   => opponentTurnStartDelay   * SpeedFactor;
    public float OpponentExtraAttackDelay => opponentExtraAttackDelay * SpeedFactor;
}
