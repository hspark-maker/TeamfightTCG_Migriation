using DG.Tweening;
using UnityEngine;

// 카드 강화 한 번의 연출(CardDetailOverlay 루트에 부착).
// 담금질이다 — 카드가 움츠러들며 달아오르고(고조), 그 빛이 카드를 통째로 삼킨 뒤(정적), 백열이 걷히며 터진다(공개).
//
// 호출부와의 콜백 계약은 CardGrowthRitualView가 진다 — 여기는 무대에 무엇을 그릴지만 정한다.
public class CardEnhanceRitualView : CardGrowthRitualView
{
    [Header("무대 (미배선이면 연출 없이 콜백만 즉시 흘린다)")]
    [Tooltip("⚠ LayoutGroup에 구동되지 않는 노드여야 한다 — 매 프레임 좌표가 되돌려지면 진동이 보이지 않는다.")]
    [SerializeField] RectTransform cardStage;                                   // 압축·진동·낙하를 받는 노드(CardSlot). 카드 그림의 부모
    [SerializeField] RetractingPanels retractPanels = new RetractingPanels();   // 연출 동안 사라졌다 돌아올 패널들(DetailPanel·BottomBar)

    [Header("연출 레이어")]
    [SerializeField] CardEnhanceShading shading = new CardEnhanceShading();     // 카드 표면이 내는 빛
    [SerializeField] CardEnhanceEmbers  embers  = new CardEnhanceEmbers();      // 성공에 흩날리는 불티
    [SerializeField] CardEnhanceHalo    halo    = new CardEnhanceHalo();        // 카드 뒤 후광
    [SerializeField] ScreenDimTint      dimTint = new ScreenDimTint();          // 화면 딤의 밝기

    [Header("과열 — 빛이 카드를 삼킨다")]
    [Range(0f, 1f)] [SerializeField] float blindPeak     = 1f;                                  // 백열의 최대 세기. 1이면 흰 실루엣만 남는다
    [Range(0f, 1f)] [SerializeField] float overheatStart = 0.4f;                                // 고조의 어디서부터 면이 덮이나. 앞쪽은 테두리 불만 올라 '가장자리부터'가 된다
    [Range(0f, 1f)] [SerializeField] float overheatRise  = 0.55f;                               // 고조가 끝나는 시점의 잠식률. 1이면 삼켜지는 과정이 안 보인다

    [Header("실패 — 훅 꺼진다")]
    [SerializeField] float snuffDuration = 0.07f;                               // 빛이 죽는 시간(밝기만). 짧아야 '꺼졌다'가 된다
    [SerializeField] Color ashColor      = new Color(0.42f, 0.16f, 0.05f, 1f);  // 남는 잉걸 잔막의 색. 밝으면 아직 타는 중으로 보인다
    [Range(0f, 1f)] [SerializeField] float ashAlpha      = 0.5f;                                // 잔막의 두께. 카드가 비쳐야 '덮인 채 식는 중'으로 읽힌다
    [SerializeField] float ashSweep      = 0.3f;                                // 잔막이 얼룩덜룩 걷히는 시간. 짧으면 눈이 무늬를 못 읽는다
    [Range(0f, 0.5f)] [SerializeField] float emberHeat     = 0.22f;                               // 꺼진 직후의 잔열. 면의 빛은 이미 없고 테두리선만 남는다
    [SerializeField] float emberFade     = 0.35f;                               // 잔열이 사그라들기까지
    [Range(0f, 0.6f)] [SerializeField] float blackoutDepth = 0.3f;              // 딤이 결과 밝기보다 더 내려가는 깊이. 이 과암이 낙차를 만든다
    [SerializeField] float coolFade      = 0.2f;                                // 잉걸이 회청으로 식는 시간

    [Header("실패 — 균열")]
    [SerializeField] float fractureShake = 11f;                                 // 균열의 몸통 흔들림 폭(px). 고조의 진동과 달리 한 번뿐이다
    [SerializeField] float fractureFade  = 0.12f;                               // 색수차가 가라앉는 시간. 길면 '부서졌다'가 '흐려졌다'로 뭉개진다
    [Range(0f, 1f)] [SerializeField] float ashGreyResidue = 0.2f;               // 펄스 뒤 남는 회색화. 낮아야 '담금질이 실패했다'이지 '카드가 상했다'가 아니다

    [Header("성공 잔광 — 카드 가장자리")]
    [Range(0f, 1f)] [SerializeField] float afterglowHeat  = 0.35f;                              // 공개 뒤 카드에 남는 열 — 방금 벼려낸 쇠붙이의 결
    [Range(0f, 1f)] [SerializeField] float afterglowAlpha = 0.3f;                               // 남는 후광의 알파. 세면 결과 카드가 안 읽힌다
    [SerializeField] float afterglowScale = 1.12f;                              // 잔광 후광의 크기. 카드보다 조금 커야 새어 나오는 것으로 읽힌다

    [Header("성공 — 표면을 훑는 빛")]
    [SerializeField] float gleamDelay = 0.14f;                                  // 폭발에서 얼마나 뒤에 훑나. 겹치면 폭발의 흰빛에 묻힌다
    [SerializeField] float gleamSweep = 0.4f;                                   // 빛줄기가 카드를 가로지르는 시간

    [Header("성공 화면 덮개 (선택)")]
    [SerializeField] bool             useScreenFlash = true;                    // 성공에만 쏜다. 실패까지 화면이 반응하면 성공의 대비가 사라진다
    [SerializeField] ScreenFlashCover successFlash   = new ScreenFlashCover { rise = 0.05f, hold = 0.02f, fall = 0.3f, peak = 0.55f };

    [Header("박자")]
    [SerializeField] float enterDuration   = 0.15f;
    [SerializeField] float buildUpDuration = 1.2f;
    [SerializeField] float holdDuration    = 0.25f;                             // 정점의 정적. 없으면 고조가 결과로 흘러들어 판정 순간이 뭉개진다
    [SerializeField] float resultHold      = 0.7f;
    [SerializeField] float returnDuration  = 0.35f;

    [Header("세기 — 카드는 조여들었다가 터진다")]
    [SerializeField] float enterScale    = 0.92f;                               // 진입에서 당겨지는 배율. 1보다 작아야 '움츠린다'
    [SerializeField] float compressScale = 0.78f;                               // 고조 끝의 압축. 여기까지 쉬지 않고 조여든다
    [SerializeField] float holdScale     = 0.74f;                               // 정적의 최대 압축. 이 한 뼘이 폭발의 반동을 만든다
    [SerializeField] float burstScale    = 1.35f;                               // 튀어오르는 배율. 즉시 이 크기가 되었다가 회수된다
    [SerializeField] float burstSettle   = 0.4f;                                // 폭발이 제자리로 회수되는 시간

    [SerializeField] float shakeStrength    = 9f;                               // 고조 마지막 마디의 몸통 진동(px). 앞 마디는 1/4, 1/2
    [Range(0f, 1f)] [SerializeField] float failDesaturation = 0.85f;
    [Range(0f, 1f)] [SerializeField] float glowPeakAlpha    = 0.9f;             // 고조 정점의 후광 짙기

    [Range(-1f, 1f)]
    [SerializeField] float resultDimLevel = -0.45f;                             // 결과를 읽는 동안의 밝기. 여기까지 가라앉아야 결과판 글자가 읽힌다

    // 카드가 완전히 덮인 채 머무는 한 박. 값 반영은 이 백지 위에서 일어난다 — 눈이 숫자가 바뀌는 과정을 보지 못한다.
    const float BlindRise = 0.05f;

    // 삼켜지는 동안 후광이 카드 밖으로 번지는 크기. 실루엣에 딱 맞으면 빛이 판때기처럼 잘려 보인다.
    const float GlowFloodScale = 1.25f;

    // 답을 기다리는 동안의 제자리 숨. 한 결이 짧으면 깜빡임이 되고, 길면 멈춘 화면으로 읽힌다.
    const float WaitBreath = 0.45f;

    // 그 숨이 미는 폭. 백열은 살짝 내려앉고 후광은 살짝 부푼다 — 불이 살아 있다는 것만 말하면 되므로
    // 눈에 띌 만큼 밀면 "무언가 더 일어나는 중"으로 읽혀 결말의 폭발과 다툰다.
    const float WaitBlindDip  = 0.9f;
    const float WaitHaloSwell = 1.04f;

    // 균열이 폭발보다 늦게 붙는 만큼. 첫 두 프레임이 성공과 같아야 BuildBurst의 계약("몸짓은 결과를 모른다")이 산다 —
    // 그 사이 백열이 이미 죽기 시작해 표면이 먼저 답을 낸다.
    const float FractureDelay = 0.03f;

    // 균열의 몸통 흔들림 길이. 이 뒤에 좌표를 못 박는다.
    const float FractureShake = 0.18f;

    // 벌어진 채 머무는 한 박. 즉시 되돌리면 눈이 어긋남을 읽기 전에 지나간다.
    const float FractureHold = 0.02f;

    // 잿빛이 정점에 머무는 한 박과 물러나는 시간. 머무름이 없으면 찍힌 것이 안 보이고, 길면 카드가 상한 것으로 읽힌다.
    const float AshGreyHold    = 0.1f;
    const float AshGreyRelease = 0.25f;

    // 실패 표면이 다 걷히기 전에 복귀가 시작되면, 잔막이 지워지는 도중에 카드가 되돌아온다.
    // (0.35는 과암에서 결과 밝기로 되돌아오는 딤 트윈 — 잔막 쓸림보다 짧아도 이만큼은 담겨야 한다.)
    // 잔막 쓸림·냉각은 둘 다 훅 꺼진 자리에서 함께 출발하므로 긴 쪽이 구간을 정한다.
    float FailSettle => Mathf.Max(0.02f, this.snuffDuration)
                      + Mathf.Max(Mathf.Max(this.ashSweep, 0.35f), Mathf.Max(0.05f, this.coolFade));

    // 성공도 마찬가지다 — 불티가 다 꺼지고 빛줄기가 다 지나갈 자리를 결과 구간이 담아야 한다.
    float SuccessSettle => Mathf.Max(this.embers.Span,
                                     this.shading.HasGleam ? Mathf.Max(0f, this.gleamDelay) + Mathf.Max(0.05f, this.gleamSweep) : 0f);

    Vector2 m_baseAnchored;                 // cardStage의 authoring 자리. 중간값을 기준으로 잡으면 반복할수록 밀린다
    bool    m_baseCaptured;

    protected override bool  HasStage       => this.cardStage != null;
    protected override float ReturnDuration => this.returnDuration;

    protected override void AttachLayers() => this.shading.Attach();

    /// <summary>담금질의 앞 절반 — 움츠러들며 달아오르고(고조) 그 빛이 카드를 통째로 삼킨다(정적).
    ///
    /// 성패를 읽는 것이 하나도 없다. 그래서 누른 프레임에 바로 태울 수 있고,
    /// 서버 주사위(rollSucceeded)의 왕복이 이 1.6초 안에 들어간다 —
    /// "이 마지막 한 겹이 덮이는 동안이 결과를 기다리는 시간이 된다"는 BuildStill의 말이 그 자리다.
    ///
    /// 절단면은 카드가 완전히 덮인 시각이다. 이 앞의 모든 트윈은 자기 박자 안에서 끝나므로
    /// 대기가 얼마나 끼든 결말이 이어받을 것은 '덮인 자세' 하나뿐이다.</summary>
    protected override float BuildLead(Sequence _seq, bool _chained)
    {
        // 저작값이 0이나 음수여도 구간이 서로를 넘지 않게 여기서 한 번 정리한다.
        float t_enterDur = Mathf.Max(0.01f, this.enterDuration);
        float t_riseDur  = Mathf.Max(0.06f, this.buildUpDuration);
        float t_stillDur = Mathf.Max(0.02f, this.holdDuration);

        float t_rise   = t_enterDur;
        float t_still  = t_rise + t_riseDur;
        float t_reveal = t_still + t_stillDur;

        BuildEnter(_seq, t_enterDur, _chained);
        BuildRise(_seq, t_rise, t_riseDur);
        BuildStill(_seq, t_still, t_stillDur);

        return t_reveal;
    }

    /// <summary>담금질의 뒤 절반 — 백열이 걷히며 터지고, 그 폭발이 드러내는 얼굴로 성패가 갈린다.
    /// 몸짓 먼저, 그 위에 표면 — 폭발은 결과를 모른다.
    ///
    /// 값 반영(Reveal)은 카드가 빛에 완전히 덮인 프레임이다 — 눈이 숫자가 바뀌는 과정을 보지 못한다.
    /// 성패를 읽는 두 곳(결과 구간 길이·표면 분기)이 전부 이 토막 안에 있다.</summary>
    protected override float BuildFinale(Sequence _seq, EEnhanceOutcome _outcome, float _at)
    {
        bool t_success = _outcome == EEnhanceOutcome.Success;

        // 결과 구간은 공통 몸짓(폭발 회수)과 결과별 표면 중 긴 쪽을 담아야 한다 — 짧으면 복귀가 그 위를 덮친다.
        float t_burst     = Mathf.Max(0.05f, this.burstSettle);
        float t_resultDur = Mathf.Max(this.resultHold, Mathf.Max(t_burst, t_success ? SuccessSettle : FailSettle));

        float t_result = _at + BlindRise;

        BuildReveal(_seq, _at);
        BuildBurst(_seq, t_result);

        if (t_success) BuildSuccessSurface(_seq, t_result, t_resultDur);
        else           BuildFailSurface(_seq, t_result);

        // 카드 위 연출이 다 끝난 자리 = 결과판이 뜰 자리. 터지는 카드 위에 글자를 얹으면 둘 다 안 읽힌다.
        return t_result + t_resultDur;
    }

    /// <summary>답을 기다리는 동안의 제자리 숨. 완전히 덮인 백열이 아주 얕게 오르내린다 —
    /// 얼어붙은 화면은 "멈췄다"로 읽히고, 그 순간 유저는 조작이 씹혔다고 생각한다.
    ///
    /// 만지는 축이 둘뿐인 것이 계약이다. 결말의 첫 BlindRise(BuildReveal)가 blind와 halo 크기를 모두
    /// 절대값으로 다시 못 박으므로, 어느 프레임에 답이 오든 결말의 출발 자세가 같다.
    /// 좌표와 덮개(Cover)를 여기서 밀면 그 보장이 깨진다.</summary>
    protected override Sequence BuildWaitLoop()
    {
        Sequence t_seq = DOTween.Sequence().SetLink(gameObject).SetId(this);

        t_seq.Insert(0f, this.shading.TweenBlind(this.blindPeak * WaitBlindDip, WaitBreath).SetEase(Ease.InOutSine));
        t_seq.Insert(0f, this.halo.TweenScale(GlowFloodScale * WaitHaloSwell, WaitBreath).SetEase(Ease.InOutSine));

        t_seq.SetLoops(-1, LoopType.Yoyo);

        return t_seq;
    }

    // 재질 **사본**은 여기서 미리 만든다(강화 순간의 생성 렉 제거). 카드에 얹는 것은 연출이 시작할 때다 —
    // 평상시까지 얹어두면 카드가 연출 셰이더로 그려져, 상세창 좌우 전환의 알파 페이드에서 색이 틀어졌다.
    // 불티(embers)는 카드가 아니라 별도 판이고 평상시 알파 0이라 미리 얹어둬도 된다.
    void Awake()
    {
        this.shading.Warm();
        this.embers.Attach();
    }

    void OnDestroy()
    {
        this.shading.Release();
        this.embers.Release();
    }

    // ── 구간 ─────────────────────────────────────────────

    // 패널이 걷히고 카드가 한 번 움츠러들며 식는다.
    void BuildEnter(Sequence _seq, float _dur, bool _chained)
    {
        _seq.InsertCallback(0f, () => this.retractPanels.SetBlocking(false));

        this.retractPanels.Insert(_seq, 0f, 0f, _dur);   // 이어받는 경우엔 이미 걷혀 있어 제자리 트윈이다.

        _seq.Insert(0f, this.cardStage.DOScale(this.enterScale, _dur).SetEase(Ease.OutCubic));

        // 어두워지는 것은 무대뿐이다 — 카드는 프리팹 그대로의 밝기로 서 있어야
        // 뒤이어 붙는 빛이 "이 카드가 달아올랐다"로 읽힌다('불이 꺼진 대장간'은 딤이 진다).
        _seq.Insert(0f, this.dimTint.TweenLevel(-1f, _dur));

        if (!_chained) return;

        // 앞 결과가 남긴 표면을 이 구간이 데려온다 — 실패의 잿빛·성공의 잔열이 "다시 식는다"로 읽히게.
        // 좌표는 이제 어느 결과도 옮기지 않지만, 진동 중에 잘린 자리를 데려오는 길은 여기뿐이라 남긴다.
        _seq.Insert(0f, this.cardStage.DOAnchorPos(this.m_baseAnchored, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(0f, this.shading.TweenHeat(0f, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(0f, this.shading.TweenCool(0f, _dur));
        _seq.Insert(0f, this.shading.TweenGrey(0f, _dur));
        _seq.Insert(0f, this.shading.TweenBlind(0f, _dur));
        _seq.Insert(0f, this.shading.TweenCover(0f, _dur));
        _seq.Insert(0f, this.shading.TweenFracture(0f, _dur));

        _seq.Insert(0f, this.halo.TweenAlpha(0f, _dur));
        _seq.Insert(0f, this.halo.TweenScale(this.halo.IdleScale, _dur));
    }

    // 고조. 카드는 쉬지 않고 조여들며 달아오르고, 몸통 진동은 세 마디로 폭과 잔떨림을 함께 키운다 —
    // 한 마디로 이으면 세기가 변하는 것이 안 읽힌다.
    void BuildRise(Sequence _seq, float _at, float _dur)
    {
        _seq.Insert(_at, this.cardStage.DOScale(this.compressScale, _dur).SetEase(Ease.InQuad));

        float t_seg = _dur / 3f;
        for (int t_i = 0; t_i < 3; t_i++)
        {
            float t_strength = this.shakeStrength * (t_i == 0 ? 0.25f : t_i == 1 ? 0.5f : 1f);
            int   t_vibrato  = 8 + t_i * 7;

            _seq.Insert(_at + t_seg * t_i,
                        this.cardStage.DOShakeAnchorPos(t_seg, t_strength, t_vibrato, 90f, false, false));
        }

        // 평상에서 백열 직전까지. 뒤로 갈수록 가팔라야 "버티다 못해 달아오른다"가 된다 —
        // 앞 구간이 거의 0에 머무는 덕에 카드는 한동안 원래 모습 그대로 조여들기만 한다.
        _seq.Insert(_at, this.shading.TweenHeat(1f, _dur).SetEase(Ease.InQuad));
        _seq.Insert(_at, this.shading.TweenShake(1f, _dur).SetEase(Ease.InQuad));

        _seq.Insert(_at, this.halo.TweenAlpha(this.glowPeakAlpha, _dur).SetEase(Ease.InQuad));
        _seq.Insert(_at, this.halo.TweenScale(1f, _dur).SetEase(Ease.OutQuad));

        // 어둠에서 출발해 빛으로 차오른다.
        _seq.Insert(_at, this.dimTint.TweenLevel(0.6f, _dur).SetEase(Ease.InQuad));

        BuildOverheat(_seq, _at, _dur);
    }

    // 잠식. 테두리에 붙은 불이 면으로 번진다 — 고조 앞쪽을 비워 두는 이유는, 처음부터 면이 덮이면
    // 카드가 그냥 흐려지는 것으로 읽히고 '가장자리부터 달아오른다'가 사라지기 때문이다.
    void BuildOverheat(Sequence _seq, float _at, float _dur)
    {
        if (!this.shading.HasSurface) return;

        float t_from = _at + _dur * Mathf.Clamp01(this.overheatStart);
        float t_span = Mathf.Max(0.05f, _at + _dur - t_from);
        float t_rise = Mathf.Clamp01(this.overheatRise);

        // 잉걸빛으로 시작한다 — 처음부터 흰색이면 번지는 것이 열이 아니라 안개로 보인다.
        _seq.InsertCallback(t_from, () => this.shading.BlindColor = this.shading.Ember);
        _seq.InsertCallback(t_from, () => SoundManager.Instance?.PlayCue(EOutgameSound.EnhanceCharge));

        _seq.Insert(t_from, this.shading.TweenBlind(t_rise, t_span).SetEase(Ease.InQuad));
        _seq.Insert(t_from, this.shading.TweenBlindColor(this.shading.LightAt(0.6f), t_span));

        // 덮개는 본체보다 늦게 올라온다 — 같이 오르면 글자가 카드보다 먼저 지워져 순서가 뒤집힌 것처럼 보인다.
        _seq.Insert(t_from + t_span * 0.3f, this.shading.TweenCover(t_rise * 0.7f, t_span * 0.7f).SetEase(Ease.InQuad));
    }

    // 정적. 몸은 멎고(진동이 그치고 카드가 한 뼘 더 눌린다) 빛만 남은 면을 마저 삼킨다 —
    // 이 마지막 한 겹이 덮이는 동안이 결과를 기다리는 시간이 된다.
    void BuildStill(Sequence _seq, float _at, float _dur)
    {
        _seq.Insert(_at, this.cardStage.DOAnchorPos(this.m_baseAnchored, Mathf.Min(0.08f, _dur)).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.cardStage.DOScale(this.holdScale, _dur).SetEase(Ease.OutQuad));

        // 떨림만 멎는다(숨을 참는다).
        _seq.Insert(_at, this.shading.TweenShake(0f, Mathf.Min(0.1f, _dur)).SetEase(Ease.OutQuad));

        _seq.Insert(_at, this.shading.TweenBlind(this.blindPeak, _dur).SetEase(Ease.InQuad));
        _seq.Insert(_at, this.shading.TweenCover(1f, _dur).SetEase(Ease.InQuad));
        _seq.Insert(_at, this.shading.TweenBlindColor(this.shading.WhiteHot, _dur));
        _seq.Insert(_at, this.dimTint.TweenLevel(1f, _dur).SetEase(Ease.InQuad));

        // 빛이 카드 밖으로 새어 나가야 실루엣이 잘린 판때기로 보이지 않는다.
        _seq.Insert(_at, this.halo.TweenAlpha(1f, _dur).SetEase(Ease.InQuad));
        _seq.Insert(_at, this.halo.TweenScale(GlowFloodScale, _dur).SetEase(Ease.InQuad));
    }

    // 공개. 카드가 완전히 덮인 채로 한 박 머물고, 그 백지 위에서 값이 바뀐다.
    void BuildReveal(Sequence _seq, float _at)
    {
        // 앞 구간이 짧게 잘려 덜 덮인 채 도착했더라도 여기서 못 박는다 — 반쯤 덮인 카드 위에서 숫자가 바뀌면 다 보인다.
        _seq.Insert(_at, this.shading.TweenBlind(this.blindPeak, BlindRise));
        _seq.Insert(_at, this.shading.TweenCover(1f, BlindRise));

        // 대기 루프가 미는 축은 여기서 전부 절대값으로 되잡는다(BuildWaitLoop 주석의 계약).
        // 후광 크기도 그 축이다 — 표면이 후광을 남기도록 저작이 바뀌면 기다린 길이가 자세로 새어 나온다.
        _seq.Insert(_at, this.halo.TweenScale(GlowFloodScale, BlindRise));

        _seq.InsertCallback(_at + BlindRise, this.m_handoff.Reveal);
    }

    // 폭발. 눌려 있던 것이 한 프레임에 터져 나왔다가 제자리로 회수된다 —
    // 부풀어 오르는 과정을 트윈에 맡기면 타격이 뭉개진다(PackCardView.PlayPunch와 같은 규칙).
    //
    // ⚠ 결과와 무관하다. 실패도 같은 크기로 같은 시간에 터진다 — 압축은 결과를 기다린 것이 아니라
    //   고조가 쌓아 둔 반동이고, 그 반동은 무엇이 나오든 똑같이 풀린다.
    void BuildBurst(Sequence _seq, float _at)
    {
        float t_settle = Mathf.Max(0.05f, this.burstSettle);

        _seq.InsertCallback(_at, () =>
        {
            if (this.cardStage != null) this.cardStage.localScale = Vector3.one * this.burstScale;
        });
        _seq.Insert(_at, this.cardStage.DOScale(1f, t_settle).SetEase(Ease.OutQuint));
    }

    // 성공의 얼굴. 터진 자리에서 백열이 밖으로 걷히고 벼려진 카드가 드러난다.
    void BuildSuccessSurface(Sequence _seq, float _at, float _hold)
    {
        float t_settle = Mathf.Max(0.05f, this.burstSettle);

        _seq.InsertCallback(_at, () => SoundManager.Instance?.PlayCue(EOutgameSound.EnhanceSuccess));

        // 백열이 걷히며 카드가 드러난다. 열은 잔열만 남기고 천천히 식는다 — 방금 벼려낸 쇠붙이의 결.
        _seq.Insert(_at, this.shading.TweenBlind(0f, t_settle).SetEase(Ease.InQuad));
        _seq.Insert(_at, this.shading.TweenCover(0f, t_settle * 0.7f).SetEase(Ease.InQuad));
        _seq.Insert(_at + t_settle * 0.4f, this.shading.TweenHeat(this.afterglowHeat, t_settle).SetEase(Ease.OutQuad));

        // 정점의 빛이 배경까지 밝혀 둔 채다. 여기서 가라앉혀야 위에 뜨는 결과판 글자가 읽힌다.
        _seq.Insert(_at, this.dimTint.TweenLevel(this.resultDimLevel, t_settle).SetEase(Ease.OutQuad));

        // 후광은 꺼지지 않고 카드 가장자리에 눌러앉는다 — 복귀 구간이 걷어간다.
        _seq.Insert(_at, this.halo.TweenScale(this.afterglowScale, t_settle).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.halo.TweenAlpha(this.afterglowAlpha, t_settle).SetEase(Ease.OutQuad));

        // 한 번 숨을 쉰다. 고정된 잔광은 배경 이미지로 보이고, 이 한 번의 들숨이 카드를 살아 있게 만든다.
        float t_breath = Mathf.Max(0.1f, _hold - t_settle);
        _seq.Insert(_at + t_settle,
                    this.halo.TweenAlpha(this.afterglowAlpha * 0.5f, t_breath * 0.5f)
                        .SetEase(Ease.InOutSine).SetLoops(2, LoopType.Yoyo));

        if (this.useScreenFlash && ScreenFlash.TryGet(out ScreenFlash t_flash))
        {
            Sequence t_cover = t_flash.BuildCover(this.successFlash);
            if (t_cover != null) _seq.Insert(_at, t_cover);
        }

        this.embers.Insert(_seq, _at);
        BuildGleam(_seq, _at);
    }

    // 표면을 한 번 훑는 빛. 벼려낸 쇠를 닦아 내는 마지막 획이다.
    void BuildGleam(Sequence _seq, float _at)
    {
        if (!this.shading.HasGleam) return;

        float t_from = _at + Mathf.Max(0f, this.gleamDelay);
        float t_dur  = Mathf.Max(0.05f, this.gleamSweep);

        _seq.InsertCallback(t_from, this.shading.BeginGleam);
        _seq.Insert(t_from, this.shading.TweenGleam(1f, t_dur).SetEase(Ease.InOutSine));
        _seq.InsertCallback(t_from + t_dur, this.shading.EndGleam);
    }

    // 실패의 얼굴. 카드는 성공과 똑같이 터지지만, 터진 자리에서 빛이 밖으로 나가지 못하고 안에서 죽는다.
    //
    // 밝기와 면적을 갈라 민다 — 빛이 죽는 것은 순간이어야 "꺼졌다"가 되고,
    // 남은 잔막이 걷히는 것은 눈이 얼룩을 읽을 만큼 이어져야 한다.
    void BuildFailSurface(Sequence _seq, float _at)
    {
        float t_snuff = Mathf.Max(0.02f, this.snuffDuration);
        float t_sweep = Mathf.Max(0.05f, this.ashSweep);
        float t_out   = _at + t_snuff;

        _seq.InsertCallback(_at, () => SoundManager.Instance?.PlayCue(EOutgameSound.EnhanceFail));

        _seq.Insert(_at, this.halo.TweenAlpha(0f, 0.03f));

        // 훅. 백열이 잉걸로 주저앉고 덮개는 카드가 비칠 두께만 남는다 — 여기서 사라지는 것은 빛이지 덮개가 아니다.
        _seq.Insert(_at, this.shading.TweenBlind(0f, t_snuff).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.shading.TweenBlindColor(this.ashColor, t_snuff));
        _seq.Insert(_at, this.shading.TweenCover(this.ashAlpha, t_snuff).SetEase(Ease.OutQuad));

        // 걷히고 나서 식는 것이 아니라, 걷었더니 이미 차갑다 — 그래서 냉각은 잔막 아래에서 끝난다.
        _seq.Insert(_at, this.shading.TweenHeat(this.emberHeat, t_snuff).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.shading.TweenGrey(this.failDesaturation, t_snuff).SetEase(Ease.OutQuad));

        // 잿빛은 훅 찍고 곧 물러난다 — 실패가 앗아간 것은 골드뿐인데 잿빛이 남으면 카드가 상한 것으로 읽힌다
        // (PackCardView가 중복 카드에 내린 것과 같은 판단).
        _seq.Insert(_at + AshGreyHold, this.shading.TweenGrey(this.ashGreyResidue, AshGreyRelease).SetEase(Ease.OutQuad));

        BuildFracture(_seq, _at);

        // 눈이 정점의 빛에 적응해 있다. 결과 밝기보다 한 번 더 내려갔다 올라와야 "빛이 사라졌다"가 몸으로 온다.
        _seq.Insert(_at,   this.dimTint.TweenLevel(Mathf.Max(-1f, this.resultDimLevel - this.blackoutDepth), t_snuff).SetEase(Ease.OutQuad));
        _seq.Insert(t_out, this.dimTint.TweenLevel(this.resultDimLevel, 0.35f).SetEase(Ease.InOutSine));

        // 잔막이 얼룩덜룩 걷힌다. 이미 어두워진 뒤라 면적이 줄어드는 것으로만 읽힌다 —
        // 밝을 때 줄이면 같은 트윈이 "불이 카드를 먹는다"가 된다.
        if (this.shading.CanSnuff) _seq.Insert(t_out, this.shading.TweenSnuff(1f, t_sweep).SetEase(Ease.InOutQuad));
        else                       _seq.Insert(t_out, this.shading.TweenCover(0f, t_sweep).SetEase(Ease.InQuad));

        // 다 걷힌 덮개를 중립으로 되돌린다. 알파와 잠식을 같은 프레임에 놓아야 되돌리는 과정이 보이지 않는다.
        _seq.InsertCallback(t_out + t_sweep, () => { this.shading.Cover = 0f; this.shading.Snuff = 0f; });

        // 잔열. 면의 빛은 이미 없고 테두리선만 남아 사그라든다 — 카드 자체는 회색화만 뒤집어쓴 채 원래 밝기로 돌아온다.
        _seq.Insert(t_out, this.shading.TweenHeat(0f, Mathf.Max(0.05f, this.emberFade)).SetEase(Ease.InQuad));

        // 그 잔열의 색이 잉걸에서 회청으로 넘어간다. 밝기만 내리면 꺼진 뒤에도 주황이 남아 "아직 타는 중"으로 보인다.
        _seq.Insert(t_out, this.shading.TweenCool(1f, Mathf.Max(0.05f, this.coolFade)).SetEase(Ease.OutQuad));
    }

    // 균열. 빛이 죽는 순간 카드가 한 번 어긋났다 돌아온다 — 실패에 없던 타격 프레임이다.
    // 벌어짐은 즉시 세우고 감쇠만 트윈에 맡긴다(폭발과 같은 규칙 — 차오르는 과정을 보여주면 타격이 뭉개진다).
    void BuildFracture(Sequence _seq, float _at)
    {
        float t_crack = _at + FractureDelay;

        // 감쇠를 한 박 늦춰 시작한다 — 같은 시각에 놓으면 트윈의 getter가 콜백보다 먼저 읽혀 0에서 0으로 흐를 수 있다.
        _seq.InsertCallback(t_crack, () => this.shading.Fracture = 1f);
        _seq.Insert(t_crack + FractureHold,
                    this.shading.TweenFracture(0f, Mathf.Max(0.02f, this.fractureFade)).SetEase(Ease.OutQuad));

        _seq.Insert(t_crack, this.cardStage.DOShakeAnchorPos(FractureShake, this.fractureShake, 24, 90f, false, false));

        // 잘린 흔들림이 좌표에 굳으면 결과판이 뜨는 동안 카드가 비뚤어진 채 선다(BuildStill과 같은 못 박기).
        _seq.Insert(t_crack + FractureShake, this.cardStage.DOAnchorPos(this.m_baseAnchored, 0.08f).SetEase(Ease.OutQuad));
    }

    protected override void BuildReturn(Sequence _seq, float _at, float _dur, float _end)
    {
        _seq.Insert(_at, this.shading.TweenHeat(0f, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.shading.TweenCool(0f, Mathf.Min(0.2f, _dur)));
        _seq.Insert(_at, this.shading.TweenGrey(0f, Mathf.Min(0.2f, _dur)));
        _seq.Insert(_at, this.shading.TweenBlind(0f, Mathf.Min(0.1f, _dur)));
        _seq.Insert(_at, this.shading.TweenCover(0f, Mathf.Min(0.1f, _dur)));
        _seq.Insert(_at, this.shading.TweenFracture(0f, Mathf.Min(0.1f, _dur)));

        // 잠식은 덮개가 다 투명해진 뒤에 되돌린다 — 먼저 되돌리면 지워졌던 덮개가 한 프레임 되살아난다.
        _seq.InsertCallback(_at + Mathf.Min(0.1f, _dur), () => this.shading.Snuff = 0f);

        _seq.Insert(_at, this.cardStage.DOAnchorPos(this.m_baseAnchored, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.cardStage.DOScale(1f, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.dimTint.TweenLevel(0f, _dur));

        // 성공 잔광을 여기서 걷는다 — RestoreVisual에만 맡기면 마지막 프레임에 후광이 툭 끊긴다.
        _seq.Insert(_at, this.halo.TweenAlpha(0f, _dur).SetEase(Ease.InQuad));
        _seq.Insert(_at, this.halo.TweenScale(this.halo.IdleScale, _dur).SetEase(Ease.InQuad));

        this.retractPanels.Insert(_seq, _at, 1f, _dur);

        // 길이를 못 박는다 — 위 트윈이 전부 미배선이면 시퀀스가 여기 닿기 전에 끝나 버린다.
        _seq.InsertCallback(_end, () => this.retractPanels.SetBlocking(true));
    }

    // ── 상태 ─────────────────────────────────────────────

    protected override void CaptureBase()
    {
        if (this.m_baseCaptured) return;

        this.m_baseCaptured = true;
        this.m_baseAnchored = this.cardStage.anchoredPosition;

        this.dimTint.Capture();
        this.embers.CapturePoses();
    }

    // 다음 연출이 중간값(압축·열·회색)에서 출발하지 않게 원복. 캡처 전이면 건드릴 것도 없다.
    protected override void OnRestoreVisual()
    {
        if (!this.m_baseCaptured) return;

        if (this.cardStage != null)
        {
            this.cardStage.anchoredPosition = this.m_baseAnchored;
            this.cardStage.localScale       = Vector3.one;
        }

        this.dimTint.Reset();
        this.halo.Reset();
        this.embers.Reset();
        this.shading.Neutralize();

        // 중립값으로 되돌린 **뒤** 재질을 벗는다. 걸친 채로 두면 카드가 기본 UI 셰이더가 아니라
        // 연출 셰이더로 계속 그려져, 알파가 1이 아닌 구간(상세창 좌우 전환의 페이드)에서 색이 틀어진다.
        this.shading.Detach();

        this.retractPanels.Reset();
    }
}
