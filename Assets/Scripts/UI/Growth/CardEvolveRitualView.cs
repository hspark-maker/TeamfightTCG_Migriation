using DG.Tweening;
using UnityEngine;

// 카드 진화 한 번의 연출(CardDetailOverlay 루트에 부착).
// 재탄생이다 — 카드가 떠오르며 금빛을 머금고(충전), 백열이 카드를 통째로 삼킨 뒤(정적),
// 그 빛이 천천히 물러나며 새 모습이 드러난다(공개).
//
// 담금질(CardEnhanceRitualView)과 **같은 빛의 언어**를 쓴다 — 같은 화면에서 연달아 누르는 두 조작이라
// 서로 다른 문법이면 같은 시스템으로 안 읽힌다. 갈라지는 것은 색과 어투뿐이다:
//  · 카드가 떨지 않고 떠오른다(담금질은 움츠러들며 떤다) — 진화는 시달리는 것이 아니라 차오르는 것이다.
//  · 잉걸이 아니라 금빛에서 출발한다(색은 shading의 저작값이 정한다).
//  · 정점에 오래 머물고 빛이 천천히 물러난다. 담금질이 "터진다"면 이쪽은 "드러난다".
//  · 실패의 얼굴이 없다 — 진화 레벨의 성공률은 1이다.
public class CardEvolveRitualView : CardGrowthRitualView
{
    [Header("무대 (미배선이면 연출 없이 콜백만 즉시 흘린다)")]
    [Tooltip("⚠ LayoutGroup에 구동되지 않는 노드여야 한다 — 매 프레임 좌표가 되돌려지면 부양이 보이지 않는다.")]
    [SerializeField] RectTransform cardStage;                                   // 부양·확대를 받는 노드(CardSlot)
    [SerializeField] RetractingPanels retractPanels = new RetractingPanels();   // 연출 동안 사라졌다 돌아올 패널들

    [Header("연출 레이어")]
    [Tooltip("담금질과 같은 재질 축을 쓴다(사본은 각자 만든다). 색만 금빛으로 저작할 것.")]
    [SerializeField] CardEnhanceShading shading = new CardEnhanceShading();     // 카드 표면이 내는 빛
    [SerializeField] CardEnhanceHalo    halo    = new CardEnhanceHalo();        // 카드 뒤에서 밀려나오는 빛
    [SerializeField] CardEvolveRays     rays    = new CardEvolveRays();         // 충전 동안 하나 둘 켜지는 빛줄기
    [SerializeField] ScreenDimTint      dimTint = new ScreenDimTint();          // 화면 딤의 밝기

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
    [SerializeField] float burstScale   = 1.40f;                                // 빛이 물러나기 시작하는 프레임의 부풂. 즉시 이 크기가 되었다가 회수된다
    [SerializeField] float burstSettle  = 0.60f;                                // 회수 시간. 빛이 물러나는 속도와 같아야 '빛이 카드가 되었다'로 읽힌다
    [SerializeField] float burstTilt    = -3f;                                  // 회수가 시작하는 프레임의 기울기(도). 0으로 풀리며 '자리를 잡는다'

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

    protected override bool  HasStage       => this.cardStage != null;
    protected override float ReturnDuration => this.returnDuration;

    // 두 줄기는 순차다(판이 한 장이라 겹칠 수 없다) — 그래서 길이가 더해진다.
    float GleamSpan => this.shading.HasGleam
                           ? Mathf.Max(0f, this.gleamDelay) + Mathf.Max(0.05f, this.gleamSweep)
                             + Mathf.Max(0f, this.gleamGap) + Mathf.Max(0.05f, this.gleamSweepSlow)
                           : 0f;

    // 결과 구간은 회수와 빛줄기·줄기 회수가 다 지나갈 자리를 담아야 한다 — 짧으면 복귀가 그 위를 덮친다.
    float RevealSettle => Mathf.Max(Mathf.Max(0.05f, this.burstSettle),
                                    Mathf.Max(GleamSpan, this.rays.RetractSpan));

    // 재질 사본은 미리 만들어 둔다(진화 순간의 생성 렉 제거). 카드에 얹는 것은 연출이 시작할 때다 —
    // 평상시까지 얹어두면 카드가 연출 셰이더로 그려져 상세창 좌우 전환의 페이드에서 색이 틀어진다.
    void Awake() => this.shading.Warm();

    void OnDestroy() => this.shading.Release();

    protected override void AttachLayers() => this.shading.Attach();

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

        // 줄기 회전은 백열(blaze·hold)을 지나 공개까지 물고 이어진다 — hold 안에서 끝나면 백지라 아무도 못 본다.
        BuildBlaze(_seq, t_blaze, t_blazeDur, t_blazeDur + t_holdDur + t_revealOut * 0.6f);

        BuildHold(_seq, t_hold, t_holdDur);
        BuildReveal(_seq, t_reveal, t_resultDur);

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
        this.shading.Neutralize();

        // 중립값으로 되돌린 **뒤** 재질을 벗는다(담금질과 같은 규약 — 걸친 채 두면 페이드 구간에서 색이 틀어진다).
        this.shading.Detach();

        this.retractPanels.Reset();
    }

    // ── 구간 ─────────────────────────────────────────────

    // 패널이 걷히고 카드가 한 번 들이쉰다. 담금질과 반대로 **커진다** — 첫 프레임부터 결이 다르다는 신호다.
    void BuildEnter(Sequence _seq, float _at, float _dur, bool _chained)
    {
        _seq.InsertCallback(_at, () => this.retractPanels.SetBlocking(false));

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
        {
            float t_to = this.m_baseAnchored.y + this.liftDistance * LiftSteps[t_i];
            _seq.Insert(_at + _dur * RayCues[t_i], this.cardStage.DOAnchorPosY(t_to, 0.18f).SetEase(Ease.OutCubic));
        }
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
    void BuildHold(Sequence _seq, float _at, float _dur)
    {
        // 앞 구간이 짧게 잘려 덜 덮인 채 도착했더라도 여기서 못 박는다.
        _seq.Insert(_at, this.shading.TweenBlind(this.blindPeak, BlindRise));
        _seq.Insert(_at, this.shading.TweenCover(1f, BlindRise));

        _seq.InsertCallback(_at + BlindRise, this.m_handoff.Reveal);
    }

    // 공개. 빛이 **물러나며** 새 모습이 드러난다.
    // 카드는 빛이 물러나기 시작하는 프레임에 한 번 부풀었다가, 빛과 같은 속도로 가라앉는다 —
    // 둘의 속도가 같아야 "빛이 카드가 되었다"로 읽힌다. 담금질처럼 확 회수하면 그건 폭발이지 재탄생이 아니다.
    void BuildReveal(Sequence _seq, float _at, float _hold)
    {
        float t_out    = Mathf.Max(0.05f, this.revealDuration);
        float t_settle = Mathf.Max(0.05f, this.burstSettle);

        _seq.InsertCallback(_at, () =>
        {
            if (this.cardStage == null) return;

            this.cardStage.localScale    = Vector3.one * this.burstScale;
            this.cardStage.localRotation = Quaternion.Euler(0f, 0f, this.burstTilt);
        });

        _seq.Insert(_at, this.cardStage.DOScale(1f, t_settle).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.cardStage.DOAnchorPos(this.m_baseAnchored, t_settle).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.cardStage.DOLocalRotate(Vector3.zero, t_settle).SetEase(Ease.OutQuad));

        // 본체의 백열은 덮개보다 빨리 죽는다 — 그래야 갉혀 뚫린 자리 아래로 **새 모습**이 보인다.
        _seq.Insert(_at, this.shading.TweenBlind(0f, t_out * 0.5f).SetEase(Ease.OutQuad));
        BuildVeilRetreat(_seq, _at, t_out);
        _seq.Insert(_at + t_out * 0.4f, this.shading.TweenHeat(this.afterglowHeat, t_out).SetEase(Ease.OutQuad));

        // 정점의 빛이 배경까지 밝혀 둔 채다. 여기서 가라앉혀야 위에 뜨는 결과판 글자가 읽힌다.
        _seq.Insert(_at, this.dimTint.TweenLevel(this.resultDim, t_out).SetEase(Ease.OutQuad));

        // 후광은 꺼지지 않고 카드 가장자리에 눌러앉는다 — 복귀 구간이 걷어간다.
        _seq.Insert(_at, this.halo.TweenScale(this.afterglowScale, t_out).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.halo.TweenAlpha(this.afterglowAlpha, t_out).SetEase(Ease.OutQuad));

        // 한 번 숨을 쉰다. 고정된 잔광은 배경 이미지로 보이고, 이 들숨이 카드를 살아 있게 만든다.
        float t_breath = Mathf.Max(0.1f, _hold - t_out);
        _seq.Insert(_at + t_out,
                    this.halo.TweenAlpha(this.afterglowAlpha * 0.5f, t_breath * 0.5f)
                        .SetEase(Ease.InOutSine).SetLoops(2, LoopType.Yoyo));

        this.rays.InsertRetract(_seq, _at);   // 빛이 카드 안으로. 담금질의 불티가 밖으로 흩어지는 자리와 정확히 반대다

        if (this.useScreenFlash && ScreenFlash.TryGet(out ScreenFlash t_flash))
        {
            Sequence t_cover = t_flash.BuildCover(this.rebirthFlash);
            if (t_cover != null) _seq.Insert(_at, t_cover);
        }

        BuildGleam(_seq, _at);
    }

    // 덮개가 물러나는 방식. 갉아 없애면 빛이 면을 따라 물러나고, 알파로 내리면 그냥 투명해진다.
    void BuildVeilRetreat(Sequence _seq, float _at, float _out)
    {
        if (!this.shading.CanSnuff)
        {
            _seq.Insert(_at, this.shading.TweenCover(0f, _out * 0.7f).SetEase(Ease.OutQuad));
            return;
        }

        float t_sweep = _out * 0.85f;

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
