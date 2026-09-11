using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// 아웃게임 튜토리얼의 씬 수명 브리지(씬당 1개). 강제 시퀀스(세이브 커서)와 자율 안내(메모리 커서)를 같은 코드로 그린다 —
// 둘은 시간상 겹치지 않으므로(자율은 졸업 뒤에만 열린다) "지금 어느 커서인가"는 러너에게 매번 묻는다.
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

    [Tooltip("강화 결과판을 대신 걷기까지 쥐고 있는 시간(초). 결과 행이 다 떠오른 시점부터 센다.\n" +
             "**실패한** 판, 그리고 해금 연출을 기다리는(waitUnlockIntro) **성공한** 판이 이 시간 뒤에 걷힌다.\n" +
             "그 밖의 성공한 판은 걷지 않는다 — 다음 안내가 그 화면 위에서 이어지고, 닫는 것도 그쪽 몫이다.\n" +
             "튜토리얼 동안에만 적용된다 — 평상시의 결과판은 유저가 탭할 때까지 그대로 서 있다.")]
    [SerializeField] float enhanceResultHold = 1.1f;

    // 이 씬에서 대기 중인 스텝. null이면 걸 게이트가 없다(자동 스텝·씬 전환·완료).
    TutorialStepDef m_step;
    bool m_subscribed;

    static OutgameTutorialBridge s_rankEntryOwner;
    internal static bool IsRankEntryPending => s_rankEntryOwner != null;
    TutorialStepDef m_rankEntryStep;
    bool m_rankPreparing;
    bool m_rankPrepared;
    bool m_rankFailed;
    bool m_rankCompleting;

    // 억제 모드에서 클릭을 직접 듣는 타깃. 게이트가 없으니 리스너 부착·해제를 브리지가 진다.
    Button m_silentButton;
    bool m_silentDone;

    // 스텝 진입이 오버레이를 열어 ApplyCurrentStep이 자기 자신을 다시 부르는 경로를 막는다(예약 후 재실행).
    bool m_applying;
    bool m_pendingApply;

    // 강화 연출이 무대를 쥔 구간. 이 동안의 앵커 재등록은 무시한다 —
    // 진화 연출은 공개 시점에 다음 단계(진화 아님)로 버튼을 갈아끼워 같은 키를 다시 등록하는데,
    // 그때 버튼은 연출 잠금으로 비활성이라 게이트가 뜨지 못하고 경고만 남는다.
    bool m_enhancing;

    // 강화 성공이 연 해금 연출이 끝나기를 기다리는 구간(waitUnlockIntro 저작이 켜진 스텝에서만).
    bool m_awaitingUnlockFx;

    // 개봉 오버레이가 떠 있는 동안은 로비 안내를 억제한다 — 예전에 개봉 "씬"이 이 플래그로 하던 일과 같다.
    bool SuppressGuideUI => suppressGuideUI || PackOpenOverlay.IsOpen;

    // ── 커서 창구 4개. 이 넷 밖에서는 강제/자율을 구분하지 않는다 — 나머지 코드는 스텝의 Completion만 본다.
    // 자율 세션이 도는 동안은 강제 시퀀스가 끝난 뒤라(졸업이 문이다) 두 커서가 동시에 서지 않는다.
    static bool GuidedCursor => OutgameTutorialRunner.IsGuidedRunning;

    static bool CursorRunning => OutgameTutorialRunner.IsGuidedRunning || OutgameTutorialRunner.IsRunning;

    static bool TryGetCursorStep(out TutorialStepDef _step) => OutgameTutorialGuide.TryGetCurrentStep(out _step);

    static EOutgameTutorialStepResult EnterCursorStep()
        => GuidedCursor ? OutgameTutorialRunner.EnterGuidedStep() : OutgameTutorialRunner.EnterCurrentStep();

    static void SatisfyCursorStep()
    {
        if (GuidedCursor) OutgameTutorialRunner.NotifyGuidedStepSatisfied();
        else              OutgameTutorialRunner.NotifyStepSatisfied();
    }

    static string CursorCoord
        => GuidedCursor ? $"guided({OutgameTutorialRunner.GuidedTrigger})"
                        : $"{OutgameTutorialProgress.ChapterIndex}-{OutgameTutorialProgress.StepIndex}";

    // 구독이 Start가 아니라 Awake인 이유: 자율 발화 지점인 LobbyTabController.Start()가 이 브리지 Start보다 먼저 돌 수 있고
    // (둘 다 DefaultExecutionOrder가 없다) 그러면 OnGuidedActivated를 통째로 놓쳐 게이트가 영영 안 뜬다.
    void Awake()
    {
        OutgameTutorialRunner.EnsureData(data);
        Subscribe();
    }

    void Start()
    {
        // 씬 재진입 재개. 자율 발화 자체는 OnGuidedActivated가 잡으므로 여기서는 이미 도는 커서만 이어받는다.
        // 초기화 로딩 완료는 LoadingScene이 보장하고 넘겨준다 — 여기서 대기할 것이 없다.
        if (CursorRunning) ApplyCurrentStep();
    }

    void OnDestroy()
    {
        if (s_rankEntryOwner == this) s_rankEntryOwner = null;
        ServerWaitOverlay.Release(this);
        // static 이벤트에 죽은 씬 오브젝트가 남으면 다음 씬에서 오발화한다.
        Unsubscribe();
        CloseGate();

        // 자율 안내는 로비 안에서 시작해 로비 안에서 끝난다 — 씬을 떠나면 낙인 없이 끊고, 다음에 알림 점이 다시 부른다.
        OutgameTutorialRunner.AbortGuided();
    }

    // 현재 스텝을 진입시킨다. 재진입(스텝 Enter → 오버레이 열림 → OnOpened)은 버리지 않고 예약한다 —
    // 그 시점엔 이미 다음 스텝으로 커밋된 뒤라 버리면 개봉 대기 스텝이 영영 적용되지 않는다.
    void ApplyCurrentStep()
    {
        if (!CursorRunning) return;   // 강제도 자율도 서 있지 않으면 걸 게이트가 없다

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

            Debug.LogWarning("[OutgameTutorialBridge] Step entry keeps re-entering, so it is aborted — check the step authoring.");
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

        // 해금은 좌표에서 파생되므로 좌표가 움직인 뒤 한 번 반영해야 잠금 UI가 따라온다.
        // 스텝 적용의 단일 창구라 자동 스텝이 스스로 커밋하는 경로까지 여기서 함께 잡힌다
        // (Runner.OnStepChanged는 NotifyStepSatisfied에서만 발화해 그 경로를 놓친다).
        OutgameFeatureLock.Refresh();

        // 진입 "전" 스텝과 좌표. 자동 스텝은 Enter 안에서 좌표를 커밋하므로 진입 뒤에는 다음 칸이 보인다.
        TryGetCursorStep(out var t_entering);
        bool   t_guided = GuidedCursor;
        string t_at     = CursorCoord;

        var t_result = EnterCursorStep();

        // 씬에 남는 자동 스텝은 여기서 끊으면 다음 스텝이 무관한 외부 신호(개봉 닫힘 등)를 기다리게 된다.
        // 그 자리 의존을 없애려고 같은 루프에서 다음 칸을 이어 진입시킨다(상한 8회가 폭주를 막는다).
        if (t_result == EOutgameTutorialStepResult.Advanced)
        {
            if (CursorRunning && t_entering != null && !t_entering.LeavesScene) m_pendingApply = true;
            return;
        }

        // 좌표가 그대로라 이 씬에서 이 스텝을 다시 세울 방법이 없다 — 위 CloseGate가 m_step을 비워 앵커 등록 통지도
        // 못 깨운다. 강제는 진행이 여기서 멈추므로 기능 잠금만이라도 걷어 유저가 게임을 이어갈 수 있게 하고,
        // 자율은 낙인 없이 이번 세션만 접는다(다음에 알림 점이 다시 부른다).
        if (t_result == EOutgameTutorialStepResult.Failed)
        {
            if (t_guided)
            {
                Debug.LogWarning($"[OutgameTutorialBridge] Guided step {t_at} failed to enter — deferring it for this session.");
                OutgameTutorialRunner.AbortGuided(OutgameTutorialRunner.GuidedTrigger);
            }
            else if (OutgameTutorialRunner.IsRunning)
            {
                Debug.LogWarning($"[OutgameTutorialBridge] Progress stops because entering step {t_at} failed — releasing the feature lock.");
                OutgameFeatureLock.NotifyStalled();
            }

            return;
        }

        if (!TryGetCursorStep(out var t_step)) return;

        m_step = t_step;

        PresentStep();
    }

    // 현재 스텝의 표시를 세운다 — 진입(EnterCursorStep)과 갈라 둔다. 스텝을 다시 진입시키지 않고 표시만 세우는
    // 재진입 창구다(자동 스텝이 좌표를 두 번 커밋하는 사고를 막는다).
    // 이미 만족된 완료 조건을 여기서 다시 판정하는 것도 같은 이유다 — 서버 소진 표식처럼 뒤늦게 뒤집히는 상태는
    // 신호로 다시 오지 않으므로, 상태를 되물어야 진행이 되살아난다.
    void PresentStep()
    {
        if (m_step == null) return;

        // 개봉 대기는 클릭이 아니라 개봉 신호로 완료된다 — 걸 앵커도 없다(개봉 화면의 팩엔 TutorialAnchor가 없다).
        // 그래서 게이트를 건너뛰고 배너만 띄운다. 아래 앵커 조회에 도달하지 않는 유일한 스텝이다.
        // 억제 씬에서는 배너도 생략 — 완료는 개봉 신호(Subscribe에서 이미 구독)가 그대로 확정한다.
        if (m_step.Completion == EOutgameTutorialCompletion.PackOpen)
        {
            if (!SuppressGuideUI) OutgameTutorialGateUI.Ensure(this.gatePrefab).ShowBanner(this, m_step.GuideMessage);
            return;
        }

        // 랭크 승급 연출이 무대를 쥐고 있는 구간이다 — 걸 앵커도 없고 그려서도 안 된다(딤이 그 연출을 덮는다).
        // 연출이 끝나는 신호만 기다린다.
        if (m_step.Completion == EOutgameTutorialCompletion.RankEffect)
        {
            if (m_rankEntryStep != m_step)
            {
                m_rankEntryStep = m_step;
                m_rankPrepared = false;
                m_rankFailed = false;
            }
            if (!m_rankPrepared)
            {
                s_rankEntryOwner = this;
                if (!m_rankPreparing && !m_rankFailed) PrepareFirstRankAsync().Forget();
                return;
            }
            // 놓아줄 디렉터가 이 씬에 없으면 기다릴 신호도 없다 — 여기서 끊지 않으면 영구 정지다.
            // 완료 경로를 하나로 두려고 OnGateSatisfied가 아니라 신호 핸들러를 직접 부른다(재진입 잠금이 그쪽에 있다).
            if (!LobbyRankEffectDirector.Exists || !LobbyRankEffectDirector.Playing) OnRankEffectFinished();
            return;
        }

        // 카드 획득 연출이 무대를 쥐고 있는 구간이다 — 랭크 승급과 같은 이유로 그리지 않고 기다린다
        // (딤이 날아가는 카드를 덮는다).
        if (m_step.Completion == EOutgameTutorialCompletion.CardGain)
        {
            // 놓아줄 디렉터가 이 씬에 없으면 기다릴 신호도 없다 — 여기서 끊지 않으면 영구 정지다.
            if (!LobbyGainEffectDirector.Exists) OnGateSatisfied();
            return;
        }

        // 유저가 열어 둔 오버레이를 스스로 닫기를 기다리는 구간 — 그 위에 안내를 얹지 않는다.
        // 어디까지 걷혀야 하는지는 완료 조건이 정한다. 이미 걷혀 있으면 기다릴 것이 없다(뒤이을 안내를 한 프레임도 미루지 않는다).
        if (IsSurfaceWait(m_step.Completion))
        {
            if (IsSurfaceReady(m_step.Completion)) OnGateSatisfied();
            return;
        }

        // 삽입 연출은 스스로 손가락·문구를 띄운다 — 게이트를 겹쳐 걸지 않고 세션이 끝나기만 기다린다.
        // 연출 중 다른 탭으로 새면 그 탭 버튼이 꺼져(Focus가 대신한다) 뒤이어 그 탭을 가리키는 안내가 뜨지 못하므로,
        // 세션에게 이탈을 삼키라고 알린다.
        if (m_step.Completion == EOutgameTutorialCompletion.AlbumInsert)
        {
            AlbumInsertSession.TutorialMode = true;

            // 설 세션이 아예 없으면(연출 배선 실패·좌표가 밀린 옛 세이브) 기다릴 신호도 없다 — 여기서 끊지 않으면 영구 정지다.
            if (!AlbumInsertQueue.HasPending && !AlbumInsertSession.IsRunning) OnGateSatisfied();
            return;
        }

        // 서버가 이 축의 무료 한 방을 이미 소진했다면 이 스텝이 시킨 강화는 성립한 뒤다 — 응답 유실로 완료 신호만 잃은 자리라 여기서 통과시킨다.
        // 미달성이면 잘라내지 않고 흘려보낸다 — 이 스텝의 딤을 세우는 것은 아래 TryOpenGate 하나뿐이다.
        if (m_step.FreeOfCharge && IsFreeShotSpent(m_step.Completion))
        {
            OnGateSatisfied();
            return;
        }

        // 이 스텝에 들어선 순간 강화 한 방의 값이 0으로 눕는다(안내가 대주는 무료 한 방).
        // 화면은 이 스텝보다 먼저 열리므로(같은 클릭이 창을 먼저 띄운다) 옛 비용을 띄운 채다 —
        // 다시 읽게 하지 않으면 잔액이 그에 못 미치는 유저의 강화 버튼이 비활성으로 굳는다.
        if (m_step.Completion == EOutgameTutorialCompletion.Enhance)             CardGrowthManager.NotifyCostRuleChanged();
        else if (m_step.Completion == EOutgameTutorialCompletion.KeywordEnhance) KeywordGrowthManager.NotifyCostRuleChanged();

        // 설명 스텝은 앵커가 없어도 정상이다(강조 없이 문구만) — 완료가 딤 탭이라 진행이 막히지 않는다.
        // 억제 씬에서도 띄운다: 억제하면 완료 신호인 딤 자체가 사라져 진행이 영구히 멈춘다.
        if (m_step.Completion == EOutgameTutorialCompletion.Confirm && m_step.Anchor == EOutgameTutorialAnchor.None)
        {
            OutgameTutorialGateUI.Ensure(this.gatePrefab)
                .ShowMessageGate(this, null, m_step.GuideMessage, OnGateSatisfied, m_step.MessageAtBottom, m_step.UseDim);
            return;
        }

        if (m_step.Anchor == EOutgameTutorialAnchor.None)
        {
            // 클릭 대기 스텝인데 타깃이 없으면 진행이 불가능하다(저작 실수).
            Debug.LogWarning($"[OutgameTutorialBridge] Step {CursorCoord}({m_step.Action}) has no anchor, so a gate cannot be placed.");
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
            OutgameTutorialGateUI.Ensure(this.gatePrefab)
                .ShowMessageGate(this, t_rect, m_step.GuideMessage, OnGateSatisfied, m_step.MessageAtBottom, m_step.UseDim, SpotlightRect());
            return;
        }

        // 구매·강화는 눌러도 실패할 수 있다(골드 부족·확률 실패) → 클릭을 완료로 넘기지 않는다. 딤만 유지하고
        // 완료는 성공 신호가 확정하며, 버튼이 잠기면 게이트가 알아서 딤을 걷는다(탈출로 겸 연출 관람로).
        Action t_onSatisfied = m_step.Completion == EOutgameTutorialCompletion.Purchase
                            || m_step.Completion == EOutgameTutorialCompletion.Enhance
                            || m_step.Completion == EOutgameTutorialCompletion.KeywordEnhance
                            || m_step.Completion == EOutgameTutorialCompletion.DeckEquip
                            || m_step.Completion == EOutgameTutorialCompletion.DeckSave
            ? null
            : (Action)OnGateSatisfied;

        // 억제 중에는 게이트가 없다 → 게이트가 대신 걸어주던 클릭 구독을 브리지가 직접 진다.
        if (SuppressGuideUI)
        {
            HookSilently(t_button, t_onSatisfied);
            return;
        }

        OutgameTutorialGateUI.Ensure(this.gatePrefab)
            .ShowGate(this, t_rect, t_button, m_step.GuideMessage, t_onSatisfied, m_step.UseDim, SpotlightRect());
    }

    // 타깃과 함께 밝힐 영역. 아직 등록되지 않았으면 강조 없이 진행한다 — 이 축이 진행을 막을 이유가 없다.
    RectTransform SpotlightRect()
    {
        if (m_step == null || m_step.Spotlight == EOutgameTutorialAnchor.None) return null;

        return TutorialAnchorRegistry.TryGet(m_step.Spotlight, out var t_rect, out _) ? t_rect : null;
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
        if (m_enhancing || m_awaitingUnlockFx) return;
        if (m_step == null) return;

        // 함께 밝힐 영역이 늦게 등록되는 경우도 다시 세운다 — 안 그러면 그 스텝은 강조 없이 굳는다.
        if (_key != m_step.Anchor && _key != m_step.Spotlight) return;

        TryOpenGate();
    }

    // 팩 개봉 신호. 다음 스텝(획득 버튼)이 같은 씬(오버레이 위)이라 그대로 이어간다.
    void OnPackOpened()
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.PackOpen) return;

        OnGateSatisfied();
    }

    // 삽입 세션 종료 신호. 세션이 오버레이까지 걷고 알려 오므로 다음 안내를 그대로 이어 건다.
    void OnAlbumInsertFinished()
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.AlbumInsert) return;

        OnGateSatisfied();
    }

    // 지목한 카드가 덱에 들어갔는가. anchorCard가 있으면 그 카드만 인정한다 — 아무 카드나 끼워도 넘어가면
    // "이 카드를 골라라"라는 안내가 거짓말이 된다.
    void OnDeckCardEquipped(int _cardId)
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.DeckEquip) return;
        if (m_step.AnchorCardId > 0 && _cardId != m_step.AnchorCardId) return;

        OnGateSatisfied();
    }

    // 덱 저장이 확정됐다. 클릭이 아니라 저장이 완료인 이유는 구매·강화와 같다 —
    // 6장이 안 찬 덱에서는 눌러도 저장되지 않고 안내 팝업만 뜬다.
    void OnDeckSaved()
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.DeckSave) return;

        OnGateSatisfied();
    }

    // 오버레이 하나가 닫혔다. 기다리던 화면이 아직 남아 있으면 계속 기다린다 — 어디까지 걷혀야 하는지는 완료 조건이 정한다.
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

    // 서버가 그 축의 무료 한 방을 이미 소진했는가(= 기다리던 강화는 이미 끝났다).
    static bool IsFreeShotSpent(EOutgameTutorialCompletion _completion)
    {
        switch (_completion)
        {
            case EOutgameTutorialCompletion.Enhance:        return OutgameTutorialGuide.IsFreeShotSpentOnServer(EOutgameTutorialAction.WaitEnhance);
            case EOutgameTutorialCompletion.KeywordEnhance: return OutgameTutorialGuide.IsFreeShotSpentOnServer(EOutgameTutorialAction.WaitKeywordEnhance);
            default:                                        return false;
        }
    }

    // 로비 탭 화면이 그대로 보이는가(도감·보상이 띄우는 팝업이 하나도 없는 상태).
    static bool IsLobbySurfaceVisible()
        => !CardDetailOverlayView.IsOpen && !AlbumPageOverlayView.IsOpen && !AnyRewardOverlayOpen;

    // 보상·예고 화면 중 하나라도 떠 있는가. 같은 자리를 쓰는 오버레이라 판정도 한 곳에서 한다 —
    // 하나만 보는 코드가 남으면 그 구간에서 판정이 조용히 틀린다.
    static bool AnyRewardOverlayOpen
        => CardRewardOverlay.IsOpen || CardSetRewardOverlay.IsOpen || PackRewardOverlay.IsOpen;

    // 랭크 연출 종료 신호. 보여줄 것이 없어 지나간 경우도 같은 신호로 온다.
    void OnRankEffectFinished()
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.RankEffect) return;
        if (!m_rankPrepared || m_rankCompleting) return;

        // 완료 처리 안에서 같은 신호가 재진입해 다음 메시지까지 넘기지 않도록 먼저 잠근다.
        m_rankCompleting = true;
        try
        {
            OnGateSatisfied();
        }
        finally
        {
            m_rankCompleting = false;
        }
    }

    // 진행 좌표 저장과 서버 확정이 끝날 때까지 디렉터와 스텝 모두 기다린다.
    async UniTask PrepareFirstRankAsync()
    {
        if (m_rankPreparing || m_rankPrepared) return;
        m_rankPreparing = true;
        m_rankFailed = false;
        s_rankEntryOwner = this;
        var t_token = this.GetCancellationTokenOnDestroy();
        var t_step = m_step;
        var t_save = DataSaveManager.Data;
        int t_chapter = OutgameTutorialProgress.ChapterIndex;
        int t_index = OutgameTutorialProgress.StepIndex;
        Exception t_failure = null;
        bool t_abandoned = false;
        bool IsCurrentRequest() => !t_token.IsCancellationRequested && t_save == DataSaveManager.Data &&
            OutgameTutorialRunner.IsRunning && t_chapter == OutgameTutorialProgress.ChapterIndex &&
            t_index == OutgameTutorialProgress.StepIndex &&
            OutgameTutorialRunner.TryGetCurrentStep(out var t_current) && t_current == t_step;
        ServerWaitOverlay.Hold(this);
        try
        {
            await PlayerSaveCloud.FlushAsync().AttachExternalCancellation(t_token);
            if (!IsCurrentRequest()) { t_abandoned = true; return; }
            if (!PlayerSaveCloud.CanRunServerCommand || PlayerSaveCloud.HasPendingUpload)
                throw new InvalidOperationException("Tutorial progress has not reached the server.");

            var t_result = await RankManager.FetchServerProgressAsync().AttachExternalCancellation(t_token);
            if (!IsCurrentRequest()) { t_abandoned = true; return; }

            // 서버 미반영을 성공으로 읽으면 연출만 먼저 지나가므로 점수도 확인한다.
            if (!RankManager.HasEnteredRank(t_result.Points))
                throw new InvalidOperationException("The server has not confirmed first rank entry.");

            RankManager.AdoptServerProgress(t_result.Points, t_result.SeasonId,
                t_result.BestTierIndex, t_result.ClaimedTierIndexes);
            m_rankPrepared = true;
            if (s_rankEntryOwner == this) s_rankEntryOwner = null;

            // 초기화에서 채택했더라도 이 스텝에 서 있다면 첫 진입 연출을 재개한다.
            if (LobbyRankEffectDirector.Exists)
            {
                RankResultHandoff.Set(new RankApplyResult(0, -1, t_result.TierIndex));
                LobbyRankEffectDirector.ResumePending();
            }
        }
        catch (OperationCanceledException) when (t_token.IsCancellationRequested) { }
        catch (Exception t_exception)
        {
            if (!IsCurrentRequest()) { t_abandoned = true; return; }
            t_failure = t_exception;
            m_rankFailed = true;
        }
        finally
        {
            ServerWaitOverlay.Release(this);
            m_rankPreparing = false;
            if ((t_abandoned || t_token.IsCancellationRequested) && s_rankEntryOwner == this)
                s_rankEntryOwner = null;
        }

        if (t_token.IsCancellationRequested) return;
        if (t_failure != null)
        {
            Debug.LogWarning($"[Tutorial] First rank confirmation failed: {t_failure.GetBaseException().Message}");
            UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
            {
                titleText = "랭크 진입을 확인하지 못했습니다.\n연결을 확인한 뒤 다시 시도해 주세요.",
                yesText = "재시도",
                yesAction = () => { if (this != null) RetryFirstRankAsync().Forget(); },
                noText = "종료",
                noAction = () =>
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                },
            });
            return;
        }
        if (m_rankPrepared && !LobbyRankEffectDirector.Exists) OnRankEffectFinished();
    }

    async UniTask RetryFirstRankAsync()
    {
        // SimpleYNPopup는 콜백 뒤에 Hide하므로 새 대기를 열기 전에 한 프레임 비운다.
        await UniTask.Yield(this.GetCancellationTokenOnDestroy());
        await PrepareFirstRankAsync();
    }

    // 획득 연출 종료 신호. 실을 것이 없어 지나간 경우도 같은 신호로 온다.
    //
    // 보상 화면이 떠 있는 동안 오는 신호는 이 스텝의 것이 아니다 — 전투에서 돌아온 로비는 골드 획득 연출을
    // 스스로 재생하는데, 그 종료를 완료로 받으면 유저가 [획득]을 누르기도 전에 다음 안내가 화면 밑에 깔린다.
    // 지급이 트는 연출은 화면을 먼저 닫고 시작하므로 이 가드에 걸리지 않는다.
    void OnCardGainFinished()
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.CardGain) return;
        if (AnyRewardOverlayOpen) return;

        OnGateSatisfied();
    }

    // 강화가 무대를 쥐었다 — 안내만 접는다(스텝은 그대로 대기).
    // 접지 않으면 결과판이 되살린 "한 번 더" 버튼 위에 손가락이 다시 떠서, 유저가 그걸 따라 누르는 동안
    // 결과판이 닫히지 않아 완료 신호가 영영 오지 않는다(= 이 스텝이 반복되는 것처럼 보인다).
    void OnEnhanceStarted()
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.Enhance) return;

        m_enhancing = true;
        HideGuide();
    }

    // 결과판에 읽을 것이 다 떠올랐다. 성공이면 판을 걷지 않고 여기서 다음 안내로 넘긴다 —
    // 결과를 읽고 있는 그 화면이 마지막 말을 얹을 자리이고, 판은 그 말을 받은 다음 스텝이 닫는다.
    // 해금 연출을 기다리는 스텝만은 성공도 넘기지 않는다 — 결과판을 실패와 같이 대신 걷어
    // 무대를 돌려줘야 그 연출이 설 자리가 생긴다(m_enhancing은 켠 채 둔다).
    void OnEnhanceResultReady(EnhanceResult _result)
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.Enhance) return;

        if (_result.Outcome == EEnhanceOutcome.Success && !m_step.WaitUnlockIntro)
        {
            // 무대는 아직 강화가 쥐고 있지만 안내는 여기서 손을 뗀다 — 남겨 두면 다음 스텝의 앵커 등록을 무시한다.
            m_enhancing = false;
            OnGateSatisfied();
            return;
        }

        DOVirtual.DelayedCall(Mathf.Max(0f, this.enhanceResultHold), CardDetailOverlayView.CloseEnhanceResult)
                 .SetLink(gameObject);
    }

    // 강화 한 방이 연출·결과판까지 끝나 상세로 돌아왔다. 판정 순간에 넘겨받으면 다음 스텝(상세 닫기)이
    // 연출을 통째로 잘라내므로 이 시점을 쓴다. 실패는 같은 자리에서 다시 누르는 일이라 안내만 되세운다.
    void OnEnhanceSettled(EnhanceResult _result)
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.Enhance)
        {
            m_enhancing = false;
            return;
        }

        if (_result.Outcome == EEnhanceOutcome.Success && m_step.WaitUnlockIntro)
        {
            // 이 통지는 해금 연출을 트는 PlayPendingUnlockFx() "다음"에 온다 —
            // 그래서 지금의 IsUnlockFxPlaying이 "연출이 설지 말지"의 확정 답이다.
            if (CardDetailOverlayView.IsUnlockFxPlaying) { m_awaitingUnlockFx = true; return; }

            m_enhancing = false;
            OnGateSatisfied();
            return;
        }

        m_enhancing = false;

        if (_result.Outcome == EEnhanceOutcome.Success) { OnGateSatisfied(); return; }

        TryOpenGate();
    }

    // 해금 연출이 마지막 축까지 끝났다(잘려 끝난 경우 포함) — 미뤄 둔 완료를 여기서 넘긴다.
    void OnUnlockFxFinished()
    {
        if (!m_awaitingUnlockFx) return;

        m_awaitingUnlockFx = false;
        m_enhancing        = false;
        OnGateSatisfied();
    }

    // 키워드 강화 성공. 카드 강화와 달리 무대를 쥐는 결과판이 없어 기다릴 것 없이 바로 넘긴다.
    void OnKeywordEnhanced(CardKeyword _keyword)
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.KeywordEnhance) return;

        OnGateSatisfied();
    }

    // 자율 안내 발화 통지. 탭 전환 도중에 켜지므로 이 씬이 그대로 이어받는다.
    void OnGuidedActivated() => ApplyCurrentStep();

    // 구매 성공 신호. 서버 응답이 성립한 뒤에 오고 곧바로 개봉 오버레이가 열리므로
    // 커밋만 하고, 다음 스텝은 OnPackOverlayOpened가 재개한다.
    void OnPurchased()
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.Purchase) return;

        SatisfyCursorStep();
        CloseGate();
    }

    // 개봉 오버레이 열림/닫힘. 씬이 바뀌지 않으므로 재개해 줄 새 브리지가 없다 — 이 브리지가 직접 이어간다.
    // 세션 도중 서버 소진 표식이 켜졌다 = 이 스텝이 시킨 강화가 이미 성립했다.
    // 다시 적용하면 PresentStep의 프리체크가 통과 판정을 한다(응답을 잃어 완료 신호만 못 받은 자리).
    void OnServerFreeShotSpentChanged() => ApplyCurrentStep();

    void OnPackOverlayOpened() => ApplyCurrentStep();

    void OnPackOverlayClosed() => ApplyCurrentStep();

    // 완료 → 커밋 후 다음 스텝을 같은 씬에서 이어간다(씬을 떠나는 스텝이면 다음 씬 브리지가 재개).
    void OnGateSatisfied()
    {
        bool t_leftScene = m_step != null && m_step.LeavesScene;

        SatisfyCursorStep();

        // 완료로 닫히는 경로는 ApplyCurrentStep까지 가지 않는다(커서가 서지 않아 조기 반환) —
        // 완주 순간 전 기능이 열리는 것을 반영할 곳이 여기뿐이다.
        OutgameFeatureLock.Refresh();

        if (!CursorRunning) { CloseGate(); return; }

        // 방금 누른 버튼이 이미 LoadScene을 걸었을 수 있다 — 여기서 다음 스텝까지 진입시키면
        // 그쪽 LoadScene이 뒤에 실행돼 목적지가 뒤집히거나(자동 스텝), 곧 사라질 게이트가 한 프레임 깜빡인다(전투 진입).
        // 방금 완료된 스텝이 씬을 떠났으면 다음 브리지가 재개한다. 자동 스텝이라도 제자리에 남는 것
        // (개봉 오버레이)은 이 브리지가 직접 이어가야 한다.
        //
        // 다음 스텝을 미리 끊는 것은 "진입만으로" 씬을 떠나는 자동 스텝뿐이다 — 클릭을 기다리는 스텝은
        // LeavesScene이어도 게이트를 걸어 줘야 한다(전투 시작 버튼). 안 걸면 씬이 그대로라 재개해 줄
        // 브리지가 없고, CloseGate가 m_step을 비워 앵커 등록 통지로도 깨어나지 못한다 = 영구 정지.
        if (t_leftScene
            || (TryGetCursorStep(out var t_next)
                && t_next.LeavesScene
                && t_next.Completion == EOutgameTutorialCompletion.Auto))
        {
            CloseGate();
            return;
        }

        ApplyCurrentStep();
    }

    // 안내 표시만 접는다 — 스텝은 그대로 서 있고, TryOpenGate로 언제든 다시 세울 수 있다.
    // CloseGate와 갈라 둔다: 그쪽은 m_step까지 비워 완료 신호를 받을 주체가 사라진다.
    void HideGuide()
    {
        DetachSilent();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear(this);
    }

    void CloseGate()
    {
        m_step = null;

        // 안내가 삽입 세션을 몰던 상태를 여기서 되돌린다 — 스위치가 남으면 이후 일반 개봉의 탭 이탈까지 막는다.
        AlbumInsertSession.TutorialMode = false;

        DetachSilent();   // 리스너가 남으면 다음 스텝·다음 씬에서 오발화한다

        // 표시는 시너지 소개와 공용이다 — 남의 안내를 걷으면 그쪽은 완료 신호를 받을 주체를 잃고 영영 멈춘다.
        // 판정은 게이트가 소유권으로 한다(불변식 3): 무대가 남의 것이면 이 호출은 조용히 지나간다.
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear(this);
    }

    void Subscribe()
    {
        if (m_subscribed) return;

        TutorialAnchorRegistry.OnRegistered   += OnAnchorRegistered;
        OutgameTutorialRunner.OnGuidedActivated += OnGuidedActivated;
        KeywordGrowthManager.OnEnhanced       += OnKeywordEnhanced;
        PackRevealView.OnAnyPackOpened        += OnPackOpened;
        PackShowcaseController.OnAnyPurchased += OnPurchased;
        PackOpenOverlay.OnOpened              += OnPackOverlayOpened;
        PackOpenOverlay.OnClosed              += OnPackOverlayClosed;
        AlbumInsertSession.OnAnyFinished      += OnAlbumInsertFinished;
        DeckEditController.OnAnyCardEquipped  += OnDeckCardEquipped;
        DeckEditController.OnAnySaved         += OnDeckSaved;
        CardDetailOverlayView.OnAnyEnhanceStarted     += OnEnhanceStarted;
        CardDetailOverlayView.OnAnyEnhanceResultReady += OnEnhanceResultReady;
        CardDetailOverlayView.OnAnyEnhanceSettled     += OnEnhanceSettled;
        CardDetailOverlayView.OnAnyUnlockFxFinished   += OnUnlockFxFinished;
        LobbyRankEffectDirector.OnAnyFinished     += OnRankEffectFinished;
        LobbyGainEffectDirector.OnAnyFinished     += OnCardGainFinished;
        CardDetailOverlayView.OnAnyClosed         += OnOverlayClosed;
        AlbumPageOverlayView.OnAnyClosed          += OnOverlayClosed;
        CardRewardOverlay.OnAnyClosed             += OnOverlayClosed;
        CardSetRewardOverlay.OnAnyClosed          += OnOverlayClosed;
        PackRewardOverlay.OnAnyClosed             += OnOverlayClosed;
        OutgameTutorialGuide.OnFreeShotSpentChanged += OnServerFreeShotSpentChanged;
        m_subscribed = true;
    }

    void Unsubscribe()
    {
        if (!m_subscribed) return;

        TutorialAnchorRegistry.OnRegistered   -= OnAnchorRegistered;
        OutgameTutorialRunner.OnGuidedActivated -= OnGuidedActivated;
        KeywordGrowthManager.OnEnhanced       -= OnKeywordEnhanced;
        PackRevealView.OnAnyPackOpened        -= OnPackOpened;
        PackShowcaseController.OnAnyPurchased -= OnPurchased;
        PackOpenOverlay.OnOpened              -= OnPackOverlayOpened;
        PackOpenOverlay.OnClosed              -= OnPackOverlayClosed;
        AlbumInsertSession.OnAnyFinished      -= OnAlbumInsertFinished;
        DeckEditController.OnAnyCardEquipped  -= OnDeckCardEquipped;
        DeckEditController.OnAnySaved         -= OnDeckSaved;
        CardDetailOverlayView.OnAnyEnhanceStarted     -= OnEnhanceStarted;
        CardDetailOverlayView.OnAnyEnhanceResultReady -= OnEnhanceResultReady;
        CardDetailOverlayView.OnAnyEnhanceSettled     -= OnEnhanceSettled;
        CardDetailOverlayView.OnAnyUnlockFxFinished   -= OnUnlockFxFinished;
        LobbyRankEffectDirector.OnAnyFinished     -= OnRankEffectFinished;
        LobbyGainEffectDirector.OnAnyFinished     -= OnCardGainFinished;
        CardDetailOverlayView.OnAnyClosed         -= OnOverlayClosed;
        AlbumPageOverlayView.OnAnyClosed          -= OnOverlayClosed;
        CardRewardOverlay.OnAnyClosed             -= OnOverlayClosed;
        CardSetRewardOverlay.OnAnyClosed          -= OnOverlayClosed;
        PackRewardOverlay.OnAnyClosed             -= OnOverlayClosed;
        OutgameTutorialGuide.OnFreeShotSpentChanged -= OnServerFreeShotSpentChanged;
        m_subscribed = false;
    }
}
