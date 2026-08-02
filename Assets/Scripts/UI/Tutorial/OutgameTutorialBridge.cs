using System;
using UnityEngine;
using UnityEngine.UI;

// 아웃게임 튜토리얼의 씬 수명 브리지(씬당 1개).
// 개봉은 씬이 아니라 로비 오버레이라 재개해 줄 다른 브리지가 없다 — 오버레이 열림/닫힘도 이 브리지가 직접 이어받는다.
// 씬 이름을 보지 않는다 — 현재 스텝의 앵커가 이 씬에 등록되는 순간에만 게이트가 켜지고, 없으면 조용히 대기한다.
// 스텝 타입도 보지 않는다 — 어떤 신호를 기다릴지는 스텝의 Completion 하나로 갈린다.
public class OutgameTutorialBridge : MonoBehaviour
{
    [Tooltip("튜토리얼 스텝 시퀀스 SO. 모든 씬의 브리지에 같은 에셋을 배선한다(주입은 멱등).")]
    [SerializeField] OutgameTutorialData data;

    [Tooltip("안내 UI 프리팹(OutgameTutorialGate). 미배선이면 딤+문구만 그리는 코드 폴백으로 떨어진다.")]
    [SerializeField] OutgameTutorialGateUI gatePrefab;

    [Tooltip("이 씬에서는 딤·배너를 띄우지 않는다. 스텝 완료 감지와 진행도 커밋은 그대로 — 화면 자체 안내(개봉 스와이프 문구 등)가 역할을 대신한다. 개봉 오버레이가 떠 있는 동안은 이 값과 무관하게 자동 억제된다.")]
    [SerializeField] bool suppressGuideUI;

    // 이 씬에서 대기 중인 스텝. null이면 걸 게이트가 없다(자동 스텝·씬 전환·완료).
    OutgameTutorialStep m_step;
    bool m_subscribed;

    // 억제 모드에서 클릭을 직접 듣는 타깃. 게이트가 없으니 리스너 부착·해제를 브리지가 진다.
    Button m_silentButton;
    bool m_silentDone;

    // 스텝 진입이 오버레이를 열어 ApplyCurrentStep이 자기 자신을 다시 부르는 경로를 막는다(예약 후 재실행).
    bool m_applying;
    bool m_pendingApply;

    // 개봉 오버레이가 떠 있는 동안은 로비 안내를 억제한다 — 예전에 개봉 "씬"이 이 플래그로 하던 일과 같다.
    bool SuppressGuideUI => suppressGuideUI || PackOpenOverlay.IsOpen;

    void Awake() => OutgameTutorialRunner.EnsureData(data);

    void Start()
    {
        if (!OutgameTutorialRunner.IsRunning) return;

        Subscribe();          // 타깃이 나중에 등장하는 경우를 기다린다(구독은 스텝 진입 전에).

        // 부트 로딩 완료는 LoadingScene이 보장하고 넘겨준다 — 여기서 대기할 것이 없다.
        ApplyCurrentStep();
    }

    void OnDestroy()
    {
        // static 이벤트에 죽은 씬 오브젝트가 남으면 다음 씬에서 오발화한다.
        Unsubscribe();
        CloseGate();
    }

    // 현재 스텝을 진입시킨다. 재진입(스텝 Enter → 오버레이 열림 → OnOpened)은 버리지 않고 예약한다 —
    // 그 시점엔 이미 다음 스텝으로 커밋된 뒤라 버리면 개봉 대기 스텝이 영영 적용되지 않는다.
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

            Debug.LogWarning("[OutgameTutorialBridge] 스텝 진입이 반복 재진입해 중단합니다 — 스텝 저작을 확인하세요.");
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
        // 이전 스텝의 딤·배너를 먼저 내린다 — 새 타깃이 아직 등장 전이면(개봉 연출 중의 획득 버튼 등)
        // 옛 안내가 화면에 남는다.
        CloseGate();

        // false = 자동 스텝·씬 전환 등 이 씬에서 걸 게이트가 없음.
        if (!OutgameTutorialRunner.EnterCurrentStep()) return;
        if (!OutgameTutorialRunner.TryGetCurrentStep(out var t_step)) return;

        m_step = t_step;

        // 개봉 대기는 클릭이 아니라 개봉 신호로 완료된다 — 걸 앵커도 없다(개봉 화면의 팩엔 TutorialAnchor가 없다).
        // 그래서 게이트를 건너뛰고 배너만 띄운다. 아래 앵커 조회에 도달하지 않는 유일한 스텝이다.
        // 억제 씬에서는 배너도 생략 — 완료는 개봉 신호(Subscribe에서 이미 구독)가 그대로 확정한다.
        if (m_step.Completion == EOutgameTutorialCompletion.PackOpen)
        {
            if (!SuppressGuideUI) OutgameTutorialGateUI.Ensure(this.gatePrefab).ShowBanner(m_step.GuideMessage);
            return;
        }

        // 설명 스텝은 앵커가 없어도 정상이다(강조 없이 문구만) — 완료가 딤 탭이라 진행이 막히지 않는다.
        // 억제 씬에서도 띄운다: 억제하면 완료 신호인 딤 자체가 사라져 진행이 영구히 멈춘다.
        if (m_step.Completion == EOutgameTutorialCompletion.Confirm && m_step.Anchor == EOutgameTutorialAnchor.None)
        {
            OutgameTutorialGateUI.Ensure(this.gatePrefab).ShowMessageGate(null, m_step.GuideMessage, OnGateSatisfied);
            return;
        }

        if (m_step.Anchor == EOutgameTutorialAnchor.None)
        {
            // 클릭 대기 스텝인데 타깃이 없으면 진행이 불가능하다(저작 실수).
            Debug.LogWarning($"[OutgameTutorialBridge] 스텝 {OutgameTutorialProgress.ChapterIndex}-{OutgameTutorialProgress.StepIndex}('{m_step.name}')에 앵커가 없어 게이트를 걸 수 없습니다.");
            CloseGate();
            return;
        }

        TryOpenGate();
    }

    // 타깃이 이미 등록돼 있으면 즉시 게이트, 아니면 등록 통지를 기다린다.
    void TryOpenGate()
    {
        if (m_step == null || m_step.Anchor == EOutgameTutorialAnchor.None) return;
        if (!TutorialAnchorRegistry.TryGet(m_step.Anchor, out var t_rect, out var t_button)) return;

        // 설명 스텝은 앵커를 "강조할 영역"으로만 쓴다 — 누를 대상이 아니라 Button이 없어도 되고 완료는 딤 탭이다.
        // 억제 씬에서도 예외적으로 띄운다(딤이 없으면 완료 신호가 없어 진행이 멈춘다).
        if (m_step.Completion == EOutgameTutorialCompletion.Confirm)
        {
            OutgameTutorialGateUI.Ensure(this.gatePrefab).ShowMessageGate(t_rect, m_step.GuideMessage, OnGateSatisfied);
            return;
        }

        // 구매는 눌러도 실패할 수 있다(골드 부족) → 클릭을 완료로 넘기지 않는다. 딤만 유지하고
        // 완료는 구매 성공 신호가 확정하며, 버튼이 잠기면 게이트가 알아서 딤을 걷는다(탈출로).
        Action t_onSatisfied = m_step.Completion == EOutgameTutorialCompletion.Purchase
            ? null
            : (Action)OnGateSatisfied;

        // 억제 중에는 게이트가 없다 → 게이트가 대신 걸어주던 클릭 구독을 브리지가 직접 진다.
        if (SuppressGuideUI)
        {
            HookSilently(t_button, t_onSatisfied);
            return;
        }

        OutgameTutorialGateUI.Ensure(this.gatePrefab).ShowGate(t_rect, t_button, m_step.GuideMessage, t_onSatisfied);
    }

    // 딤 없이 클릭만 듣는다. onSatisfied가 null인 스텝(구매 대기)은 딤이 유일한 표시였으므로 걸 것이 없다
    // — 완료는 구매 성공 신호가 확정한다.
    void HookSilently(Button _button, Action _onSatisfied)
    {
        DetachSilent();

        if (_button == null || _onSatisfied == null) return;

        m_silentButton = _button;
        m_silentDone   = false;
        m_silentButton.onClick.AddListener(OnSilentClicked);
    }

    void OnSilentClicked()
    {
        if (m_silentDone) return;
        m_silentDone = true;

        DetachSilent();      // 콜백이 다음 스텝을 걸 수 있도록 먼저 정리(GateUI.OnTargetClicked와 같은 순서)
        OnGateSatisfied();
    }

    void DetachSilent()
    {
        if (m_silentButton != null) m_silentButton.onClick.RemoveListener(OnSilentClicked);
        m_silentButton = null;
    }

    void OnAnchorRegistered(EOutgameTutorialAnchor _key)
    {
        if (m_step == null || _key != m_step.Anchor) return;

        TryOpenGate();
    }

    // 팩 개봉 신호. 다음 스텝(획득 버튼)이 같은 씬(오버레이 위)이라 그대로 이어간다.
    void OnPackOpened()
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.PackOpen) return;

        OnGateSatisfied();
    }

    // 구매 성공 신호. 곧바로 개봉 오버레이가 열리므로 커밋만 하고, 다음 스텝은 OnPackOverlayOpened가 재개한다.
    void OnPurchased()
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.Purchase) return;

        OutgameTutorialRunner.NotifyStepSatisfied();
        CloseGate();
    }

    // 개봉 오버레이 열림/닫힘. 씬이 바뀌지 않으므로 재개해 줄 새 브리지가 없다 — 이 브리지가 직접 이어간다.
    void OnPackOverlayOpened() => ApplyCurrentStep();

    void OnPackOverlayClosed() => ApplyCurrentStep();

    // 완료 → 커밋 후 다음 스텝을 같은 씬에서 이어간다(씬을 떠나는 스텝이면 다음 씬 브리지가 재개).
    void OnGateSatisfied()
    {
        bool t_leftScene = m_step != null && m_step.LeavesScene;

        OutgameTutorialRunner.NotifyStepSatisfied();

        if (!OutgameTutorialRunner.IsRunning) { CloseGate(); return; }

        // 방금 누른 버튼이 이미 LoadScene을 걸었을 수 있다 — 여기서 다음 스텝까지 진입시키면
        // 그쪽 LoadScene이 뒤에 실행돼 목적지가 뒤집히거나(자동 스텝), 곧 사라질 게이트가 한 프레임 깜빡인다(전투 진입).
        // 판정 기준은 Completion이 아니라 LeavesScene이다 — 씬을 떠나는 스텝만 다음 브리지가 재개할 수 있고,
        // 자동 스텝이라도 제자리에 남는 것(개봉 오버레이)은 이 브리지가 직접 이어가야 한다.
        if (t_leftScene
            || (OutgameTutorialRunner.TryGetCurrentStep(out var t_next) && t_next.LeavesScene))
        {
            CloseGate();
            return;
        }

        ApplyCurrentStep();
    }

    void CloseGate()
    {
        m_step = null;

        DetachSilent();   // 리스너가 남으면 다음 스텝·다음 씬에서 오발화한다
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();
    }

    void Subscribe()
    {
        if (m_subscribed) return;

        TutorialAnchorRegistry.OnRegistered   += OnAnchorRegistered;
        PackRevealView.OnAnyPackOpened        += OnPackOpened;
        PackShowcaseController.OnAnyPurchased += OnPurchased;
        PackOpenOverlay.OnOpened              += OnPackOverlayOpened;
        PackOpenOverlay.OnClosed              += OnPackOverlayClosed;
        m_subscribed = true;
    }

    void Unsubscribe()
    {
        if (!m_subscribed) return;

        TutorialAnchorRegistry.OnRegistered   -= OnAnchorRegistered;
        PackRevealView.OnAnyPackOpened        -= OnPackOpened;
        PackShowcaseController.OnAnyPurchased -= OnPurchased;
        PackOpenOverlay.OnOpened              -= OnPackOverlayOpened;
        PackOpenOverlay.OnClosed              -= OnPackOverlayClosed;
        m_subscribed = false;
    }
}
