using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public sealed class PackGradeFxPalette
{
    [SerializeField] Color rare = new Color(0.92f, 0.72f, 0.32f, 1f);
    [SerializeField] Color arcane = new Color(0.48f, 0.30f, 0.72f, 1f);
    [SerializeField] Gradient mythic = CreateMythicGradient();
    [Min(0f)] [SerializeField] float mythicCyclesPerSecond = 0.5f;

    public bool TryEvaluate(ECardGrade _grade, out Color _color)
    {
        if (_grade == ECardGrade.Rare)   { _color = rare; return true; }
        if (_grade == ECardGrade.Arcane) { _color = arcane; return true; }
        if (_grade == ECardGrade.Mythic && mythic != null)
        {
            _color = mythic.Evaluate(Mathf.Repeat(Time.unscaledTime * mythicCyclesPerSecond, 1f));
            return true;
        }

        _color = default;
        return false;
    }

    static Gradient CreateMythicGradient()
    {
        var t_gradient = new Gradient();
        t_gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.55f, 0.02f), 0f),
                new GradientColorKey(new Color(1f, 0.02f, 0.55f), 0.16f),
                new GradientColorKey(new Color(0.48f, 0.03f, 1f), 0.32f),
                new GradientColorKey(new Color(0.02f, 0.25f, 1f), 0.48f),
                new GradientColorKey(new Color(0.02f, 0.9f, 1f), 0.64f),
                new GradientColorKey(new Color(0.05f, 1f, 0.35f), 0.8f),
                new GradientColorKey(new Color(1f, 0.55f, 0.02f), 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return t_gradient;
    }
}

// 카드팩 개봉 연출의 진행자. 스테이지를 순서대로 몰고 가며, 각 단계의 실제 조작·표현은
// PackTearHandle(스와이프 제스처) · PackTearSkin(찢김 그림) · PackShellRig(팩의 몸짓) ·
// PackCardStack(카드 더미)이 나눠 맡는다.
//
// 흐름: 입장 → 스와이프 대기 → 자리잡기(팩이 화면 아래로) → 씰 찢기 → 뽑기(뭉치째 솟아오름)
//       → 한 장씩 밀어내기 → 결과 격자 → OnRevealComplete.
//
// 유저가 손을 대는 지점은 둘뿐이다 — 개봉을 여는 스와이프 한 번과, 그 뒤 카드를 넘기는 스와이프.
// 그 사이(자리잡기 → 씰 찢기 → 뽑기)는 손을 떼는 순간 끊기지 않고 자동으로 이어진다.
//
// 이 연출의 전제는 단 하나 — 카드는 처음부터 팩 속에 들어 있다.
//   BeginOpen 시점에 더미를 만들어 팩 앞뒷면 사이에 끼워 두고(PlaceInsidePack), 그 뒤로는 아무것도 "등장"시키지 않는다.
//   찢으면 구멍 너머로 카드 끝이 저절로 드러나고, 뽑으면 그 구멍에서 빠져나온다.
//   카드를 따로 페이드인하거나 패널을 띄우면 그 순간 "팩에서 꺼냈다"가 깨진다 — 옛 연출이 실패한 지점이 정확히 여기였다.
//
// ⚠ 계층 전제(PackShellRig 주석의 구성과 짝을 이룬다):
//   PackStage > [ShellBack, CardHost, ShellFront] — 카드가 팩 앞뒷면 사이에 끼어야 구멍으로만 보인다.
//   화면을 덮는 어둠(Dim)은 PackStage보다 앞 sibling이어야 한다 — 뒤에 두면 팩 속 카드를 덮어버린다.
//   결과 UI(RevealPanel)·스킵 버튼은 PackStage보다 뒤 sibling으로 두어 팩에 묻히지 않게 한다.
//
// 진입은 컨트롤러가 넘기는 OpenedPack(BeginOpen)뿐 — 구매·소유·덱은 이 뷰 밖의 책임이다.
// 연출은 이미 끝난 거래(TryPurchase가 원자 영속)를 보여줄 뿐, 경제를 건드리지 않는다.
public class PackRevealView : MonoBehaviour
{
    // 요약 도달 시 1회 발화(스킵으로 건너뛰어도 반드시 발화 — 획득 버튼 데드락 방지).
    public event Action OnRevealComplete;

    // 어느 씬의 어느 뷰든 팩이 열린 순간 발화(구독자는 씬 참조 없이 개봉 시점을 알 수 있다).
    // 발화 시점은 "팩이 열린 순간" = 스와이프 확정. 튜토리얼이 물려 있어 의미를 옮기지 않는다.
    public static event Action OnAnyPackOpened;

    [Header("팩")]
    [SerializeField] PackTearHandle tearHandle;    // 개봉을 여는 스와이프 제스처
    [SerializeField] PackTearSkin tearSkin;        // 찢김 그림(구멍·조각·그늘·빛)
    [SerializeField] PackShellRig shellRig;        // 팩의 몸짓(등장·부유·퇴장)
    [SerializeField] PackGradeFxPalette gradeFxPalette = new PackGradeFxPalette();

    [Tooltip("팩이 이만큼 아래에서 올라오며 등장한다(캔버스 참조px, 1440x3120 기준). 카드도 팩 속에 든 채 함께 올라온다.")]
    [SerializeField] float packEnterDrop = 811f;
    [SerializeField] float packEnterDuration = 0.45f;

    [Header("팩 속 카드")]
    [SerializeField] PackCardStack cardStack;
    [Tooltip("팩 속에 든 더미의 중심(무대 로컬 = 캔버스 참조px). 카드 윗변이 찢김선보다 조금 위여야 " +
             "봉인을 뜯는 순간 카드 끝이 삐져나온 것이 보인다 — 이 연출의 핵심 한 줄이다.")]
    [SerializeField] Vector2 cardInPackCenter = new Vector2(0f, 219f);
    [Tooltip("팩 속에 있을 때의 더미 배율. 팩 안쪽 폭에 겨우 들어갈 만큼만 줄인다 " +
             "— 최종 크기와 갭이 크면 \"뽑혔다\"가 아니라 \"커졌다\"로 읽힌다.")]
    [Range(0.3f, 1f)] [SerializeField] float cardInPackScale = 0.8f;
    [Tooltip("뽑혀 나온 더미가 정착하는 중심(화면 기준 = 캔버스 참조px, 0,0이 화면 한가운데). " +
             "팩은 화면 아래에 잠겨 있고 더미는 여기까지 올라온다 — 둘의 거리가 곧 \"뽑혀 나온 거리\"다. " +
             "카드가 700x930이라 y가 ~1095를 넘으면 윗변이 화면 밖으로 나간다(3120 참조 높이 기준).")]
    [SerializeField] Vector2 cardEmergeCenter = Vector2.zero;

    [Header("찢기 전 자리잡기")]
    [Tooltip("팩이 등장한 뒤 찢기 입력을 받기 전에 여기로 옮겨 간다(무대 오프셋, 캔버스 참조px). " +
             "팩 아래쪽이 화면 밖으로 조금 잠길 만큼 내려야 \"손에 쥔 팩에서 뽑아 올린다\"가 성립한다. " +
             "카드는 아직 팩 속이라 무대째 함께 내려간다 — 따로 움직이면 그 순간 \"속에 들어 있다\"가 깨진다.")]
    [SerializeField] Vector2 packOpenOffset = new Vector2(0f, -900f);
    [Tooltip("자리를 잡으며 팩이 커지는 배율. 다가오는 만큼 시선이 팩에 묶인다 — 뒤이어 씰이 찢기는 곳이 여기다. " +
             "뽑혀 나오는 카드는 이 값을 되물려 제 크기로 서므로, 키워도 결과 카드 크기는 변하지 않는다. " +
             "다만 팩이 커진 만큼 아래로 더 잠기니 offset과 함께 본다.")]
    [Range(1f, 2f)] [SerializeField] float packOpenScale = 1.15f;
    [SerializeField] float packShiftDuration = 0.4f;
    [Tooltip("자리를 잡고 봉인이 찢기기까지의 뜸. 팩이 멈춘 것을 눈이 확인할 틈이다.")]
    [SerializeField] float packShiftHold = 0.1f;

    [Header("뽑기")]
    [Tooltip("봉인이 다 뜯긴 뒤 팩과 더미가 움직이기까지의 뜸. 조각이 날아가 화면에서 빠질 틈을 준다.")]
    [SerializeField] float cardPullDelay = 0.12f;
    [Tooltip("뽑는 데 걸리는 시간. 더미가 솟아오르는 시간이자 팩이 빠져나가는 시간이다 — " +
             "둘은 같은 한 동작이라 값을 나누지 않는다.")]
    [SerializeField] float cardPullDuration = 0.55f;
    [Tooltip("뽑기가 끝난 뒤 카드 조작을 열기까지의 여유.")]
    [SerializeField] float pullHold = 0.35f;

    [Header("팩 퇴장")]
    [Tooltip("속을 비운 팩이 시드는 정도(x, y 배율). 부피가 남아 있으면 아직 뭔가 든 봉지로 보인다.")]
    [SerializeField] Vector2 packSagSquash = new Vector2(0.97f, 0.93f);
    [Tooltip("팩이 빠져나가며 내려가는 거리(캔버스 참조px). 화면 밖까지 가도록 넉넉히.")]
    [SerializeField] float packExitDrop = 2400f;
    [Tooltip("빠져나가며 기우는 각도(도). 곧게 내려가면 사라지는 UI로 읽힌다.")]
    [SerializeField] float packExitTilt = -9f;
    [Tooltip("내려가기 직전 팩이 되레 들리는 정도(InBack 오버슈트). 카드에 딸려 한 번 들렸다 빠지는 " +
             "이 한 박자가 \"붙잡고 있던 걸 빼냈다\"를 만든다 — 0이면 그냥 아래로 사라지는 UI가 된다.")]
    [SerializeField] float packExitBack = 0.5f;

    [Header("결과 화면")]
    [Tooltip("결과 UI 묶음. 페이드하지 않는다 — 입력 개폐(blocksRaycasts)만 담당한다. " +
             "화면을 덮는 어둠은 PackStage보다 앞 sibling의 별도 Dim이 상시 깔고 있다.")]
    [SerializeField] CanvasGroup revealPanel;

    [Header("개봉 화면 흔들림")]
    [Tooltip("팩이 완전히 찢기는 순간 최상위 UI 캔버스 전체를 흔드는 시간.")]
    [Min(0f)] [SerializeField] float screenShakeDuration = 0.72f;
    [Tooltip("기본 화면 흔들림 거리(px). 희귀 1.5배, 신비 2.25배, 신화 3.25배로 강해진다.")]
    [Min(0f)] [SerializeField] float screenShakeStrength = 52.5f;
    [Tooltip("화면 흔들림의 방향 전환 횟수.")]
    [Min(1)] [SerializeField] int screenShakeVibrato = 48;
    [Tooltip("기본 회전 흔들림 각도. 카드 등급 배율을 동일하게 적용한다.")]
    [Min(0f)] [SerializeField] float screenShakeRotation = 1.05f;

    [Header("신규 카드 반응")]
    [Tooltip("신규 카드가 드러나는 순간 화면 전체가 순간 밝아졌다 돌아온다. Dim에 붙인 PackScreenFlash를 물린다. " +
             "화면이 반응하는 것은 신규뿐이어야 한다 — 중복까지 번쩍이면 그 대비가 사라진다. 미배선이면 화면 반응 없음.")]
    [SerializeField] PackScreenFlash newCardFlash;

    // 중복 환급 합계. 낱장마다 떴다 사라진 칩들을 한 줄로 합쳐 "이번 개봉으로 얼마가 돌아왔나"를 말한다 —
    // 결과 격자에는 칩이 없으므로(PackCardView.PlayDupeChip) 이 줄이 그 축의 유일한 답이다.
    //
    // ⚠ 칩과 획득 버튼은 **같은 앵커 기준(화면 하단)**이어야 한다. 한쪽만 중앙 앵커면 화면비가 바뀔 때
    //   서로 파고들고, 화면비가 낮으면(태블릿) 칩이 화면 밖으로 밀려난다.
    [Header("표시 (옵션)")]
    [Tooltip("코인+합계 숫자 칩 묶음. 미배선이면 숫자만 토글된다(뒷배경 없이).")]
    [SerializeField] GameObject totalRefundBadge;
    [SerializeField] TMP_Text totalRefundText;     // 중복 환급 합계
    [Tooltip("합계 칩의 재화 아이콘. 비워두면 프리팹에 저작된 그림 그대로다(자동 탐색하지 않는다 — " +
             "칩의 자식 Image는 밑판일 수 있다).")]
    [SerializeField] Image totalRefundCoin;
    [Tooltip("합계가 0에서 이 값까지 굴러 오르는 시간. 0이면 곧장 최종 숫자.")]
    [SerializeField] float totalRefundCountUp = 0.5f;
    [Tooltip("환급이 빛이 되어 떠날 때 칩이 사그라드는 시간. 빛이 피어나는 시간과 겹쳐야 '그것이 변했다'로 읽힌다 — " +
             "길면 빛이 먼저 날아가고 칩만 남아 두 사건으로 갈린다.")]
    [SerializeField] float totalRefundDismiss = 0.22f;
    [SerializeField] GameObject summaryGroup;      // 요약 단계에서만 켜지는 묶음
    [Tooltip("모든 카드를 3열로 다시 보여주는 결과 격자. summaryGroup에 직접 붙이면 그 묶음 전체가 페이드인된다.")]
    [SerializeField] PackResultGrid resultGrid;
    // 개봉 연출(등장~찢기~뽑기) 동안에는 숨긴다 — 그 구간은 보여주려고 만든 장면이라 빠져나갈 구멍을 두지 않는다.
    // 카드를 넘기기 시작하는 순간부터 나타나 "남은 걸 한 번에 걷는" 수단이 된다.
    [SerializeField] Button skipButton;            // 명시적 건너뛰기(넘기기 단계부터 노출)

    [Tooltip("찢기 입력 대기 중에만 보이는 씬 안내(\"옆으로 그어 열기\"). 찢기 완료 시 사라진다. 미배선이면 안내 없음.")]
    [SerializeField] CanvasGroup tearHint;         // RevealPanel 바깥에 두어야 한다 — 그 패널은 입력을 막고 있다
    [SerializeField] float tearHintFade = 0.2f;

    // Idle → Entering(팩 등장) → Shifting(팩이 화면 아래로 자리잡기)
    // → Tearing(손가락 이동량만큼 찢김) → Pulling(뭉치째 뽑기) → Flicking(넘기기) → Summary.
    // Tearing만 유저 입력을 기다린다. 부분 찢김은 손을 떼도 유지되어 다음 드래그에서 이어진다.
    enum EStage { Idle, Entering, Shifting, Tearing, Pulling, Flicking, Summary }

    EStage m_stage = EStage.Idle;

    // 이번 개봉 세션 결과.
    OpenedPack m_pending;
    ECardGrade m_topGrade = ECardGrade.Unknown;

    // 현재 스테이지의 시간 기반 연출. 스킵은 이걸 Complete로 밀어 다음 단계로 넘긴다.
    Sequence m_stageSeq;
    Sequence m_screenShakeSeq;
    RectTransform m_screenShakeRoot;
    Vector2 m_screenShakeHome;
    Quaternion m_screenShakeRotationHome;

    // 환급 합계가 굴러 오르는 트윈. 다음 개봉이 시작될 때 끊지 않으면 이전 세션의 숫자가 계속 올라간다.
    Tween m_totalRefundTween;

    // 합계 칩이 빛이 되어 걷히는 트윈. 위와 한 축이라 같은 자리에서 함께 끊는다.
    Tween m_refundDismissTween;

    // 칩의 알파를 다루는 손잡이. 프리팹이 안 들고 있으면 처음 쓸 때 붙인다.
    CanvasGroup m_refundGroup;

    // 스킵 횟수. 첫 번째는 현재 단계만, 두 번째부터는 요약까지 단번에.
    int m_skips;

    // 이번 세션에서 개봉 신호를 이미 쐈는지. 뜯김과 스킵 어느 쪽으로 열려도 정확히 1회여야 한다.
    bool m_announced;

    /// <summary>개봉 세션 시작: 카드를 팩 속에 넣은 채 팩이 등장하고 찢기 대기로 이어진다.
    /// _pack은 이 결과를 낳은 팩 정의 — 껍데기 그림이 그 팩의 것으로 갈린다(미지정이면 프리팹 기본 그림).</summary>
    public void BeginOpen(OpenedPack _opened, CardPackData _pack)
    {
        if (m_stage != EStage.Idle) return;   // 재진입 = 중복 개봉 방지
        if (_opened == null || !_opened.Success)
        {
            Debug.LogWarning("[PackRevealView] BeginOpen에 유효하지 않은 OpenedPack — 개봉 취소.");
            return;
        }

        SoundManager.Instance?.PlayCue(EOutgameSound.PackOpenBegin);

        m_pending = _opened;
        m_skips = 0;
        m_announced = false;
        ECardGrade t_topGrade = TopGrade(_opened.Cards);
        m_topGrade = t_topGrade;

        GateInput(false);
        SetTearHint(false, true);

        if (shellRig != null)
        {
            shellRig.ShowShells();
            shellRig.ResetPose();
            shellRig.SetRingGrade(t_topGrade, gradeFxPalette);
        }
        if (tearSkin != null)
        {
            // 그림을 먼저 갈고 상태를 되돌린다 — 진행도·조각 자리는 그림과 무관하지만 순서를 고정해 둔다.
            tearSkin.ApplyPackArt(_pack != null ? _pack.PackArt : null);
            tearSkin.ResetTear();
            // 되돌린 **뒤에** 등급을 물린다(ResetTear가 세기를 0으로 내린다).
            // 찢는 동안 새는 빛이 곧 "무엇이 나오는가"의 예고다.
            tearSkin.SetGlowGrade(t_topGrade, gradeFxPalette);
        }

        if (resultGrid != null) resultGrid.Hide();
        if (summaryGroup != null) summaryGroup.SetActive(false);
        if (skipButton != null) skipButton.gameObject.SetActive(false);

        // 지난 세션의 합계가 굴러가던 중이었다면 끊는다.
        KillTotalRefundTween();

        // 카드는 여기서 단 한 번 세워 팩 속에 넣는다. 이후 어느 단계도 카드를 "등장"시키지 않는다.
        if (cardStack != null)
        {
            cardStack.Build(m_pending.Cards);
            cardStack.PlaceInsidePack(cardInPackCenter, cardInPackScale);
        }
        else Debug.LogWarning("[PackRevealView] cardStack 미배선 → 카드 표시 생략.");

        EnterEntering();
    }

    // 한 팩에서 여러 장이 나오면 가장 높은 등급이 빛을 정한다 — 빛은 하나뿐이라 최고치가 기대를 대표한다.
    static ECardGrade TopGrade(IReadOnlyList<DrawnCard> _cards)
    {
        ECardGrade t_top = ECardGrade.Unknown;
        if (_cards == null) return t_top;

        for (int t_i = 0; t_i < _cards.Count; t_i++)
        {
            CardData t_card = _cards[t_i].Card;
            if (t_card != null && t_card.grade > t_top) t_top = t_card.grade;
        }
        return t_top;
    }

    /// <summary>건너뛰기. 첫 요청은 현재 단계를 즉시 끝내고, 이후 요청은 요약까지 단번에 간다.</summary>
    public void RequestSkip()
    {
        if (m_stage == EStage.Idle || m_stage == EStage.Summary) return;

        m_skips++;
        if (m_skips == 1) SkipCurrentStage();
        else SkipToSummary();
    }

    /// <summary>세션을 완전히 되돌려 다음 BeginOpen을 받을 수 있게 한다.
    /// OnDisable은 요약 도달분을 일부러 남기므로(중복 발화 방지), 오버레이가 닫힐 때는 이쪽이 필요하다.</summary>
    public void ResetSession()
    {
        KillStageSeq();
        KillTotalRefundTween();

        if (tearHandle != null) tearHandle.Disarm();
        if (shellRig != null) shellRig.ResetPose();

        SetTearHint(false, true);
        GateInput(false);

        if (resultGrid != null) resultGrid.Hide();
        if (summaryGroup != null) summaryGroup.SetActive(false);
        if (skipButton != null) skipButton.gameObject.SetActive(false);

        m_stage = EStage.Idle;
        m_pending = null;
        m_topGrade = ECardGrade.Unknown;
        m_announced = false;
        m_skips = 0;
    }

    void OnEnable()
    {
        if (tearHandle != null) tearHandle.OnTorn += HandleTorn;

        if (cardStack != null)
        {
            cardStack.OnCardRevealed  += HandleCardRevealed;
            cardStack.OnEmptied       += HandleStackEmptied;
            cardStack.OnSkipRequested += RequestSkip;
        }

        if (skipButton != null) skipButton.onClick.AddListener(RequestSkip);
    }

    void OnDisable()
    {
        if (tearHandle != null)
        {
            tearHandle.OnTorn -= HandleTorn;
            tearHandle.Disarm();
        }

        if (cardStack != null)
        {
            cardStack.OnCardRevealed  -= HandleCardRevealed;
            cardStack.OnEmptied       -= HandleStackEmptied;
            cardStack.OnSkipRequested -= RequestSkip;
        }

        if (skipButton != null) skipButton.onClick.RemoveListener(RequestSkip);

        // 연출 중 비활성 시 좀비 트윈 정리 + 상태 리셋(재활성 후 "중간 단계에 갇힘" 방지).
        KillStageSeq();
        if (shellRig != null) shellRig.ResetPose();
        SetTearHint(false, true);

        // 이미 끝난 세션은 끝난 채로 둔다. Idle로 되돌리면 BeginOpen 재진입 가드가 풀려
        // 같은 OpenedPack이 한 번 더 열리고 OnAnyPackOpened가 두 번 발화한다(튜토리얼이 이 신호를 센다).
        if (m_stage == EStage.Summary) return;

        // 진행 중이던 세션은 여기서 끊긴다 — 결과까지 함께 비워 다음 BeginOpen이 온전히 새 세션이 되게 한다.
        m_stage = EStage.Idle;
        m_pending = null;
        m_topGrade = ECardGrade.Unknown;
        m_announced = false;
    }

    // ── 스테이지 ────────────────────────────────────────────────

    // 입장: 팩이 아래에서 올라와 안착한다. 카드는 이미 팩 속에 있으므로 함께 올라온다(무대를 통째로 움직인다).
    void EnterEntering()
    {
        m_stage = EStage.Entering;

        if (shellRig == null)
        {
            Debug.LogWarning("[PackRevealView] shellRig 미배선 → 등장 생략.");
            EnterShifting();
            return;
        }

        shellRig.SetIdle(true);
        shellRig.StageOffset = new Vector2(0f, -packEnterDrop);

        KillStageSeq();
        m_stageSeq = DOTween.Sequence()
            .SetLink(gameObject)
            .Append(DOTween.To(() => shellRig.StageOffset, _v => shellRig.StageOffset = _v, Vector2.zero, packEnterDuration)
                .SetEase(Ease.OutBack))
            .OnComplete(EnterShifting);
    }

    // 손가락 진행도가 1에 도달했다. 이 시점이 실제 개봉 확정이다.
    void HandleTorn()
    {
        if (m_stage != EStage.Tearing) return;

        SoundManager.Instance?.PlayCue(EOutgameSound.PackTear);

        if (tearSkin != null) tearSkin.SetProgress(1f);
        AnnounceOpened();
        SetTearHint(false);
        EnterPulling();
    }

    // 자리잡기: 팩이 화면 아래로 내려가 뽑기 자세를 잡는다. 아래쪽이 화면 밖으로 조금 잠기는 자리다.
    // 카드는 아직 팩 속이므로 무대(Stage축)를 통째로 옮긴다 — 껍데기만 옮기면 카드가 제자리에 남아 팩 밖으로 드러난다.
    //
    void EnterShifting()
    {
        if (m_stage != EStage.Entering) return;
        m_stage = EStage.Shifting;

        if (shellRig == null)
        {
            Debug.LogWarning("[PackRevealView] shellRig 미배선 → 자리잡기 생략.");
            EnterTearing();
            return;
        }

        shellRig.SetIdle(false);   // 자리를 옮기는 동안 부유가 겹치면 목표 지점이 흔들린다.

        KillStageSeq();
        m_stageSeq = DOTween.Sequence()
            .SetLink(gameObject)
            // 내려감과 커짐은 한 동작이라 같은 시간·같은 이즈로 묶는다 — 어긋나면 "다가온다"가 아니라 두 애니메이션이 겹친 것으로 읽힌다.
            .Append(DOTween.To(() => shellRig.StageOffset, _v => shellRig.StageOffset = _v, packOpenOffset, packShiftDuration)
                .SetEase(Ease.OutCubic))
            .Join(DOTween.To(() => shellRig.StageScale, _v => shellRig.StageScale = _v, packOpenScale, packShiftDuration)
                .SetEase(Ease.OutCubic))
            .AppendInterval(packShiftHold)
            .OnComplete(EnterTearing);
    }

    // 씰 찢기: 손가락 진행도를 PackTearSkin이 직접 받아 그만큼 구멍을 벌린다.
    // 손을 떼도 되감지 않고, 임계값/플릭 이후의 남은 짧은 구간만 자동 완주한다.
    void EnterTearing()
    {
        if (m_stage != EStage.Shifting) return;
        m_stage = EStage.Tearing;

        KillStageSeq();

        if (tearSkin == null || tearHandle == null)
        {
            Debug.LogWarning("[PackRevealView] tearSkin/tearHandle 미배선 → 직접 찢기 생략.");
            if (tearSkin != null) tearSkin.SetProgress(1f);
            AnnounceOpened();
            EnterPulling();
            return;
        }

        tearHandle.Arm();
        SetTearHint(true);
    }

    // 뽑기: 뜯긴 조각이 먼저 화면 밖으로 날아가 길을 비우고, 그다음 팩이 아래로·더미가 위로 동시에 미끄러진다.
    // 그 이동이 끝난 뒤에야 넘기기로 넘어간다 — 뽑히는 중에 조작이 열리면 두 동작이 겹쳐 읽힌다.
    // 조각 비산·카드 솟기·팩 퇴장을 한 시퀀스에 모아 스킵 한 번이 셋을 함께 끝내게 한다 —
    //   따로 재생하면 스킵 후 팩만 화면에 남는 상태가 생긴다.
    void EnterPulling()
    {
        // 씰 찢기 완료와 "찢기 단계 스킵"이 겹쳐도 뽑기가 두 번 재생되지 않게(조각 비산·카드 솟기 중복 방지).
        if (m_stage != EStage.Tearing) return;
        m_stage = EStage.Pulling;

        if (tearHandle != null) tearHandle.Disarm();
        SetTearHint(false);

        // 다 찢은 순간 팩 뒤 방사광이 원형으로 팍 퍼진다. 여기서 부르는 이유는
        // 찢기 완료의 세 경로(손가락 완주·스킵·미배선 폴백)가 모두 이 문을 지나기 때문이다.
        if (shellRig != null) shellRig.PlayOpenBurst();

        KillStageSeq();
        m_stageSeq = DOTween.Sequence().SetLink(gameObject);
        PlayScreenShake();

        // 뜯긴 조각이 날아간다.
        if (tearSkin != null)
        {
            var t_fly = tearSkin.PlayLidFly();
            if (t_fly != null) m_stageSeq.Insert(0f, t_fly);
        }

        // 더미가 솟고 팩이 빠진다 — 시작 시각도 길이도 같게 묶는다.
        // 이 한 쌍이 연출의 축이다: 어긋나면 "서로 반대로 미끄러지며 뽑혔다"가 아니라 각자 따로 노는 것으로 읽힌다.
        // 단 더미 쪽 시퀀스는 cardPullDuration보다 조금 길다 — 뒷장이 앞장을 따라붙는 꼬리가 뒤에 붙기 때문이다
        // (PackCardStack.PlayEmerge). 맞물려야 하는 것은 이 축의 시작·길이이고, 그 꼬리는 팩이 이미 화면 밖일 때 닫힌다.
        // 아래쪽은 팩 앞면이 계속 가리므로, 그동안 카드는 입구에서 빠져나오는 것으로 보인다.
        if (cardStack != null)
        {
            // 목표는 화면 기준으로 잡되, 자리잡기가 무대에 건 이동·확대를 되물려 무대 로컬로 되돌린다 —
            // 카드는 무대의 자식이라 이미 그만큼 내려가 있고 그만큼 커져 있다.
            // 되물리지 않으면 팩을 내리고 키운 만큼 더미가 위로 뜨고 함께 커진다(카드 윗변이 화면 밖으로 나간다).
            var t_stageOffset = shellRig != null ? shellRig.StageOffset : Vector2.zero;
            float t_stageScale = shellRig != null ? shellRig.StageScale : 1f;
            if (t_stageScale <= 0f) t_stageScale = 1f;

            var t_pull = cardStack.PlayEmerge(
                (cardEmergeCenter - t_stageOffset) / t_stageScale, 1f / t_stageScale, cardPullDuration);
            if (t_pull != null) m_stageSeq.Insert(cardPullDelay, t_pull);
        }

        if (shellRig != null)
        {
            // InBack이라 내려가기 전에 살짝 들렸다 쑥 빠진다 — 그 오버슈트가 곧 "카드에 딸려 들린" 한 박자다.
            // 들림을 따로 트윈하지 않는 이유: 같은 ShellOffset을 두 트윈이 동시에 몰면 서로를 덮어쓴다.
            m_stageSeq.Insert(cardPullDelay,
                DOTween.To(() => shellRig.ShellOffset, _v => shellRig.ShellOffset = _v,
                           new Vector2(0f, -packExitDrop), cardPullDuration).SetEase(Ease.InBack, packExitBack));
            m_stageSeq.Insert(cardPullDelay,
                DOTween.To(() => shellRig.ShellAngle, _v => shellRig.ShellAngle = _v,
                           packExitTilt, cardPullDuration).SetEase(Ease.InQuad));

            // 속이 빈 만큼 시든다(별개 축이라 퇴장과 다투지 않는다).
            m_stageSeq.Insert(cardPullDelay,
                DOTween.To(() => shellRig.ShellSquash, _v => shellRig.ShellSquash = _v,
                           packSagSquash, cardPullDuration * 0.8f).SetEase(Ease.OutCubic));

            // 다 빠진 뒤엔 꺼 둔다 — 팩은 화면 밖이지만 카드·결과 패널 위를 계속 덮고 있다.
            // 기준은 팩 자신의 퇴장이 끝나는 시각이다(더미의 따라붙기 꼬리와는 무관 — 팩은 그 전에 이미 화면 밖이다).
            m_stageSeq.InsertCallback(cardPullDelay + cardPullDuration, () => shellRig.HideShells());
        }

        m_stageSeq.AppendInterval(pullHold)
                  .OnComplete(EnterFlicking);
    }

    // 넘기기: 맨 위부터 스와이프로 한 장씩. 여기부터는 유저 페이스다.
    void EnterFlicking()
    {
        m_stage = EStage.Flicking;

        GateInput(true);

        if (cardStack == null || cardStack.Remaining == 0)
        {
            // 표시할 카드가 없어도 요약까지는 간다(획득 버튼 대기 데드락 방지).
            EnterSummary();
            return;
        }

        // 넘길 것이 실제로 있을 때만 나타난다 — 스킵은 "남은 카드를 걷는" 버튼이라 넘길 게 없으면 뜻이 없다.
        if (skipButton != null) skipButton.gameObject.SetActive(true);

        cardStack.BeginInteraction();
    }

    // 더미가 비었다 → 요약. 넘기기 단계에서만 온다(스킵으로 걷을 땐 Clear라 이 이벤트가 없다).
    void HandleStackEmptied()
    {
        if (m_stage != EStage.Flicking) return;
        EnterSummary();
    }

    // 요약: 뽑은 카드 전부를 3열 격자로 되짚어 주고 총 환급과 함께 획득을 넘긴다.
    void EnterSummary(bool _instant = false)
    {
        if (m_stage == EStage.Summary) return;
        m_stage = EStage.Summary;

        SoundManager.Instance?.PlayCue(EOutgameSound.PackSummary);

        KillStageSeq();
        GateInput(true);

        if (skipButton != null) skipButton.gameObject.SetActive(false);
        if (summaryGroup != null) summaryGroup.SetActive(true);

        // 격자는 더미와 별개로 결과 사본을 새로 세운다 — 밀려나 사라진 카드를 여기서 다시 만난다.
        if (resultGrid != null) resultGrid.Show(m_pending != null ? m_pending.Cards : null, _instant);

        PlayTotalRefund(m_pending != null ? m_pending.TotalRefund.Amount : 0, _instant);

        OnRevealComplete?.Invoke();
    }

    // 환급 합계 줄을 세운다. 0이면 줄 자체를 숨긴다 — "+0"은 정보가 아니라 잡음이다.
    // 숫자는 0에서 굴러 오른다: 합계는 이 화면의 정산이고, 정산은 세어 보이는 편이 받은 느낌을 준다.
    void PlayTotalRefund(long _refund, bool _instant)
    {
        KillTotalRefundTween();

        bool t_show = _refund > 0;
        if (totalRefundBadge != null) totalRefundBadge.SetActive(t_show);

        // 지난 세션이 빛으로 걷어 간 알파를 되돌린다 — 안 되돌리면 재개봉의 칩이 투명한 채 뜬다.
        var t_group = ResolveRefundGroup();
        if (t_group != null) t_group.alpha = 1f;

        // 아이콘은 환급 재화를 따른다. 표가 답을 안 주면 프리팹 그림을 그대로 둔다(null 대입 금지).
        if (totalRefundCoin != null && m_pending != null)
        {
            Sprite t_icon = CurrencyLook.IconOf(m_pending.TotalRefund.Type);
            if (t_icon != null) totalRefundCoin.sprite = t_icon;
        }

        // 칩이 미배선이어도 숫자만으로 성립하도록 텍스트도 직접 토글한다.
        if (totalRefundText != null) totalRefundText.gameObject.SetActive(t_show);

        if (!t_show || totalRefundText == null) return;

        if (_instant || totalRefundCountUp <= 0f)
        {
            totalRefundText.text = $"+{_refund:N0}";
            return;
        }

        totalRefundText.text = "+0";

        // long을 직접 트윈할 플러그인이 없어 float로 굴리고 표시할 때 되돌린다.
        float t_shown = 0f;
        m_totalRefundTween = DOTween.To(() => t_shown, _v =>
                             {
                                 t_shown = _v;
                                 if (totalRefundText != null) totalRefundText.text = $"+{(long)_v:N0}";
                             }, (float)_refund, totalRefundCountUp)
                             .SetEase(Ease.OutCubic)
                             .SetLink(totalRefundText.gameObject)
                             // 굴리다 끊기면 최종 숫자가 아닌 중간값이 남는다 — 마지막 한 번을 못 박는다.
                             .OnKill(() => { if (totalRefundText != null) totalRefundText.text = $"+{_refund:N0}"; });
    }

    void KillTotalRefundTween()
    {
        if (m_totalRefundTween != null)
        {
            m_totalRefundTween.Kill();
            m_totalRefundTween = null;
        }

        if (m_refundDismissTween == null) return;

        m_refundDismissTween.Kill();
        m_refundDismissTween = null;
    }

    /// <summary>환급이 빛으로 피어날 자리(합계 칩의 재화 아이콘). 미배선이면 null —
    /// 호출부는 그때 자기 폴백으로 내려간다.</summary>
    public RectTransform RefundCoinRect => totalRefundCoin != null ? (RectTransform)totalRefundCoin.transform : null;

    /// <summary>합계가 다 굴러 오르는 데 걸리는 시간. 획득 연출은 이 뒤에 와야 한다 —
    /// 세는 도중에 칩을 걷으면 얼마를 받았는지 읽을 자리가 사라진다.</summary>
    public float RefundCountUp => totalRefundCountUp;

    /// <summary>합계 칩을 사그라뜨려 걷는다. 빛이 피어나는 것과 같은 시각에 불러야
    /// "그것이 빛이 되어 갔다"로 읽힌다(따로 부르면 그냥 사라진 것이 된다).</summary>
    public void DismissRefundBadge()
    {
        var t_target = totalRefundBadge != null ? totalRefundBadge
                     : totalRefundText != null ? totalRefundText.gameObject : null;
        if (t_target == null || !t_target.activeSelf) return;

        var t_group = ResolveRefundGroup();
        if (t_group == null)
        {
            t_target.SetActive(false);
            return;
        }

        m_refundDismissTween = t_group.DOFade(0f, totalRefundDismiss)
                               .SetEase(Ease.InQuad)
                               .SetLink(t_target)
                               // 끊겨도 결과는 같다 — 칩은 걷힌 상태로 끝난다(알파는 다음 세션이 되돌린다).
                               .OnKill(() => { if (t_target != null) t_target.SetActive(false); });
    }

    // 칩의 CanvasGroup. 프리팹이 안 들고 있어도 붙여서 쓴다(PackCardView의 환급 칩과 같은 관용구).
    CanvasGroup ResolveRefundGroup()
    {
        var t_target = totalRefundBadge != null ? totalRefundBadge
                     : totalRefundText != null ? totalRefundText.gameObject : null;
        if (t_target == null) return null;

        if (m_refundGroup == null || m_refundGroup.gameObject != t_target)
        {
            m_refundGroup = t_target.GetComponent<CanvasGroup>();
            if (m_refundGroup == null) m_refundGroup = t_target.AddComponent<CanvasGroup>();
        }

        return m_refundGroup;
    }

    // ── 스킵 ────────────────────────────────────────────────────

    // 현재 단계만 즉시 끝낸다. 시간 기반 단계는 Complete로 밀고, 입력 대기 단계는 직접 넘긴다.
    void SkipCurrentStage()
    {
        switch (m_stage)
        {
            case EStage.Entering:
            case EStage.Shifting:
            case EStage.Pulling:
                // Complete(true)면 OnComplete가 실행되며 다음 단계로 이어진다.
                if (m_stageSeq != null && m_stageSeq.IsActive()) m_stageSeq.Complete(true);
                break;

            case EStage.Tearing:
                // 입력 대기 단계에는 완료할 시퀀스가 없다. 완전히 찢은 상태를 만든 뒤 뽑기로 잇는다.
                if (tearHandle != null) tearHandle.Disarm();
                if (tearSkin != null) tearSkin.SetProgress(1f);
                AnnounceOpened();
                SetTearHint(false, true);
                EnterPulling();
                break;

            case EStage.Flicking:
                // 넘기기의 "현재 단계 완료"는 남은 전부 정리다 — 한 장만 넘기는 건 스킵이 아니다.
                if (cardStack != null) cardStack.FlickAllImmediate();
                else EnterSummary();
                break;
        }
    }

    // 어느 단계든 요약으로 직행.
    void SkipToSummary()
    {
        KillStageSeq();

        if (tearHandle != null) tearHandle.Disarm();

        // 찢기 전에 요약으로 직행해도 팩이 열렸다는 사실은 참이다 —
        // 팩이 열렸다는 사실은 연출을 건너뛰어도 참이므로 여기서 보장한다(튜토리얼이 이 신호를 기다린다).
        AnnounceOpened();
        SetTearHint(false, true);

        // 팩 껍데기와 뜯긴 조각을 함께 걷는다 — 조각은 팩과 별개 노드라 껍데기만 꺼선 남는다.
        if (shellRig != null) shellRig.HideShells();
        if (tearSkin != null) tearSkin.HideLid();

        // 더미는 걷어내기만 한다 — 카드는 요약 격자가 전부 다시 보여주므로 넘기는 시늉이 필요 없다.
        if (cardStack != null) cardStack.Clear();

        EnterSummary(true);
    }

    // ── 카드 단계 콜백 ──────────────────────────────────────────

    // 새 맨 위 카드가 드러났다 — 등장 타격과 신규/중복 강조는 이 시점에 터진다.
    void HandleCardRevealed(PackCardView _view)
    {
        if (_view == null) return;

        _view.PlayRevealAccent();
        SoundManager.Instance?.PlayCue(EOutgameSound.PackCardFlick);

        // 카드 한 장의 축(뷰)과 화면의 축(여기)을 갈라 둔다 — 화면 전체가 반응하는 것은 신규뿐이라
        // 그 판단을 카드 프리팹 안에 두면 "이 화면에 Dim이 있는가"를 카드가 알아야 한다.
        if (_view.IsNew && newCardFlash != null) newCardFlash.Play();
    }

    // ── 보조 ────────────────────────────────────────────────────

    // "팩이 열렸다"를 세션당 1회만 알린다. 발화 시점은 찢기 확정 — 튜토리얼이 물려 있어 의미를 옮기지 않는다.
    void AnnounceOpened()
    {
        if (m_announced) return;
        m_announced = true;

        OnAnyPackOpened?.Invoke();
    }

    // 결과 UI의 입력 개폐. 뽑기 전까지는 반드시 닫아 둔다 —
    // 열려 있으면 화면을 덮은 StackInput이 찢기 드래그를 가로채 개봉 자체가 막힌다.
    void GateInput(bool _open)
    {
        if (revealPanel == null) return;

        revealPanel.blocksRaycasts = _open;
        revealPanel.interactable = _open;
    }

    // 찢기 안내 표시/숨김. 입력은 절대 먹지 않는다 — 문구가 드래그를 가로막으면 개봉 자체가 막힌다.
    void SetTearHint(bool _show, bool _instant = false)
    {
        if (tearHint == null) return;

        tearHint.DOKill();
        tearHint.blocksRaycasts = false;
        tearHint.interactable   = false;

        float t_to = _show ? 1f : 0f;
        if (_instant || tearHintFade <= 0f) { tearHint.alpha = t_to; return; }

        tearHint.DOFade(t_to, tearHintFade).SetLink(tearHint.gameObject);
    }

    void KillStageSeq()
    {
        StopScreenShake();
        if (m_stageSeq != null && m_stageSeq.IsActive()) m_stageSeq.Kill();
        m_stageSeq = null;
    }

    // Screen Space Overlay에서는 카메라 Transform을 흔들어도 UI가 움직이지 않는다.
    // 팩 무대와 결과 UI를 함께 품은 최상위 Canvas를 흔들어 개봉 충격이 화면 전체에 걸리게 한다.
    void PlayScreenShake()
    {
        StopScreenShake();

        if (screenShakeDuration <= 0f) return;
        RectTransform t_root = ResolveScreenShakeRoot();
        if (t_root == null) return;

        float t_gradeScale = ScreenShakeGradeScale(m_topGrade);
        m_screenShakeHome = t_root.anchoredPosition;
        m_screenShakeRotationHome = t_root.localRotation;

        Sequence t_sequence = DOTween.Sequence()
            .SetUpdate(true)
            .SetLink(gameObject);
        m_screenShakeSeq = t_sequence;

        t_sequence.Join(t_root.DOShakeAnchorPos(
            screenShakeDuration,
            screenShakeStrength * t_gradeScale,
            screenShakeVibrato,
            90f,
            false,
            true));
        t_sequence.Join(t_root.DOShakeRotation(
            screenShakeDuration,
            new Vector3(0f, 0f, screenShakeRotation * t_gradeScale),
            screenShakeVibrato,
            90f,
            true));
        t_sequence.OnComplete(() =>
        {
            t_root.anchoredPosition = m_screenShakeHome;
            t_root.localRotation = m_screenShakeRotationHome;
            if (m_screenShakeSeq == t_sequence) m_screenShakeSeq = null;
        });
    }

    RectTransform ResolveScreenShakeRoot()
    {
        if (m_screenShakeRoot != null) return m_screenShakeRoot;

        Canvas t_canvas = shellRig != null ? shellRig.GetComponentInParent<Canvas>() : null;
        if (t_canvas == null || shellRig == null) return null;

        // Canvas 자체는 렌더 모드가 좌표를 관리하고, SafeArea는 해상도 변경 시 Fitter가 좌표를 다시 쓴다.
        // SafeArea 직하의 화면 요소들을 별도 래퍼로 한 번 감싸 그 래퍼만 흔든다.
        Transform t_safeArea = shellRig.transform;
        while (t_safeArea.parent != null && t_safeArea.parent != t_canvas.transform)
            t_safeArea = t_safeArea.parent;
        if (t_safeArea.parent != t_canvas.transform) return null;

        int t_childCount = t_safeArea.childCount;
        var t_children = new List<Transform>(t_childCount);
        for (int t_i = 0; t_i < t_childCount; t_i++) t_children.Add(t_safeArea.GetChild(t_i));

        var t_rootObject = new GameObject("PackScreenShakeRoot", typeof(RectTransform));
        t_rootObject.layer = t_safeArea.gameObject.layer;
        m_screenShakeRoot = (RectTransform)t_rootObject.transform;
        m_screenShakeRoot.SetParent(t_safeArea, false);
        m_screenShakeRoot.anchorMin = Vector2.zero;
        m_screenShakeRoot.anchorMax = Vector2.one;
        m_screenShakeRoot.offsetMin = Vector2.zero;
        m_screenShakeRoot.offsetMax = Vector2.zero;
        m_screenShakeRoot.pivot = new Vector2(0.5f, 0.5f);

        for (int t_i = 0; t_i < t_children.Count; t_i++)
            t_children[t_i].SetParent(m_screenShakeRoot, false);

        return m_screenShakeRoot;
    }

    static float ScreenShakeGradeScale(ECardGrade _grade)
    {
        if (_grade == ECardGrade.Rare) return 1.5f;
        if (_grade == ECardGrade.Arcane) return 2.25f;
        if (_grade == ECardGrade.Mythic) return 3.25f;
        return 1f;
    }

    void StopScreenShake()
    {
        Sequence t_sequence = m_screenShakeSeq;
        m_screenShakeSeq = null;
        if (t_sequence != null && t_sequence.IsActive()) t_sequence.Kill();

        if (m_screenShakeRoot == null) return;
        m_screenShakeRoot.anchoredPosition = m_screenShakeHome;
        m_screenShakeRoot.localRotation = m_screenShakeRotationHome;
    }
}
