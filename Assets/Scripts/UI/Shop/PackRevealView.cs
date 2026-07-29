using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
             "카드가 1376x1926로 커서 y가 ~597를 넘으면 윗변이 화면 밖으로 나간다.")]
    [SerializeField] Vector2 cardEmergeCenter = Vector2.zero;

    [Header("자리잡기 (스와이프 직후)")]
    [Tooltip("스와이프가 확정되면 팩이 여기로 옮겨 간다(무대 오프셋, 캔버스 참조px). " +
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

    [Header("씰 찢기")]
    [Tooltip("봉인이 저절로 그어지는 시간. 스와이프는 방아쇠일 뿐이고 찢김은 여기서 자동으로 진행된다.")]
    [SerializeField] float sealTearDuration = 0.45f;

    [Header("뽑기")]
    [Tooltip("봉인이 다 뜯긴 뒤 팩과 더미가 움직이기까지의 뜸. 조각이 날아가 화면에서 빠질 틈을 준다.")]
    [SerializeField] float cardPullDelay = 0.12f;
    [Tooltip("뽑는 데 걸리는 시간. 더미가 솟아오르는 시간이자 팩이 빠져나가는 시간이다 — " +
             "둘은 같은 한 동작이라 값을 나누지 않는다.")]
    [SerializeField] float cardPullDuration = 0.55f;
    // ⚠ Overlay 캔버스 위에는 ParticleSystem이 렌더되지 않는다 — 실제로 붙이려면 Screen Space-Camera 캔버스가 필요하다.
    [SerializeField] ParticleSystem burstEffect;   // 개봉 순간 파티클(옵션)
    // 팩은 빠져나가는 중이라 제 위치를 리그에 내주고 있다 — 팩을 걸면 두 축이 다투므로 배경처럼 가만히 있는 것을 건다.
    [SerializeField] Transform shakeTarget;        // 배경 RectTransform(옵션)
    [SerializeField] float shakeDuration = 0.3f;
    [Tooltip("DOShakePosition은 월드 좌표를 흔든다 — Overlay 캔버스의 월드는 디바이스 스크린px다(참조px 아님).")]
    [SerializeField] float shakeStrength = 48f;
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

    [Header("표시 (옵션)")]
    [SerializeField] TMP_Text remainingText;       // 남은 장수
    [SerializeField] TMP_Text totalRefundText;     // 중복 환급 합계
    [SerializeField] GameObject summaryGroup;      // 요약 단계에서만 켜지는 묶음
    [Tooltip("모든 카드를 3열로 다시 보여주는 결과 격자. summaryGroup에 직접 붙이면 그 묶음 전체가 페이드인된다.")]
    [SerializeField] PackResultGrid resultGrid;
    [SerializeField] Button skipButton;            // 명시적 건너뛰기(찢기 단계에서도 빠져나갈 수 있게)

    [Tooltip("스와이프 대기 중에만 보이는 씬 안내(\"옆으로 그어 열기\"). 스와이프 확정 시 사라진다. 미배선이면 안내 없음.")]
    [SerializeField] CanvasGroup tearHint;         // RevealPanel 바깥에 두어야 한다 — 그 패널은 입력을 막고 있다
    [SerializeField] float tearHintFade = 0.2f;

    // Idle → Entering(팩 등장) → Swipe(그어 열기 대기) → Shifting(팩이 화면 아래로 자리잡기)
    // → Tearing(씰이 저절로 찢김) → Pulling(뭉치째 뽑기) → Flicking(넘기기) → Summary.
    // Swipe만 유저 입력을 기다린다 — Shifting부터 Pulling까지는 한 흐름으로 자동 진행된다.
    enum EStage { Idle, Entering, Swipe, Shifting, Tearing, Pulling, Flicking, Summary }

    EStage m_stage = EStage.Idle;

    // 이번 개봉 세션 결과.
    OpenedPack m_pending;

    // 현재 스테이지의 시간 기반 연출. 스킵은 이걸 Complete로 밀어 다음 단계로 넘긴다.
    Sequence m_stageSeq;

    // 스킵 횟수. 첫 번째는 현재 단계만, 두 번째부터는 요약까지 단번에.
    int m_skips;

    // 이번 세션에서 개봉 신호를 이미 쐈는지. 뜯김과 스킵 어느 쪽으로 열려도 정확히 1회여야 한다.
    bool m_announced;

    /// <summary>개봉 세션 시작: 카드를 팩 속에 넣은 채 팩이 등장하고 찢기 대기로 이어진다.</summary>
    public void BeginOpen(OpenedPack _opened)
    {
        if (m_stage != EStage.Idle) return;   // 재진입 = 중복 개봉 방지
        if (_opened == null || !_opened.Success)
        {
            Debug.LogWarning("[PackRevealView] BeginOpen에 유효하지 않은 OpenedPack — 개봉 취소.");
            return;
        }

        m_pending = _opened;
        m_skips = 0;
        m_announced = false;

        GateInput(false);
        SetTearHint(false, true);

        if (shellRig != null) { shellRig.ShowShells(); shellRig.ResetPose(); }
        if (tearSkin != null) tearSkin.ResetTear();

        if (resultGrid != null) resultGrid.Hide();
        if (summaryGroup != null) summaryGroup.SetActive(false);
        if (skipButton != null) skipButton.gameObject.SetActive(true);

        // 카드는 여기서 단 한 번 세워 팩 속에 넣는다. 이후 어느 단계도 카드를 "등장"시키지 않는다.
        if (cardStack != null)
        {
            cardStack.Build(m_pending.Cards);
            cardStack.PlaceInsidePack(cardInPackCenter, cardInPackScale);
        }
        else Debug.LogWarning("[PackRevealView] cardStack 미배선 → 카드 표시 생략.");

        EnterEntering();
    }

    /// <summary>건너뛰기. 첫 요청은 현재 단계를 즉시 끝내고, 이후 요청은 요약까지 단번에 간다.</summary>
    public void RequestSkip()
    {
        if (m_stage == EStage.Idle || m_stage == EStage.Summary) return;

        m_skips++;
        if (m_skips == 1) SkipCurrentStage();
        else SkipToSummary();
    }

    void OnEnable()
    {
        if (tearHandle != null) tearHandle.OnTorn += HandleTorn;

        if (cardStack != null)
        {
            cardStack.OnCardRevealed    += HandleCardRevealed;
            cardStack.OnRemainingChanged += HandleRemainingChanged;
            cardStack.OnEmptied         += HandleStackEmptied;
            cardStack.OnSkipRequested   += RequestSkip;
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
            cardStack.OnCardRevealed    -= HandleCardRevealed;
            cardStack.OnRemainingChanged -= HandleRemainingChanged;
            cardStack.OnEmptied         -= HandleStackEmptied;
            cardStack.OnSkipRequested   -= RequestSkip;
        }

        if (skipButton != null) skipButton.onClick.RemoveListener(RequestSkip);

        // 연출 중 비활성 시 좀비 트윈 정리 + 상태 리셋(재활성 후 "중간 단계에 갇힘" 방지).
        KillStageSeq();
        if (shellRig != null) shellRig.ResetPose();
        // 셰이크도 같이 끊는다 — 대상은 팩과 달리 계속 보이는 배경이라, 중간에 멈추면 어긋난 자리에 그대로 굳는다.
        if (shakeTarget != null) shakeTarget.DOKill();
        SetTearHint(false, true);

        // 이미 끝난 세션은 끝난 채로 둔다. Idle로 되돌리면 BeginOpen 재진입 가드가 풀려
        // 같은 OpenedPack이 한 번 더 열리고 OnAnyPackOpened가 두 번 발화한다(튜토리얼이 이 신호를 센다).
        if (m_stage == EStage.Summary) return;

        // 진행 중이던 세션은 여기서 끊긴다 — 결과까지 함께 비워 다음 BeginOpen이 온전히 새 세션이 되게 한다.
        m_stage = EStage.Idle;
        m_pending = null;
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
            EnterSwipe();
            return;
        }

        shellRig.SetIdle(true);
        shellRig.StageOffset = new Vector2(0f, -packEnterDrop);

        KillStageSeq();
        m_stageSeq = DOTween.Sequence()
            .SetLink(gameObject)
            .Append(DOTween.To(() => shellRig.StageOffset, _v => shellRig.StageOffset = _v, Vector2.zero, packEnterDuration)
                .SetEase(Ease.OutBack))
            .OnComplete(EnterSwipe);
    }

    // 스와이프 대기: 유저가 가로로 그을 때까지 기다린다(시간 기반 아님).
    // 긋는 동안에는 아무것도 찢기지 않는다 — 제스처는 개봉의 방아쇠일 뿐이다.
    void EnterSwipe()
    {
        m_stage = EStage.Swipe;

        if (tearHandle == null)
        {
            // 제스처 미배선: 소프트락을 만들지 않고 바로 다음 단계로.
            Debug.LogWarning("[PackRevealView] tearHandle 미배선 → 스와이프 생략.");
            HandleTorn();
            return;
        }

        tearHandle.Arm();
        SetTearHint(true);
    }

    // 스와이프 확정 → 자리잡기.
    void HandleTorn()
    {
        if (m_stage != EStage.Swipe) return;
        EnterShifting();
    }

    // 자리잡기: 팩이 화면 아래로 내려가 뽑기 자세를 잡는다. 아래쪽이 화면 밖으로 조금 잠기는 자리다.
    // 카드는 아직 팩 속이므로 무대(Stage축)를 통째로 옮긴다 — 껍데기만 옮기면 카드가 제자리에 남아 팩 밖으로 드러난다.
    //
    // 여기서 개봉을 알리는 이유: 유저가 손을 뗀 순간이 곧 "팩이 열렸다"이고,
    // 그 뒤는 되돌릴 수 없는 자동 흐름이다 — 씰이 찢길 때까지 미루면 신호가 연출 길이만큼 늦어진다.
    void EnterShifting()
    {
        if (m_stage != EStage.Swipe) return;
        m_stage = EStage.Shifting;

        AnnounceOpened();
        SetTearHint(false);

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

    // 씰 찢기: 자리를 잡은 팩의 봉인이 저절로 그어진다. 그어지는 만큼 구멍이 벌어지고
    // 그 너머로 카드 끝이 드러난다 — 표현은 전부 PackTearSkin이 그린다(여기서는 진행도만 민다).
    void EnterTearing()
    {
        if (m_stage != EStage.Shifting) return;
        m_stage = EStage.Tearing;

        if (tearSkin == null)
        {
            Debug.LogWarning("[PackRevealView] tearSkin 미배선 → 씰 찢기 생략.");
            EnterPulling();
            return;
        }

        KillStageSeq();
        float t_progress = 0f;
        m_stageSeq = DOTween.Sequence()
            .SetLink(gameObject)
            // InCubic — 처음엔 버티다 끝에서 쭉 갈라진다. 등속이면 찢김이 아니라 게이지가 차는 것으로 읽힌다.
            .Append(DOTween.To(() => t_progress, _v => { t_progress = _v; tearSkin.SetProgress(_v); }, 1f, sealTearDuration)
                .SetEase(Ease.InCubic))
            .OnComplete(EnterPulling);
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

        if (burstEffect != null) burstEffect.Play();
        if (shakeTarget != null)
        {
            shakeTarget.DOKill();
            shakeTarget.DOShakePosition(shakeDuration, shakeStrength).SetLink(shakeTarget.gameObject);
        }

        KillStageSeq();
        m_stageSeq = DOTween.Sequence().SetLink(gameObject);

        // 뜯긴 조각이 날아간다.
        if (tearSkin != null)
        {
            var t_fly = tearSkin.PlayLidFly();
            if (t_fly != null) m_stageSeq.Insert(0f, t_fly);
        }

        // 더미가 솟고 팩이 빠진다 — 시작 시각도 길이도 같게 묶는다.
        // 이 한 쌍이 연출의 축이다: 어긋나면 "서로 반대로 미끄러지며 뽑혔다"가 아니라 각자 따로 노는 것으로 읽힌다.
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

        KillStageSeq();
        GateInput(true);

        if (skipButton != null) skipButton.gameObject.SetActive(false);
        if (remainingText != null) remainingText.gameObject.SetActive(false);
        if (summaryGroup != null) summaryGroup.SetActive(true);

        // 격자는 더미와 별개로 결과 사본을 새로 세운다 — 밀려나 사라진 카드를 여기서 다시 만난다.
        if (resultGrid != null) resultGrid.Show(m_pending != null ? m_pending.Cards : null, _instant);

        if (totalRefundText != null)
        {
            long t_refund = m_pending != null ? m_pending.TotalRefund : 0;
            // 환급이 없으면 줄 자체를 숨긴다 — "+0"은 정보가 아니라 잡음이다.
            totalRefundText.gameObject.SetActive(t_refund > 0);
            if (t_refund > 0) totalRefundText.text = $"+{t_refund:N0}";
        }

        OnRevealComplete?.Invoke();
    }

    // ── 스킵 ────────────────────────────────────────────────────

    // 현재 단계만 즉시 끝낸다. 시간 기반 단계는 Complete로 밀고, 입력 대기 단계는 직접 넘긴다.
    void SkipCurrentStage()
    {
        switch (m_stage)
        {
            case EStage.Entering:
            case EStage.Shifting:
            case EStage.Tearing:
            case EStage.Pulling:
                // Complete(true)면 OnComplete가 실행되며 다음 단계로 이어진다.
                if (m_stageSeq != null && m_stageSeq.IsActive()) m_stageSeq.Complete(true);
                break;

            case EStage.Swipe:
                // 그은 셈 치고 자동 흐름을 연다 — 스킵이 개봉 자체를 건너뛰지는 않는다.
                if (tearHandle != null) tearHandle.Disarm();
                EnterShifting();
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

        // 스와이프 전에 요약으로 직행하면 EnterShifting을 거치지 않는다 —
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

    // 새 맨 위 카드가 드러났다 — 신규/중복 강조는 이 시점에 터진다.
    void HandleCardRevealed(PackCardView _view)
    {
        if (_view != null) _view.PlayRevealAccent();
    }

    void HandleRemainingChanged(int _remaining)
    {
        if (remainingText == null) return;
        remainingText.text = $"{_remaining}";
        remainingText.gameObject.SetActive(_remaining > 0);
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
        if (m_stageSeq != null && m_stageSeq.IsActive()) m_stageSeq.Kill();
        m_stageSeq = null;
    }
}
