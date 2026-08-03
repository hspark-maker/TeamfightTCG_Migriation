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

    // 이 씬에서 대기 중인 스텝. null이면 걸 게이트가 없다.
    TutorialStepDef m_step;

    // 스텝 진입이 다시 ApplyCurrentStep을 부르는 경로를 막는다(예약 후 재실행).
    bool m_applying;
    bool m_pendingApply;

    // 구독이 Start가 아니라 Awake인 이유: 발화 지점인 LobbyTabController.Start()가 이 브리지 Start보다 먼저 돌 수 있고
    // (둘 다 DefaultExecutionOrder가 없다) 그러면 OnActivated를 통째로 놓쳐 게이트가 영영 안 뜬다.
    void Awake()
    {
        TriggeredTutorialRunner.EnsureData(this.data);

        TriggeredTutorialRunner.OnActivated  += OnActivated;
        TutorialAnchorRegistry.OnRegistered  += OnAnchorRegistered;
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

        // 진입 "전" 스텝·좌표. 자동 스텝은 Enter 안에서 좌표를 커밋하므로 진입 뒤에는 다음 칸이 보인다.
        TriggeredTutorialRunner.TryGetCurrentStep(out var t_entering);
        int t_before = TriggeredTutorialRunner.StepIndex;

        // false = 자동 스텝·씬 전환 등 이 씬에서 걸 게이트가 없음.
        if (!TriggeredTutorialRunner.EnterCurrentStep())
        {
            // 씬에 남는 자동 스텝은 여기서 끊으면 다음 스텝이 영영 진입하지 못한다 → 같은 루프에서 이어 진입시킨다.
            // 좌표가 안 움직였으면 잇지 않는다 — 같은 실패를 되풀이한다.
            bool t_moved = TriggeredTutorialRunner.IsRunning && t_before != TriggeredTutorialRunner.StepIndex;

            if (t_moved && t_entering != null && !t_entering.LeavesScene) m_pendingApply = true;

            return;
        }

        if (!TriggeredTutorialRunner.TryGetCurrentStep(out var t_step)) return;

        m_step = t_step;

        // 설명 스텝은 앵커가 없어도 정상이다(강조 없이 문구만) — 완료가 딤 탭이라 진행이 막히지 않는다.
        if (m_step.Completion == EOutgameTutorialCompletion.Confirm)
        {
            if (m_step.Anchor == EOutgameTutorialAnchor.None)
            {
                OutgameTutorialGateUI.Ensure(this.gatePrefab).ShowMessageGate(null, m_step.GuideMessage, OnGateSatisfied);
                return;
            }

            TryOpenGate();
            return;
        }

        if (m_step.Completion == EOutgameTutorialCompletion.Click)
        {
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
            OutgameTutorialGateUI.Ensure(this.gatePrefab).ShowMessageGate(t_rect, m_step.GuideMessage, OnGateSatisfied);
            return;
        }

        OutgameTutorialGateUI.Ensure(this.gatePrefab).ShowGate(t_rect, t_button, m_step.GuideMessage, OnGateSatisfied);
    }

    void OnAnchorRegistered(EOutgameTutorialAnchor _key)
    {
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

    void CloseGate()
    {
        m_step = null;

        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();
    }
}
