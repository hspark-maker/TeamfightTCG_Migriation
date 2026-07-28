using System;
using UnityEngine;
using UnityEngine.UI;

// 아웃게임 튜토리얼의 씬 수명 브리지(씬당 1개, 로비·개봉 씬).
// 씬 이름을 보지 않는다 — 현재 스텝의 앵커가 이 씬에 등록되는 순간에만 게이트가 켜지고, 없으면 조용히 대기한다.
// 완료 판정은 두 갈래: 클릭이 곧 완료인 스텝(WaitClick/BattleEntry)과, 결과 신호가 완료인 스텝
// (WaitPackOpen=개봉, WaitPurchase=구매 성공). 후자는 눌러도 실패할 수 있어 클릭으로 커밋하면 진행도가 앞서 나간다.
public class OutgameTutorialBridge : MonoBehaviour
{
    [Tooltip("튜토리얼 스텝 시퀀스 SO. 모든 씬의 브리지에 같은 에셋을 배선한다(주입은 멱등).")]
    [SerializeField] OutgameTutorialData data;

    [Tooltip("이 씬에서는 딤·배너를 띄우지 않는다. 스텝 완료 감지와 진행도 커밋은 그대로 — 씬 자체 안내(개봉 스와이프 문구 등)가 역할을 대신한다.")]
    [SerializeField] bool suppressGuideUI;

    // 게이트가 기다리는 앵커. None이면 이 씬에서 걸 게이트가 없다.
    EOutgameTutorialAnchor m_waiting = EOutgameTutorialAnchor.None;
    string m_message;
    bool m_subscribed;

    // 억제 모드에서 클릭을 직접 듣는 타깃. 게이트가 없으니 리스너 부착·해제를 브리지가 진다.
    Button m_silentButton;
    bool m_silentDone;

    // 현재 스텝 종류. AutoPurchase는 "대기 중인 스텝 없음"과 같은 뜻이다(자동 스텝이라 여기서 대기하지 않는다).
    OutgameTutorialData.EStepKind m_kind = OutgameTutorialData.EStepKind.AutoPurchase;

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
        // 이전 스텝의 딤·배너를 먼저 내린다 — 새 타깃이 아직 등장 전이면(개봉 연출 중의 획득 버튼 등)
        // 옛 안내가 화면에 남는다.
        CloseGate();

        // false = 자동 스텝·씬 전환 등 이 씬에서 걸 게이트가 없음.
        if (!OutgameTutorialRunner.EnterCurrentStep()) return;
        if (!OutgameTutorialRunner.TryGetCurrentStep(out var t_step)) return;

        m_kind    = t_step.kind;
        m_message = t_step.guideMessage;

        // 3D 팩은 Overlay 딤 아래로 가려져 구멍을 뚫을 수 없다 → 앵커 없이 배너만 띄우고 개봉 신호로 완료한다.
        // 억제 씬에서는 배너도 생략 — 완료는 개봉 신호(Subscribe에서 이미 구독)가 그대로 확정한다.
        if (m_kind == OutgameTutorialData.EStepKind.WaitPackOpen)
        {
            if (!suppressGuideUI) OutgameTutorialGateUI.Ensure().ShowBanner(m_message);
            return;
        }

        if (t_step.anchor == EOutgameTutorialAnchor.None)
        {
            // 클릭 대기 스텝인데 타깃이 없으면 진행이 불가능하다(저작 실수).
            Debug.LogWarning($"[OutgameTutorialBridge] 스텝 {OutgameTutorialProgress.StepIndex}({t_step.kind})에 앵커가 없어 게이트를 걸 수 없습니다.");
            CloseGate();
            return;
        }

        m_waiting = t_step.anchor;
        TryOpenGate();
    }

    // 타깃이 이미 등록돼 있으면 즉시 게이트, 아니면 등록 통지를 기다린다.
    void TryOpenGate()
    {
        if (m_waiting == EOutgameTutorialAnchor.None) return;
        if (!TutorialAnchorRegistry.TryGet(m_waiting, out var t_rect, out var t_button)) return;

        // 구매는 눌러도 실패할 수 있다(골드 부족) → 클릭을 완료로 넘기지 않는다. 딤만 유지하고
        // 완료는 구매 성공 신호가 확정하며, 버튼이 잠기면 게이트가 알아서 딤을 걷는다(탈출로).
        Action t_onSatisfied = m_kind == OutgameTutorialData.EStepKind.WaitPurchase
            ? null
            : (Action)OnGateSatisfied;

        // 억제 씬에는 게이트가 없다 → 게이트가 대신 걸어주던 클릭 구독을 브리지가 직접 진다.
        if (suppressGuideUI)
        {
            HookSilently(t_button, t_onSatisfied);
            return;
        }

        OutgameTutorialGateUI.Ensure().ShowGate(t_rect, t_button, m_message, t_onSatisfied);
    }

    // 딤 없이 클릭만 듣는다. onSatisfied가 null인 스텝(WaitPurchase)은 딤이 유일한 표시였으므로 걸 것이 없다
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
        if (_key != m_waiting) return;

        TryOpenGate();
    }

    // 팩 개봉 신호. 다음 스텝(획득 버튼)이 같은 씬이라 그대로 이어간다.
    void OnPackOpened()
    {
        if (m_kind != OutgameTutorialData.EStepKind.WaitPackOpen) return;

        OnGateSatisfied();
    }

    // 구매 성공 신호. 곧바로 개봉 씬 로드가 뒤따르므로 커밋만 하고 다음 스텝은 그 씬의 브리지가 재개한다.
    void OnPurchased()
    {
        if (m_kind != OutgameTutorialData.EStepKind.WaitPurchase) return;

        OutgameTutorialRunner.NotifyStepSatisfied();
        CloseGate();
    }

    // 완료 → 커밋 후 다음 스텝을 같은 씬에서 이어간다(씬을 떠나는 스텝이면 다음 씬 브리지가 재개).
    void OnGateSatisfied()
    {
        var t_completed = m_kind;

        OutgameTutorialRunner.NotifyStepSatisfied();

        if (!OutgameTutorialRunner.IsRunning) { CloseGate(); return; }

        // 방금 누른 버튼이 이미 LoadScene을 걸었을 수 있다 — 여기서 다음 스텝까지 진입시키면
        // 그쪽 LoadScene이 뒤에 실행돼 목적지가 뒤집히거나(자동 스텝), 곧 사라질 게이트가 한 프레임 깜빡인다(BattleEntry).
        // 두 경우 모두 다음 씬의 브리지가 재개한다.
        if (t_completed == OutgameTutorialData.EStepKind.BattleEntry
            || (OutgameTutorialRunner.TryGetCurrentStep(out var t_next)
                && (t_next.kind == OutgameTutorialData.EStepKind.AutoPurchase
                    || t_next.kind == OutgameTutorialData.EStepKind.AutoBattle)))
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
        m_kind    = OutgameTutorialData.EStepKind.AutoPurchase;

        DetachSilent();   // 리스너가 남으면 다음 스텝·다음 씬에서 오발화한다
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear();
    }

    void Subscribe()
    {
        if (m_subscribed) return;

        TutorialAnchorRegistry.OnRegistered   += OnAnchorRegistered;
        PackRevealView.OnAnyPackOpened        += OnPackOpened;
        PackShowcaseController.OnAnyPurchased += OnPurchased;
        m_subscribed = true;
    }

    void Unsubscribe()
    {
        if (!m_subscribed) return;

        TutorialAnchorRegistry.OnRegistered   -= OnAnchorRegistered;
        PackRevealView.OnAnyPackOpened        -= OnPackOpened;
        PackShowcaseController.OnAnyPurchased -= OnPurchased;
        m_subscribed = false;
    }
}
