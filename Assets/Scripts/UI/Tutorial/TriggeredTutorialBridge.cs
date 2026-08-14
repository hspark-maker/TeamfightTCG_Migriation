using System;
using DG.Tweening;
using UnityEngine;

// 트리거 발화 튜토리얼의 씬 수명 브리지(씬당 1개). OutgameTutorialBridge의 축소판이다 —
// 팩 구매·개봉 신호를 듣지 않고 억제 모드도 없다(트리거 튜토는 로비 안에서 시작해 로비 안에서 끝난다).
// 표시(OutgameTutorialGateUI)·타깃(TutorialAnchorRegistry)은 온보딩과 같은 것을 그대로 쓴다.
public class TriggeredTutorialBridge : MonoBehaviour
{
    [Tooltip("트리거 튜토리얼 목록 SO. 모든 씬의 브리지에 같은 에셋을 배선한다(주입은 멱등).")]
    [SerializeField] TriggeredTutorialData data;

    [Tooltip("안내 UI 프리팹(OutgameTutorialGate). 미배선이면 딤+문구만 그리는 코드 폴백으로 떨어진다.")]
    [SerializeField] OutgameTutorialGateUI gatePrefab;

    [Tooltip("**실패한** 강화의 결과판을 대신 걷기까지 쥐고 있는 시간(초). 결과 행이 다 떠오른 시점부터 센다.\n" +
             "성공한 판은 걷지 않는다 — 다음 안내가 그 화면 위에서 이어지고, 닫는 것도 그쪽 몫이다.\n" +
             "튜토리얼 동안에만 적용된다 — 평상시의 결과판은 유저가 탭할 때까지 그대로 서 있다.")]
    [SerializeField] float enhanceResultHold = 1.1f;

    // 이 씬에서 대기 중인 스텝. null이면 걸 게이트가 없다.
    TutorialStepDef m_step;

    // 스텝 진입이 다시 ApplyCurrentStep을 부르는 경로를 막는다(예약 후 재실행).
    bool m_applying;
    bool m_pendingApply;

    // 강화 연출이 무대를 쥔 구간. 이 동안의 앵커 재등록은 무시한다 —
    // 진화 연출은 공개 시점에 다음 단계(진화 아님)로 버튼을 갈아끼워 같은 키를 다시 등록하는데,
    // 그때 버튼은 연출 잠금으로 비활성이라 게이트가 뜨지 못하고 경고만 남는다.
    bool m_enhancing;

    // 구독이 Start가 아니라 Awake인 이유: 발화 지점인 LobbyTabController.Start()가 이 브리지 Start보다 먼저 돌 수 있고
    // (둘 다 DefaultExecutionOrder가 없다) 그러면 OnActivated를 통째로 놓쳐 게이트가 영영 안 뜬다.
    void Awake()
    {
        TriggeredTutorialRunner.EnsureData(this.data);

        TriggeredTutorialRunner.OnActivated  += OnActivated;
        TutorialAnchorRegistry.OnRegistered  += OnAnchorRegistered;

        CardDetailOverlayView.OnAnyEnhanceStarted     += OnEnhanceStarted;
        CardDetailOverlayView.OnAnyEnhanceResultReady += OnEnhanceResultReady;
        CardDetailOverlayView.OnAnyEnhanceSettled     += OnEnhanceSettled;
        CardDetailOverlayView.OnAnyClosed             += OnOverlayClosed;
        AlbumPageOverlayView.OnAnyClosed              += OnOverlayClosed;

        KeywordGrowthManager.OnEnhanced += OnKeywordEnhanced;
    }

    void Start()
    {
        // 씬 재진입 재개. 발화 자체는 OnActivated가 잡으므로 여기서는 이미 도는 런만 이어받는다.
        if (TriggeredTutorialRunner.IsRunning) ApplyCurrentStep();
    }

    void OnDestroy()
    {
        // static 이벤트에 죽은 씬 오브젝트가 남으면 다음 씬에서 오발화한다.
        TriggeredTutorialRunner.OnActivated  -= OnActivated;
        TutorialAnchorRegistry.OnRegistered  -= OnAnchorRegistered;

        CardDetailOverlayView.OnAnyEnhanceStarted     -= OnEnhanceStarted;
        CardDetailOverlayView.OnAnyEnhanceResultReady -= OnEnhanceResultReady;
        CardDetailOverlayView.OnAnyEnhanceSettled     -= OnEnhanceSettled;
        CardDetailOverlayView.OnAnyClosed             -= OnOverlayClosed;
        AlbumPageOverlayView.OnAnyClosed              -= OnOverlayClosed;

        KeywordGrowthManager.OnEnhanced -= OnKeywordEnhanced;

        CloseGate();
    }

    // 현재 스텝을 진입시킨다. 재진입(스텝 Enter → 그 안에서 다시 적용)은 버리지 않고 예약한다 —
    // 그 시점엔 이미 다음 스텝으로 넘어간 뒤라 버리면 그 스텝이 영영 적용되지 않는다.
    void ApplyCurrentStep()
    {
        if (m_applying) { m_pendingApply = true; return; }

        m_applying = true;
        try
        {
            // 상한: 스텝이 서로를 무한히 재진입시키는 저작 실수로 에디터가 멎지 않게.
            for (int t_i = 0; t_i < 8; t_i++)
            {
                m_pendingApply = false;
                ApplyStepOnce();
                if (!m_pendingApply) return;
            }

            Debug.LogWarning("[TriggeredTutorialBridge] 스텝 진입이 반복 재진입해 중단합니다 — 스텝 저작을 확인하세요.");
        }
        finally
        {
            m_applying     = false;
            m_pendingApply = false;
        }
    }

    // 현재 스텝 1회 진입. 게이트가 필요하면 앵커를 찾아 건다(없으면 등록 대기).
    void ApplyStepOnce()
    {
        // 이전 스텝의 딤·배너를 먼저 내린다 — 새 타깃이 아직 등장 전이면 옛 안내가 화면에 남는다.
        CloseGate();

        // 진입 "전" 스텝. 자동 스텝은 Enter 안에서 좌표를 커밋하므로 진입 뒤에는 다음 칸이 보인다.
        TriggeredTutorialRunner.TryGetCurrentStep(out var t_entering);

        var t_result = TriggeredTutorialRunner.EnterCurrentStep();

        // Gated가 아니면 이 씬에서 걸 게이트가 없다. 씬에 남는 자동 스텝은 여기서 끊으면 다음 스텝이 영영
        // 진입하지 못하므로 같은 루프에서 이어 진입시킨다 — 완주로 닫힌 뒤라면 이을 곳이 없다.
        if (t_result != EOutgameTutorialStepResult.Gated)
        {
            if (t_result == EOutgameTutorialStepResult.Advanced && TriggeredTutorialRunner.IsRunning
                && t_entering != null && !t_entering.LeavesScene)
                m_pendingApply = true;

            return;
        }

        if (!TriggeredTutorialRunner.TryGetCurrentStep(out var t_step)) return;

        m_step = t_step;

        // 유저가 열어 둔 화면을 스스로 닫기를 기다리는 구간 — 그 위에 안내를 얹지 않는다.
        // 어디까지 걷혀야 하는지는 완료 조건이 정한다. 이미 걷혀 있으면 기다릴 것이 없다.
        if (IsSurfaceWait(m_step.Completion))
        {
            if (IsSurfaceReady(m_step.Completion)) OnGateSatisfied();
            return;
        }

        // 설명 스텝은 앵커가 없어도 정상이다(강조 없이 문구만) — 완료가 딤 탭이라 진행이 막히지 않는다.
        if (m_step.Completion == EOutgameTutorialCompletion.Confirm)
        {
            if (m_step.Anchor == EOutgameTutorialAnchor.None)
            {
                OutgameTutorialGateUI.Ensure(this.gatePrefab)
                    .ShowMessageGate(null, m_step.GuideMessage, OnGateSatisfied, m_step.MessageAtBottom, m_step.UseDim);
                return;
            }

            TryOpenGate();
            return;
        }

        if (m_step.Completion == EOutgameTutorialCompletion.Click
         || m_step.Completion == EOutgameTutorialCompletion.Enhance
         || m_step.Completion == EOutgameTutorialCompletion.KeywordEnhance)
        {
            // 이 스텝에 들어선 순간 강화 한 방의 값이 0으로 눕는다(안내가 대주는 무료 한 방).
            // 화면은 이 스텝보다 먼저 열리므로(같은 클릭이 창을 먼저 띄운다) 옛 비용을 띄운 채다 —
            // 다시 읽게 하지 않으면 잔액이 그에 못 미치는 유저의 강화 버튼이 비활성으로 굳는다.
            if (m_step.Completion == EOutgameTutorialCompletion.Enhance)
                CardGrowthManager.NotifyCostRuleChanged();
            else if (m_step.Completion == EOutgameTutorialCompletion.KeywordEnhance)
                KeywordGrowthManager.NotifyCostRuleChanged();

            TryOpenGate();
            return;
        }

        // 이 브리지는 팩 개봉·구매 신호를 구독하지 않는다 → 그 스텝을 꽂으면 완료 신호가 없어 영구 정지다(저작 실수).
        Debug.LogWarning($"[TriggeredTutorialBridge] 스텝 {TriggeredTutorialRunner.StepIndex}({m_step.Action})의 완료 조건({m_step.Completion})은 트리거 튜토리얼에서 지원하지 않습니다 — 중단합니다.");
        CloseGate();
    }

    // 타깃이 이미 등록돼 있으면 즉시 게이트, 아니면 등록 통지를 기다린다.
    void TryOpenGate()
    {
        if (m_step == null || m_step.Anchor == EOutgameTutorialAnchor.None) return;
        if (!TutorialAnchorRegistry.TryGet(m_step.Anchor, out var t_rect, out var t_button)) return;

        // 설명 스텝은 앵커를 "강조할 영역"으로만 쓴다 — 누를 대상이 아니라 Button이 없어도 되고 완료는 딤 탭이다.
        if (m_step.Completion == EOutgameTutorialCompletion.Confirm)
        {
            OutgameTutorialGateUI.Ensure(this.gatePrefab)
                .ShowMessageGate(t_rect, m_step.GuideMessage, OnGateSatisfied, m_step.MessageAtBottom, m_step.UseDim);
            return;
        }

        // 강화는 눌러도 실패할 수 있다(골드 부족·확률 실패) → 클릭을 완료로 넘기지 않는다.
        // 완료는 성공 신호(OnEnhanceSettled·OnKeywordEnhanced)가 확정한다.
        Action t_onSatisfied = m_step.Completion == EOutgameTutorialCompletion.Enhance
                            || m_step.Completion == EOutgameTutorialCompletion.KeywordEnhance
            ? null
            : (Action)OnGateSatisfied;

        OutgameTutorialGateUI.Ensure(this.gatePrefab).ShowGate(t_rect, t_button, m_step.GuideMessage, t_onSatisfied, m_step.UseDim);
    }

    // 오버레이 하나가 닫혔다. 기다리던 화면이 아직 남아 있으면 계속 기다린다.
    void OnOverlayClosed()
    {
        if (m_step == null || !IsSurfaceWait(m_step.Completion)) return;
        if (!IsSurfaceReady(m_step.Completion)) return;

        OnGateSatisfied();
    }

    // 이 완료 조건이 "유저가 화면을 닫기를 기다리는" 부류인가.
    static bool IsSurfaceWait(EOutgameTutorialCompletion _completion)
        => _completion == EOutgameTutorialCompletion.LobbyReturn
        || _completion == EOutgameTutorialCompletion.CardDetailReturn;

    // 기다리던 화면이 걷혔는가. 상세 하나만 묻는 스텝은 뒤에 남는 도감 페이지를 세지 않는다 —
    // 카드에서 손을 뗀 그 순간이 안내를 이어 붙일 자리이고, 도감을 마저 닫을 이유는 안내에 없다.
    static bool IsSurfaceReady(EOutgameTutorialCompletion _completion)
        => _completion == EOutgameTutorialCompletion.CardDetailReturn
            ? !CardDetailOverlayView.IsOpen
            : IsLobbySurfaceVisible();

    // 로비 탭 화면이 그대로 보이는가(도감이 띄우는 팝업이 하나도 없는 상태).
    static bool IsLobbySurfaceVisible()
        => !CardDetailOverlayView.IsOpen && !AlbumPageOverlayView.IsOpen;

    // 강화가 무대를 쥐었다 — 안내만 접는다(스텝은 그대로 대기).
    // 접지 않으면 결과판이 되살린 "한 번 더" 버튼 위에 손가락이 다시 떠서, 유저가 그걸 따라 누르는 동안
    // 결과판이 닫히지 않아 완료 신호가 영영 오지 않는다.
    void OnEnhanceStarted()
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.Enhance) return;

        m_enhancing = true;
        HideGuide();
    }

    // 결과판에 읽을 것이 다 떠올랐다. 성공이면 판을 걷지 않고 여기서 다음 안내로 넘긴다 —
    // 결과를 읽고 있는 그 화면이 마지막 말을 얹을 자리이고, 판은 그 말을 받은 다음 스텝이 닫는다.
    // 실패는 같은 자리에서 다시 누르는 일이라 종전대로다: 잠깐 쥐었다 대신 걷어 상세로 돌려보내고,
    // 그 복귀(OnEnhanceSettled)가 안내를 되세운다.
    // 유저가 먼저 탭했거나 "한 번 더"로 넘어갔으면 걷을 판이 없다(RequestClose가 조용히 지나간다).
    void OnEnhanceResultReady(EnhanceResult _result)
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.Enhance) return;

        if (_result.Outcome == EEnhanceOutcome.Success)
        {
            // 무대는 아직 강화가 쥐고 있지만 안내는 여기서 손을 뗀다 — 남겨 두면 다음 스텝의 앵커 등록을 무시한다.
            m_enhancing = false;
            OnGateSatisfied();
            return;
        }

        DOVirtual.DelayedCall(Mathf.Max(0f, this.enhanceResultHold), CardDetailOverlayView.CloseEnhanceResult)
                 .SetLink(gameObject);
    }

    // 강화 한 방이 연출·결과판까지 끝나 상세로 돌아왔다. 실패는 같은 자리에서 다시 누르는 일이라 안내만 되세운다.
    // 성공은 결과판이 떠오른 시점에 이미 넘어갔으므로 여기 오면 스텝이 아니다(위 가드가 걸러낸다) —
    // 결과판 없이 끝난 강화(연출 미배선)만 성공 분기로 들어온다.
    void OnEnhanceSettled(EnhanceResult _result)
    {
        m_enhancing = false;

        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.Enhance) return;

        if (_result.Outcome == EEnhanceOutcome.Success) { OnGateSatisfied(); return; }

        TryOpenGate();
    }

    // 키워드 강화 성공. 카드 강화와 달리 무대를 쥐는 결과판이 없어 기다릴 것 없이 바로 넘긴다.
    void OnKeywordEnhanced(CardKeyword _keyword)
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.KeywordEnhance) return;

        OnGateSatisfied();
    }

    void OnAnchorRegistered(EOutgameTutorialAnchor _key)
    {
        if (m_enhancing) return;
        if (m_step == null || _key != m_step.Anchor) return;

        TryOpenGate();
    }

    // 발화 통지. 탭 전환 도중에 켜지므로 이 씬이 그대로 이어받는다.
    void OnActivated() => ApplyCurrentStep();

    // 완료 → 다음 스텝을 같은 씬에서 이어간다(트리거 튜토는 씬을 떠나지 않는다).
    void OnGateSatisfied()
    {
        TriggeredTutorialRunner.NotifyStepSatisfied();

        if (!TriggeredTutorialRunner.IsRunning) { CloseGate(); return; }

        ApplyCurrentStep();
    }

    // 안내 표시만 접는다 — 스텝은 그대로 서 있고, TryOpenGate로 언제든 다시 세울 수 있다.
    // CloseGate와 갈라 둔다: 그쪽은 m_step까지 비워 완료 신호를 받을 주체가 사라진다.
    void HideGuide()
    {
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();
    }

    void CloseGate()
    {
        m_step      = null;
        m_enhancing = false;

        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();
    }
}
