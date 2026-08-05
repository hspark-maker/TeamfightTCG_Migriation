using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 카드 강화 한 번의 연출(CardDetailOverlay 루트에 부착).
// 담금질이다 — 카드가 움츠러들며 달아오르고(고조), 그 빛이 카드를 통째로 삼킨 뒤(정적), 백열이 걷히며 터진다(공개).
// 실패는 그 열이 터지지 못하고 훅 꺼져 식는 모습으로 갈린다 — 성공은 빛이 밖으로 걷히고, 실패는 안에서 죽는다.
//
// ⚠ 결과는 백열이 카드를 완전히 덮은 뒤에 갈린다 — 성공·실패가 같은 백지에서 출발해야 결과가 미리 새지 않는다.
//
// 판정은 하지 않는다 — 강화는 CardGrowthManager.TryEnhance가 이미 원자적으로 끝낸 거래이고,
// 여기서는 그 결과를 보여줄 뿐이다(PackRevealView와 같은 결).
//
// ⚠ 값 반영 시점의 진실원은 호출부다. 연출 중에 Lv·HP가 먼저 튀면 공개할 것이 없으므로,
//   호출부가 갱신을 유예했다가 _onReveal에서 한 번에 반영한다.
//
// ⚠ 결과 문구도 여기서 찍지 않는다 — 결과를 읽는 화면은 EnhanceResultPanelView가 따로 진다.
//   이 무대는 카드가 달아올랐다 터지는 것까지만 책임지고, 결과를 남긴 채 멈춰 복귀 신호를 기다린다.
//
// ⚠ 파티클을 쓰지 않는다 — 이 캔버스가 Overlay라 ParticleSystem이 렌더되지 않는다(PackCardView 주석 참고).
//
// 표면 연출은 AllIn1SpriteShader(UiMask 변형)가 진다. 올리는 축은 전부 UV 위치와 무관한 것들이라
// 카드가 여러 장의 이미지로 쪼개져 있어도 재질 인스턴스 하나를 함께 쓰면 조각나지 않는다.
// (반대로 SHINE 같은 UV 의존 축은 이미지마다 rect가 달라 경계에서 어긋난다 — 그래서 쓰지 않는다.)
//
// 다만 재질은 자기가 얹힌 이미지만 덮는다. 글자·아이콘까지 삼키는 마지막 한 겹은 셰이더가 아니라
// 카드 실루엣 모양의 판(floodCover) 하나가 진다 — 카드가 몇 조각으로 쪼개져 있든 덮개는 한 장이면 된다.
//
// 그 덮개는 이미지가 한 장이므로 UV 의존 축을 써도 된다(rect가 하나뿐이라 어긋날 경계가 없다).
// 실패의 꺼짐이 이걸 쓴다 — FADE로 덮개만 얼룩덜룩 갉아 없앤다. 지워지는 것은 빛이지 카드가 아니다.
//
// ⚠ 셰이더 키워드는 런타임에 켜지 않는다 — shader_feature라 빌드에서 미사용 변형이 스트립되고,
//   첫 EnableKeyword는 변형 컴파일 렉을 만든다. 필요한 키워드는 .mat 자산에 켜둔 채 값만 민다.
public class CardEnhanceRitualView : MonoBehaviour
{
    [Header("무대 (미배선이면 연출 없이 콜백만 즉시 흘린다)")]
    [Tooltip("압축·진동·낙하를 받는 노드(CardSlot). 카드 그림의 부모여야 카드가 통째로 움직인다.\n" +
             "⚠ LayoutGroup에 구동되지 않는 노드여야 한다 — 매 프레임 좌표가 되돌려지면 진동이 보이지 않는다.")]
    [SerializeField] RectTransform cardStage;

    [Tooltip("화면을 덮은 딤. 알파는 건드리지 않고 색만 민다 — 알파를 내리면 구도가 바뀐다(PackScreenFlash와 같은 규칙).")]
    [SerializeField] Graphic dim;

    [Header("걷히는 패널 (선택)")]
    [Tooltip("연출 동안 사라졌다 돌아올 패널들(DetailPanel·BottomBar).\n" +
             "⚠ SetActive로 끄지 않는다 — 루트 VerticalLayoutGroup에서 CardArea가 남는 높이를 전부 먹어 카드 크기가 튄다.")]
    [SerializeField] CanvasGroup[] retractGroups;

    [Header("셰이딩 (선택 — 미배선이면 이 축을 통째로 건너뛴다)")]
    [Tooltip("카드 본체 이미지들(Frame·Portrait·프레임 장식). 재질 인스턴스 한 장을 함께 쓴다.\n" +
             "⚠ TMP 텍스트는 넣지 않는다 — 자체 재질을 쓰므로 덮어쓰면 글자가 깨진다.")]
    [SerializeField] Graphic[] cardSurfaces;
    [Tooltip("Materials/Growth/CardRitualBody. GLOW·GREYSCALE·HITEFFECT·INNEROUTLINE·SHAKEUV가 켜져 있어야 한다.")]
    [SerializeField] Material bodyMaterial;

    [Tooltip("카드 실루엣을 그대로 덮는 판(Frame 스프라이트를 그대로 쓰는 Image, 알파 0·Raycast 끔).\n" +
             "⚠ CardUIView의 맨 마지막 자식이어야 한다 — 글자·아이콘 위에 오지 않으면 그것들만 남아 뜬다.\n" +
             "미배선이면 cardSurfaces에 물린 이미지만 하얘지고 나머지는 그대로 보인다.")]
    [SerializeField] Graphic floodCover;

    [Header("카드 뒤 후광 (선택)")]
    [Tooltip("카드 뒤에서 조여드는 빛. 에셋 후보: Sprites/CardPack/Glow_Radial.")]
    [SerializeField] Graphic backGlow;

    [Header("담금질 — 온도")]
    [Tooltip("달아오르기 시작할 때의 빛색(잉걸).")]
    [SerializeField] Color emberColor    = new Color(1f, 0.50f, 0.15f, 1f);
    [Tooltip("정점의 빛색(백열). 여기까지 색이 올라와야 다음이 '터진다'로 읽힌다.")]
    [SerializeField] Color whiteHotColor = new Color(1f, 0.93f, 0.78f, 1f);
    [Tooltip("정점의 자체 발광 세기(_Glow). 1을 넘기면 카드 색이 날아가기 시작한다.")]
    [SerializeField] float heatGlow      = 1.6f;
    [Tooltip("정점의 테두리 불(_InnerOutlineAlpha). 카드 실루엣 안쪽을 따라 빛이 붙는다.")]
    [Range(0f, 1f)]
    [SerializeField] float rimStrength   = 0.75f;
    [Tooltip("정점의 픽셀 진동 폭(_ShakeUvX/Y). 크게 주면 스프라이트 밖을 샘플링해 이웃 그림이 새어든다.")]
    [Range(0f, 2f)]
    [SerializeField] float pixelShake    = 0.35f;

    [Header("과열 — 빛이 카드를 삼킨다")]
    [Tooltip("카드를 덮는 백열의 최대 세기(_HitEffectBlend). 1이면 카드가 흰 실루엣만 남는다.")]
    [Range(0f, 1f)]
    [SerializeField] float blindPeak     = 1f;
    [Tooltip("고조 구간의 어디서부터 면이 덮이기 시작하는가(0~1). 앞쪽은 테두리 불만 올라 '가장자리부터 달아오른다'가 된다.")]
    [Range(0f, 1f)]
    [SerializeField] float overheatStart = 0.4f;
    [Tooltip("고조가 끝나는 시점의 잠식률. 나머지는 정적 구간이 마저 덮는다 — 여기서 이미 1이면 삼켜지는 과정이 안 보인다.")]
    [Range(0f, 1f)]
    [SerializeField] float overheatRise  = 0.55f;

    [Header("실패 — 훅 꺼진다")]
    [Tooltip("Materials/Growth/CardRitualEmber. FADE가 켜져 있어야 한다. floodCover에만 얹는다(이미지 한 장이라 UV 축이 안전하다).\n" +
             "미배선이면 덮개가 얼룩 없이 균일하게 걷힌다 — 꺼지는 것이 아니라 페이드아웃으로 읽힌다.")]
    [SerializeField] Material coverMaterial;
    [Tooltip("백열이 죽는 시간. 짧아야 '꺼졌다'가 된다 — 길어지면 불이 카드를 먹는 것으로 읽힌다.")]
    [SerializeField] float snuffDuration  = 0.1f;
    [Tooltip("꺼진 직후 남는 잔열. 면의 빛(_Glow)은 제곱이라 이 값에서 이미 없고 테두리선만 남는다 — 불이 있었다는 유일한 증거.")]
    [Range(0f, 0.5f)]
    [SerializeField] float emberHeat      = 0.22f;
    [Tooltip("잔열이 사그라들어 카드가 차갑게 식기까지의 시간.")]
    [SerializeField] float emberFade      = 0.35f;
    [Tooltip("빛이 사라진 순간 딤이 결과 밝기보다 더 내려가는 깊이. 눈이 정점의 빛에 적응해 있어서 이 과암이 있어야 낙차가 몸으로 온다.")]
    [Range(0f, 0.6f)]
    [SerializeField] float blackoutDepth  = 0.3f;

    [Header("성공 잔광 — 카드 가장자리")]
    [Tooltip("공개 뒤 카드에 남는 열. 테두리 불(_InnerOutlineAlpha)이 여기에 비례해 남는다 — 방금 벼려낸 쇠붙이의 결.")]
    [Range(0f, 1f)]
    [SerializeField] float afterglowHeat  = 0.35f;
    [Tooltip("공개 뒤 카드 뒤에 남는 후광의 알파. 은은해야 한다 — 여기서 세면 결과 카드가 안 읽힌다.")]
    [Range(0f, 1f)]
    [SerializeField] float afterglowAlpha = 0.3f;
    [Tooltip("잔광 후광의 크기. 카드보다 조금 커야 가장자리에서 새어 나오는 것으로 읽힌다.")]
    [SerializeField] float afterglowScale = 1.12f;

    [Header("성공 화면 덮개 (선택)")]
    [Tooltip("성공에만 쏜다. 실패까지 화면이 반응하면 성공의 대비가 사라진다.")]
    [SerializeField] bool             useScreenFlash = true;
    [SerializeField] ScreenFlashCover successFlash   = new ScreenFlashCover { rise = 0.05f, hold = 0.02f, fall = 0.3f, peak = 0.55f };

    [Header("박자")]
    [SerializeField] float enterDuration   = 0.15f;
    [SerializeField] float buildUpDuration = 1.2f;
    [Tooltip("정점에서 모든 것이 멈추는 시간. 이 정적이 없으면 고조가 결과로 흘러들어 판정 순간이 뭉개진다.")]
    [SerializeField] float holdDuration    = 0.25f;
    [SerializeField] float resultHold      = 0.7f;
    [SerializeField] float returnDuration  = 0.35f;

    [Header("세기 — 카드는 조여들었다가 터진다")]
    [Tooltip("진입에서 당겨지는 배율. 1보다 작아야 '움츠린다'로 읽힌다.")]
    [SerializeField] float enterScale    = 0.92f;
    [Tooltip("고조 끝의 압축 배율. 여기까지 쉬지 않고 조여든다.")]
    [SerializeField] float compressScale = 0.78f;
    [Tooltip("정적 구간의 최대 압축. 이 한 뼘이 폭발의 반동을 만든다.")]
    [SerializeField] float holdScale     = 0.74f;
    [Tooltip("성공 순간 튀어오르는 배율. 즉시 이 크기가 되었다가 제자리로 회수된다.")]
    [SerializeField] float burstScale    = 1.35f;
    [Tooltip("폭발이 제자리로 회수되는 시간.")]
    [SerializeField] float burstSettle   = 0.4f;

    [Tooltip("고조 마지막 구간의 몸통 진동 폭(px). 앞 구간은 이 값의 1/4, 1/2로 커진다.")]
    [SerializeField] float shakeStrength    = 9f;
    [SerializeField] float failDrop         = 20f;
    [SerializeField] float failShake        = 6f;
    [Range(0f, 1f)]
    [SerializeField] float failDesaturation = 0.85f;
    [Range(0f, 1f)]
    [SerializeField] float glowPeakAlpha    = 0.9f;
    [SerializeField] float glowStartScale   = 1.6f;

    [Header("딤 색")]
    [SerializeField] Color dimDarkColor   = new Color(0.02f, 0.02f, 0.05f, 1f);
    [SerializeField] Color dimBrightColor = new Color(0.30f, 0.28f, 0.45f, 1f);
    [Tooltip("결과를 읽는 동안의 밝기(-1 어둠 ~ +1 빛). 정점의 빛이 여기까지 가라앉아야 위에 뜨는 결과판 글자가 읽힌다.")]
    [Range(-1f, 1f)]
    [SerializeField] float resultDimLevel = -0.45f;

    // 카드가 완전히 덮인 채 머무는 한 박. 값 반영은 이 백지 위에서 일어난다 — 눈이 숫자가 바뀌는 과정을 보지 못한다.
    const float BlindRise = 0.05f;

    // 셰이더가 '아직 멀쩡함'으로 보는 _FadeAmount. 0이 아니라 음수라, 꺼짐 축은 이 구간을 감춘 0~1로 민다.
    const float FadeIdle = -0.1f;

    // 꺼짐 뒤의 낙하(0.25) + 착지 흔들림(0.28)이 끝나기 전에 복귀가 시작되면 두 트윈이 같은 좌표를 다툰다.
    float FailSettle => Mathf.Max(0.02f, this.snuffDuration) + 0.53f;

    // 삼켜지는 동안 후광이 카드 밖으로 번지는 크기. 카드 실루엣에 딱 맞으면 빛이 판때기처럼 잘려 보인다.
    const float GlowFloodScale = 1.25f;

    static readonly int P_Glow              = Shader.PropertyToID("_Glow");
    static readonly int P_GlowColor         = Shader.PropertyToID("_GlowColor");
    static readonly int P_InnerOutlineAlpha = Shader.PropertyToID("_InnerOutlineAlpha");
    static readonly int P_InnerOutlineColor = Shader.PropertyToID("_InnerOutlineColor");
    static readonly int P_ShakeUvX          = Shader.PropertyToID("_ShakeUvX");
    static readonly int P_ShakeUvY          = Shader.PropertyToID("_ShakeUvY");
    static readonly int P_GreyscaleBlend    = Shader.PropertyToID("_GreyscaleBlend");
    static readonly int P_HitEffectBlend    = Shader.PropertyToID("_HitEffectBlend");
    static readonly int P_HitEffectColor    = Shader.PropertyToID("_HitEffectColor");
    static readonly int P_FadeAmount        = Shader.PropertyToID("_FadeAmount");

    Sequence m_seq;
    Action   m_onReveal;
    Action   m_onSettled;
    Action   m_onFinished;

    // 결과를 무대에 남긴 채 복귀 신호(PlayReturn)를 기다리는 중. 이 동안에도 재진입은 막혀야 하므로 IsPlaying에 포함된다.
    bool m_awaitingReturn;

    // 잘라내는 중. 이때 흘리는 콜백이 호출부를 타고 PlayReturn으로 되돌아오면 걷어내는 도중에 새 시퀀스가 선다.
    bool m_cancelling;

    // 무대를 걷은 채로 남겨 두고 다음 연출을 기다리는 중("한 번 더" 경로). 다음 Play가 이 자세를 이어받는다.
    bool m_stageRetracted;

    // 카드에 얹은 재질 사본. 자산을 직접 밀면 같은 셰이더를 쓰는 다른 화면까지 함께 달아오른다.
    Material m_body;
    Material m_cover;

    // cardStage·딤의 authoring 상태. 연출 중간값을 기준으로 잡으면 반복할수록 자리가 밀린다 → 1회만 캡처한다.
    Vector2 m_baseAnchored;
    Color   m_baseDim;
    bool    m_baseCaptured;

    // 트윈이 이어 붙을 때의 출발점들. getter가 시작 시점에 한 번 읽히므로 앞 구간이 남긴 값에서 이어진다.
    float m_dimLevel;   // -1 어둠 ~ +1 빛
    float m_heat;       // 0 평상 ~ +1 백열
    float m_shake;      // 픽셀 진동 0~1
    float m_snuff;      // 덮개가 꺼진 정도 0~1

    /// <summary>연출이 진행 중인가(결과를 남긴 채 기다리는 동안도 포함).
    /// 호출부는 이 동안 강화 재입력·카드 넘기기·닫기를 막는다.</summary>
    public bool IsPlaying => this.m_awaitingReturn || (this.m_seq != null && this.m_seq.IsActive());

    /// <summary>강화 결과를 한 번 보여준다. _outcome은 Success/Failed만 온다(나머지는 결제 전 차단이라 보여줄 것이 없다).
    ///
    /// _onReveal은 카드가 빛에 완전히 덮인 시점 — 호출부가 여기서 값을 화면에 반영한다(보이지 않는 갱신).
    /// _onSettled는 카드 위 연출이 다 끝난 시점 — 호출부가 여기서 결과판을 띄운다. 둘을 나누는 이유는
    /// 값 반영은 빛 아래에서 끝나야 하고, 읽을 것은 카드가 조용해진 뒤에 와야 서로를 잡아먹지 않기 때문이다.
    ///
    /// _awaitReturn이면 결과를 무대에 남긴 채 멈추고 <see cref="PlayReturn"/>을 기다린다(결과판이 걷힐 때까지).
    /// 아니면 지금까지처럼 스스로 걷고 _onFinished까지 이어간다 — 결과판 미배선이 소프트락이 되면 안 된다.
    /// _onFinished는 그 복귀가 끝난 시점 — 호출부가 여기서 조작을 되살린다.
    ///
    /// 세 콜백은 스킵·중단·재진입 어느 경로로든 각각 정확히 한 번, 이 순서로 온다.</summary>
    public void Play(EEnhanceOutcome _outcome, bool _awaitReturn, Action _onReveal, Action _onSettled, Action _onFinished)
    {
        // 재진입은 호출부가 막지만 여기서도 닫는다 — 두 연출이 같은 노드를 두고 싸우면 카드가 굳는다.
        // 다만 콜백은 삼키지 않는다. 삼키면 호출부의 갱신 유예가 영영 풀리지 않아 버튼이 죽는다.
        if (IsPlaying)
        {
            _onReveal?.Invoke();
            _onSettled?.Invoke();
            _onFinished?.Invoke();
            return;
        }

        // 걷힌 무대를 그대로 물려받는가("한 번 더"). 플래그는 여기서 소비한다 — 이어받는 것은 이 한 번뿐이다.
        bool t_chained        = this.m_stageRetracted;
        this.m_stageRetracted = false;

        this.m_onReveal   = _onReveal;
        this.m_onSettled  = _onSettled;
        this.m_onFinished = _onFinished;

        if (this.cardStage == null)
        {
            // 무대가 없으면 보여줄 것이 없다. 값 반영까지 막지는 않는다(배선 실패가 소프트락이 되지 않게).
            // 결과판을 기다리는 경우엔 그 닫힘(PlayReturn)이 마무리를 이어받는다.
            this.m_awaitingReturn = _awaitReturn;
            FireReveal();
            FireSettled();
            if (!_awaitReturn) FireFinished();
            return;
        }

        CaptureBase();

        // 이어받는 경우엔 즉시 원복하지 않는다 — 결과 자세(실패의 잿빛·떨궈진 카드)를 진입 구간이 식히며 되돌린다.
        if (!t_chained) RestoreVisual();

        bool t_success = _outcome == EEnhanceOutcome.Success;

        // 저작값이 0이나 음수여도 구간이 서로를 넘지 않게 여기서 한 번 정리한다 — 아래 시간축은 이 값들만 쓴다.
        float t_enterDur  = Mathf.Max(0.01f, this.enterDuration);
        float t_riseDur   = Mathf.Max(0.06f, this.buildUpDuration);
        float t_stillDur  = Mathf.Max(0.02f, this.holdDuration);
        float t_resultDur = Mathf.Max(t_success ? 0.1f : FailSettle, this.resultHold);
        float t_backDur   = Mathf.Max(0.05f, this.returnDuration);

        float t_rise   = t_enterDur;
        float t_still  = t_rise + t_riseDur;
        float t_reveal = t_still + t_stillDur;
        float t_result = t_reveal + BlindRise;
        float t_return = t_result + t_resultDur;
        float t_end    = t_return + t_backDur;

        Sequence t_seq = DOTween.Sequence().SetLink(gameObject).SetId(this);

        BuildEnter(t_seq, t_enterDur, t_chained);
        BuildRise(t_seq, t_rise, t_riseDur);
        BuildStill(t_seq, t_still, t_stillDur);
        BuildReveal(t_seq, t_reveal);

        if (t_success) BuildSuccess(t_seq, t_result, t_resultDur);
        else           BuildFail(t_seq, t_result);

        // 카드 위 연출이 다 끝난 자리 = 결과판이 뜰 자리. 터지는 카드 위에 글자를 얹으면 둘 다 안 읽힌다.
        //
        // 결과를 남기고 멈추는 경우엔 이 시각이 곧 시퀀스의 끝이므로 신호를 OnKill로 미룬다 —
        // 시퀀스가 죽은 **뒤**에 흘려야 호출부가 곧바로 PlayReturn을 되받아 불러도 재진입이 없다.
        // (여기 콜백은 시퀀스 길이를 못 박는 역할만 한다. 위 트윈이 전부 미배선이면 여기 닿기 전에 끝나 버린다.)
        if (_awaitReturn)
        {
            t_seq.InsertCallback(t_return, () => { });
        }
        else
        {
            t_seq.InsertCallback(t_return, FireSettled);
            BuildReturn(t_seq, t_return, t_backDur, t_end);
        }

        // 정상 종료든 스킵이든 중단이든 여기로 온다 — 콜백 유실과 굳은 화면을 동시에 막는 안전망이다.
        t_seq.OnKill(() =>
        {
            // 콜백보다 상태가 먼저다 — 호출부가 FireSettled 안에서 PlayReturn을 부를 수 있고,
            // 그때 이미 "기다리는 중"이어야 복귀가 정상 경로를 탄다. 중단이었다면 CancelImmediate가 곧바로 지운다.
            this.m_seq            = null;
            this.m_awaitingReturn = _awaitReturn;

            FireReveal();
            FireSettled();

            if (_awaitReturn) return;

            RestoreVisual();
            FireFinished();
        });

        this.m_seq = t_seq;
        t_seq.Play();   // 재생 책임을 코드에 남긴다(PopupTransition과 같은 결).
    }

    /// <summary>남은 구간을 최종 상태로 끌어당긴다. 콜백은 순서대로 그대로 실행된다.
    /// 결과를 남기고 기다리는 동안은 끌어당길 것이 없다 — 그때의 입력은 결과판이 받는다.</summary>
    public void RequestSkip()
    {
        if (this.m_seq != null && this.m_seq.IsActive()) this.m_seq.Complete(true);
    }

    /// <summary>결과를 걷고 무대를 원래대로 되돌린다(결과판이 닫힐 때 호출부가 부른다).
    /// 기다리는 중이 아니면 남은 콜백만 흘린다 — 어느 경로로 와도 조작이 죽은 채 굳지 않게.</summary>
    public void PlayReturn()
    {
        // 잘라내는 중에 콜백을 타고 되돌아온 것이다 — 남은 콜백은 CancelImmediate가 마저 흘린다.
        if (this.m_cancelling) return;

        // 결과판이 무대보다 먼저 닫힐 수 있다(스킵 경로). 남은 구간을 끌어당겨야 복귀가 결과 자세에서 출발한다.
        if (this.m_seq != null && this.m_seq.IsActive()) this.m_seq.Complete(true);

        if (!this.m_awaitingReturn)
        {
            // 기다린 적이 없는데 불렸다 = 어딘가에서 순서가 어긋났다. 남은 콜백을 계약 순서대로 전부 흘린다 —
            // 여기서 _onFinished만 흘리면 값 반영을 못 받은 채 잠금만 풀려 화면과 세이브가 갈린다.
            FireReveal();
            FireSettled();
            FireFinished();
            return;
        }

        this.m_awaitingReturn = false;

        if (this.cardStage == null)
        {
            RestoreVisual();
            FireFinished();
            return;
        }

        float t_dur = Mathf.Max(0.05f, this.returnDuration);

        Sequence t_seq = DOTween.Sequence().SetLink(gameObject).SetId(this);
        BuildReturn(t_seq, 0f, t_dur, t_dur);

        t_seq.OnKill(() =>
        {
            this.m_seq = null;
            RestoreVisual();
            FireFinished();
        });

        this.m_seq = t_seq;
        t_seq.Play();   // 재생 책임을 코드에 남긴다(PopupTransition과 같은 결).
    }

    /// <summary>결과판이 "한 번 더"로 걷혔다 — 무대를 되돌리지 않고 대기만 푼다.
    /// 걷힌 패널·가라앉은 딤·결과 자세가 그대로 남으므로 **곧바로 <see cref="Play"/>로 이어야 한다**
    /// (다음 Play가 그 자세에서 이어 출발한다). 이을 수 없게 됐다면 <see cref="CancelImmediate"/>로 무대를 되돌릴 것 —
    /// 그냥 두면 상세 패널이 사라진 채 굳는다.
    ///
    /// 복귀를 건너뛸 뿐 콜백 계약은 그대로다 — _onFinished까지 여기서 흘린다.</summary>
    public void EndAwaitForChain()
    {
        if (this.m_cancelling) return;

        // 결과판이 무대보다 먼저 닫힐 수 있다(스킵 경로). 남은 구간을 끌어당겨 결과 자세에서 이어지게 한다.
        if (this.m_seq != null && this.m_seq.IsActive()) this.m_seq.Complete(true);

        if (!this.m_awaitingReturn)
        {
            // 기다린 적이 없다 = 이어받을 무대도 없다. 남은 콜백만 계약 순서대로 흘린다(PlayReturn과 같은 결).
            FireReveal();
            FireSettled();
            FireFinished();
            return;
        }

        this.m_awaitingReturn = false;
        this.m_stageRetracted = true;   // 콜백보다 먼저 — 호출부가 FireFinished 안에서 곧바로 Play를 되받아 부른다.

        FireFinished();
    }

    /// <summary>연출을 잘라내고 화면만 원복한다(카드 전환·닫힘 경로).
    /// 어느 단계에서 잘렸든 남은 콜백을 전부 흘린다 — 안 그러면 호출부의 값 갱신 유예가 영영 풀리지 않는다.</summary>
    public void CancelImmediate()
    {
        if (this.m_cancelling) return;
        this.m_cancelling = true;

        this.m_seq?.Kill();   // OnKill이 공개·정착 콜백을 흘린다.
        this.m_seq            = null;
        this.m_awaitingReturn = false;

        RestoreVisual();

        // 결과를 남긴 채 기다리다 잘린 경우엔 아직 안 나간 콜백이 있다. 이미 나간 것은 null이라 무해하다.
        FireReveal();
        FireSettled();
        FireFinished();

        this.m_cancelling = false;
    }

    // 첫 강화가 아니라 오버레이가 켜지는 순간 재질을 꽂는다 — 평상값이 전부 중립이라
    // 미리 얹어둬도 보이는 것이 달라지지 않고, 강화 순간의 재질 교체 프레임이 사라진다.
    void Awake()
    {
        EnsureMaterial();
    }

    void OnDisable()
    {
        // 잘린 채 굳은 압축·열·오프셋이 다음 열기로 새지 않게.
        CancelImmediate();
    }

    void OnDestroy()
    {
        if (this.m_body  != null) Destroy(this.m_body);
        if (this.m_cover != null) Destroy(this.m_cover);
    }

    // ── 구간 ─────────────────────────────────────────────

    // 패널이 걷히고 카드가 한 번 움츠러들며 식는다.
    void BuildEnter(Sequence _seq, float _dur, bool _chained)
    {
        _seq.InsertCallback(0f, () => SetRetractBlocking(false));

        if (this.retractGroups != null)
            foreach (CanvasGroup t_g in this.retractGroups)
            {
                if (t_g == null) continue;
                _seq.Insert(0f, t_g.DOFade(0f, _dur));   // 이어받는 경우엔 이미 걷혀 있어 제자리 트윈이다.
            }

        _seq.Insert(0f, this.cardStage.DOScale(this.enterScale, _dur).SetEase(Ease.OutCubic));

        // 어두워지는 것은 무대뿐이다 — 카드는 프리팹 그대로의 밝기로 서 있어야
        // 뒤이어 붙는 빛이 "이 카드가 달아올랐다"로 읽힌다('불이 꺼진 대장간'은 딤이 진다).
        _seq.Insert(0f, DimTween(-1f, _dur));

        if (!_chained) return;

        // 앞 결과가 남긴 자세를 이 구간이 데려온다 — 실패의 잿빛·떨궈진 자리가 "다시 식는다"로 읽히게.
        // (RestoreVisual로 즉시 원복하면 같은 되돌림이 한 프레임에 튄다.)
        _seq.Insert(0f, this.cardStage.DOAnchorPos(this.m_baseAnchored, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(0f, HeatTween(0f, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(0f, GreyTween(0f, _dur));
        _seq.Insert(0f, BlindTween(0f, _dur));
        _seq.Insert(0f, CoverTween(0f, _dur));

        if (this.backGlow != null)
        {
            _seq.Insert(0f, this.backGlow.DOFade(0f, _dur));
            _seq.Insert(0f, this.backGlow.rectTransform.DOScale(this.glowStartScale, _dur));
        }
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
        _seq.Insert(_at, HeatTween(1f, _dur).SetEase(Ease.InQuad));
        _seq.Insert(_at, ShakeTween(1f, _dur).SetEase(Ease.InQuad));

        if (this.backGlow != null)
        {
            _seq.Insert(_at, this.backGlow.DOFade(this.glowPeakAlpha, _dur).SetEase(Ease.InQuad));
            _seq.Insert(_at, this.backGlow.rectTransform.DOScale(1f, _dur).SetEase(Ease.OutQuad));
        }

        // 어둠에서 출발해 빛으로 차오른다.
        _seq.Insert(_at, DimTween(0.6f, _dur).SetEase(Ease.InQuad));

        BuildOverheat(_seq, _at, _dur);
    }

    // 잠식. 테두리에 붙은 불이 면으로 번진다 — 고조 앞쪽을 비워 두는 이유는, 처음부터 면이 덮이면
    // 카드가 그냥 흐려지는 것으로 읽히고 '가장자리부터 달아오른다'가 사라지기 때문이다.
    void BuildOverheat(Sequence _seq, float _at, float _dur)
    {
        if (this.m_body == null && this.floodCover == null) return;

        float t_from = _at + _dur * Mathf.Clamp01(this.overheatStart);
        float t_span = Mathf.Max(0.05f, _at + _dur - t_from);
        float t_rise = Mathf.Clamp01(this.overheatRise);

        // 잉걸빛으로 시작한다 — 처음부터 흰색이면 번지는 것이 열이 아니라 안개로 보인다.
        _seq.InsertCallback(t_from, () => SetBlindColor(this.emberColor));

        _seq.Insert(t_from, BlindTween(t_rise, t_span).SetEase(Ease.InQuad));
        _seq.Insert(t_from, BlindColorTween(Color.Lerp(this.emberColor, this.whiteHotColor, 0.6f), t_span));

        // 덮개는 본체보다 늦게 올라온다 — 같이 오르면 글자가 카드보다 먼저 지워져 순서가 뒤집힌 것처럼 보인다.
        _seq.Insert(t_from + t_span * 0.3f, CoverTween(t_rise * 0.7f, t_span * 0.7f).SetEase(Ease.InQuad));
    }

    // 정적. 몸은 멎고(진동이 그치고 카드가 한 뼘 더 눌린다) 빛만 남은 면을 마저 삼킨다 —
    // 이 마지막 한 겹이 덮이는 동안이 결과를 기다리는 시간이 된다.
    void BuildStill(Sequence _seq, float _at, float _dur)
    {
        _seq.Insert(_at, this.cardStage.DOAnchorPos(this.m_baseAnchored, Mathf.Min(0.08f, _dur)).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.cardStage.DOScale(this.holdScale, _dur).SetEase(Ease.OutQuad));

        // 떨림만 멎는다(숨을 참는다).
        _seq.Insert(_at, ShakeTween(0f, Mathf.Min(0.1f, _dur)).SetEase(Ease.OutQuad));

        _seq.Insert(_at, BlindTween(this.blindPeak, _dur).SetEase(Ease.InQuad));
        _seq.Insert(_at, CoverTween(1f, _dur).SetEase(Ease.InQuad));
        _seq.Insert(_at, BlindColorTween(this.whiteHotColor, _dur));
        _seq.Insert(_at, DimTween(1f, _dur).SetEase(Ease.InQuad));

        // 빛이 카드 밖으로 새어 나가야 실루엣이 잘린 판때기로 보이지 않는다.
        if (this.backGlow != null)
        {
            _seq.Insert(_at, this.backGlow.DOFade(1f, _dur).SetEase(Ease.InQuad));
            _seq.Insert(_at, this.backGlow.rectTransform.DOScale(GlowFloodScale, _dur).SetEase(Ease.InQuad));
        }
    }

    // 공개. 카드가 완전히 덮인 채로 한 박 머물고, 그 백지 위에서 값이 바뀐다.
    void BuildReveal(Sequence _seq, float _at)
    {
        // 앞 구간이 짧게 잘려 덜 덮인 채 도착했더라도 여기서 못 박는다 — 반쯤 덮인 카드 위에서 숫자가 바뀌면 다 보인다.
        if (this.m_body    != null) _seq.Insert(_at, BlindTween(this.blindPeak, BlindRise));
        if (this.floodCover != null) _seq.Insert(_at, CoverTween(1f, BlindRise));

        _seq.InsertCallback(_at + BlindRise, FireReveal);
    }

    // 폭발. 눌려 있던 것이 한 프레임에 터져 나왔다가 제자리로 회수된다 —
    // 부풀어 오르는 과정을 트윈에 맡기면 타격이 뭉개진다(PackCardView.PlayPunch와 같은 규칙).
    void BuildSuccess(Sequence _seq, float _at, float _hold)
    {
        float t_settle = Mathf.Max(0.05f, this.burstSettle);

        _seq.InsertCallback(_at, () =>
        {
            if (this.cardStage != null) this.cardStage.localScale = Vector3.one * this.burstScale;
        });
        _seq.Insert(_at, this.cardStage.DOScale(1f, t_settle).SetEase(Ease.OutQuint));

        // 백열이 걷히며 카드가 드러난다. 열은 잔열만 남기고 천천히 식는다 — 방금 벼려낸 쇠붙이의 결.
        _seq.Insert(_at, BlindTween(0f, t_settle).SetEase(Ease.InQuad));
        _seq.Insert(_at, CoverTween(0f, t_settle * 0.7f).SetEase(Ease.InQuad));
        _seq.Insert(_at + t_settle * 0.4f, HeatTween(this.afterglowHeat, t_settle).SetEase(Ease.OutQuad));

        // 정점의 빛이 배경까지 밝혀 둔 채다. 여기서 가라앉혀야 위에 뜨는 결과판 글자가 읽힌다.
        _seq.Insert(_at, DimTween(this.resultDimLevel, t_settle).SetEase(Ease.OutQuad));

        if (this.backGlow != null)
        {
            // 후광은 꺼지지 않고 카드 가장자리에 눌러앉는다 — 복귀 구간이 걷어간다.
            _seq.Insert(_at, this.backGlow.rectTransform.DOScale(this.afterglowScale, t_settle).SetEase(Ease.OutQuad));
            _seq.Insert(_at, this.backGlow.DOFade(this.afterglowAlpha, t_settle).SetEase(Ease.OutQuad));

            // 한 번 숨을 쉰다. 고정된 잔광은 배경 이미지로 보이고, 이 한 번의 들숨이 카드를 살아 있게 만든다.
            float t_breath = Mathf.Max(0.1f, _hold - t_settle);
            _seq.Insert(_at + t_settle,
                        this.backGlow.DOFade(this.afterglowAlpha * 0.5f, t_breath * 0.5f)
                            .SetEase(Ease.InOutSine).SetLoops(2, LoopType.Yoyo));
        }

        if (this.useScreenFlash && ScreenFlash.TryGet(out ScreenFlash t_flash))
        {
            Sequence t_cover = t_flash.BuildCover(this.successFlash);
            if (t_cover != null) _seq.Insert(_at, t_cover);
        }
    }

    // 열이 터지지 못하고 꺼진다. 타는 것은 전선이 이동하며 시간이 걸리고, 꺼지는 것은 제자리에서 순간이다 —
    // 그래서 방향 없이 전면에서 동시에, 짧게 죽인다. 지워지는 것은 덮개(빛)뿐이고 카드는 그 밑에 그대로 남는다.
    void BuildFail(Sequence _seq, float _at)
    {
        float t_snuff = Mathf.Max(0.02f, this.snuffDuration);
        float t_out   = _at + t_snuff;

        if (this.backGlow != null) _seq.Insert(_at, this.backGlow.DOFade(0f, 0.03f));

        // 덮개가 얼룩덜룩 갉혀 사라진다. 본체의 백열은 그보다 먼저 죽어야 뚫린 구멍으로 흰빛이 아니라 식은 카드가 보인다.
        _seq.Insert(_at, BlindTween(0f, t_snuff * 0.6f).SetEase(Ease.OutQuad));

        if (this.m_cover != null) _seq.Insert(_at, SnuffTween(1f, t_snuff).SetEase(Ease.OutQuad));
        else                      _seq.Insert(_at, CoverTween(0f, t_snuff).SetEase(Ease.InQuad));

        // 걷히고 나서 식는 것이 아니라, 걷었더니 이미 차갑다 — 그래서 냉각은 빛 아래에서 끝난다.
        _seq.Insert(_at, HeatTween(this.emberHeat, t_snuff * 0.8f).SetEase(Ease.OutQuad));
        _seq.Insert(_at, GreyTween(this.failDesaturation, t_snuff * 0.8f).SetEase(Ease.OutQuad));

        // 눈이 정점의 빛에 적응해 있다. 결과 밝기보다 한 번 더 내려갔다 올라와야 "빛이 사라졌다"가 몸으로 온다.
        _seq.Insert(_at,   DimTween(Mathf.Max(-1f, this.resultDimLevel - this.blackoutDepth), t_snuff).SetEase(Ease.OutQuad));
        _seq.Insert(t_out, DimTween(this.resultDimLevel, 0.35f).SetEase(Ease.InOutSine));

        // 다 꺼진 덮개를 중립으로 되돌린다. 알파와 잠식을 같은 프레임에 놓아야 되돌리는 과정이 보이지 않는다.
        _seq.InsertCallback(t_out, () => { SetCover(0f); SetSnuff(0f); });

        // 잔열. 면의 빛은 이미 없고 테두리선만 남아 사그라든다 — 카드 자체는 회색화만 뒤집어쓴 채 원래 밝기로 돌아온다.
        _seq.Insert(t_out, HeatTween(0f, Mathf.Max(0.05f, this.emberFade)).SetEase(Ease.InQuad));

        // 낙하는 꺼진 다음이다 — 같이 떨어지면 꺼짐이 낙하에 묻힌다.
        _seq.Insert(t_out, this.cardStage.DOScale(1f, 0.3f).SetEase(Ease.OutQuad));
        _seq.Insert(t_out, this.cardStage.DOAnchorPosY(this.m_baseAnchored.y - this.failDrop, 0.25f).SetEase(Ease.OutQuad));

        // 흔들림은 낙하가 끝난 뒤 — 같은 시간에 겹치면 두 트윈이 같은 좌표를 두고 싸운다.
        _seq.Insert(t_out + 0.25f, this.cardStage.DOShakeAnchorPos(0.28f, this.failShake, 12, 90f, false, true));
    }

    void BuildReturn(Sequence _seq, float _at, float _dur, float _end)
    {
        _seq.Insert(_at, HeatTween(0f, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(_at, GreyTween(0f, Mathf.Min(0.2f, _dur)));
        _seq.Insert(_at, BlindTween(0f, Mathf.Min(0.1f, _dur)));
        _seq.Insert(_at, CoverTween(0f, Mathf.Min(0.1f, _dur)));

        // 잠식은 덮개가 다 투명해진 뒤에 되돌린다 — 먼저 되돌리면 지워졌던 덮개가 한 프레임 되살아난다.
        _seq.InsertCallback(_at + Mathf.Min(0.1f, _dur), () => SetSnuff(0f));

        _seq.Insert(_at, this.cardStage.DOAnchorPos(this.m_baseAnchored, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(_at, this.cardStage.DOScale(1f, _dur).SetEase(Ease.OutQuad));
        _seq.Insert(_at, DimTween(0f, _dur));

        // 성공 잔광을 여기서 걷는다 — RestoreVisual에만 맡기면 마지막 프레임에 후광이 툭 끊긴다.
        if (this.backGlow != null)
        {
            _seq.Insert(_at, this.backGlow.DOFade(0f, _dur).SetEase(Ease.InQuad));
            _seq.Insert(_at, this.backGlow.rectTransform.DOScale(this.glowStartScale, _dur).SetEase(Ease.InQuad));
        }

        if (this.retractGroups != null)
            foreach (CanvasGroup t_g in this.retractGroups)
            {
                if (t_g == null) continue;
                _seq.Insert(_at, t_g.DOFade(1f, _dur));
            }

        // 길이를 못 박는다 — 위 트윈이 전부 미배선이면 시퀀스가 여기 닿기 전에 끝나 버린다.
        _seq.InsertCallback(_end, () => SetRetractBlocking(true));
    }

    // ── 재질 ─────────────────────────────────────────────

    // 사본을 한 번만 만들어 그대로 둔다. 평상값이 전부 중립(열 0·회색 0·백열 0)이라
    // 연출 밖에서는 기본 UI 재질과 구분되지 않는다 — 열 때마다 재질을 갈아끼울 이유가 없다.
    void EnsureMaterial()
    {
        if (this.m_body == null && this.bodyMaterial != null && this.cardSurfaces != null)
        {
            this.m_body = new Material(this.bodyMaterial) { name = this.bodyMaterial.name + " (ritual)" };

            foreach (Graphic t_g in this.cardSurfaces)
            {
                if (t_g != null) t_g.material = this.m_body;
            }
        }

        // 덮개는 본체와 다른 재질을 쓴다 — 본체가 못 쓰는 UV 의존 축(FADE)이 여기서만 성립한다.
        if (this.m_cover == null && this.coverMaterial != null && this.floodCover != null)
        {
            this.m_cover = new Material(this.coverMaterial) { name = this.coverMaterial.name + " (ritual)" };

            this.floodCover.material = this.m_cover;
        }
    }

    // 0이 평상(카드 원래 밝기), +1이 백열. 흩어진 프로퍼티를 한 축으로 묶어 구간마다 하나만 밀면 되게 한다.
    //
    // ⚠ 카드 색(_Color)은 어느 구간에서도 건드리지 않는다 — 달아오르기 전에 톤이 먼저 밀리면
    //   "이 카드가 달아오른다"가 아니라 "다른 카드로 바뀌었다"가 된다. 올리는 것은 없던 빛(글로·테두리)뿐이다.
    void SetHeat(float _level)
    {
        this.m_heat = _level;

        if (this.m_body == null) return;

        float t_hot = Mathf.Clamp01(_level);

        Color t_light = Color.Lerp(this.emberColor, this.whiteHotColor, t_hot);
        this.m_body.SetColor(P_GlowColor, t_light);
        this.m_body.SetColor(P_InnerOutlineColor, t_light);

        // 제곱으로 민다 — 선형이면 앞 절반에서 이미 밝아져 정점이 밋밋해진다.
        this.m_body.SetFloat(P_Glow, t_hot * t_hot * this.heatGlow);
        this.m_body.SetFloat(P_InnerOutlineAlpha, t_hot * this.rimStrength);
    }

    void SetShake(float _amount)
    {
        this.m_shake = _amount;

        if (this.m_body == null) return;

        this.m_body.SetFloat(P_ShakeUvX, _amount * this.pixelShake);
        this.m_body.SetFloat(P_ShakeUvY, _amount * this.pixelShake * 0.6f);
    }

    void SetGrey(float _blend)
    {
        if (this.m_body != null) this.m_body.SetFloat(P_GreyscaleBlend, _blend);
    }

    void SetBlind(float _blend)
    {
        if (this.m_body != null) this.m_body.SetFloat(P_HitEffectBlend, _blend);
    }

    // 본체의 백열과 덮개는 같은 빛이다 — 색을 따로 두면 경계에서 두 장으로 갈라져 보인다.
    void SetBlindColor(Color _color)
    {
        if (this.m_body != null) this.m_body.SetColor(P_HitEffectColor, _color);

        if (this.floodCover == null) return;

        _color.a = this.floodCover.color.a;
        this.floodCover.color = _color;
    }

    void SetCover(float _alpha)
    {
        SetGraphicAlpha(this.floodCover, _alpha);
    }

    // 0이면 덮개가 멀쩡하고 1이면 다 꺼졌다. 셰이더의 유휴값이 음수라 그 구간을 여기서 감춘다.
    void SetSnuff(float _amount)
    {
        this.m_snuff = _amount;

        if (this.m_cover != null) this.m_cover.SetFloat(P_FadeAmount, Mathf.Lerp(FadeIdle, 1f, _amount));
    }

    Tween HeatTween(float _to, float _dur)  => DOTween.To(() => this.m_heat, SetHeat, _to, _dur);
    Tween ShakeTween(float _to, float _dur) => DOTween.To(() => this.m_shake, SetShake, _to, _dur);
    Tween GreyTween(float _to, float _dur)  => DOTween.To(() => this.m_body != null ? this.m_body.GetFloat(P_GreyscaleBlend) : 0f, SetGrey, _to, _dur);
    Tween BlindTween(float _to, float _dur) => DOTween.To(() => this.m_body != null ? this.m_body.GetFloat(P_HitEffectBlend) : 0f, SetBlind, _to, _dur);

    Tween BlindColorTween(Color _to, float _dur)
        => DOTween.To(() => this.m_body != null ? this.m_body.GetColor(P_HitEffectColor) : Color.white, SetBlindColor, _to, _dur);

    Tween CoverTween(float _to, float _dur)
        => DOTween.To(() => this.floodCover != null ? this.floodCover.color.a : 0f, SetCover, _to, _dur);

    Tween SnuffTween(float _to, float _dur) => DOTween.To(() => this.m_snuff, SetSnuff, _to, _dur);

    // ── 상태 ─────────────────────────────────────────────

    void FireReveal()
    {
        Action t_cb = this.m_onReveal;
        this.m_onReveal = null;
        t_cb?.Invoke();
    }

    void FireSettled()
    {
        Action t_cb = this.m_onSettled;
        this.m_onSettled = null;
        t_cb?.Invoke();
    }

    void FireFinished()
    {
        Action t_cb = this.m_onFinished;
        this.m_onFinished = null;
        t_cb?.Invoke();
    }

    void CaptureBase()
    {
        if (this.m_baseCaptured) return;

        this.m_baseCaptured = true;
        this.m_baseAnchored = this.cardStage.anchoredPosition;
        if (this.dim != null) this.m_baseDim = this.dim.color;
    }

    // 걷힌 패널은 투명해도 여전히 입력을 먹는다 — 그대로 두면 그 위를 탭해도 스킵이 안 되는 죽은 영역이 생긴다.
    void SetRetractBlocking(bool _on)
    {
        if (this.retractGroups == null) return;

        foreach (CanvasGroup t_g in this.retractGroups)
        {
            if (t_g == null) continue;
            t_g.blocksRaycasts = _on;
        }
    }

    // 다음 연출이 중간값(압축·열·회색)에서 출발하지 않게 원복. 캡처 전이면 건드릴 것도 없다.
    void RestoreVisual()
    {
        // 무대가 제자리로 돌아오는 모든 길이 여기를 지난다 — 이어받을 자세도 여기서 무효가 된다.
        this.m_stageRetracted = false;

        if (!this.m_baseCaptured) return;

        if (this.cardStage != null)
        {
            this.cardStage.anchoredPosition = this.m_baseAnchored;
            this.cardStage.localScale       = Vector3.one;
        }

        this.m_dimLevel = 0f;
        if (this.dim != null) this.dim.color = this.m_baseDim;

        SetGraphicAlpha(this.backGlow, 0f);
        if (this.backGlow != null) this.backGlow.rectTransform.localScale = Vector3.one * this.glowStartScale;

        SetHeat(0f);
        SetShake(0f);
        SetGrey(0f);
        SetBlind(0f);
        SetCover(0f);
        SetSnuff(0f);
        SetBlindColor(Color.white);

        if (this.retractGroups != null)
            foreach (CanvasGroup t_g in this.retractGroups)
            {
                if (t_g == null) continue;
                t_g.alpha = 1f;
            }

        SetRetractBlocking(true);
    }

    // getter는 트윈이 시작할 때 한 번 읽힌다 — 그래서 앞 구간이 남긴 밝기에서 이어 출발한다(PackScreenFlash와 같은 관용구).
    Tween DimTween(float _level, float _duration)
    {
        return DOTween.To(() => this.m_dimLevel, SetDim, _level, _duration);
    }

    // -1이면 가장 어둡고 +1이면 가장 밝다. 알파는 언제나 원래 값 — 어둠의 두께는 그대로 두고 색만 민다.
    void SetDim(float _level)
    {
        this.m_dimLevel = _level;

        if (this.dim == null || !this.m_baseCaptured) return;

        Color t_c = _level < 0f ? Color.Lerp(this.m_baseDim, this.dimDarkColor, -_level)
                                : Color.Lerp(this.m_baseDim, this.dimBrightColor, _level);
        t_c.a = this.m_baseDim.a;
        this.dim.color = t_c;
    }

    static void SetGraphicAlpha(Graphic _g, float _a)
    {
        if (_g == null) return;

        Color t_c = _g.color;
        t_c.a = _a;
        _g.color = t_c;
    }
}
