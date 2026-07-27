using UnityEngine;

// 아웃게임 튜토리얼의 씬 수명 브리지(씬당 1개, 로비·개봉 씬).
// 씬 이름을 보지 않는다 — 현재 스텝의 앵커가 이 씬에 등록되는 순간에만 게이트가 켜지고, 없으면 조용히 대기한다.
public class OutgameTutorialBridge : MonoBehaviour
{
    [Tooltip("튜토리얼 스텝 시퀀스 SO. 모든 씬의 브리지에 같은 에셋을 배선한다(주입은 멱등).")]
    [SerializeField] OutgameTutorialData data;

    // 게이트가 기다리는 앵커. None이면 이 씬에서 걸 게이트가 없다.
    EOutgameTutorialAnchor m_waiting = EOutgameTutorialAnchor.None;
    string m_message;
    bool m_subscribed;

    void Awake() => OutgameTutorialRunner.EnsureData(data);

    void Start()
    {
        if (!OutgameTutorialRunner.IsRunning) return;

        Subscribe();          // 타깃이 나중에 등장하는 경우를 기다린다(구독은 스텝 진입 전에).
        ApplyCurrentStep();
    }

    void OnDestroy()
    {
        // static 이벤트에 죽은 씬 오브젝트가 남으면 다음 씬에서 오발화한다.
        Unsubscribe();
        CloseGate();
    }

    // 현재 스텝을 진입시키고, 게이트가 필요하면 앵커를 찾아 건다(없으면 등록 대기).
    void ApplyCurrentStep()
    {
        m_waiting = EOutgameTutorialAnchor.None;
        m_message = null;

        // false = 자동 스텝·씬 전환 등 이 씬에서 걸 게이트가 없음.
        if (!OutgameTutorialRunner.EnterCurrentStep()) { CloseGate(); return; }
        if (!OutgameTutorialRunner.TryGetCurrentStep(out var t_step)) { CloseGate(); return; }

        if (t_step.anchor == EOutgameTutorialAnchor.None)
        {
            // 클릭 대기 스텝인데 타깃이 없으면 진행이 불가능하다(저작 실수).
            Debug.LogWarning($"[OutgameTutorialBridge] 스텝 {OutgameTutorialProgress.StepIndex}({t_step.kind})에 앵커가 없어 게이트를 걸 수 없습니다.");
            CloseGate();
            return;
        }

        m_waiting = t_step.anchor;
        m_message = t_step.guideMessage;
        TryOpenGate();
    }

    // 타깃이 이미 등록돼 있으면 즉시 게이트, 아니면 등록 통지를 기다린다.
    void TryOpenGate()
    {
        if (m_waiting == EOutgameTutorialAnchor.None) return;
        if (!TutorialAnchorRegistry.TryGet(m_waiting, out var t_rect, out var t_button)) return;

        OutgameTutorialGateUI.Ensure().ShowGate(t_rect, t_button, m_message, OnGateSatisfied);
    }

    void OnAnchorRegistered(EOutgameTutorialAnchor _key)
    {
        if (_key != m_waiting) return;

        TryOpenGate();
    }

    // 클릭 완료 → 커밋 후 다음 스텝을 같은 씬에서 이어간다(씬을 떠나는 스텝이면 다음 씬 브리지가 재개).
    void OnGateSatisfied()
    {
        OutgameTutorialRunner.NotifyStepSatisfied();

        if (!OutgameTutorialRunner.IsRunning) { CloseGate(); return; }

        // 방금 누른 버튼이 이미 LoadScene을 걸었을 수 있다 — 여기서 AutoPurchase까지 진입시키면
        // 그쪽 LoadScene이 뒤에 실행돼 목적지가 뒤집힌다. 자동 전환 스텝은 다음 씬의 브리지가 재개한다.
        if (OutgameTutorialRunner.TryGetCurrentStep(out var t_next)
            && t_next.kind == OutgameTutorialData.EStepKind.AutoPurchase)
        {
            CloseGate();
            return;
        }

        ApplyCurrentStep();
    }

    void CloseGate()
    {
        m_waiting = EOutgameTutorialAnchor.None;
        m_message = null;
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();
    }

    void Subscribe()
    {
        if (m_subscribed) return;

        TutorialAnchorRegistry.OnRegistered += OnAnchorRegistered;
        m_subscribed = true;
    }

    void Unsubscribe()
    {
        if (!m_subscribed) return;

        TutorialAnchorRegistry.OnRegistered -= OnAnchorRegistered;
        m_subscribed = false;
    }
}
