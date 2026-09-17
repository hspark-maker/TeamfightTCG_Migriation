using System;
using System.Threading;
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
    static OutgameTutorialBridge s_instance;

    [Tooltip("튜토리얼 스텝 시퀀스 SO. 모든 씬의 브리지에 같은 에셋을 배선한다(주입은 멱등).")]
    [SerializeField] OutgameTutorialData data;

    [Tooltip("안내 UI 프리팹(OutgameTutorialGate). 미배선이면 딤+문구만 그리는 코드 폴백으로 떨어진다.")]
    [SerializeField] OutgameTutorialGateUI gatePrefab;

    internal OutgameTutorialGateUI GatePrefabForDebug => gatePrefab;

    internal static OutgameTutorialGateUI EnsureGateForGuidance()
        => OutgameTutorialGateUI.Instance != null ? OutgameTutorialGateUI.Instance
            : s_instance != null && s_instance.gatePrefab != null
                ? OutgameTutorialGateUI.Ensure(s_instance.gatePrefab) : null;

    [Tooltip("이 씬에서는 딤·배너를 띄우지 않는다. 스텝 완료 감지와 진행도 커밋은 그대로 — 화면 자체 안내(개봉 스와이프 문구 등)가 역할을 대신한다. 개봉 오버레이가 떠 있는 동안은 이 값과 무관하게 자동 억제된다.")]
    [SerializeField] bool suppressGuideUI;

    [Tooltip("강화 결과판을 대신 걷기까지 쥐고 있는 시간(초). 결과 행이 다 떠오른 시점부터 센다.\n" +
             "**실패한** 판, 그리고 해금 연출을 기다리는(waitUnlockIntro) **성공한** 판이 이 시간 뒤에 걷힌다.\n" +
             "그 밖의 성공한 판은 걷지 않는다 — 다음 안내가 그 화면 위에서 이어지고, 닫는 것도 그쪽 몫이다.\n" +
             "튜토리얼 동안에만 적용된다 — 평상시의 결과판은 유저가 탭할 때까지 그대로 서 있다.")]
    [SerializeField] float enhanceResultHold = 1.1f;

    // 이 씬에서 대기 중인 스텝. null이면 걸 게이트가 없다(자동 스텝·씬 전환·완료).
    TutorialStepDef m_step;
    TutorialStepDef m_satisfiedStep;
    int m_sessionVersion;
    bool m_subscribed;
    bool m_contentIntroStarted;
    int m_stepVersion;
    CancellationToken m_stepToken;
    bool m_completing;
    bool m_returnPreparing;
    Exception m_returnFailure;
    readonly object m_entryWait = new object();
    readonly object m_completionWait = new object();
    float m_anchorMissingSince = -1f;
    int m_anchorRestoreStepId;
    bool m_restoringSurface;
    bool m_freeBattleHintSuspended;
    int m_contentIntroVersion;

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
    Tween m_enhanceResultClose;
    bool m_waitingEnhanceRequest;
    float m_synergyDeckOpenDeadline;

    // 개봉 오버레이가 떠 있는 동안은 로비 안내를 억제한다 — 예전에 개봉 "씬"이 이 플래그로 하던 일과 같다.
    bool SuppressGuideUI => suppressGuideUI || PackOpenOverlay.IsOpen;

    // ── 커서 창구 4개. 이 넷 밖에서는 강제/자율을 구분하지 않는다 — 나머지 코드는 스텝의 Completion만 본다.
    // 자율 세션이 도는 동안은 강제 시퀀스가 끝난 뒤라(졸업이 문이다) 두 커서가 동시에 서지 않는다.
    static bool GuidedCursor => OutgameTutorialRunner.IsGuidedRunning;

    static bool CursorRunning => OutgameTutorialRunner.IsGuidedRunning || OutgameTutorialRunner.IsRunning;

    static bool TryGetCursorStep(out TutorialStepDef _step) => OutgameTutorialGuide.TryGetCurrentStep(out _step);

    static UniTask<EOutgameTutorialStepResult> EnterCursorStepAsync(CancellationToken _ct)
        => GuidedCursor ? OutgameTutorialRunner.EnterGuidedStepAsync(_ct) : OutgameTutorialRunner.EnterCurrentStepAsync(_ct);

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
        s_instance = this;
        OutgameTutorialRunner.EnsureData(data);
        Subscribe();
    }

    void Start()
    {
        // 씬 재진입 재개. 자율 발화 자체는 OnGuidedActivated가 잡으므로 여기서는 이미 도는 커서만 이어받는다.
        // 초기화 로딩 완료는 LoadingScene이 보장하고 넘겨준다 — 여기서 대기할 것이 없다.
        if (CursorRunning && !LoadingCoverView.OwnsLobbyPreparation) ApplyCurrentStep();
    }

    /// <summary>복귀 커버 아래 첫 스텝의 서버 확정과 화면 준비를 끝낸다. 사용자 입력·연출 종료는 기다리지 않는다.</summary>
    public static async UniTask PrepareLobbyReturnAsync(CancellationToken _ct)
    {
        await OnboardingCommands.RecoverPendingAsync(_ct);
        _ct.ThrowIfCancellationRequested();
        // 결과 없이 돌아온 무효 경기는 안내를 전투 진입점으로 복원한다.
        // 정상 결과는 TurnRunner의 결과 통지에서 pending을 이미 해제했다.
        OutgameTutorialRunner.RestorePendingBattleEntry();
        if (!CursorRunning) return;
        if (OutgameTutorialRunner.IsRunning && !await GuideResume.SaveConfirmedAsync(_ct))
            throw new InvalidOperationException("전투 안내 진행을 저장하지 못했습니다. 다시 시도해 주세요.");
        var t_owner = s_instance;
        if (t_owner == null) throw new InvalidOperationException("로비 안내 화면을 찾지 못했습니다.");
        t_owner.m_returnPreparing = true;
        t_owner.m_returnFailure = null;
        t_owner.m_rankFailed = false;
        try
        {
            t_owner.ApplyCurrentStep();
            await UniTask.WaitUntil(() => t_owner == null ||
                (!t_owner.m_applying && !t_owner.m_completing && !t_owner.m_rankPreparing), cancellationToken: _ct);
            if (t_owner == null) throw new OperationCanceledException(_ct);
            if (t_owner.m_returnFailure != null) throw t_owner.m_returnFailure;
            if (CursorRunning && t_owner.m_step == null)
                throw new InvalidOperationException("로비 안내 준비가 중단되었습니다.");
        }
        finally { if (t_owner != null) t_owner.m_returnPreparing = false; }
    }

    void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
        if (s_rankEntryOwner == this) s_rankEntryOwner = null;
        ServerWaitOverlay.Release(this);
        ServerWaitOverlay.Release(m_entryWait);
        ServerWaitOverlay.Release(m_completionWait);
        // static 이벤트에 죽은 씬 오브젝트가 남으면 다음 씬에서 오발화한다.
        Unsubscribe();
        OnboardingSession.Suspend();
        CloseGate();

        // 자율 안내는 로비 안에서 시작해 로비 안에서 끝난다 — 씬을 떠나면 낙인 없이 끊고, 다음에 알림 점이 다시 부른다.
        OutgameTutorialRunner.AbortGuided();
    }

    // 현재 스텝을 진입시킨다. 재진입(스텝 Enter → 오버레이 열림 → OnOpened)은 버리지 않고 예약한다 —
    // 그 시점엔 이미 다음 스텝으로 커밋된 뒤라 버리면 개봉 대기 스텝이 영영 적용되지 않는다.
    void ApplyCurrentStep()
    {
        if (LoadingCoverView.OwnsLobbyPreparation && !m_returnPreparing) return;
        if (GuidanceCoordinator.IsRestoring || !CursorRunning) return;
        if (m_completing) return;
        if (m_applying) { m_pendingApply = true; return; }
        // 같은 대기 스텝의 화면 갱신은 진입 명령이나 해금 연출을 다시 시작하지 않는다.
        if (m_step != null && TryGetCursorStep(out var t_current) && ReferenceEquals(t_current, m_step)
            && OnboardingSession.Phase == EOnboardingPhase.Waiting
            && OnboardingSession.IsCurrent(m_sessionVersion, m_step.StepId))
        {
            if (ReferenceEquals(m_satisfiedStep, m_step)) OnGateSatisfied();
            else PresentStep();
            return;
        }
        ApplyCurrentStepAsync().Forget();
    }

    async UniTask ApplyCurrentStepAsync()
    {
        m_applying = true;
        try
        {
            for (int t_i = 0; t_i < 64 && CursorRunning; t_i++)
            {
                m_pendingApply = false;
                CloseGate();
                OutgameFeatureLock.Refresh();
                TryGetCursorStep(out var t_entering);
                m_stepToken = OnboardingSession.Begin(t_entering, this.GetCancellationTokenOnDestroy());
                int t_version = OnboardingSession.Version;
                m_sessionVersion = t_version;
                bool t_wait = t_entering != null && TutorialActionMeta.Of(t_entering.Action).RequiresEntryConfirmation
                    || OnboardingCommands.HasPending || DataSaveManager.Data.Tutorial?.Execution?.Phase == "Confirming";
                if (t_wait && !m_returnPreparing) ServerWaitOverlay.Hold(m_entryWait);
                EOutgameTutorialStepResult t_result;
                try
                {
                    if (DataSaveManager.Data.Tutorial?.Execution?.Phase == "Confirming"
                        && !await GuideResume.SaveConfirmedAsync(m_stepToken))
                        throw new InvalidOperationException("진행 위치를 저장하지 못했습니다.");
                    if (t_entering != null && ReferenceEquals(m_satisfiedStep, t_entering))
                    {
                        // 카드 지급처럼 진입 자체가 연출을 여는 스텝도 다시 실행하지 않는다.
                        await OnboardingCommands.RecoverPendingAsync(m_stepToken);
                        m_stepToken.ThrowIfCancellationRequested();
                        OnboardingSession.SetPhase(EOnboardingPhase.Waiting);
                        t_result = EOutgameTutorialStepResult.Gated;
                    }
                    else
                        t_result = t_entering == null ? await EnterCursorStepAsync(m_stepToken)
                            : await OnboardingSession.ExecuteAsync(t_entering, EnterCursorStepAsync, m_stepToken);
                }
                finally { ServerWaitOverlay.Release(m_entryWait); }
                if (this == null || !OnboardingSession.IsCurrent(t_version, t_entering?.StepId ?? 0)) return;
                if (t_result == EOutgameTutorialStepResult.Failed)
                    throw new InvalidOperationException("안내 화면을 준비하지 못했습니다.");
                if (t_result == EOutgameTutorialStepResult.Advanced)
                {
                    if (t_entering != null && t_entering.LeavesScene) return;
                    continue;
                }
                if (!TryGetCursorStep(out m_step)) return;
                if (m_step.Completion == EOutgameTutorialCompletion.SynergyDeckEditor)
                    m_synergyDeckOpenDeadline = Time.unscaledTime + 5f;
                // 완료 동작 후 저장만 실패했다면, 재시도에서는 완료 확정만 이어간다.
                if (ReferenceEquals(m_satisfiedStep, m_step)) OnGateSatisfied();
                else PresentStep();
                return;
            }
            if (CursorRunning) throw new InvalidOperationException("온보딩 자동 진행이 반복됩니다.");
        }
        catch (OperationCanceledException) { }
        catch (Exception t_error) { ShowStepFailure(t_error); }
        finally
        {
            m_applying = false;
            if (m_pendingApply && this != null && CursorRunning && !GuidanceCoordinator.IsRestoring)
            {
                m_pendingApply = false;
                ApplyCurrentStep();
            }
        }
    }

    void ShowStepFailure(Exception _error)
    {
        if (this == null) return;
        ServerWaitOverlay.Release(this);
        OnboardingSession.SetPhase(EOnboardingPhase.Failed);
        Debug.LogWarning($"[Onboarding] step={OnboardingSession.CurrentStepId} phase=Failed reason={_error.GetBaseException().Message}");
        CloseGate();
        if (m_returnPreparing)
        {
            m_returnFailure = _error;
            m_pendingApply = false;
            return;
        }
        if (GuidedCursor)
        {
            GuidanceCoordinator.DeferCurrentGuide("안내를 이어가지 못했습니다. 다시 시도해 주세요.");
            return;
        }
        UIPoolManager.Instance?.AddOrUpdateUI<SimpleYNPopup>(new SimpleYNPopupData
        {
            titleText = "진행 결과를 확인하지 못했습니다. 다시 시도해 주세요.",
            yesText = "재시도", yesAction = () => RetryStepAsync().Forget(),
            noText = "종료", noAction = QuitOnboarding,
        });
    }

    async UniTask RetryStepAsync()
    {
        await UniTask.Yield(this.GetCancellationTokenOnDestroy());
        m_anchorRestoreStepId = 0;
        ApplyCurrentStep();
    }

    static void QuitOnboarding()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void PresentStep()
    {
        if (m_step == null) return;
        if (m_enhancing || m_awaitingUnlockFx || GuidanceCoordinator.IsRestoring) return;
        if (SuspendFreeBattleHint()) return;
        if (!PackOpenOverlay.IsOpen && (m_step.Completion == EOutgameTutorialCompletion.PackOpen
            || m_step.Anchor == EOutgameTutorialAnchor.PackAcquireButton))
        {
            if (OnboardingCommands.TryRestorePackPresentation(out var t_pack, out string t_packId))
            {
                PackHandoff.Set(t_pack, t_packId, null, false);
                if (!PackOpenOverlay.TryOpen())
                    ShowStepFailure(new InvalidOperationException("구매한 카드팩 화면을 복원하지 못했습니다."));
                return;
            }
            // 구버전 세이브에는 개봉 결과가 없다. 이미 지난 구매를 재실행하지 않고 연출만 생략한다.
            Debug.LogWarning($"[Onboarding] step={m_step.StepId}: saved pack presentation is unavailable; continuing without a new purchase.");
            OnGateSatisfied();
            return;
        }
        if (m_step.Completion == EOutgameTutorialCompletion.DeckSave
            && DeckEditController.OpenEditor != null && DeckEditController.OpenEditor.IsSavedComplete)
        {
            OnGateSatisfied();
            return;
        }
        if (m_step.Completion == EOutgameTutorialCompletion.Click
            && GuidanceCoordinator.IsCurrentTabAnchor(m_step.Anchor))
        {
            OnGateSatisfied();
            return;
        }

        if (m_step.Completion == EOutgameTutorialCompletion.SynergyDeckEditor)
        {
            if (SynergyBattleGuide.IsEditorOpen) OnGateSatisfied();
            return;
        }

        if (m_step.Completion == EOutgameTutorialCompletion.SynergyDeck)
        {
            if (SynergyBattleGuide.IsDeckReady) { OnGateSatisfied(); return; }
            OutgameTutorialGateUI.Ensure(this.gatePrefab).ShowBanner(this, OutgameTutorialGuide.MessageOf(m_step));
            return;
        }

        if (m_step.Completion == EOutgameTutorialCompletion.UnlockIntro)
        {
            if (!CardDetailOverlayView.IsUnlockFxPlaying) { OnGateSatisfied(); return; }
            if (!SuppressGuideUI)
                OutgameTutorialGateUI.Ensure(this.gatePrefab).ShowBanner(this,
                    OutgameTutorialGuide.MessageOf(m_step), m_step.MessageAtBottom);
            return;
        }

        if (m_step.Completion == EOutgameTutorialCompletion.ContentUnlockIntro)
        {
            TryPresentContentIntro();
            return;
        }

        // 개봉 대기는 클릭이 아니라 개봉 신호로 완료된다 — 걸 앵커도 없다(개봉 화면의 팩엔 TutorialAnchor가 없다).
        // 그래서 게이트를 건너뛰고 배너만 띄운다. 아래 앵커 조회에 도달하지 않는 유일한 스텝이다.
        // 억제 씬에서는 배너도 생략 — 완료는 개봉 신호(Subscribe에서 이미 구독)가 그대로 확정한다.
        if (m_step.Completion == EOutgameTutorialCompletion.PackOpen)
        {
            if (!SuppressGuideUI) OutgameTutorialGateUI.Ensure(this.gatePrefab).ShowBanner(this, OutgameTutorialGuide.MessageOf(m_step));
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
        if ((m_step.FreeOfCharge && IsFreeShotSpent(m_step.Completion)
                && (!GuidedCursor || m_step.Completion != EOutgameTutorialCompletion.Enhance
                    || OutgameTutorialGuide.IsGrowthGoalReached))
            || (GuidedCursor && m_step.Completion == EOutgameTutorialCompletion.Enhance
                && OutgameTutorialGuide.IsGrowthGoalReached))
        {
            OnGateSatisfied();
            return;
        }

        if (m_step.Completion == EOutgameTutorialCompletion.Enhance
            && OutgameTutorialGuide.IsEnhanceIntroduction
            && !OutgameTutorialGuide.CanContinueEnhance())
        {
            OnUnlockIntroCancelled();
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
                .ShowMessageGate(this, null, OutgameTutorialGuide.MessageOf(m_step), OnGateSatisfied, m_step.MessageAtBottom, m_step.UseDim);
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
        if (m_step == null || m_step.Anchor == EOutgameTutorialAnchor.None || GuidanceCoordinator.IsRestoring) return;
        if (SuspendFreeBattleHint()) return;
        if (!TutorialAnchorRegistry.TryGet(m_step.Anchor, out var t_rect, out var t_button))
        {
            if (GuidedCursor && GuideResume.Record?.GoalReached == true
                && OutgameTutorialGuide.TargetCardId == 0 && m_step.Completion == EOutgameTutorialCompletion.Confirm
                && (m_step.Anchor == EOutgameTutorialAnchor.CardDetailKeywordDescription
                    || m_step.Anchor == EOutgameTutorialAnchor.CardDetailCardView))
                OutgameTutorialGateUI.Ensure(gatePrefab).ShowMessageGate(this, null,
                    OutgameTutorialGuide.MessageOf(m_step), OnGateSatisfied, m_step.MessageAtBottom, m_step.UseDim);
            return;
        }

        // 설명 스텝은 앵커를 "강조할 영역"으로만 쓴다 — 누를 대상이 아니라 Button이 없어도 되고 완료는 딤 탭이다.
        // 억제 씬에서도 예외적으로 띄운다(딤이 없으면 완료 신호가 없어 진행이 멈춘다).
        if (m_step.Completion == EOutgameTutorialCompletion.Confirm)
        {
            OutgameTutorialGateUI.Ensure(this.gatePrefab)
                .ShowMessageGate(this, t_rect, OutgameTutorialGuide.MessageOf(m_step), OnGateSatisfied, m_step.MessageAtBottom, m_step.UseDim, SpotlightRect());
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
            .ShowGate(this, t_rect, t_button, OutgameTutorialGuide.MessageOf(m_step), t_onSatisfied, m_step.UseDim, SpotlightRect());
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
        if (m_step.Completion == EOutgameTutorialCompletion.ContentUnlockIntro) return;

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
        if (GuidanceCoordinator.IsRestoring) return;
        if (m_step != null && m_step.Completion == EOutgameTutorialCompletion.Enhance
            && !CardDetailOverlayView.IsOpen)
        {
            OnUnlockIntroCancelled();
            return;
        }
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
        if (!m_returnPreparing) ServerWaitOverlay.Hold(this);
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
            if (m_returnPreparing) { m_returnFailure = t_failure; return; }
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

        if (_result.Outcome == EEnhanceOutcome.Success && !m_step.WaitUnlockIntro
            && !OutgameTutorialGuide.NeedsMoreSynergyGrowth)
        {
            // 무대는 아직 강화가 쥐고 있지만 안내는 여기서 손을 뗀다 — 남겨 두면 다음 스텝의 앵커 등록을 무시한다.
            m_enhancing = false;
            OnGateSatisfied();
            return;
        }

        m_enhanceResultClose?.Kill();
        var t_step = m_step;
        m_enhanceResultClose = DOVirtual.DelayedCall(Mathf.Max(0f, this.enhanceResultHold), () =>
                 {
                     if (m_step == t_step && m_enhancing) CardDetailOverlayView.CloseEnhanceResult();
                 })
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
            // 샤드 일부 투입도 성공이다. 2성 전의 키워드 해금은 보고 같은 강화 안내로 돌아온다.
            if (OutgameTutorialGuide.NeedsMoreSynergyGrowth)
            {
                if (CardDetailOverlayView.IsUnlockFxPlaying) { m_awaitingUnlockFx = true; return; }
                m_enhancing = false;
                PresentStep();
                return;
            }
            if (NextStepWaitsForUnlockIntro())
            {
                m_enhancing = false;
                OnGateSatisfied();
                return;
            }
            // 이 통지는 해금 연출을 트는 PlayPendingUnlockFx() "다음"에 온다 —
            // 그래서 지금의 IsUnlockFxPlaying이 "연출이 설지 말지"의 확정 답이다.
            if (CardDetailOverlayView.IsUnlockFxPlaying) { m_awaitingUnlockFx = true; return; }

            m_enhancing = false;
            OnGateSatisfied();
            return;
        }

        m_enhancing = false;

        if (_result.Outcome == EEnhanceOutcome.Success && !OutgameTutorialGuide.NeedsMoreSynergyGrowth)
        { OnGateSatisfied(); return; }

        PresentStep();
    }

    // 해금 설명의 최종 확인 뒤 미뤄 둔 완료를 넘긴다.
    void OnUnlockFxFinished()
    {
        if (m_step?.Completion == EOutgameTutorialCompletion.UnlockIntro)
        {
            OnGateSatisfied();
            return;
        }
        if (!m_awaitingUnlockFx) return;

        m_awaitingUnlockFx = false;
        m_enhancing        = false;
        if (OutgameTutorialGuide.NeedsMoreSynergyGrowth) { PresentStep(); return; }
        OnGateSatisfied();
    }

    void OnUnlockIntroCancelled()
    {
        if (m_step == null || (m_step.Completion != EOutgameTutorialCompletion.Enhance
            && m_step.Completion != EOutgameTutorialCompletion.UnlockIntro)) return;
        CloseGate();
        if (OutgameTutorialRunner.IsGuidedRunning)
            GuidanceCoordinator.DeferCurrentGuide("강화 안내를 계속할 수 없어 진행을 보관했습니다.");
    }

    bool NextStepWaitsForUnlockIntro()
    {
        var t_data = OutgameTutorialRunner.Data;
        if (t_data == null) return false;
        foreach (var t_chapter in t_data.Chapters)
            for (int t_i = 0; t_i + 1 < t_chapter.StepCount; t_i++)
                if (t_chapter.TryGetStep(t_i, out var t_step) && t_step == m_step
                    && t_chapter.TryGetStep(t_i + 1, out var t_next))
                    return t_next.Action == EOutgameTutorialAction.WaitUnlockIntro;
        return false;
    }

    // 키워드 강화 성공. 카드 강화와 달리 무대를 쥐는 결과판이 없어 기다릴 것 없이 바로 넘긴다.
    void OnKeywordEnhanced(CardKeyword _keyword)
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.KeywordEnhance) return;

        OnGateSatisfied();
    }

    // 자율 안내 발화 통지. 탭 전환 도중에 켜지므로 이 씬이 그대로 이어받는다.
    void OnGuidedActivated()
    {
        m_anchorRestoreStepId = 0;
        ApplyCurrentStep();
    }

    // 구매 성공 신호. 서버 응답이 성립한 뒤에 오고 곧바로 개봉 오버레이가 열리므로
    // 커밋만 하고, 다음 스텝은 OnPackOverlayOpened가 재개한다.
    void OnPurchased()
    {
        if (m_step == null || m_step.Completion != EOutgameTutorialCompletion.Purchase) return;

        OnGateSatisfied();
    }

    // 개봉 오버레이 열림/닫힘. 씬이 바뀌지 않으므로 재개해 줄 새 브리지가 없다 — 이 브리지가 직접 이어간다.
    // 세션 도중 서버 소진 표식이 켜졌다 = 이 스텝이 시킨 강화가 이미 성립했다.
    // 다시 적용하면 PresentStep의 프리체크가 통과 판정을 한다(응답을 잃어 완료 신호만 못 받은 자리).
    void OnServerFreeShotSpentChanged()
    {
        if (m_enhancing || m_awaitingUnlockFx || CardDetailOverlayView.IsRitualPlaying) return;
        ApplyCurrentStep();
    }

    void OnPackOverlayOpened() { if (!m_completing) ApplyCurrentStep(); }

    void OnPackOverlayClosed() => ApplyCurrentStep();

    // 완료 → 커밋 후 다음 스텝을 같은 씬에서 이어간다(씬을 떠나는 스텝이면 다음 씬 브리지가 재개).
    void OnGateSatisfied()
    {
        if (LoadingCoverView.OwnsLobbyPreparation && !m_returnPreparing) return;
        if (m_step == null || m_completing || GuidanceCoordinator.IsRestoring
            || !OnboardingSession.CanAcceptCompletion
            || !TryGetCursorStep(out var t_current) || t_current != m_step) return;
        // 탭 버튼의 클릭은 이동 요청이다. 이탈 확인·슬라이드까지 끝나야 다음 안내로 넘어간다.
        if (m_step.Completion == EOutgameTutorialCompletion.Click
            && GuidanceCoordinator.IsLobbyTabAnchor(m_step.Anchor)
            && !GuidanceCoordinator.IsCurrentTabAnchor(m_step.Anchor)) return;
        m_satisfiedStep = m_step;
        CompleteStepAsync(m_step).Forget();
    }

    async UniTask CompleteStepAsync(TutorialStepDef _step)
    {
        m_completing = true;
        bool t_continue = false;
        bool t_wait = OnboardingSession.RequiresCompletionConfirmation(_step);
        if (t_wait && !m_returnPreparing) ServerWaitOverlay.Hold(m_completionWait);
        try
        {
            bool t_done = await OnboardingSession.CompleteAsync(_step, SatisfyCursorStep, m_stepToken);
            if (!t_done || this == null) return;
            if (ReferenceEquals(m_satisfiedStep, _step)) m_satisfiedStep = null;
            ServerWaitOverlay.Release(m_completionWait);
            OutgameFeatureLock.Refresh();
            if (!CursorRunning || _step.LeavesScene) { CloseGate(); return; }
            t_continue = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception t_error)
        {
            ServerWaitOverlay.Release(m_completionWait);
            ShowStepFailure(t_error);
        }
        finally
        {
            ServerWaitOverlay.Release(m_completionWait);
            m_completing = false;
        }
        if (t_continue) ApplyCurrentStep();
    }

    void HideGuide()
    {
        DetachSilent();
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear(this);
    }

    void CloseGate()
    {
        m_stepVersion++;
        m_anchorMissingSince = -1f;
        m_freeBattleHintSuspended = false;
        m_enhanceResultClose?.Kill();
        m_enhanceResultClose = null;
        m_enhancing = false;
        m_awaitingUnlockFx = false;
        m_waitingEnhanceRequest = false;
        m_step = null;
        m_contentIntroVersion++;
        if (m_contentIntroStarted)
        {
            m_contentIntroStarted = false;
            ContentUnlockPresentation.CancelCurrent();
        }

        // 안내가 삽입 세션을 몰던 상태를 여기서 되돌린다 — 스위치가 남으면 이후 일반 개봉의 탭 이탈까지 막는다.
        AlbumInsertSession.TutorialMode = false;

        DetachSilent();   // 리스너가 남으면 다음 스텝·다음 씬에서 오발화한다

        // 표시는 시너지 소개와 공용이다 — 남의 안내를 걷으면 그쪽은 완료 신호를 받을 주체를 잃고 영영 멈춘다.
        // 판정은 게이트가 소유권으로 한다(불변식 3): 무대가 남의 것이면 이 호출은 조용히 지나간다.
        if (OutgameTutorialGateUI.Instance != null) OutgameTutorialGateUI.Instance.Clear(this);
    }

    // 해금 이후의 전투 안내는 자유 이동을 허용한다. 다른 화면에서 플레이 버튼이
    // 사라진 것은 정상 이탈이므로, 차단판/5초 복구를 걸지 않고 안내 표시만 보류한다.
    bool SuspendFreeBattleHint()
    {
        if (m_step == null || GuidedCursor || !OutgameFeatureLock.IsFtueFreeNavigation
            || m_step.Action != EOutgameTutorialAction.BattleEntry
            || m_step.Anchor != EOutgameTutorialAnchor.LobbyPlayButton) return false;
        if (GuidanceCoordinator.IsCurrentTabAnchor(EOutgameTutorialAnchor.LobbyMatchTab)
            && DeckEditController.OpenEditor == null && !CardDetailOverlayView.IsOpen
            && !PackOpenOverlay.IsOpen) return false;

        m_anchorMissingSince = -1f;
        m_anchorRestoreStepId = 0;
        HideGuide();
        m_freeBattleHintSuspended = true;
        return true;
    }

    void Update()
    {
        if (LoadingCoverView.OwnsLobbyPreparation && !m_returnPreparing) return;
        if (m_step == null || m_completing || m_restoringSurface || GuidanceCoordinator.IsRestoring) return;
        if (!TryGetCursorStep(out var t_current) || !ReferenceEquals(t_current, m_step))
        {
            CloseGate();
            return;
        }
        if (SuspendFreeBattleHint()) return;
        if (m_freeBattleHintSuspended)
        {
            m_freeBattleHintSuspended = false;
            PresentStep();
            return;
        }
        if (m_step.Completion == EOutgameTutorialCompletion.Click
            && GuidanceCoordinator.IsCurrentTabAnchor(m_step.Anchor))
        {
            OnGateSatisfied();
            return;
        }
        if (m_step.Completion == EOutgameTutorialCompletion.SynergyDeckEditor)
        {
            if (SynergyBattleGuide.IsEditorOpen) OnGateSatisfied();
            else if (Time.unscaledTime >= m_synergyDeckOpenDeadline)
            {
                GuidanceCoordinator.DeferCurrentGuide("덱 편집 화면을 준비하지 못했습니다.");
                CloseGate();
            }
            return;
        }
        if (m_step.Completion == EOutgameTutorialCompletion.SynergyDeck)
        {
            if (!SynergyBattleGuide.IsEditorOpen)
            {
                GuidanceCoordinator.DeferCurrentGuide("덱 편집 화면이 닫혀 안내를 보관했습니다.");
                CloseGate();
            }
            else if (SynergyBattleGuide.IsDeckReady) OnGateSatisfied();
            return;
        }
        if (m_step.Completion == EOutgameTutorialCompletion.Enhance && !m_enhancing && !m_awaitingUnlockFx)
        {
            if (CardDetailOverlayView.IsRitualPlaying)
            {
                if (!m_waitingEnhanceRequest) HideGuide();
                m_waitingEnhanceRequest = true;
                return;
            }
            if (m_waitingEnhanceRequest)
            {
                m_waitingEnhanceRequest = false;
                PresentStep();
                return;
            }
        }
        if (!m_enhancing && !m_awaitingUnlockFx
            && !GuidanceCoordinator.IsLobbyPresentationBlockingNavigation
            && !CardDetailOverlayView.IsRitualPlaying && !CardDetailOverlayView.IsUnlockFxPlaying
            && !UnlockIntroOverlay.IsOpen && !SuppressGuideUI && m_step.Anchor != EOutgameTutorialAnchor.None
            && !(GuideResume.Record?.GoalReached == true && OutgameTutorialGuide.TargetCardId == 0
                && m_step.Completion == EOutgameTutorialCompletion.Confirm
                && (m_step.Anchor == EOutgameTutorialAnchor.CardDetailKeywordDescription
                    || m_step.Anchor == EOutgameTutorialAnchor.CardDetailCardView)))
        {
            bool t_available = TutorialAnchorRegistry.TryGet(m_step.Anchor, out var t_rect, out var t_button)
                && t_rect != null && t_rect.gameObject.activeInHierarchy
                && (m_step.Completion != EOutgameTutorialCompletion.Click
                    || t_button == null || t_button.IsInteractable());
            if (t_available)
            {
                m_anchorMissingSince = -1f;
                m_anchorRestoreStepId = 0;
                if (OutgameTutorialGateUI.Instance != null && OutgameTutorialGateUI.Instance.IsTransitionOnly) TryOpenGate();
            }
            else if (m_anchorMissingSince < 0f)
            {
                m_anchorMissingSince = Time.unscaledTime;
                OutgameTutorialGateUI.Ensure(gatePrefab).ShowTransitionGate(this);
            }
            else if (Time.unscaledTime - m_anchorMissingSince >= 5f)
            {
                RestoreMissingSurfaceAsync(m_step).Forget();
                return;
            }
        }
        if (m_step.Completion == EOutgameTutorialCompletion.ContentUnlockIntro) TryPresentContentIntro();
        if (m_step.Completion == EOutgameTutorialCompletion.Enhance && !m_enhancing && !m_awaitingUnlockFx
            && OutgameTutorialGuide.IsEnhanceIntroduction
            && !OutgameTutorialGuide.CanContinueEnhance()) OnUnlockIntroCancelled();
    }

    async UniTask RestoreMissingSurfaceAsync(TutorialStepDef _step)
    {
        m_restoringSurface = true;
        int t_version = OnboardingSession.Version;
        try
        {
            if (m_anchorRestoreStepId == _step.StepId)
                throw new InvalidOperationException("안내 대상을 찾지 못했습니다. 다시 시도해 주세요.");
            m_anchorRestoreStepId = _step.StepId;
            if (GuidedCursor) await GuidanceCoordinator.TryRestoreCurrentSurfaceAsync(m_stepToken);
            else if (!await GuidanceCoordinator.TryRestoreForcedSurfaceAsync(_step, m_stepToken))
                throw new InvalidOperationException("안내 화면을 복구하지 못했습니다. 다시 시도해 주세요.");
            m_stepToken.ThrowIfCancellationRequested();
            if (!OnboardingSession.IsCurrent(t_version, _step.StepId)) return;
            m_anchorMissingSince = Time.unscaledTime;
            PresentStep();
        }
        catch (OperationCanceledException) { }
        catch (Exception t_error) { ShowStepFailure(t_error); }
        finally { m_restoringSurface = false; }
    }

    void TryPresentContentIntro()
    {
        if (m_contentIntroStarted || SuppressGuideUI || m_step == null) return;
        TutorialStepDef t_step = m_step;
        int t_version = m_contentIntroVersion;
        bool t_guided = GuidedCursor;
        var t_trigger = OutgameTutorialRunner.GuidedTrigger;
        m_contentIntroStarted = ContentUnlockPresentation.TryPresent(t_step, () =>
        {
            if (!IsCurrentContentIntro(t_step, t_version)) return;
            m_contentIntroStarted = false;
            foreach (EContentUnlockIntro t_content in t_step.ContentIntros)
            {
                string t_key = ContentUnlockIntroDef.KeyOf(t_content);
                if (t_key != null) ContentUnlockManager.MarkPresented(t_key);
            }
            OnGateSatisfied();
        }, () =>
        {
            if (!IsCurrentContentIntro(t_step, t_version)) return;
            m_contentIntroStarted = false;
            if (t_guided)
            {
                CloseGate();
                GuidanceCoordinator.DeferCurrentGuide("해금 소개가 중단되어 진행을 보관했습니다.");
            }
        });
    }

    bool IsCurrentContentIntro(TutorialStepDef _step, int _version)
        => this != null && isActiveAndEnabled && m_contentIntroVersion == _version
            && ReferenceEquals(m_step, _step) && TryGetCursorStep(out var t_current)
            && ReferenceEquals(t_current, _step);

    void Subscribe()
    {
        if (m_subscribed) return;

        TutorialAnchorRegistry.OnRegistered   += OnAnchorRegistered;
        OutgameTutorialRunner.OnBattleEntryRestored += ApplyCurrentStep;
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
        CardDetailOverlayView.OnUnlockIntroCancelled  += OnUnlockIntroCancelled;
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
        OutgameTutorialRunner.OnBattleEntryRestored -= ApplyCurrentStep;
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
        CardDetailOverlayView.OnUnlockIntroCancelled  -= OnUnlockIntroCancelled;
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
