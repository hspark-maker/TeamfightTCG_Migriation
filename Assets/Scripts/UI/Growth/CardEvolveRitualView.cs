using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 카드 진화 한 번의 연출(CardDetailOverlay 루트에 부착).
// 재탄생이다 — 카드가 떠오르며 금빛을 머금고(충전), 백열이 카드를 통째로 삼킨 뒤(정적),
// 그 빛이 천천히 물러나며 새 모습이 드러난다(공개).
//
// 담금질(CardEnhanceRitualView)과 **같은 빛의 언어**를 쓴다 — 같은 화면에서 연달아 누르는 두 조작이라
// 서로 다른 문법이면 같은 시스템으로 안 읽힌다. 갈라지는 것은 색과 어투뿐이다:
//  · 카드가 떠오르며 떤다(담금질은 움츠러들며 떤다) — 진화는 시달리는 것이 아니라 차오르는 것이다.
//    두 떨림은 결이 다르다: 담금질은 세 마디로 툭툭 끊어 흔들리고(두들겨 맞는 것),
//    진화는 마디 없이 진폭이 연속으로 커지다 백열이 삼키며 멎는다(안에서 차오르는 압력).
//  · 잉걸이 아니라 금빛에서 출발한다(색은 shading의 저작값이 정한다).
//  · 정점에 오래 머물고 빛이 천천히 물러난다. 담금질이 "터진다"면 이쪽은 "드러난다".
//  · 실패의 얼굴이 없다 — 진화 레벨의 성공률은 1이다.
public class CardEvolveRitualView : CardGrowthRitualView
{
    [Header("무대 (미배선이면 연출 없이 콜백만 즉시 흘린다)")]
    [Tooltip("⚠ LayoutGroup에 구동되지 않는 노드여야 한다 — 매 프레임 좌표가 되돌려지면 부양이 보이지 않는다.")]
    [SerializeField] RectTransform cardStage;                                   // 부양·진동·확대를 받는 노드(CardSlot)
    [Tooltip("cardStage 자신의 AspectRatioFitter(FitInParent). 연출 동안만 재운다 —\n" +
             "  이 모드는 레이아웃 리빌드마다 anchoredPosition을 0으로 못 박아 부양·진동을 지운다.\n" +
             "  미배선이면 그냥 그 위험을 안고 돈다(리빌드가 안 끼면 보이지 않는다).")]
    [SerializeField] AspectRatioFitter stageFitter;
    [SerializeField] RetractingPanels retractPanels = new RetractingPanels();   // 연출 동안 사라졌다 돌아올 패널들

    [Header("연출 레이어")]
    [Tooltip("담금질과 같은 재질 축을 쓴다(사본은 각자 만든다). 색만 금빛으로 저작할 것.")]
    [SerializeField] CardEnhanceShading shading = new CardEnhanceShading();     // 카드 표면이 내는 빛
    [SerializeField] CardEnhanceHalo    halo    = new CardEnhanceHalo();        // 카드 뒤에서 밀려나오는 빛
    [SerializeField] CardEvolveRays     rays    = new CardEvolveRays();         // 충전 동안 하나 둘 켜지는 빛줄기
    [SerializeField] ScreenDimTint      dimTint = new ScreenDimTint();          // 화면 딤의 밝기

    [Tooltip("진화로 새로 열리는 프레임 문양. 대상은 호출부가 Play 직전에 넘긴다(카드마다 다르다) — 여기 배선할 것이 없다.")]
    [SerializeField] CardEvolveEmblems  emblems = new CardEvolveEmblems();      // 숨 박자에 새겨지는 문양

    [Header("과열 — 빛이 카드를 삼킨다")]
    [Range(0f, 1f)] [SerializeField] float blindPeak     = 1f;                  // 백열의 최대 세기. 1이면 흰 실루엣만 남는다
    [Range(0f, 1f)] [SerializeField] float overheatStart = 0.45f;               // 충전의 어디서부터 면이 덮이나. 맥동 구간(~58%)을 비워 둬야 그 셋이 보인다
    [Range(0f, 1f)] [SerializeField] float overheatRise  = 0.5f;                // 충전이 끝나는 시점의 잠식률. 1이면 삼켜지는 과정이 안 보인다

    [Header("재탄생 섬광 (선택)")]
    [Tooltip("빛이 물러나기 시작하는 프레임에 쏜다. 담금질보다 세게 — 진화가 더 큰 사건이라는 것을 화면이 말한다.")]
    [SerializeField] bool             useScreenFlash = true;
    [SerializeField] ScreenFlashCover rebirthFlash   = new ScreenFlashCover
    {
        rise = 0.05f, hold = 0.04f, fall = 0.45f, peak = 0.7f,
        color = new Color(1f, 0.95f, 0.82f, 1f),
    };

    [Header("표면을 훑는 빛 — 두 줄, 순차")]
    [Tooltip("⚠ 두 줄기는 겹칠 수 없다 — 판(GleamCover)이 한 장이고 축도 하나라, 겹치면 뒷줄기가 앞줄기의 중간 위치에서 출발한다.")]
    [SerializeField] float gleamDelay     = 0.18f;                              // 빛이 걷히고 얼마나 뒤에 훑나. 겹치면 백열에 묻힌다
    [SerializeField] float gleamSweep     = 0.22f;                              // 빠른 줄기 — 갓 태어난 것을 스친다
    [SerializeField] float gleamGap       = 0.06f;                              // 두 줄기 사이의 빈 자리. 0이면 한 줄로 뭉쳐 읽힌다
    [SerializeField] float gleamSweepSlow = 0.5f;                               // 느린 줄기 — 표면을 닦아 낸다

    [Header("진동 — 차오르는 것을 못 견딘다")]
    [Tooltip("담금질의 세 마디 진동과 갈리는 지점이다 — 이쪽은 마디가 없다.\n" +
             "  진폭이 0에서 연속으로 커지다 백열에 삼켜지며 멎는다. 마디를 넣으면 두 연출이 같은 것이 된다.")]
    [Range(0f, 1f)] [SerializeField] float tremorStart   = 0.85f;               // 충전의 어디서 떨기 시작하나(줄기 셋째가 켜지는 자리)
    [SerializeField] float tremorAmp     = 7f;                                  // 정점 진폭(px). 담금질(9)보다 작아야 '두들겨 맞는 것'이 안 된다
    [SerializeField] float tremorHz      = 26f;                                 // 떨림의 잦기. 높을수록 '견디는 중'으로 읽힌다
    [Range(0f, 1f)] [SerializeField] float tremorPeak    = 0.35f;               // 백열의 어디서 정점을 찍나. 충전이 아니라 여기까지 자라야 blaze로 이어진다
    [Range(0f, 1f)] [SerializeField] float tremorSurface = 0.6f;                // 함께 도는 표면 픽셀 진동의 세기(shading.Shake)

    [Header("빛줄기 박자")]
    [SerializeField] float rayIgniteDur = 0.26f;                                // 줄기 하나가 뻗어 나오는 시간
    [SerializeField] float raySpinDeg   = 14f;                                  // 백열~공개를 관통하며 도는 각도. hold 안에서 끝나면 백지라 아무도 못 본다

    [Header("박자")]
    [SerializeField] float enterDuration  = 0.18f;
    [SerializeField] float chargeDuration = 1.40f;                              // 금빛이 차오르는 동안. 맥동 3회와 줄기 3회가 이 안에서 층을 이룬다
    [SerializeField] float blazeDuration  = 0.40f;                              // 백열이 카드를 마저 삼키는 동안
    [SerializeField] float holdDuration   = 0.45f;                              // 완전히 덮인 채 머무는 한 박. 이 정적이 진화의 무게다(담금질은 0.05다)
    [SerializeField] float revealDuration = 0.75f;                              // 빛이 물러나며 새 모습이 드러나는 시간. 담금질의 회수(0.4)보다 길어야 '드러난다'가 된다
    [SerializeField] float resultHold     = 0.50f;
    [SerializeField] float returnDuration = 0.35f;

    [Header("몸짓 — 카드는 떨지 않고 떠오른다")]
    [SerializeField] float enterScale   = 1.02f;                                // 진입의 들숨. 1보다 **커야** 담금질의 움츠림과 갈린다
    [SerializeField] float liftDistance = 22f;                                  // 충전 동안 떠오르는 높이(px). 줄기가 켜질 때마다 세 뼘에 나눠 오른다
    [SerializeField] float blazeScale   = 1.06f;                                // 삼켜지는 동안의 완만한 확대
    [Tooltip("정적(hold) 후반에 빛이 부푸는 최대 크기. 공개는 여기서 **이어받아** 내려앉는다 —\n" +
             "  이 크기를 공개 첫 프레임에 대입하면 카드가 팝하듯 튀어나온다(그것이 '픽 등장'의 정체였다).")]
    [SerializeField] float burstScale   = 1.40f;
    [SerializeField] float burstSettle  = 0.60f;                                // 내려앉는 시간. 빛이 물러나는 속도와 같아야 '빛이 카드가 되었다'로 읽힌다
    [Tooltip("부푸는 동안 함께 기울어지는 각도(도). 공개 구간에서 0으로 풀리며 '자리를 잡는다'.")]
    [SerializeField] float burstTilt    = -3f;
    [Tooltip("착지가 제 크기 밑으로 지나치는 깊이. 1이면 언더슛 없이 그냥 멎는다 — 무게는 이 한 뼘에서 나온다.")]
    [Range(0.9f, 1f)] [SerializeField] float settleUndershoot = 0.985f;

    [Header("숨 — 태어난 것이 한 번 크게 뛴다")]
    [Tooltip("착지가 끝나고 이만큼 뒤에 뛴다. 0이면 착지에 묻혀 두 사건이 하나로 읽힌다.")]
    [SerializeField] float beatDelay = 0.08f;
    [SerializeField] float beatScale = 1.05f;                                   // 부푸는 크기. 착지의 언더슛과 대칭이라 크면 다시 폭발이 된다
    [SerializeField] float beatRise  = 0.12f;                                   // 차오르는 시간(짧을수록 심박에 가깝다)
    [SerializeField] float beatFall  = 0.24f;                                   // 가라앉는 시간. 오름보다 길어야 '뛰었다'가 된다
    [Range(0f, 1f)] [SerializeField] float beatHaloAlpha = 0.62f;               // 박동 정점의 후광 짙기(잔광 alpha에서 여기까지 뛴다)
    [SerializeField] float beatHaloScale = 1.34f;                               // 박동 정점의 후광 크기. 카드 밖으로 밀려나야 파동으로 읽힌다
    [Range(0f, 1f)] [SerializeField] float beatDim = 0.12f;                     // 박동이 배경까지 밝히는 폭(결과 밝기 기준 가산)

    [Header("빛")]
    [Range(0f, 1f)] [SerializeField] float haloPeakAlpha  = 0.9f;
    [SerializeField] float haloPeakScale  = 1.25f;                              // 충전 정점의 후광 크기. 카드보다 커야 뒤에서 새어 나오는 것으로 읽힌다
    [Range(0f, 1f)] [SerializeField] float afterglowHeat  = 0.35f;              // 공개 뒤 카드에 남는 열 — 갓 태어난 것의 온기
    [Range(0f, 1f)] [SerializeField] float afterglowAlpha = 0.3f;
    [SerializeField] float afterglowScale = 1.12f;

    [Header("밝기 (-1 어둠 ~ +1 빛)")]
    [Range(-1f, 1f)] [SerializeField] float enterDim     = -0.55f;
    [Range(-1f, 1f)] [SerializeField] float chargeDipDim = -0.78f;              // 차오르기 전에 한 번 더 내려앉는 자리. 배경이 죽어야 카드가 머금은 빛이 보인다
    [Range(-1f, 1f)] [SerializeField] float chargeDim    = 0.05f;               // 충전 끝의 밝기. 담금질(0.6)보다 낮게 — 진화만 마지막까지 어둠을 쥔다
    [Range(-1f, 1f)] [SerializeField] float peakDim      = 1f;                  // 정점. 배경까지 빛이 찬다(담금질과 같은 자리)
    [Range(-1f, 1f)] [SerializeField] float resultDim    = -0.45f;              // 결과를 읽는 동안의 밝기. 담금질과 같은 값이라 결과판 글자가 같게 읽힌다

    // 덮인 것을 못 박는 짧은 트윈. 앞 구간이 잘려 덜 덮인 채 도착했더라도 값이 바뀌는 프레임엔 완전히 덮여 있어야 한다.
    const float BlindRise = 0.05f;

    // 삼켜지는 동안 후광이 카드 밖으로 번지는 크기. 실루엣에 딱 맞으면 빛이 판때기처럼 잘려 보인다.
    const float GlowFloodScale = 1.25f;

    // 공개 뒤 숨 한 번이 결과 구간 안에 들어가도록 확보하는 여유. 없으면 결과판이 뜬 뒤에도 후광이 뛴다.
    const float BreathRoom = 0.2f;

    // 덮개가 물러나는 데 쓰는 공개 구간의 비율. 숨은 그 뒤에 와야 하므로 BeatAt이 같은 값을 본다.
    const float VeilRetreatSpan = 0.85f;

    // 박동 정점에서 표면 열이 잔광 대비 부푸는 배율.
    const float BeatHeatBoost = 1.7f;

    // 숨에 얹히는 축(후광·표면 열·딤)이 그 전에 도착하도록 두는 틈.
    const float LeadGap = 0.02f;

    // 줄기 점화 시각(충전 대비). 간격이 좁아지는 것이 고조의 본체다 — 등간격이면 사건이 아니라 배경이 된다.
    static readonly float[] RayCues = { 0.55f, 0.72f, 0.85f };

    // 줄기가 켜질 때마다 카드가 오르는 높이(liftDistance 대비). RayCues와 같은 길이여야 한다.
    static readonly float[] LiftSteps = { 0.35f, 0.7f, 1f };

    // 충전의 맥동. 마디끼리 겹치지 않는다 — TweenHeat의 getter는 시작할 때 한 번 읽히므로,
    // 겹치면 뒷마디가 앞마디의 중간값에서 출발해 파형이 무너진다.
    static readonly Beat[] ChargeBeats =
    {
        new Beat(0.000f, 0.071f, 0.30f, Ease.OutQuad),      // 첫 숨
        new Beat(0.071f, 0.115f, 0.14f, Ease.InOutSine),
        new Beat(0.186f, 0.071f, 0.58f, Ease.OutQuad),      // 둘째 — 더 높이
        new Beat(0.257f, 0.129f, 0.30f, Ease.InOutSine),
        new Beat(0.386f, 0.071f, 0.86f, Ease.OutQuad),      // 셋째 — 거의 정점까지
        new Beat(0.457f, 0.121f, 0.52f, Ease.InOutSine),
        new Beat(0.578f, 0.422f, 1.00f, Ease.InQuad),       // 마지막은 맥동이 아니라 램프. 여기서 잠식이 시작된다
    };

    Vector2 m_baseAnchored;   // cardStage의 authoring 자리. 중간값을 기준으로 잡으면 반복할수록 밀린다
    bool    m_baseCaptured;

    // 무대의 자리는 부양과 진동이 함께 정한다. 둘이 각자 anchoredPosition을 밀면 나중 트윈이 앞 트윈을 지운다.
    float m_lift;
    float m_tremorAmp;
    float m_tremorPhase;

    protected override bool  HasStage       => this.cardStage != null;
    protected override float ReturnDuration => this.returnDuration;

    /// <summary>충전이 밀어 올린 높이(px).</summary>
    float Lift
    {
        get => this.m_lift;
        set { this.m_lift = value; ApplyStagePose(); }
    }

    /// <summary>진동의 위상(라디안). 매 프레임 도는 축이라 자세를 여기서 다시 그린다.</summary>
    float TremorPhase
    {
        get => this.m_tremorPhase;
        set { this.m_tremorPhase = value; ApplyStagePose(); }
    }

    /// <summary>0 멎음 ~ 1 정점. 몸통과 표면이 같은 세기로 함께 떤다 — 따로 두면 두 물건이 떠는 것으로 보인다.</summary>
    float TremorAmp
    {
        get => this.m_tremorAmp;
        set
        {
            this.m_tremorAmp   = value;
            this.shading.Shake = value * this.tremorSurface;
        }
    }

    // 두 줄기는 순차다(판이 한 장이라 겹칠 수 없다) — 그래서 길이가 더해진다.
    float GleamSpan => this.shading.HasGleam
                           ? Mathf.Max(0f, this.gleamDelay) + Mathf.Max(0.05f, this.gleamSweep)
                             + Mathf.Max(0f, this.gleamGap) + Mathf.Max(0.05f, this.gleamSweepSlow)
                           : 0f;

    float SettleSpan => Mathf.Max(0.05f, this.burstSettle);

    // 숨이 시작하는 자리(공개 시작 대비). 착지가 끝나고, 그리고 빛이 다 물러난 뒤여야 한다 —
    // 아직 덮개가 남은 카드가 뛰면 무엇이 뛰는지가 안 읽힌다. 각인도 이 박에 얹힌다.
    float BeatAt => Mathf.Max(SettleSpan, Mathf.Max(0.05f, this.revealDuration) * VeilRetreatSpan)
                  + Mathf.Max(0f, this.beatDelay);

    float BeatSpan => Mathf.Max(0.05f, this.beatRise) + Mathf.Max(0.05f, this.beatFall);

    // 결과 구간은 착지·빛줄기·줄기 회수와 숨·각인이 다 지나갈 자리를 담아야 한다 — 짧으면 복귀가 그 위를 덮친다.
    float RevealSettle => Mathf.Max(Mathf.Max(SettleSpan, GleamSpan),
                                    Mathf.Max(this.rays.RetractSpan,
                                              BeatAt + Mathf.Max(BeatSpan, this.emblems.Span)));

    Tween TweenLift(float _to, float _dur)      => DOTween.To(() => this.Lift,      _v => this.Lift      = _v, _to, _dur);
    Tween TweenTremorAmp(float _to, float _dur) => DOTween.To(() => this.TremorAmp, _v => this.TremorAmp = _v, _to, _dur);

    // 부양과 진동을 한 자리에서 합성한다. 진동은 난수가 아니라 어긋난 두 주기의 겹침이다 —
    // 불티(CardEnhanceEmbers)와 같은 이유로 같은 진화는 매번 같아야 한다.
    void ApplyStagePose()
    {
        if (this.cardStage == null) return;

        Vector2 t_off = Vector2.zero;

        if (this.m_tremorAmp > 0f)
        {
            float t_p = this.m_tremorPhase;
            float t_a = this.m_tremorAmp * this.tremorAmp;

            t_off.x = (Mathf.Sin(t_p)                 * 0.7f + Mathf.Sin(t_p * 2.3f + 0.7f) * 0.3f) * t_a;
            t_off.y = (Mathf.Sin(t_p * 1.37f + 1.1f)  * 0.7f + Mathf.Sin(t_p * 3.1f + 2.2f) * 0.3f) * t_a * 0.75f;
        }

        this.cardStage.anchoredPosition = this.m_baseAnchored + new Vector2(0f, this.m_lift) + t_off;
    }

    // 축만 0으로 되돌린다(자세는 건드리지 않는다) — 이어받는 길에서 앞 판이 남긴 높이가 다음 부양의 출발점이 되면 카드가 내려간다.
    void ClearStageAxes()
    {
        this.m_lift        = 0f;
        this.m_tremorPhase = 0f;
        this.TremorAmp     = 0f;
    }

    // 재질 사본은 미리 만들어 둔다(진화 순간의 생성 렉 제거). 카드에 얹는 것은 연출이 시작할 때다 —
    // 평상시까지 얹어두면 카드가 연출 셰이더로 그려져 상세창 좌우 전환의 페이드에서 색이 틀어진다.
    void Awake() => this.shading.Warm();

    void OnDestroy() => this.shading.Release();

    /// <summary>이번 진화로 새로 열리는 프레임 문양들. <see cref="CardGrowthRitualView.Play"/> **직전에** 넘긴다 —
    /// 시퀀스는 Play에서 한 번에 짜이므로 그 뒤에 주면 이번 판에는 반영되지 않는다.
    /// 넘길 것이 없으면 빈 목록(또는 null)을 줘야 앞 판의 문양이 남지 않는다.</summary>
    public void SetEmblems(IReadOnlyList<Graphic> _emblems) => this.emblems.SetTargets(_emblems);

    protected override void AttachLayers()
    {
        this.shading.Attach();

        // 피터가 깨어 있으면 리빌드 한 번에 카드가 제자리로 튄다(shading.Attach와 같은 수명 — 연출 동안만).
        if (this.stageFitter != null) this.stageFitter.enabled = false;
    }

    /// <summary>재탄생 한 판. _outcome은 언제나 Success다 — 진화 레벨의 성공률은 1이고,
    /// 실패가 생긴다면 그때는 담금질이 그 얼굴을 맡아야 한다(여기에 실패의 얼굴을 덧대지 말 것).</summary>
    protected override float BuildStage(Sequence _seq, EEnhanceOutcome _outcome, bool _chained)
    {
        // 저작값이 0이나 음수여도 구간이 서로를 넘지 않게 여기서 한 번 정리한다.
        float t_enterDur  = Mathf.Max(0.01f, this.enterDuration);
        float t_chargeDur = Mathf.Max(0.06f, this.chargeDuration);
        float t_blazeDur  = Mathf.Max(0.05f, this.blazeDuration);
        float t_holdDur   = Mathf.Max(BlindRise, this.holdDuration);
        float t_revealOut = Mathf.Max(0.05f, this.revealDuration);
        float t_resultDur = Mathf.Max(t_revealOut + BreathRoom, Mathf.Max(this.resultHold, RevealSettle));

        float t_charge = t_enterDur;
        float t_blaze  = t_charge + t_chargeDur;
        float t_hold   = t_blaze + t_blazeDur;
        float t_reveal = t_hold + t_holdDur;

        BuildEnter(_seq, 0f, t_enterDur, _chained);
        BuildCharge(_seq, t_charge, t_chargeDur);

        // 진동은 충전 끝을 넘어 백열까지 이어진다 — 그래서 충전이 아니라 여기서 짠다.
        BuildTremor(_seq, t_charge, t_chargeDur, t_blaze, t_blazeDur);

        // 줄기 회전은 백열(blaze·hold)을 지나 공개까지 물고 이어진다 — hold 안에서 끝나면 백지라 아무도 못 본다.
        BuildBlaze(_seq, t_blaze, t_blazeDur, t_blazeDur + t_holdDur + t_revealOut * 0.6f);

        BuildHold(_seq, t_hold, t_holdDur);
        BuildReveal(_seq, t_reveal);

        // 카드 위 연출이 다 끝난 자리 = 결과판이 뜰 자리.
        return t_reveal + t_resultDur;
    }

    protected override void BuildReturn(Sequence _seq, float _at, float _dur, float _end)
    {
        _seq.Insert(_at, this.shading.TweenHeat(0f, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.shading.TweenBlind(0f, Mathf.Min(0.1f, _dur)));
        _seq.Insert(_at, this.shading.TweenCover(0f, Mathf.Min(0.1f, _dur)));

        _seq.Insert(_at, this.cardStage.DOAnchorPos(this.m_baseAnchored, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.cardStage.DOScale(1f, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.cardStage.DOLocalRotate(Vector3.zero, Mathf.Min(0.1f, _dur)));
        _seq.Insert(_at, this.dimTint.TweenLevel(0f, _dur));

        // 잠식은 알파가 다 내려간 뒤에 되돌린다 — 순서가 뒤집히면 갉혔던 덮개가 한 프레임 되살아난다.
        _seq.InsertCallback(_at + Mathf.Min(0.1f, _dur), () => this.shading.Snuff = 0f);

        // 잔광을 여기서 걷는다 — OnRestoreVisual에만 맡기면 마지막 프레임에 후광이 툭 끊긴다.
        _seq.Insert(_at, this.halo.TweenAlpha(0f, _dur).SetEase(Ease.InQuad));
        _seq.Insert(_at, this.halo.TweenScale(this.halo.IdleScale, _dur).SetEase(Ease.InQuad));

        this.retractPanels.Insert(_seq, _at, 1f, _dur);

        // 길이를 못 박는다 — 위 트윈이 전부 미배선이면 시퀀스가 여기 닿기 전에 끝나 버린다.
        _seq.InsertCallback(_end, () => this.retractPanels.SetBlocking(true));
    }

    protected override void CaptureBase()
    {
        if (this.m_baseCaptured) return;

        this.m_baseCaptured = true;
        this.m_baseAnchored = this.cardStage.anchoredPosition;

        this.dimTint.Capture();
        this.rays.CapturePoses();
    }

    protected override void OnRestoreVisual()
    {
        if (!this.m_baseCaptured) return;

        ClearStageAxes();   // 자세를 못 박기 전에 축부터 — 남은 높이·진폭이 다음 판의 출발점이 되면 안 된다

        if (this.cardStage != null)
        {
            this.cardStage.anchoredPosition = this.m_baseAnchored;
            this.cardStage.localScale       = Vector3.one;

            // 담금질과 공유하는 노드다 — 남긴 각도는 다음 강화에 비뚤어진 카드로 나타난다.
            this.cardStage.localRotation = Quaternion.identity;
        }

        this.dimTint.Reset();
        this.halo.Reset();
        this.rays.Reset();

        // 문양만은 "꺼짐"이 아니라 authoring(제 색으로 켜짐)으로 돌아간다 — 새기다 잘려도 결과는 열린 문양이다.
        this.emblems.Restore();

        this.shading.Neutralize();

        // 중립값으로 되돌린 **뒤** 재질을 벗는다(담금질과 같은 규약 — 걸친 채 두면 페이드 구간에서 색이 틀어진다).
        this.shading.Detach();

        // 자리를 되돌린 뒤에 깨운다 — 먼저 깨우면 그 프레임의 리빌드가 원복과 겹친다.
        if (this.stageFitter != null) this.stageFitter.enabled = true;

        this.retractPanels.Reset();
    }

    // ── 구간 ─────────────────────────────────────────────

    // 패널이 걷히고 카드가 한 번 들이쉰다. 담금질과 반대로 **커진다** — 첫 프레임부터 결이 다르다는 신호다.
    void BuildEnter(Sequence _seq, float _at, float _dur, bool _chained)
    {
        _seq.InsertCallback(_at, () => this.retractPanels.SetBlocking(false));

        // 이어받는 길엔 RestoreVisual이 지나가지 않는다 — 앞 판이 남긴 높이에서 부양이 출발하면 카드가 내려간다.
        _seq.InsertCallback(_at, ClearStageAxes);

        this.retractPanels.Insert(_seq, _at, 0f, _dur);   // 이어받는 경우엔 이미 걷혀 있어 제자리 트윈이다.

        _seq.Insert(_at, this.cardStage.DOScale(this.enterScale, _dur).SetEase(Ease.OutCubic));

        // 어두워지는 것은 무대뿐이다 — 카드는 프리팹 그대로의 밝기로 서 있어야
        // 뒤이어 붙는 빛이 "이 카드가 빛을 머금었다"로 읽힌다.
        _seq.Insert(_at, this.dimTint.TweenLevel(this.enterDim, _dur));

        if (!_chained) return;

        // 앞 결과가 남긴 표면·자세를 이 구간이 데려온다("한 번 더"로 이어온 길 — 여기엔 Reset이 지나가지 않는다).
        _seq.Insert(_at, this.cardStage.DOAnchorPos(this.m_baseAnchored, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.cardStage.DOLocalRotate(Vector3.zero, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.shading.TweenHeat(0f, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.shading.TweenCool(0f, _dur));
        _seq.Insert(_at, this.shading.TweenGrey(0f, _dur));
        _seq.Insert(_at, this.shading.TweenBlind(0f, _dur));
        _seq.Insert(_at, this.shading.TweenCover(0f, _dur));

        // 잠식이 1로 남은 채 이어오면 다음 판의 덮개가 통째로 갉힌 채라 백열이 카드를 못 덮는다.
        _seq.InsertCallback(_at + _dur, () => this.shading.Snuff = 0f);

        _seq.Insert(_at, this.halo.TweenAlpha(0f, _dur));
        _seq.Insert(_at, this.halo.TweenScale(this.halo.IdleScale, _dur));
    }

    // 충전. 빛은 카드 뒤에서 밀려나오고 카드는 떠오르며 금빛을 머금는다.
    // **떨림이 없는 것**이 이 구간의 뜻이다 — 담금질의 세 마디 진동이 여기 있으면 두 연출이 같은 것이 된다.
    // 대신 사건을 세 겹으로 쌓고 간격을 좁힌다: 맥동 3회 → 줄기 3회 → 일제(blaze). 이 사다리가 고조의 본체다.
    void BuildCharge(Sequence _seq, float _at, float _dur)
    {
        BuildChargeLift(_seq, _at, _dur);
        BuildChargeHeat(_seq, _at, _dur);

        // 후광만은 끊기지 않고 오른다 — 맥동을 여기까지 걸면 같은 말을 두 번 해 고조가 무너진다.
        _seq.Insert(_at, this.halo.TweenAlpha(this.haloPeakAlpha, _dur).SetEase(Ease.InQuad));
        _seq.Insert(_at, this.halo.TweenScale(this.haloPeakScale, _dur).SetEase(Ease.OutQuad));

        // 차오르기 전에 한 번 더 내려앉는다. 두 트윈은 겹치지 않는다(35% 뒤 35% 정지, 70%부터 상승).
        _seq.Insert(_at, this.dimTint.TweenLevel(this.chargeDipDim, _dur * 0.35f).SetEase(Ease.OutQuad));
        _seq.Insert(_at + _dur * 0.7f, this.dimTint.TweenLevel(this.chargeDim, _dur * 0.3f).SetEase(Ease.InQuad));

        if (this.rays.HasRays)
            for (int t_i = 0; t_i < RayCues.Length; t_i++)
                this.rays.InsertIgnite(_seq, t_i, _at + _dur * RayCues[t_i], this.rayIgniteDur);

        BuildOverheat(_seq, _at, _dur);
    }

    // 몸짓. 줄기가 켜질 때마다 한 뼘씩 오른다 — 한 번에 OutQuad로 밀면 초반에 다 떠 버려 고조와 반대로 간다.
    void BuildChargeLift(Sequence _seq, float _at, float _dur)
    {
        for (int t_i = 0; t_i < RayCues.Length; t_i++)
            _seq.Insert(_at + _dur * RayCues[t_i],
                        TweenLift(this.liftDistance * LiftSteps[t_i], 0.18f).SetEase(Ease.OutCubic));
    }

    // 진동. 마디가 없는 것이 이 구간의 뜻이다 — 줄기가 다 켜진 자리에서 시작해 백열 한복판에서 정점을 찍고,
    // 빛이 카드를 다 삼킬 때 멎는다. 충전 안에서 끝내면 '차오르다 만 것'이 되어 blaze로 이어지지 않는다.
    void BuildTremor(Sequence _seq, float _chargeAt, float _chargeDur, float _blazeAt, float _blazeDur)
    {
        float t_from = _chargeAt + _chargeDur * Mathf.Clamp01(this.tremorStart);
        float t_rise = Mathf.Max(0.05f, _blazeAt + _blazeDur * Mathf.Clamp01(this.tremorPeak) - t_from);
        float t_hold = _blazeDur * 0.15f;
        float t_fall = _blazeDur * 0.3f;
        float t_span = t_rise + t_hold + t_fall;

        _seq.InsertCallback(t_from, ClearStageAxesButLift);

        // 위상은 끊기지 않는 한 줄이다 — 나눠 꽂으면 이음매마다 떨림이 한 번 튄다.
        _seq.Insert(t_from, DOTween.To(() => this.TremorPhase, _v => this.TremorPhase = _v,
                                       t_span * this.tremorHz * Mathf.PI * 2f, t_span).SetEase(Ease.Linear));

        _seq.Insert(t_from, TweenTremorAmp(1f, t_rise).SetEase(Ease.InQuad));
        _seq.Insert(t_from + t_rise + t_hold, TweenTremorAmp(0f, t_fall).SetEase(Ease.OutQuad));

        // 멎은 자리를 못 박는다 — 잘린 프레임에서 어긋난 채 굳으면 백열이 비뚤어진 실루엣으로 선다.
        _seq.InsertCallback(t_from + t_span, () => { this.TremorAmp = 0f; ApplyStagePose(); });
    }

    // 진동만 0에서 출발시킨다. 부양은 이미 올라와 있으므로 함께 지우면 카드가 툭 떨어진다.
    void ClearStageAxesButLift()
    {
        this.m_tremorPhase = 0f;
        this.TremorAmp     = 0f;
    }

    // 표면. 골은 계속 올라가고 마루만 새로 갱신된다 — "추세는 상승, 최고점만 갱신".
    void BuildChargeHeat(Sequence _seq, float _at, float _dur)
    {
        foreach (Beat t_b in ChargeBeats)
            _seq.Insert(_at + _dur * t_b.At, this.shading.TweenHeat(t_b.To, _dur * t_b.Dur).SetEase(t_b.Curve));
    }

    // 잠식. 테두리에 붙은 빛이 면으로 번진다 — 앞쪽을 비워 두는 이유는 처음부터 면이 덮이면
    // 카드가 그냥 흐려지는 것으로 읽히고 '가장자리부터'가 사라지기 때문이다(담금질과 같은 규칙).
    void BuildOverheat(Sequence _seq, float _at, float _dur)
    {
        if (!this.shading.HasSurface) return;

        float t_from = _at + _dur * Mathf.Clamp01(this.overheatStart);
        float t_span = Mathf.Max(0.05f, _at + _dur - t_from);
        float t_rise = Mathf.Clamp01(this.overheatRise);

        // 금빛으로 시작한다 — 처음부터 흰색이면 번지는 것이 빛이 아니라 안개로 보인다.
        _seq.InsertCallback(t_from, () => this.shading.BlindColor = this.shading.Ember);

        _seq.Insert(t_from, this.shading.TweenBlind(t_rise, t_span).SetEase(Ease.InQuad));
        _seq.Insert(t_from, this.shading.TweenBlindColor(this.shading.LightAt(0.6f), t_span));

        // 덮개는 본체보다 늦게 올라온다 — 같이 오르면 글자가 카드보다 먼저 지워져 순서가 뒤집힌 것처럼 보인다.
        _seq.Insert(t_from + t_span * 0.3f, this.shading.TweenCover(t_rise * 0.7f, t_span * 0.7f).SetEase(Ease.InQuad));
    }

    // 백열. 남은 면을 마저 삼키고 배경까지 빛이 찬다. 카드는 멎지 않고 완만히 커진다 —
    // 담금질이 여기서 한 뼘 더 눌리는 자리에서, 이쪽은 계속 펼쳐진다.
    void BuildBlaze(Sequence _seq, float _at, float _dur, float _spinSpan)
    {
        _seq.Insert(_at, this.cardStage.DOScale(this.blazeScale, _dur).SetEase(Ease.InOutSine));

        _seq.Insert(_at, this.shading.TweenBlind(this.blindPeak, _dur).SetEase(Ease.InQuad));
        _seq.Insert(_at, this.shading.TweenCover(1f, _dur).SetEase(Ease.InQuad));
        _seq.Insert(_at, this.shading.TweenBlindColor(this.shading.WhiteHot, _dur));
        _seq.Insert(_at, this.dimTint.TweenLevel(this.peakDim, _dur).SetEase(Ease.InQuad));

        // 빛이 카드 밖으로 새어 나가야 실루엣이 잘린 판때기로 보이지 않는다.
        _seq.Insert(_at, this.halo.TweenAlpha(1f, _dur).SetEase(Ease.InQuad));
        _seq.Insert(_at, this.halo.TweenScale(GlowFloodScale, _dur).SetEase(Ease.InQuad));

        if (!this.rays.HasRays) return;

        // 회전보다 **먼저** 꽂는다 — 같은 시각이면 삽입 순서대로 처리되므로 자세 못 박기가 회전 시작보다 앞서야 한다.
        this.rays.InsertFlare(_seq, _at, _dur * 0.6f, RayCues.Length);
        this.rays.InsertSpin(_seq, _at, _spinSpan, this.raySpinDeg);
    }

    // 정적. 완전히 덮인 채 머문다 — 이 한 박이 진화의 무게다(담금질은 0.05초만 덮이고 곧 터진다).
    // 값이 바뀌는 것은 이 백지 위에서다. 눈은 숫자도 그림도 바뀌는 과정을 보지 못한다.
    //
    // 머무는 동안 실루엣이 부푼다 — 공개가 이 크기를 **이어받아** 내려앉아야 카드가 팝하듯 튀지 않는다.
    void BuildHold(Sequence _seq, float _at, float _dur)
    {
        // 앞 구간이 짧게 잘려 덜 덮인 채 도착했더라도 여기서 못 박는다.
        _seq.Insert(_at, this.shading.TweenBlind(this.blindPeak, BlindRise));
        _seq.Insert(_at, this.shading.TweenCover(1f, BlindRise));

        _seq.InsertCallback(_at + BlindRise, this.m_handoff.Reveal);

        // 공개 콜백이 켠 문양을 그 **직후**에 못 박는다(같은 시각이면 삽입 순서대로 처리된다) —
        // 늦으면 다 자란 문양이 백열 아래에서 한 프레임 비친다.
        this.emblems.InsertSeed(_seq, _at + BlindRise);

        // 앞 3분의 1은 정적이다 — 곧바로 부풀면 머무는 한 박이 사라진다.
        float t_swellAt  = _at + _dur * 0.35f;
        float t_swellDur = Mathf.Max(0.05f, _dur * 0.65f);

        _seq.Insert(t_swellAt, this.cardStage.DOScale(this.burstScale, t_swellDur).SetEase(Ease.InOutSine));
        _seq.Insert(t_swellAt, this.cardStage.DOLocalRotate(new Vector3(0f, 0f, this.burstTilt), t_swellDur)
                                   .SetEase(Ease.InOutSine));
    }

    // 공개. 빛이 **물러나며** 새 모습이 드러나고, 부푼 빛이 그대로 카드가 되어 내려앉는다.
    // 자세는 hold가 데려온 크기·기울기에서 이어받는다 — 여기서 대입하면 그 프레임에 카드가 튄다(팝).
    //
    // 빛이 다 걷힌 뒤에야 한 번 크게 뛴다(BuildBeat). 잔광·표면 열·딤은 그 전에 제 자리에 도착해 있어야 한다 —
    // 같은 축을 두 트윈이 겹쳐 밀면 뒷마디가 앞마디의 중간값에서 출발한다.
    void BuildReveal(Sequence _seq, float _at)
    {
        float t_out  = Mathf.Max(0.05f, this.revealDuration);
        float t_beat = BeatAt;
        float t_lead = Mathf.Max(0.05f, t_beat - LeadGap);   // 숨보다 먼저 끝나야 하는 축들의 길이

        BuildLanding(_seq, _at, SettleSpan);

        // 본체의 백열은 덮개보다 빨리 죽는다 — 그래야 갉혀 뚫린 자리 아래로 **새 모습**이 보인다.
        _seq.Insert(_at, this.shading.TweenBlind(0f, t_out * 0.5f).SetEase(Ease.OutQuad));
        BuildVeilRetreat(_seq, _at, t_out);

        float t_heatAt = _at + t_out * 0.4f;
        _seq.Insert(t_heatAt, this.shading.TweenHeat(this.afterglowHeat,
                                                     Mathf.Max(0.05f, _at + t_lead - t_heatAt)).SetEase(Ease.OutQuad));

        // 정점의 빛이 배경까지 밝혀 둔 채다. 여기서 가라앉혀야 위에 뜨는 결과판 글자가 읽힌다.
        _seq.Insert(_at, this.dimTint.TweenLevel(this.resultDim, t_lead).SetEase(Ease.OutQuad));

        // 후광은 꺼지지 않고 카드 가장자리에 눌러앉는다 — 복귀 구간이 걷어간다.
        _seq.Insert(_at, this.halo.TweenScale(this.afterglowScale, t_lead).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.halo.TweenAlpha(this.afterglowAlpha, t_lead).SetEase(Ease.OutQuad));

        BuildBeat(_seq, _at + t_beat);

        this.rays.InsertRetract(_seq, _at);   // 빛이 카드 안으로. 담금질의 불티가 밖으로 흩어지는 자리와 정확히 반대다

        if (this.useScreenFlash && ScreenFlash.TryGet(out ScreenFlash t_flash))
        {
            Sequence t_cover = t_flash.BuildCover(this.rebirthFlash);
            if (t_cover != null) _seq.Insert(_at, t_cover);
        }

        BuildGleam(_seq, _at);
    }

    // 착지. 제 크기 밑을 한 뼘 지나쳤다 올라온다 — 곧장 1에서 멎으면 '내려앉았다'가 아니라 '멈췄다'가 된다.
    // 자리·각도는 한 마디로 푼다(빛이 물러나는 속도와 같아야 "빛이 카드가 되었다"가 유지된다).
    void BuildLanding(Sequence _seq, float _at, float _settle)
    {
        float t_drop = _settle * 0.72f;

        _seq.Insert(_at, this.cardStage.DOScale(this.settleUndershoot, t_drop).SetEase(Ease.OutCubic));
        _seq.Insert(_at + t_drop, this.cardStage.DOScale(1f, _settle - t_drop).SetEase(Ease.InOutSine));

        _seq.Insert(_at, this.cardStage.DOAnchorPos(this.m_baseAnchored, _settle).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.cardStage.DOLocalRotate(Vector3.zero, _settle).SetEase(Ease.OutQuad));
    }

    // 숨. 갓 태어난 것이 한 번 크게 뛰고, 그 박동에 새 문양이 새겨진다.
    // 고정된 잔광은 배경 그림으로 보인다 — 이 한 박이 카드를 살아 있게 만들고, 진화의 마지막 획이 된다.
    void BuildBeat(Sequence _seq, float _at)
    {
        float t_rise = Mathf.Max(0.05f, this.beatRise);
        float t_fall = Mathf.Max(0.05f, this.beatFall);

        _seq.Insert(_at,           this.cardStage.DOScale(this.beatScale, t_rise).SetEase(Ease.OutQuad));
        _seq.Insert(_at + t_rise,  this.cardStage.DOScale(1f, t_fall).SetEase(Ease.InOutSine));

        // 후광이 카드 밖으로 밀려났다 돌아온다 — 파동이 몸 밖으로 번지는 축이다.
        _seq.Insert(_at,          this.halo.TweenAlpha(this.beatHaloAlpha, t_rise).SetEase(Ease.OutQuad));
        _seq.Insert(_at,          this.halo.TweenScale(this.beatHaloScale, t_rise).SetEase(Ease.OutQuad));
        _seq.Insert(_at + t_rise, this.halo.TweenAlpha(this.afterglowAlpha, t_fall).SetEase(Ease.InQuad));
        _seq.Insert(_at + t_rise, this.halo.TweenScale(this.afterglowScale, t_fall).SetEase(Ease.OutQuad));

        _seq.Insert(_at,          this.shading.TweenHeat(Mathf.Min(1f, this.afterglowHeat * BeatHeatBoost), t_rise)
                                      .SetEase(Ease.OutQuad));
        _seq.Insert(_at + t_rise, this.shading.TweenHeat(this.afterglowHeat, t_fall).SetEase(Ease.InOutSine));

        _seq.Insert(_at,          this.dimTint.TweenLevel(this.resultDim + this.beatDim, t_rise).SetEase(Ease.OutQuad));
        _seq.Insert(_at + t_rise, this.dimTint.TweenLevel(this.resultDim, t_fall).SetEase(Ease.InOutSine));

        this.emblems.InsertEngrave(_seq, _at);
    }

    // 덮개가 물러나는 방식. 갉아 없애면 빛이 면을 따라 물러나고, 알파로 내리면 그냥 투명해진다.
    void BuildVeilRetreat(Sequence _seq, float _at, float _out)
    {
        if (!this.shading.CanSnuff)
        {
            _seq.Insert(_at, this.shading.TweenCover(0f, _out * 0.7f).SetEase(Ease.OutQuad));
            return;
        }

        float t_sweep = _out * VeilRetreatSpan;

        // 알파(Cover)는 1로 붙들어 둔다 — 셰이더의 번짐 테두리는 Cover에 스케일되지 않아 알파를 내려도 얼룩이 남는다.
        _seq.Insert(_at, this.shading.TweenSnuff(1f, t_sweep).SetEase(Ease.InOutQuad));
        _seq.InsertCallback(_at + t_sweep, () =>
        {
            this.shading.Cover = 0f;
            this.shading.Snuff = 0f;
        });
    }

    // 표면을 훑는 빛 두 줄. 갓 태어난 것을 닦아 내는 마지막 획이다.
    // ⚠ 겹칠 수 없다 — 판이 한 장이라 앞줄기의 EndGleam이 뒷줄기의 알파를 죽인다.
    void BuildGleam(Sequence _seq, float _at)
    {
        if (!this.shading.HasGleam) return;

        float t_fast = Mathf.Max(0.05f, this.gleamSweep);
        float t_at   = _at + Mathf.Max(0f, this.gleamDelay);

        InsertGleamSweep(_seq, t_at, t_fast, Ease.OutQuad);
        InsertGleamSweep(_seq, t_at + t_fast + Mathf.Max(0f, this.gleamGap),
                         Mathf.Max(0.05f, this.gleamSweepSlow), Ease.InOutSine);
    }

    void InsertGleamSweep(Sequence _seq, float _at, float _dur, Ease _ease)
    {
        _seq.InsertCallback(_at, this.shading.BeginGleam);
        _seq.Insert(_at, this.shading.TweenGleam(1f, _dur).SetEase(_ease));
        _seq.InsertCallback(_at + _dur, this.shading.EndGleam);
    }

    // 충전 맥동의 한 마디. At·Dur는 충전 길이 대비 비율이고, 마디끼리 겹치지 않는 것이 계약이다.
    readonly struct Beat
    {
        public readonly float At;
        public readonly float Dur;
        public readonly float To;
        public readonly Ease  Curve;

        public Beat(float _at, float _dur, float _to, Ease _curve)
        {
            this.At    = _at;
            this.Dur   = _dur;
            this.To    = _to;
            this.Curve = _curve;
        }
    }
}
