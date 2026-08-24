using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 매칭 화면의 안무(대기 → 발견 → 대치). MonoBehaviour가 아니라 셸이 필드로 소유한다
// (ScreenDimTint·RewardRevealFx와 같은 계열).
//
// ⚠ 오브젝트 참조를 뺀 모든 필드에 C# 이니셜라이저로 기본값을 준다. 기존 프리팹 YAML에는 이 필드들이 아직 없어
//   역직렬화가 건드리지 않고, 그래서 이니셜라이저 값이 그대로 살아난다 — 배선 없이도 도는 값이 여기 적힌 값이다.
[Serializable]
public class MatchmakingFx
{
    [Header("대기(스캔)")]
    [Tooltip("빈 상대 틀을 훑는 띠의 그림. 비우면 스캔 축을 통째로 건너뛴다 — 대기가 점(...)만 남는다.\n" +
             "  에셋 후보: Assets/Sprites/CardPack/Glow_Radial.")]
    [SerializeField] Sprite scanSprite;

    [Tooltip("띠의 색. 상대 자리가 아직 '비어 있다'로 읽혀야 하므로 차가운 쪽이 맞는다.")]
    [SerializeField] Color scanColor = new Color(0.55f, 0.85f, 1f, 1f);

    [Tooltip("한 번 훑는 데 걸리는 초. 0.6 아래로 내리면 훑는 게 아니라 깜빡이는 것으로 읽힌다.")]
    [Min(0.1f)] [SerializeField] float scanPeriod = 0.9f;

    [Tooltip("띠의 두께(px). 틀 높이의 절반쯤이 적당하다 — 얇으면 선이 되고 두꺼우면 그냥 밝아진다.")]
    [Min(1f)] [SerializeField] float scanThickness = 110f;

    [Range(0f, 1f)] [SerializeField] float scanAlpha = 0.5f;

    [Header("대기(호흡)")]
    [Tooltip("기다리는 동안 두 프로필 틀이 쉬는 호흡의 배율. 이 화면에서 대기는 가장 긴 구간인데" +
             "(길이의 주인이 매치메이커다) 축이 스캔 띠 하나뿐이라, 0으로 두면 그 구간이 통째로 정지 화면이 된다.\n" +
             "배너(가로 띠) 전체가 아니라 틀만 숨쉬는 이유: 띠를 부풀리면 화면이 커졌다 작아진 것으로 읽힌다.")]
    [Min(0f)] [SerializeField] float idleBreath = 0.014f;

    [Tooltip("한 번 숨쉬는 데 걸리는 시간(초). 스캔 주기와 같게 두면 두 축이 한 덩어리로 붙어 하나로 읽힌다.")]
    [Min(0.1f)] [SerializeField] float idleBreathDuration = 1.15f;

    [Tooltip("내 쪽과 상대 쪽 호흡이 어긋나는 시간(초). 0이면 두 틀이 동시에 부풀어 " +
             "'각자 기다린다'가 아니라 '화면이 맥동한다'가 된다.")]
    [Min(0f)] [SerializeField] float idleBreathOffset = 0.55f;

    [Header("발견(취소 물러남)")]
    [Tooltip("상대가 확정되는 순간 취소 버튼이 물러나는 거리(px). 여기서부터는 물러날 수 없다는 사실을 " +
             "버튼이 흐려지는 것이 아니라 자리를 뜨는 것으로 말한다 — 흐린 버튼은 '눌러도 되나?'를 남긴다.\n" +
             "0이면 물러남 없이 그 자리에서 꺼진다.")]
    [Min(0f)] [SerializeField] float cancelDismissDrop = 44f;

    [Min(0.01f)] [SerializeField] float cancelDismissDuration = 0.18f;

    [Header("발견(슬램)")]
    [Tooltip("상대 카드가 꽂히기 전 배율. 이 배율은 t=0에 즉시 적용되고 트윈은 회복만 한다 — " +
             "눈이 봐야 하는 것은 커지는 과정이 아니라 이미 큰 것이 내려꽂히는 순간이다.")]
    [Min(1f)] [SerializeField] float slamScale = 1.3f;

    [Tooltip("꽂히기 전 떠 있는 높이(px).")]
    [SerializeField] float slamRise = 64f;

    [Tooltip("내려꽂히는 시간. 0.1을 넘기면 '내려온다'가 되어 타격이 사라진다.")]
    [Min(0.01f)] [SerializeField] float slamDuration = 0.08f;

    [Tooltip("꽂히는 프레임에 화면 전체가 받는 킥(배율). 0이면 화면이 반응하지 않는다.")]
    [Min(0f)] [SerializeField] float rootKick = 0.05f;

    [Tooltip("꽂히는 순간 딤이 진해지는 정도(-1 가장 어둡게 ~ 0 평상). 어두워졌다 돌아오는 이 왕복이 충격을 대신한다.")]
    [Range(-1f, 0f)] [SerializeField] float foundDimPunch = -0.55f;

    [Tooltip("이름·랭크가 순서대로 들어오는 간격(초). 0이면 둘이 동시에 뜬다 — 읽는 순서가 사라진다.")]
    [Min(0f)] [SerializeField] float infoStagger = 0.05f;

    [Tooltip("이름·랭크가 옆에서 밀려오는 거리(px).")]
    [SerializeField] float infoSlide = 36f;

    [Tooltip("꽂히는 프레임의 화면 섬광. 색을 흰색으로 두면 매칭 화면이 하얗게 날아간다 — 어두운 화면엔 색을 얹는다.")]
    [SerializeField] ScreenFlashCover foundFlash = new ScreenFlashCover
    {
        rise = 0.03f, hold = 0.01f, fall = 0.3f, peak = 0.42f,
        color = new Color(0.55f, 0.78f, 1f, 1f),
        burstColor = new Color(0.8f, 0.92f, 1f, 1f),
        burstStartScale = 0.2f, burstEndScale = 1.4f, burstFall = 0.4f,
    };

    [Header("조임(축적) — 발견과 충돌 사이")]
    [Tooltip("이 구간의 정체는 '빈 정지'가 아니라 '압력이 차오르는 시간'이다. 아래 축들이 동시에 조금씩 올라가고, " +
             "충돌이 그걸 한 번에 방출한다 — 여기를 0으로 만들면 충돌도 같이 약해진다.")]
    [Range(-1f, 0f)] [SerializeField] float chargeDim = -0.32f;

    [Tooltip("두 배너가 서로에게 끌리는 거리(px). 처음엔 안 보이다가 끝에 가서 알아채는 정도가 맞는다 — " +
             "15를 넘기면 '이동'으로 읽혀 충돌의 예비동작이 미리 소진된다.")]
    [Min(0f)] [SerializeField] float chargeDrift = 8f;

    [Tooltip("구간 끝에 도달하는 떨림 진폭(px). 시작은 0이고 제곱으로 붙는다 — 마지막 0.2초에 거의 다 온다.")]
    [Min(0f)] [SerializeField] float chargeShake = 2.4f;

    [Tooltip("떨림 주기(Hz). 15 아래로 내리면 흔들림이 아니라 미끄러짐으로 보인다.")]
    [Min(1f)] [SerializeField] float chargeShakeFreq = 26f;

    [Tooltip("VS가 뜰 자리에 고이는 빛의 크기(px). 충돌 프레임에 이 빛이 터지며 VS로 바뀐다.")]
    [Min(0f)] [SerializeField] float chargeGlowSize = 260f;

    [Tooltip("고이는 빛의 그림. 비우면 대기 스캔과 같은 그림을 쓴다 — 둘 다 비면 이 축만 빠진다.")]
    [SerializeField] Sprite chargeGlowSprite;

    [SerializeField] Color chargeGlowColor = new Color(0.62f, 0.82f, 1f, 1f);

    [Range(0f, 1f)] [SerializeField] float chargeGlowAlpha = 0.38f;

    [Min(0f)] [SerializeField] float chargeGlowFrom = 0.55f;
    [Min(0f)] [SerializeField] float chargeGlowTo   = 1.05f;

    [Header("대치(충돌)")]
    [Tooltip("부딪히기 전 뒤로 물러나는 거리(px). 이 예비동작이 없으면 두 카드가 그냥 가까워질 뿐이다.")]
    [Min(0f)] [SerializeField] float windUpDistance = 26f;

    [Min(0.01f)] [SerializeField] float windUpDuration = 0.1f;

    [Tooltip("물러난 최고점에서 얼어붙어 있는 시간(초). 쌓인 것이 눈에 보이는 한 박이다 — " +
             "0이면 물러나자마자 돌진해 '당겨졌다'가 화면에 남지 않고, 그만큼 방출도 작아진다.\n" +
             "0.12를 넘기면 당겨진 게 아니라 멈춘 것으로 읽힌다.")]
    [Min(0f)] [SerializeField] float windUpHold = 0.06f;

    [Tooltip("부딪히는 데 걸리는 시간. 물러나는 시간보다 반드시 짧아야 '때렸다'로 읽힌다.")]
    [Min(0.01f)] [SerializeField] float impactDuration = 0.08f;

    [Tooltip("부딪힌 자리에서 두 배너가 얼어붙는 시간(초) — 히트스톱. 충격을 빛이 아니라 무게로 만드는 축이다.\n" +
             "이 정지 동안 배너는 겹친 채 멈춰 있고 섬광·VS·가로선만 돈다. 그래서 '부딪혔다'가 몸으로 온다.\n" +
             "0.08을 넘기면 충돌이 아니라 화면이 걸린 것으로 읽힌다.")]
    [Min(0f)] [SerializeField] float impactHold = 0.04f;

    [Min(0.01f)] [SerializeField] float settleDuration = 0.26f;

    [Tooltip("VS가 꽂히기 전 배율. 이 배율은 t=0에 즉시 적용되고 트윈은 회복만 한다 — 발견 슬램과 같은 규약이다.\n" +
             "⚠ 되돌아오는 이징에 오버슈트를 쓰지 않는다(OutBack 금지). 1 아래로 언더슛하는 순간 " +
             "'꽂혔다'가 '통통 튄다'가 된다 — 이 화면의 임팩트는 스쿼시도 회전도 아니고 한 프레임에 몰린 슬램이다.")]
    [Min(0f)] [SerializeField] float vsOvershoot = 0.4f;

    [Min(0.01f)] [SerializeField] float vsPopDuration = 0.14f;

    [Tooltip("VS 좌우 가로선이 바깥에서 날아와 글자에 꽂히는 거리(px). 두 배너가 부딪히는 그 프레임에 " +
             "선도 함께 부딪혀야 화면 전체가 한 사건이 된다.\n" +
             "0이면 선은 제자리에 있고 배율 축만 남는다.")]
    [Min(0f)] [SerializeField] float vsDividerTravel = 130f;

    [Min(0.01f)] [SerializeField] float vsDividerDuration = 0.13f;

    [Range(-1f, 0f)] [SerializeField] float versusDimPunch = -0.35f;

    [Tooltip("충돌 프레임에 딤이 밝은 쪽으로 튀는 정도(0~1). 조임이 어둠을 -0.32까지 끌어내렸으므로 " +
             "여기서 양수로 넘겨야 어둠→빛의 왕복이 생긴다 — 이 반전이 방출감의 대부분이다.")]
    [Range(0f, 1f)] [SerializeField] float releaseDimPeak = 0.2f;

    [Tooltip("충돌 프레임에 화면 전체가 받는 킥(배율). 조임이 클수록 여기가 커야 균형이 맞는다.")]
    [Min(0f)] [SerializeField] float releaseKick = 0.09f;

    [Tooltip("고여 있던 빛이 터져 나가는 시간. VS가 튀어나오는 시간과 비슷해야 '빛이 VS가 됐다'로 읽힌다.")]
    [Min(0.01f)] [SerializeField] float releaseGlowBurst = 0.18f;

    [Tooltip("터질 때 빛이 부푸는 배율(고인 크기 대비).")]
    [Min(1f)] [SerializeField] float releaseGlowScale = 2.1f;

    [Header("여운")]
    [Tooltip("VS가 박힌 뒤 한 번 쉬는 호흡의 배율. 이 한 박이 없으면 정지가 '멈춤'으로만 읽힌다.")]
    [Min(0f)] [SerializeField] float afterglowBreath = 0.035f;

    [Min(0.01f)] [SerializeField] float afterglowBreathDuration = 0.34f;

    [Tooltip("충돌 프레임의 섬광. 발견보다 옅어야 한다 — 같은 세기면 두 사건이 한 덩어리로 뭉친다.")]
    [SerializeField] ScreenFlashCover versusFlash = new ScreenFlashCover
    {
        rise = 0.02f, hold = 0f, fall = 0.22f, peak = 0.3f,
        color = new Color(1f, 0.92f, 0.72f, 1f),
        burstColor = new Color(1f, 0.9f, 0.65f, 1f),
        burstStartScale = 0.3f, burstEndScale = 1.5f, burstFall = 0.32f,
    };

    [Header("딤")]
    [Tooltip("화면을 덮은 어둠(Dimed). 미배선이면 딤 축이 통째로 무시된다 — 전환이 걷어낼 대상도 여기서 온다.")]
    [SerializeField] ScreenDimTint dim = new ScreenDimTint();

    // 스캔은 상대를 구할 때까지 도는 상주 트윈이라 시퀀스 밖에서 돈다 — StopScan이 반드시 걷는다.
    Tween  m_scanTween;
    Image  m_scanBand;

    // 호흡도 스캔과 같은 상주 트윈이다(길이의 주인이 매치메이커라 끝을 모른다) — StopIdle이 반드시 걷는다.
    // 배율을 미는 축이라 걷을 때 1로 되돌리는 일까지 여기가 책임진다. 안 되돌리면 발견 슬램이
    // 1.008배쯤 부푼 틀에서 시작해, 이후 어느 축도 그 어긋남을 바로잡지 않는다.
    Tween         m_idleMy;
    Tween         m_idleOpponent;
    RectTransform m_idleMyRect;
    RectTransform m_idleOpponentRect;

    // VS 글자를 감싼 좌우 가로선과 그 저작 자리. 어느 쪽이 왼쪽인지는 저작 좌표가 정한다(CaptureVsDividers).
    RectTransform m_vsLeft;
    RectTransform m_vsRight;
    Vector2       m_vsLeftHome;
    Vector2       m_vsRightHome;
    bool          m_vsDividersCaptured;

    // 조임이 세운 빛. 조임 시퀀스가 끊기고(대치가 무대를 갈아탄다) 나서도 살아 있어야 충돌이 이걸 터뜨릴 수 있다 —
    // 그래서 시퀀스가 아니라 여기가 소유한다. 실제로 걷는 것은 ClearCharge / BuildVersus의 마지막 콜백이다.
    Image m_chargeGlow;

    // 떨림이 밀기 전의 화면 좌표. 이미 밀린 값을 기준으로 잡으면 매칭을 열 때마다 화면이 조금씩 밀려난다.
    Vector2 m_rootHome;
    bool    m_rootCaptured;

    /// <summary>전환(MatchHandoffFx)이 이어서 걷어낼 딤. 진실원을 둘로 만들지 않으려 여기서 빌려준다.</summary>
    public ScreenDimTint Dim => this.dim;

    /// <summary>전환의 빛줄기가 쓸 그림. 매칭 화면에 이미 배선된 것을 빌려준다 — 전환에 스프라이트를 또 배선하지 않게.</summary>
    public Sprite RaySprite => this.chargeGlowSprite != null ? this.chargeGlowSprite : this.scanSprite;

    /// <summary>발견 안무가 끝나는 시각 — 조임은 이 뒤에 이어 붙는다.</summary>
    public float FoundDuration => this.slamDuration + this.infoStagger * 2f + 0.2f;

    /// <summary>상대 카드가 꽂히는 시각. 발견에 얹을 다른 축은 이 한 프레임에 맞춰 붙는다(대치의 HitAt과 같은 자리다).</summary>
    public float SlamAt => this.slamDuration;

    /// <summary>대치 안무가 끝나는 시각. 두 정지(windUpHold·impactHold)도 길이에 든다 —
    /// 빼먹으면 셸의 여운이 안무보다 먼저 끝나 갈라짐이 충돌 위로 겹친다.</summary>
    public float VersusDuration => this.HitAt + this.impactHold + this.settleDuration;

    /// <summary>
    /// 두 배너가 부딪히는 시각. 충돌의 표식(섬광·VS·가로선·킥·딤)은 전부 이 한 시각에 몰린다 —
    /// 히트스톱(impactHold)은 이 뒤에 붙어, 표식이 도는 동안 배너만 겹친 채 얼어 있다.
    /// </summary>
    public float HitAt => this.windUpDuration + this.windUpHold + this.impactDuration;

    /// <summary>돌진이 시작되는 시각. 물러남과 돌진 사이의 정지가 여기서 갈린다.</summary>
    float ImpactAt => this.windUpDuration + this.windUpHold;

    /// <summary>대치가 끝난 뒤 VS가 한 번 쉬는 데 걸리는 시각. 셸의 여운이 이보다 짧으면 호흡이 잘린다.</summary>
    public float AfterglowDuration => this.afterglowBreath > 0f ? this.afterglowBreathDuration : 0f;

    public void Capture()
    {
        this.dim.Capture();
    }

    /// <summary>
    /// 기다리는 동안 두 프로필 틀이 쉬는 호흡을 건다. 스캔과 같은 이유로 무한 반복이다 — 길이의 주인은 매치메이커다.
    /// 무는 것은 <b>틀</b>이지 배너가 아니다(배너를 부풀리면 화면이 커졌다 작아진 것으로 읽힌다).
    /// </summary>
    public void StartIdle(RectTransform _myFrame, RectTransform _opponentFrame)
    {
        this.StopIdle();

        if (this.idleBreath <= 0f) return;

        // 서로 어긋난 위상으로 돈다 — 같이 부풀면 두 사람이 각자 기다리는 것이 아니라 화면 하나가 맥동하는 것이 된다.
        this.m_idleMyRect       = _myFrame;
        this.m_idleOpponentRect = _opponentFrame;

        this.m_idleMy       = this.BuildBreath(_myFrame,       0f);
        this.m_idleOpponent = this.BuildBreath(_opponentFrame, this.idleBreathOffset);
    }

    /// <summary>호흡을 걷고 배율을 저작값으로 되돌린다. 되돌리지 않으면 발견 슬램이 부푼 틀에서 시작한다.</summary>
    public void StopIdle()
    {
        this.m_idleMy?.Kill();
        this.m_idleOpponent?.Kill();
        this.m_idleMy       = null;
        this.m_idleOpponent = null;

        RestoreScale(this.m_idleMyRect);
        RestoreScale(this.m_idleOpponentRect);

        this.m_idleMyRect       = null;
        this.m_idleOpponentRect = null;
    }

    Tween BuildBreath(RectTransform _rect, float _delay)
    {
        if (_rect == null) return null;

        _rect.DOKill();
        _rect.localScale = Vector3.one;

        return _rect.DOScale(1f + this.idleBreath, this.idleBreathDuration)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo)
                    .SetDelay(_delay)
                    .SetLink(_rect.gameObject);
    }

    static void RestoreScale(RectTransform _rect)
    {
        // ?. 을 쓰지 않는다 — UnityEngine.Object의 가짜 null은 null 조건 연산자가 걸러 주지 못한다.
        if (_rect == null) return;

        _rect.DOKill();
        _rect.localScale = Vector3.one;
    }

    /// <summary>
    /// 상대가 확정되는 순간 취소 버튼이 물러나는 안무(재생은 호출자). 끝나면 호출자가 버튼을 내린다 —
    /// 흐려진 채 남겨 두면 "눌러도 되나?"가 남고, 그 상태로 갈라짐(MatchHandoffFx)이 알파를 1로 되돌려 다시 비친다.
    /// </summary>
    public Sequence BuildCancelDismiss(RectTransform _cancel)
    {
        var t_seq = DOTween.Sequence();

        if (_cancel == null) return t_seq;

        _cancel.DOKill();

        var t_group = _cancel.GetComponent<CanvasGroup>();
        if (t_group == null) t_group = _cancel.gameObject.AddComponent<CanvasGroup>();

        t_group.DOKill();
        t_group.alpha = 1f;

        // 가속이라 "물러났다"가 된다. 자리는 셸이 홈으로 되돌린다(RestoreRiders) — 여기는 밀기만 한다.
        if (this.cancelDismissDrop > 0f)
        {
            Vector2 t_to = _cancel.anchoredPosition - new Vector2(0f, this.cancelDismissDrop);

            t_seq.Insert(0f, _cancel.DOAnchorPos(t_to, this.cancelDismissDuration).SetEase(Ease.InQuad));
        }

        t_seq.Insert(0f, t_group.DOFade(0f, this.cancelDismissDuration).SetEase(Ease.InQuad));

        return t_seq;
    }

    /// <summary>빈 상대 틀을 훑기 시작한다. 대기 시간의 주인은 매치메이커라 길이를 모르므로 무한 반복이다.</summary>
    public void StartScan(RectTransform _frame)
    {
        this.StopScan();

        if (_frame == null || this.scanSprite == null) return;

        // 틀 안쪽 마스크에 붙여야 띠가 둥근 테두리를 넘지 않는다.
        var t_mask   = _frame.GetComponentInChildren<Mask>();
        var t_parent = t_mask != null ? (RectTransform)t_mask.transform : _frame;

        var t_go = new GameObject("ScanBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var t_rt = (RectTransform)t_go.transform;

        t_rt.SetParent(t_parent, false);
        t_rt.anchorMin = new Vector2(0f, 0.5f);
        t_rt.anchorMax = new Vector2(1f, 0.5f);
        t_rt.pivot     = new Vector2(0.5f, 0.5f);
        t_rt.sizeDelta = new Vector2(0f, this.scanThickness);

        this.m_scanBand = t_go.GetComponent<Image>();
        this.m_scanBand.sprite         = this.scanSprite;
        this.m_scanBand.raycastTarget  = false;
        this.m_scanBand.preserveAspect = false;
        this.m_scanBand.color          = new Color(this.scanColor.r, this.scanColor.g, this.scanColor.b, 0f);

        float t_travel = Mathf.Max(_frame.rect.height, this.scanThickness * 2f) * 0.5f + this.scanThickness * 0.5f;
        float t_fade   = this.scanPeriod * 0.25f;

        // 출발 자리를 트윈 생성 전에 박는다 — DOTween이 그 값을 시작점으로 잡아 반복마다 위에서 다시 내려온다.
        t_rt.anchoredPosition = new Vector2(0f, t_travel);

        var t_seq = DOTween.Sequence().SetLink(t_go);
        t_seq.Append(t_rt.DOAnchorPosY(-t_travel, this.scanPeriod).SetEase(Ease.InOutSine));
        t_seq.Insert(0f, this.m_scanBand.DOFade(this.scanAlpha, t_fade).SetEase(Ease.OutQuad));
        t_seq.Insert(this.scanPeriod - t_fade, this.m_scanBand.DOFade(0f, t_fade).SetEase(Ease.InQuad));

        this.m_scanTween = t_seq.SetLoops(-1, LoopType.Restart);
    }

    public void StopScan()
    {
        this.m_scanTween?.Kill();
        this.m_scanTween = null;

        if (this.m_scanBand != null) UnityEngine.Object.Destroy(this.m_scanBand.gameObject);
        this.m_scanBand = null;
    }

    /// <summary>
    /// 상대가 꽂히는 안무를 만들어 돌려준다(재생은 호출자).
    /// 배율·위치는 t=0에 즉시 밀어 넣고 트윈은 회복만 한다 — 부풀어 오르는 과정을 보여주면 타격이 뭉개진다.
    /// </summary>
    public Sequence BuildFound(MatchProfileView _opponent, RectTransform _root)
    {
        var t_seq = DOTween.Sequence();

        if (_opponent == null) return t_seq;

        var t_card = _opponent.Rect;
        var t_home = t_card.anchoredPosition;

        t_card.DOKill();
        t_card.localScale       = Vector3.one * this.slamScale;
        t_card.anchoredPosition = t_home + new Vector2(0f, this.slamRise);

        t_seq.Insert(0f, t_card.DOScale(1f, this.slamDuration).SetEase(Ease.InQuad));
        t_seq.Insert(0f, t_card.DOAnchorPos(t_home, this.slamDuration).SetEase(Ease.InQuad));

        // 꽂히는 프레임에 전부 몰아 넣는다 — 시간축에 흩으면 약한 사건 넷이 되고, 겹치면 하나의 큰 사건이 된다.
        float t_hit = this.slamDuration;

        if (this.rootKick > 0f && _root != null)
            t_seq.InsertCallback(t_hit, () => UiPunch.Play(_root, this.rootKick, 0.24f));

        this.InsertDimPunch(t_seq, t_hit, this.foundDimPunch);
        this.InsertFlash(t_seq, t_hit, this.foundFlash);

        this.InsertInfo(t_seq, t_hit, _opponent.NicknameText, 0);
        this.InsertInfo(t_seq, t_hit, _opponent.RankNameText, 1);

        return t_seq;
    }

    /// <summary>
    /// 발견과 충돌 사이를 채우는 안무. 이 구간은 "빈 정지"가 아니라 "압력이 차오르는 시간"이다 —
    /// 어둠이 짙어지고, 두 배너가 서로에게 끌리고, 화면이 점점 떨리고, VS 자리에 빛이 고인다.
    /// 네 축이 동시에 올라가다 충돌 한 프레임에 전부 방출된다.
    /// </summary>
    /// <remarks>
    /// 여기서 세운 빛(<see cref="m_chargeGlow"/>)만은 시퀀스가 아니라 이 클래스가 소유한다 —
    /// 셸이 대치로 무대를 갈아타며 이 시퀀스를 죽이는데, 빛은 그 뒤 충돌까지 살아 있어야 터질 것이 남는다.
    /// </remarks>
    public Sequence BuildCharge(RectTransform _my,  Vector2 _myHome,
                                RectTransform _opp, Vector2 _oppHome,
                                Vector2 _step, RectTransform _root, Vector2 _vsAnchored, float _duration)
    {
        var t_seq = DOTween.Sequence();

        float t_dur = Mathf.Max(0.05f, _duration);

        this.InsertDrift(t_seq, _my,  _myHome,   _step, t_dur);
        this.InsertDrift(t_seq, _opp, _oppHome, -_step, t_dur);

        // 어둠은 끝까지 단조 증가한다 — 중간에 되돌아오면 쌓이던 것이 한 번 풀려 다시 시작이 된다.
        t_seq.Insert(0f, this.dim.TweenLevel(this.chargeDim, t_dur).SetEase(Ease.InQuad));

        this.InsertTremor(t_seq, _root, t_dur);
        this.InsertChargeGlow(t_seq, _root, _vsAnchored, t_dur);

        return t_seq;
    }

    /// <summary>고여 있던 빛을 걷는다. 대치까지 가지 못하고 화면이 닫히는 길에서 잔해가 남지 않게.</summary>
    public void ClearCharge()
    {
        if (this.m_chargeGlow != null)
        {
            this.m_chargeGlow.DOKill();
            UnityEngine.Object.Destroy(this.m_chargeGlow.gameObject);
        }

        this.m_chargeGlow = null;
    }

    /// <summary>
    /// 두 카드가 물러났다 부딪히고 VS가 튀어나오는 안무. 미는 방향은 호출자가 준 걸음(_step)이 정한다 —
    /// 어느 쪽이 위인지 이 클래스는 몰라도 된다.
    /// </summary>
    public Sequence BuildVersus(RectTransform _my,  Vector2 _myHome,
                                RectTransform _opp, Vector2 _oppHome,
                                Vector2 _step, RectTransform _vs, RectTransform _root)
    {
        var t_seq = DOTween.Sequence();

        this.StageClash(t_seq, _my,  _myHome,   _step);
        this.StageClash(t_seq, _opp, _oppHome, -_step);

        float t_hit = this.HitAt;

        // 조임이 어둠을 끌어내렸으므로 여기는 "더 어둡게"가 아니라 "밝은 쪽으로" 넘겨야 왕복이 생긴다.
        // 조임 없이 열리는 길(디버그·미배선)에서도 versusDimPunch가 앞을 맡아 왕복 자체는 남는다.
        this.InsertDimPunch(t_seq, t_hit, this.versusDimPunch);
        t_seq.Insert(t_hit, this.dim.TweenLevel(this.releaseDimPeak, 0.06f).SetEase(Ease.OutQuad));
        t_seq.Insert(t_hit + 0.06f, this.dim.TweenLevel(0f, 0.34f).SetEase(Ease.OutQuad));

        this.InsertFlash(t_seq, t_hit, this.versusFlash);
        this.InsertGlowBurst(t_seq, t_hit);

        if (this.releaseKick > 0f && _root != null)
            t_seq.InsertCallback(t_hit, () => UiPunch.Play(_root, this.releaseKick, 0.24f));

        if (_vs != null)
        {
            _vs.DOKill();
            _vs.localScale    = Vector3.one * (1f + this.vsOvershoot);
            _vs.localRotation = Quaternion.identity;   // 기울이지 않는다 — 아래 주석 참고

            // VS는 충돌의 결과로 튀어나온다 — 켜지는 시각이 부딪히는 프레임과 어긋나면 따로 논다.
            //
            // 기울였다 되돌리는 축(예전의 vsSpin + OutBack 회전)은 뺐다. OutBack이 0도를 지나쳤다 돌아와
            // 배지가 흔들렸고, 배율까지 OutBack이라 1 아래로 언더슛해 두 오버슈트가 겹쳤다 —
            // 임팩트가 아니라 떨림으로 읽혔다. 이 화면의 임팩트는 스쿼시도 회전도 아니라 한 프레임에 몰린 슬램이다.
            t_seq.Insert(t_hit, _vs.DOScale(1f, this.vsPopDuration).SetEase(Ease.OutQuint));

            // 좌우 가로선이 바깥에서 날아와 글자에 꽂힌다. 두 배너가 부딪히는 바로 그 프레임이라
            // 화면 전체가 한 사건이 된다 — 회전이 하던 "충돌의 결과"라는 말을 이쪽이 대신한다.
            this.StageVsDividers(t_seq, t_hit, _vs);

            // 여운의 한 박. 안무가 끝난 화면이 완전히 굳으면 정지가 '멈춤'으로 읽힌다 —
            // 숨 한 번이 "다음이 온다"로 바꿔 준다. 배율은 반드시 1로 돌아온다(Yoyo).
            if (this.afterglowBreath > 0f)
                t_seq.Insert(this.VersusDuration,
                             _vs.DOScale(1f + this.afterglowBreath, this.afterglowBreathDuration * 0.5f)
                                .SetEase(Ease.InOutSine).SetLoops(2, LoopType.Yoyo));
        }

        return t_seq;
    }

    /// <summary>모든 축을 저작 상태로 되돌린다. 안무가 잘려도 중간값으로 굳지 않게.</summary>
    public void Reset(MatchProfileView _my, MatchProfileView _opponent, RectTransform _root, RectTransform _vs)
    {
        this.StopScan();
        this.StopIdle();
        this.ClearCharge();
        this.dim.Reset();

        RestoreCard(_my);
        RestoreCard(_opponent);

        if (_root != null)
        {
            _root.DOKill();
            _root.localScale = Vector3.one;

            // 조임의 떨림이 루트를 밀어 놓고 끝났을 수 있다 — 배율만 되돌리면 화면 전체가 몇 px 어긋난 채 열린다.
            if (this.m_rootCaptured) _root.anchoredPosition = this.m_rootHome;
        }

        if (_vs == null) return;

        _vs.DOKill();
        _vs.localScale = Vector3.one;

        // 회전은 이제 아무도 밀지 않지만 되돌리는 일은 남긴다 — 예전 안무(vsSpin)가 기울여 놓은 채
        // 굳은 프리팹·씬이 있을 수 있고, 그 기울기를 풀어 줄 곳이 여기뿐이다.
        _vs.localRotation = Quaternion.identity;

        RestoreVsDivider(this.m_vsLeft,  this.m_vsLeftHome,  this.m_vsDividersCaptured);
        RestoreVsDivider(this.m_vsRight, this.m_vsRightHome, this.m_vsDividersCaptured);
    }

    // 가로선을 저작 자리로. 홈은 안무가 한 번이라도 돌아야 잡힌다 — 그 전이면 밀린 적이 없어 되돌릴 것도 없다.
    static void RestoreVsDivider(RectTransform _rect, Vector2 _home, bool _captured)
    {
        if (_rect == null || !_captured) return;

        _rect.DOKill();
        _rect.anchoredPosition = _home;
    }

    // VS 글자를 감싼 좌우 가로선이 바깥에서 날아와 꽂힌다.
    //
    // 어느 것이 왼쪽인지는 저작 좌표가 말해 준다 — 이름이나 자식 순서로 판정하지 않는다(프리팹이 바뀌면 조용히 틀어진다).
    // 글자(TMP_Text)는 건너뛰고 그림을 가진 자식만 본다.
    void StageVsDividers(Sequence _seq, float _at, RectTransform _vs)
    {
        if (this.vsDividerTravel <= 0f) return;

        this.CaptureVsDividers(_vs);

        this.InsertVsDivider(_seq, _at, this.m_vsLeft,  this.m_vsLeftHome,  -1f);
        this.InsertVsDivider(_seq, _at, this.m_vsRight, this.m_vsRightHome, +1f);
    }

    void InsertVsDivider(Sequence _seq, float _at, RectTransform _rect, Vector2 _home, float _side)
    {
        if (_rect == null) return;

        _rect.DOKill();
        _rect.anchoredPosition = _home + new Vector2(_side * this.vsDividerTravel, 0f);

        // 오버슈트 없는 감속. 지나쳤다 돌아오면 선이 글자를 뚫고 나갔다 되돌아온 것으로 보인다.
        _seq.Insert(_at, _rect.DOAnchorPos(_home, this.vsDividerDuration).SetEase(Ease.OutQuint));
    }

    // 가로선의 저작 자리는 한 번만 잡는다 — 이미 밀린 값을 다시 캡처하면 매칭마다 선이 바깥으로 걸어 나간다.
    void CaptureVsDividers(RectTransform _vs)
    {
        if (this.m_vsDividersCaptured || _vs == null) return;

        this.m_vsDividersCaptured = true;

        var t_images = _vs.GetComponentsInChildren<Image>(true);

        for (int t_i = 0; t_i < t_images.Length; t_i++)
        {
            var t_rect = (RectTransform)t_images[t_i].transform;

            // 그룹 자신에 그림이 붙어 있을 수 있다 — 그건 선이 아니다.
            if (t_rect == _vs) continue;

            if (t_rect.anchoredPosition.x < 0f && this.m_vsLeft == null)
            {
                this.m_vsLeft     = t_rect;
                this.m_vsLeftHome = t_rect.anchoredPosition;
            }
            else if (t_rect.anchoredPosition.x > 0f && this.m_vsRight == null)
            {
                this.m_vsRight     = t_rect;
                this.m_vsRightHome = t_rect.anchoredPosition;
            }
        }
    }

    // 물러났다 부딪히고 제자리로. 홈 좌표는 호출자가 Awake에서 잡아 둔 값이라 반복해도 밀리지 않는다.
    //
    // ⚠ 시작 좌표를 홈으로 못 박지 않는다 — 조임이 두 배너를 서로에게 끌어다 놓은 채로 넘겨주기 때문이다.
    //   여기서 홈으로 되돌리면 그 끌림이 한 프레임에 사라지고(눈에 걸리는 스냅), 쌓아 둔 압력도 함께 풀린다.
    //   물러남은 지금 있는 자리에서 이어지고, 그래서 "당겨졌다 놓는" 반동으로 읽힌다.
    void StageClash(Sequence _seq, RectTransform _rect, Vector2 _home, Vector2 _step)
    {
        if (_rect == null) return;

        _rect.DOKill();

        Vector2 t_back = _step.sqrMagnitude > 0.0001f ? -_step.normalized * this.windUpDistance : Vector2.zero;

        // 세 구간 사이의 빈 시간이 곧 정지다. 트윈을 걸지 않은 구간에는 아무것도 이 배너를 움직이지 않는다 —
        // 물러난 최고점(windUpHold)과 부딪힌 자리(impactHold), 두 번 얼어붙는다.
        _seq.Insert(0f, _rect.DOAnchorPos(_home + t_back, this.windUpDuration).SetEase(Ease.OutQuad));

        _seq.Insert(this.ImpactAt,
                    _rect.DOAnchorPos(_home + _step, this.impactDuration).SetEase(Ease.InQuad));

        // 히트스톱이 끝나면 튕겨 돌아온다. OutBack의 오버슈트가 여기서는 반동이라 그대로 둔다 —
        // 얼어 있다 풀리는 자리라 오히려 튕김이 있어야 한다.
        _seq.Insert(this.HitAt + this.impactHold,
                    _rect.DOAnchorPos(_home, this.settleDuration).SetEase(Ease.OutBack));
    }

    // 서로에게 끌린다. 가속 곡선이라 처음엔 안 보이다가 끝에 가서 알아챈다 —
    // 등속이면 "이동"으로 읽혀 충돌의 예비동작이 미리 소진된다.
    //
    // ⚠ 여기서는 DOKill도, 좌표 못 박기도 하지 않는다. 이 안무는 꽂힘(BuildFound) 뒤에 이어 붙는데
    //   두 시퀀스가 같은 프레임에 지어지기 때문이다 — 그 자리에서 카드를 건드리면 방금 세운 슬램의
    //   출발 자세(들려 있음)와 트윈이 통째로 지워져 상대가 제자리에서 그냥 나타난다.
    //   목표만 절대 좌표로 주고, 출발은 트윈이 시작하는 시점(=슬램이 끝나 홈에 꽂힌 뒤)에 알아서 읽힌다.
    void InsertDrift(Sequence _seq, RectTransform _rect, Vector2 _home, Vector2 _step, float _duration)
    {
        if (_rect == null || this.chargeDrift <= 0f) return;

        Vector2 t_dir = _step.sqrMagnitude > 0.0001f ? _step.normalized : Vector2.zero;

        _seq.Insert(0f, _rect.DOAnchorPos(_home + t_dir * this.chargeDrift, _duration).SetEase(Ease.InQuad));
    }

    // 화면이 점점 떨린다. DOShakePosition을 쓰지 않는 이유는 진폭이 고정이기 때문이다 —
    // 여기서 필요한 것은 "일정하게 흔들림"이 아니라 "점점 커짐"이라, 진행도를 직접 몰아 진폭을 제곱으로 붙인다.
    //
    // 난수가 아니라 사인 두 겹이다. 난수는 프레임 레이트에 따라 세기가 달라 보이고, 무엇보다 되돌릴 기준이 없다.
    void InsertTremor(Sequence _seq, RectTransform _root, float _duration)
    {
        if (_root == null || this.chargeShake <= 0f) return;

        Vector2 t_home = _root.anchoredPosition;
        this.m_rootHome    = t_home;
        this.m_rootCaptured = true;

        float t_progress = 0f;

        _seq.Insert(0f, DOTween.To(() => t_progress, _v =>
                                   {
                                       t_progress = _v;

                                       float t_time = _v * _duration;
                                       float t_amp  = this.chargeShake * _v * _v;
                                       float t_w    = this.chargeShakeFreq * Mathf.PI * 2f;

                                       // 두 축의 주기를 어긋나게 둔다 — 같으면 대각선으로 미끄러지는 것으로 보인다.
                                       _root.anchoredPosition = t_home + new Vector2(
                                           Mathf.Sin(t_time * t_w)         * t_amp,
                                           Mathf.Sin(t_time * t_w * 1.37f) * t_amp * 0.7f);
                                   },
                                   1f, _duration).SetEase(Ease.Linear));

        // 충돌이 무대를 갈아타며 이 시퀀스를 죽인다 — 죽는 자리가 어디든 화면은 제자리로 돌려놓아야 한다.
        _seq.OnKill(() => { if (_root != null) _root.anchoredPosition = t_home; });
    }

    // VS가 뜰 자리에 빛이 고인다. 아직 VS는 꺼져 있고, 충돌 프레임에 이 빛이 터지며 그 자리를 VS에게 넘긴다.
    // 스캔 띠와 같은 자가설치 규약이다 — 프리팹에 배선할 자리를 만들지 않는다.
    void InsertChargeGlow(Sequence _seq, RectTransform _root, Vector2 _vsAnchored, float _duration)
    {
        this.ClearCharge();

        Sprite t_sprite = this.RaySprite;
        if (_root == null || t_sprite == null || this.chargeGlowSize <= 0f) return;

        var t_go = new GameObject("ChargeGlow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var t_rt = (RectTransform)t_go.transform;

        t_rt.SetParent(_root, false);

        // VS와 같은 앵커(중앙)라 저작 좌표를 그대로 옮겨 쓸 수 있다 — VS가 꺼져 있어도 자리를 알 수 있는 이유다.
        t_rt.anchorMin = t_rt.anchorMax = t_rt.pivot = new Vector2(0.5f, 0.5f);
        t_rt.anchoredPosition = _vsAnchored;
        t_rt.sizeDelta        = new Vector2(this.chargeGlowSize, this.chargeGlowSize);
        t_rt.localScale       = Vector3.one * this.chargeGlowFrom;

        // 배너·VS보다 뒤, 딤보다는 앞. 맨 앞 형제로 보내면 딤(검정 89%) 뒤로 들어가 아예 안 보인다 —
        // 이 화면의 딤은 0번 자식이라 "뒤로 보낸다"가 곧 "지운다"가 된다.
        PlaceJustAboveDim(t_rt, this.dim.Target);

        var t_image = t_go.GetComponent<Image>();
        t_image.sprite         = t_sprite;
        t_image.raycastTarget  = false;
        t_image.preserveAspect = true;
        t_image.color          = new Color(this.chargeGlowColor.r, this.chargeGlowColor.g, this.chargeGlowColor.b, 0f);

        UiAdditive.Apply(t_go);

        this.m_chargeGlow = t_image;

        _seq.Insert(0f, t_rt.DOScale(this.chargeGlowTo, _duration).SetEase(Ease.InQuad));
        _seq.Insert(0f, t_image.DOFade(this.chargeGlowAlpha, _duration).SetEase(Ease.InQuad));
    }

    // 고인 빛이 터진다. 조임 시퀀스는 이미 죽었으므로 빛의 트윈도 함께 죽어 있다 — 여기서 다시 건다.
    void InsertGlowBurst(Sequence _seq, float _at)
    {
        Image t_glow = this.m_chargeGlow;
        if (t_glow == null) return;

        var t_rt = (RectTransform)t_glow.transform;

        _seq.InsertCallback(_at, () =>
        {
            if (t_glow == null) return;

            t_glow.DOKill();
            t_rt.DOKill();

            t_rt.DOScale(this.chargeGlowTo * this.releaseGlowScale, this.releaseGlowBurst).SetEase(Ease.OutQuad);
            t_glow.DOFade(0f, this.releaseGlowBurst).SetEase(Ease.OutQuad);
        });

        // 다 터진 뒤에 걷는다. 남겨 두면 다음 매칭이 알파 0짜리 판을 물려받아 두 장이 겹친다.
        _seq.InsertCallback(_at + this.releaseGlowBurst, this.ClearCharge);
    }

    void InsertDimPunch(Sequence _seq, float _at, float _level)
    {
        if (Mathf.Approximately(_level, 0f)) return;

        _seq.Insert(_at, this.dim.TweenLevel(_level, 0.05f).SetEase(Ease.OutQuad));
        _seq.Insert(_at + 0.05f, this.dim.TweenLevel(0f, 0.3f).SetEase(Ease.OutQuad));
    }

    // 섬광은 자가설치 레이어라 배선할 자리가 없다 — 없으면 이 축만 조용히 빠진다.
    void InsertFlash(Sequence _seq, float _at, ScreenFlashCover _cover)
    {
        if (_cover == null || _cover.peak <= 0f) return;
        if (!ScreenFlash.TryGet(out var t_flash)) return;

        _seq.InsertCallback(_at, () =>
        {
            var t_cover = t_flash.BuildCover(_cover);
            t_cover?.Play();
        });
    }

    void InsertInfo(Sequence _seq, float _at, Graphic _target, int _order)
    {
        if (_target == null) return;

        var t_rt   = (RectTransform)_target.transform;
        var t_home = t_rt.anchoredPosition;

        t_rt.DOKill();
        _target.DOKill();

        t_rt.anchoredPosition = t_home + new Vector2(this.infoSlide, 0f);
        SetAlpha(_target, 0f);

        float t_start = _at + this.infoStagger * _order;

        _seq.Insert(t_start, t_rt.DOAnchorPos(t_home, 0.18f).SetEase(Ease.OutCubic));
        _seq.Insert(t_start, _target.DOFade(1f, 0.14f).SetEase(Ease.OutQuad));
    }

    static void RestoreCard(MatchProfileView _view)
    {
        if (_view == null) return;

        _view.Rect.DOKill();
        _view.Rect.localScale = Vector3.one;

        RestoreInfo(_view.NicknameText);
        RestoreInfo(_view.RankNameText);
    }

    // 좌표는 되돌리지 않는다 — 이 글자들은 셸이 홈을 들고 있지 않아, 되돌릴 기준이 트윈이 잡아 둔 값뿐이다.
    // DOKill(complete) 대신 알파만 세우는 이유도 같다.
    static void RestoreInfo(Graphic _target)
    {
        if (_target == null) return;

        _target.transform.DOKill(complete: true);
        _target.DOKill();
        SetAlpha(_target, 1f);
    }

    static void SetAlpha(Graphic _target, float _alpha)
    {
        var t_c = _target.color;
        t_c.a = _alpha;
        _target.color = t_c;
    }

    /// <summary>
    /// 자가설치한 빛을 딤 바로 앞에 꽂는다(uGUI는 나중 형제를 위에 그린다).
    /// 딤이 없거나 다른 부모에 있으면 맨 뒤로 보낸다 — 가릴 것이 없으니 그게 안전한 쪽이다.
    /// </summary>
    /// <remarks>전환(MatchHandoffFx)의 빛줄기도 같은 규칙을 쓴다 — 두 곳 다 이 화면의 딤 아래로 숨으면 안 된다.
    /// 이 화면의 자가설치 빛이 지켜야 할 규칙이 하나뿐이도록 여기가 단일 진실원이다.</remarks>
    public static void PlaceJustAboveDim(RectTransform _node, Graphic _dim)
    {
        if (_dim != null && _dim.transform.parent == _node.parent)
        {
            _node.SetSiblingIndex(_dim.transform.GetSiblingIndex() + 1);

            return;
        }

        _node.SetAsFirstSibling();
    }
}
