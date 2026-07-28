using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 카드팩 개봉 연출의 진행자. 스테이지를 순서대로 몰고 가며, 각 단계의 실제 조작·표현은
// PackTearHandle(뜯기) · PackCardStack(더미 넘기기) · PackCardView(카드 표시)가 나눠 맡는다.
//
// 흐름: 입장 → 뜯기(스와이프) → 뽑기 → 한 장씩 밀어내기(스와이프) → 결과 격자 → OnRevealComplete.
// 뽑기는 "팩에서 카드셋을 빼낸다"를 한 동작으로 보여준다 — 더미가 팩 자리에서 제 크기로 펴진 뒤,
//   팩이 그 아래로 쑥 빠진다. 순서를 뒤집으면 팩과 카드가 따로 노는 화면이 된다.
// ⚠ 계층 전제: packRoot가 revealPanel보다 뒤 sibling이어야 한다(= 팩이 카드를 덮는다).
//   그래야 카드가 팩에 가려진 채 커지다 삐져나오며 "속에서 나왔다"로 읽힌다.
//   대신 팩은 카드·결과 패널을 가리므로, 빠져나간 직후 반드시 꺼야 한다(EnterBursting이 그 시점에 끈다).
//   스킵 버튼·뜯기 안내는 그보다 더 뒤 sibling으로 두어 팩에 묻히지 않게 한다.
// 밀어낸 카드는 그 자리에서 사라지고, 마지막 장까지 넘긴 뒤 결과 패널이 떠오르며 전부를 3열로 되짚어 준다.
// 진입은 컨트롤러가 넘기는 OpenedPack(BeginOpen)뿐 — 구매·소유·덱은 이 뷰 밖의 책임이다.
// 연출은 이미 끝난 거래(TryPurchase가 원자 영속)를 보여줄 뿐, 경제를 건드리지 않는다.
public class PackRevealView : MonoBehaviour
{
    // 요약 도달 시 1회 발화(스킵으로 건너뛰어도 반드시 발화 — 획득 버튼 데드락 방지).
    public event Action OnRevealComplete;

    // 어느 씬의 어느 뷰든 팩이 열린 순간 발화(구독자는 씬 참조 없이 개봉 시점을 알 수 있다).
    // 발화 시점은 "팩이 열린 순간" = 뜯기 확정. 튜토리얼이 물려 있어 의미를 옮기지 않는다.
    public static event Action OnAnyPackOpened;

    [Header("팩")]
    [SerializeField] PackTearHandle tearHandle;    // 봉인 뜯기 제스처
    [SerializeField] GameObject packRoot;          // 팩 루트(UI RectTransform). 미배선이면 tearHandle의 오브젝트를 쓴다.
    [Tooltip("팩이 이만큼 아래에서 올라오며 등장한다(부모 로컬 = 캔버스 참조px, 1440x3120 기준).")]
    [SerializeField] float packEnterDrop = 811f;
    [SerializeField] float packEnterDuration = 0.45f;

    [Header("뽑기")]
    // ⚠ Overlay 캔버스 위에는 ParticleSystem이 렌더되지 않는다 — 실제로 붙이려면 Screen Space-Camera 캔버스가 필요하다.
    [SerializeField] ParticleSystem burstEffect;   // 개봉 순간 파티클(옵션)
    // 팩은 빠져나가는 중이라 제 위치를 트윈에 내주고 있다 — 팩을 걸면 두 트윈이 다투므로 배경처럼 가만히 있는 것을 건다.
    [SerializeField] Transform shakeTarget;        // 배경 RectTransform(옵션)
    [SerializeField] float shakeDuration = 0.3f;
    [Tooltip("DOShakePosition은 월드 좌표를 흔든다 — Overlay 캔버스의 월드는 디바이스 스크린px다(참조px 아님).")]
    [SerializeField] float shakeStrength = 68f;
    [Tooltip("뽑기가 끝난 뒤 카드 조작을 열기까지의 여유.")]
    [SerializeField] float burstHold = 0.4f;

    [Tooltip("카드셋이 팩 속에 들어있을 때의 크기 배율. 최종 크기와 갭이 크면 \"뽑혔다\"가 아니라 \"커졌다\"로 읽힌다 " +
             "— 팩 안에 겨우 들어갈 만큼만 줄이고, 부족하면 팩을 키울 것.")]
    [Range(0.1f, 1f)] [SerializeField] float cardEmergeScale = 0.9f;
    [Tooltip("카드셋이 잠겨 있는 지점(팩 중심 기준, 캔버스 참조px). 아래로 내릴수록 팩 깊숙이 박혀 있다 길게 뽑혀 나온다.")]
    [SerializeField] Vector2 cardEmergeOffset = new Vector2(0f, -180f);
    [Tooltip("카드셋이 팩 자리에서 제 크기로 펴지는 시간.")]
    [SerializeField] float cardEmergeDuration = 0.5f;
    [Tooltip("카드가 이만큼 나온 뒤에 팩이 빠지기 시작한다(뽑기 시간 대비 비율). 0이면 동시에 움직인다. " +
             "카드가 다 나온 뒤에 빠지면(1에 가까우면) 두 동작이 끊겨 보인다 — 겹쳐야 서로 반대로 미끄러지며 뽑힌다.")]
    [Range(0f, 1f)] [SerializeField] float packPullDelay = 0.35f;
    [Tooltip("팩이 빠져나가며 내려가는 거리(부모 로컬 = 캔버스 참조px). 화면 밖까지 가도록 넉넉히.")]
    [SerializeField] float packPullDrop = 2400f;
    [SerializeField] float packPullDuration = 0.45f;

    [Header("결과 화면")]
    [SerializeField] CanvasGroup revealPanel;      // 결과 패널(시작 alpha 0 / 입력 off)
    [SerializeField] PackCardStack cardStack;      // 카드 더미 + 넘기기
    [SerializeField] float panelFadeDuration = 0.35f;

    [Header("표시 (옵션)")]
    [SerializeField] TMP_Text remainingText;       // 남은 장수
    [SerializeField] TMP_Text totalRefundText;     // 중복 환급 합계
    [SerializeField] GameObject summaryGroup;      // 요약 단계에서만 켜지는 묶음
    [Tooltip("모든 카드를 3열로 다시 보여주는 결과 격자. summaryGroup에 직접 붙이면 그 묶음 전체가 페이드인된다.")]
    [SerializeField] PackResultGrid resultGrid;
    [SerializeField] Button skipButton;            // 명시적 건너뛰기(뜯기 단계에서도 빠져나갈 수 있게)

    [Tooltip("뜯기 대기 중에만 보이는 씬 안내(\"스와이프하여 오픈\"). 뜯김 확정 시 사라진다. 미배선이면 안내 없음.")]
    [SerializeField] CanvasGroup tearHint;         // RevealPanel 바깥에 두어야 한다 — 그 패널은 분출 전까지 alpha 0이다
    [SerializeField] float tearHintFade = 0.2f;

    // Idle → Entering(팩 등장) → Tearing(뜯기 대기) → Bursting(분출) → Flicking(넘기기) → Summary.
    enum EStage { Idle, Entering, Tearing, Bursting, Flicking, Summary }

    EStage m_stage = EStage.Idle;

    // 이번 개봉 세션 결과.
    OpenedPack m_pending;

    // 현재 스테이지의 시간 기반 연출. 스킵은 이걸 Complete로 밀어 다음 단계로 넘긴다.
    Sequence m_stageSeq;

    // 스킵 횟수. 첫 번째는 현재 단계만, 두 번째부터는 요약까지 단번에.
    int m_skips;

    // 이번 세션에서 개봉 신호를 이미 쐈는지. 분출과 스킵 어느 쪽으로 열려도 정확히 1회여야 한다.
    bool m_announced;

    // packRoot 원위치·원크기(등장 트윈 기준). 크기는 씬에 놓인 값이 곧 팩의 실제 크기다.
    Vector3 m_packHome;
    Vector3 m_packHomeScale = Vector3.one;
    bool m_packHomeCaptured;

    /// <summary>개봉 세션 시작: 팩이 등장하고 뜯기 대기로 이어진다.</summary>
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

        ResetPanel();
        SetTearHint(false, true);
        if (cardStack != null) cardStack.Clear();
        if (resultGrid != null) resultGrid.Hide();
        if (summaryGroup != null) summaryGroup.SetActive(false);
        if (skipButton != null) skipButton.gameObject.SetActive(true);

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
        if (revealPanel != null) revealPanel.DOKill();
        if (packRoot != null) packRoot.transform.DOKill();
        // 셰이크도 같이 끊는다 — 대상은 팩과 달리 계속 보이는 배경이라, 중간에 멈추면 어긋난 자리에 그대로 굳는다.
        if (shakeTarget != null) shakeTarget.DOKill();
        SetTearHint(false, true);

        m_stage = EStage.Idle;
    }

    // ── 스테이지 ────────────────────────────────────────────────

    // 입장: 팩이 아래에서 올라와 안착한다.
    void EnterEntering()
    {
        m_stage = EStage.Entering;

        var t_root = ResolvePackRoot();
        if (t_root == null)
        {
            Debug.LogWarning("[PackRevealView] 팩 오브젝트 미배선 → 등장 생략.");
            EnterTearing();
            return;
        }

        t_root.SetActive(true);

        var t_tr = t_root.transform;
        CapturePackHome(t_tr);

        t_tr.DOKill();
        t_tr.localPosition = m_packHome - new Vector3(0f, packEnterDrop, 0f);
        // 매 개봉마다 크기를 씬에 놓인 값으로 되돌린다(연출이 남긴 스케일이 다음 개봉에 누적되지 않게).
        // 팩 크기는 packRoot의 localScale로 잡는 것이 정석이다 — 자식 sizeDelta를 일일이 고치면
        // Glow·Shadow·마스크 오프셋까지 따라 손봐야 하고, 카드와의 크기 관계가 한눈에 안 잡힌다.
        t_tr.localScale = m_packHomeScale;

        KillStageSeq();
        m_stageSeq = DOTween.Sequence()
            .SetLink(t_root)
            .Append(t_tr.DOLocalMove(m_packHome, packEnterDuration).SetEase(Ease.OutBack))
            .OnComplete(EnterTearing);
    }

    // 뜯기: 유저가 스와이프로 봉인을 그을 때까지 기다린다(시간 기반 아님).
    void EnterTearing()
    {
        m_stage = EStage.Tearing;

        if (tearHandle == null)
        {
            // 제스처 미배선: 소프트락을 만들지 않고 바로 다음 단계로.
            Debug.LogWarning("[PackRevealView] tearHandle 미배선 → 뜯기 생략.");
            HandleTorn();
            return;
        }

        tearHandle.Arm();
        SetTearHint(true);
    }

    // 뜯김 확정 → 분출.
    void HandleTorn()
    {
        if (m_stage != EStage.Tearing) return;
        EnterBursting();
    }

    // 뽑기: 팩 속에 겹쳐 있던 카드셋이 제 크기로 펴지고, 뒤이어 팩이 그 아래로 빠진다.
    // 세 트윈(패널 페이드·카드 펴기·팩 빼기)을 한 시퀀스에 모아 스킵 한 번이 셋을 함께 끝내게 한다 —
    //   따로 재생하면 스킵 후 팩만 화면에 남는 상태가 생긴다.
    void EnterBursting()
    {
        m_stage = EStage.Bursting;

        AnnounceOpened();
        SetTearHint(false);   // 뜯김이 확정된 순간 = 안내의 수명 끝

        if (burstEffect != null) burstEffect.Play();
        if (shakeTarget != null)
        {
            shakeTarget.DOKill();
            shakeTarget.DOShakePosition(shakeDuration, shakeStrength).SetLink(shakeTarget.gameObject);
        }

        var t_root = ResolvePackRoot();

        // 카드는 흩어졌다 모이지 않는다 — 팩 자리에 줄어든 채 겹쳐 있다가 그대로 최종 더미 자리로 펴진다.
        if (cardStack != null)
        {
            cardStack.Build(m_pending != null ? m_pending.Cards : null);
            if (t_root != null) cardStack.PrepareEmerge(t_root.transform.position, cardEmergeOffset, cardEmergeScale);
        }
        else Debug.LogWarning("[PackRevealView] cardStack 미배선 → 카드 표시 생략.");

        KillStageSeq();
        m_stageSeq = DOTween.Sequence().SetLink(gameObject);

        if (revealPanel != null)
        {
            revealPanel.DOKill();
            revealPanel.alpha = 0f;
            // 카드가 나오는 동안 화면이 같이 밝아진다 — 페이드를 앞세우면 뽑기 전에 빈 판이 먼저 뜬다.
            m_stageSeq.Insert(0f, revealPanel.DOFade(1f, panelFadeDuration))
                      .InsertCallback(panelFadeDuration, () =>
                      {
                          if (revealPanel == null) return;
                          revealPanel.blocksRaycasts = true;
                          revealPanel.interactable = true;
                      });
        }

        if (cardStack != null)
        {
            var t_emerge = cardStack.PlayEmerge(cardEmergeDuration);
            if (t_emerge != null) m_stageSeq.Insert(0f, t_emerge);
        }

        if (t_root != null)
        {
            var t_tr = t_root.transform;
            CapturePackHome(t_tr);
            t_tr.DOKill();

            // 카드가 어느 정도 나온 뒤에 빠진다 — 먼저 내려가면 "빼냈다"가 아니라 "따로 사라졌다"가 된다.
            // InBack이라 살짝 버티다 쑥 내려간다(잡아당겨 빼는 손맛).
            float t_pullAt = cardEmergeDuration * packPullDelay;
            m_stageSeq.Insert(t_pullAt, t_tr.DOLocalMoveY(m_packHome.y - packPullDrop, packPullDuration).SetEase(Ease.InBack))
                      .InsertCallback(t_pullAt + packPullDuration, () => t_root.SetActive(false));
        }

        m_stageSeq.AppendInterval(burstHold)
                  .OnComplete(EnterFlicking);
    }

    // 넘기기: 맨 위부터 스와이프로 한 장씩. 여기부터는 유저 페이스다.
    void EnterFlicking()
    {
        m_stage = EStage.Flicking;

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
            case EStage.Bursting:
                // Complete(true)면 OnComplete가 실행되며 다음 단계로 이어진다.
                if (m_stageSeq != null && m_stageSeq.IsActive()) m_stageSeq.Complete(true);
                break;

            case EStage.Tearing:
                if (tearHandle != null) tearHandle.ForceTornInstant();
                EnterBursting();
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

        if (tearHandle != null) tearHandle.ForceTornInstant();

        // 분출 전(입장·뜯기)에서 요약으로 직행하면 EnterBursting을 거치지 않는다 —
        // 팩이 열렸다는 사실은 연출을 건너뛰어도 참이므로 여기서 보장한다(튜토리얼이 이 신호를 기다린다).
        AnnounceOpened();
        SetTearHint(false, true);   // EnterBursting을 거치지 않는 유일한 경로

        var t_root = ResolvePackRoot();
        if (t_root != null) t_root.SetActive(false);

        if (revealPanel != null)
        {
            revealPanel.DOKill();
            revealPanel.alpha = 1f;
            revealPanel.blocksRaycasts = true;
            revealPanel.interactable = true;
        }

        // 더미는 걷어내기만 한다 — 카드는 요약 격자가 전부 다시 보여주므로 넘기는 시늉이 필요 없다
        // (분출 전에 스킵했다면 더미가 아예 서지도 않았다).
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

    // "팩이 열렸다"를 세션당 1회만 알린다. 발화 시점은 뜯기 확정 — 튜토리얼이 물려 있어 의미를 옮기지 않는다.
    void AnnounceOpened()
    {
        if (m_announced) return;
        m_announced = true;

        OnAnyPackOpened?.Invoke();
    }

    // 뜯기 안내 표시/숨김. 입력은 절대 먹지 않는다 — 문구가 팩 드래그를 가로막으면 개봉 자체가 막힌다.
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

    // 패널 초기화: 완전 투명 + 입력 차단(fade 완료까지 아무것도 눌리지 않게).
    void ResetPanel()
    {
        if (revealPanel == null) return;

        revealPanel.DOKill();
        revealPanel.alpha = 0f;
        revealPanel.blocksRaycasts = false;
        revealPanel.interactable = false;
    }

    void CapturePackHome(Transform _tr)
    {
        if (m_packHomeCaptured) return;
        m_packHome = _tr.localPosition;
        m_packHomeScale = _tr.localScale;
        m_packHomeCaptured = true;
    }

    GameObject ResolvePackRoot()
        => packRoot != null ? packRoot : (tearHandle != null ? tearHandle.gameObject : null);

    void KillStageSeq()
    {
        if (m_stageSeq != null && m_stageSeq.IsActive()) m_stageSeq.Kill();
        m_stageSeq = null;
    }
}
