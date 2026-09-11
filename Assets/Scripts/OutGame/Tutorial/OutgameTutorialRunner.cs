using System;
using System.Collections.Generic;
using UnityEngine;

// 아웃게임 첫시작 튜토리얼의 시퀀스 해석 static 코어(씬 오브젝트·UI를 모른다)
public static class OutgameTutorialRunner
{
    static OutgameTutorialData s_data;

    // 시퀀스 앞쪽에 연속으로 선 강제 챕터 수. 세이브 좌표·기능 잠금·졸업 판정은 전부 이 경계 안에서만 돈다 —
    // 그 뒤의 자율 챕터를 강제 커서가 읽으면 졸업 센티널 좌표가 자율 스텝을 재생한다.
    static int s_forcedCount;

    // 자율 세션(메모리 커서). 세이브에는 완주 시점에 트리거 키 하나만 남는다 — 앱을 껐다 켜면 처음부터.
    static int s_guidedChapter = -1;
    static int s_guidedStep;

    // 이번 세션에 화면을 떠나 미뤄 둔 자율 안내. 저장하지 않는다 — 다음 세션에 알림 점이 다시 부른다.
    static readonly HashSet<EOutgameTutorialTrigger> s_deferred = new HashSet<EOutgameTutorialTrigger>();

    // 첫 랭크 승급 연출이 끝나 열린 문. 온보딩은 그 뒤로도 이어지지만(카드 강화 편) 그때부터는
    // 자율 안내가 나란히 서도 되는 구간이라 졸업 낙인을 기다리지 않는다. 세이브하지 않는다.
    static bool s_openedAtRankPromotion;

    // 진행도가 다음 스텝으로 넘어갈 때 발화
    public static event Action OnStepChanged;

    // 자율 안내가 실제로 시작됐을 때(세션 중간에 시작되므로 브리지가 pull만으로는 잡을 수 없다)
    public static event Action OnGuidedActivated;

    // 자율 안내의 남은 목록이 달라졌을 때(주입·발화·완주·미루기·중단·졸업) — 알림 점이 이걸 보고 다시 그린다
    public static event Action OnGuidedChanged;

    // 데이터가 주입됐고 강제 시퀀스가 아직 완료 전인가
    public static bool IsRunning => s_data != null && !OutgameTutorialProgress.IsCompleted;

    public static bool IsGuidedRunning => s_guidedChapter >= 0;

    // 실행 중인 자율 챕터의 트리거(없으면 None)
    public static EOutgameTutorialTrigger GuidedTrigger
        => IsGuidedRunning && TryGetChapterRaw(s_guidedChapter, out var t_chapter) ? t_chapter.Trigger : EOutgameTutorialTrigger.None;

    // 졸업 전에는 자율 안내가 통째로 잠긴다 — 게이트는 하나뿐이라 두 안내가 겹치면 서로를 가로채고,
    // 첫시작 동선 밖의 탭으로 부르는 점은 아직 못 가는 곳을 가리킨다.
    static bool IsGuidedOpen => OutgameTutorialProgress.IsCompleted || s_openedAtRankPromotion;

    // 저작된 챕터("N편") 총수 — 강제·자율을 다 센다(미주입·빈 시퀀스는 0). 강제 커서의 범위는 ForcedChapterCount다
    public static int ChapterCount => s_data != null && s_data.chapters != null ? s_data.chapters.Count : 0;

    // 강제 시퀀스의 챕터 수 = 졸업 센티널 좌표의 챕터 인덱스
    public static int ForcedChapterCount => s_forcedCount;

    static int TotalStepCount
    {
        get
        {
            int t_total = 0;
            for (int i = 0; i < ForcedChapterCount; i++) t_total += StepCountOf(i);
            return t_total;
        }
    }

    /// <summary>온보딩 졸업 처리의 유일한 창구(멱등).
    ///
    /// 첫 랭크 진입은 서버가 저장된 튜토리얼 진행으로 확정한다.
    /// 졸업은 로컬 점수를 올리거나 승급 연출을 예약하지 않는다.</summary>
    public static void CompleteSequence()
    {
        if (OutgameTutorialProgress.IsCompleted) return;

        OutgameTutorialProgress.Complete();

        // 졸업으로 전 기능이 열린다. 게이트를 거치지 않고 닫히는 경로(전투에서 돌아와 확정하는 졸업·디버그 스킵)에도
        // 잠김 룩이 따라오게 여기서 알린다 — FeatureLockView는 OnChanged로만 다시 그린다.
        OutgameFeatureLock.Refresh();

        // 자율 안내도 졸업과 함께 풀린다 — 그 전까지 전부 false였던 HasPending의 답이 한꺼번에 뒤집힌다.
        OnGuidedChanged?.Invoke();
    }

    /// <summary>첫 랭크 승급 연출까지 끝났다 — 여기서부터 자율 안내가 열린다(졸업은 아직 남았다).</summary>
    public static void NotifyRankPromotionFinished()
    {
        if (s_openedAtRankPromotion) return;

        s_openedAtRankPromotion = true;
        OnGuidedChanged?.Invoke();
    }

    // ───────────── 자율 안내(메모리 커서) ─────────────

    /// <summary>이 트리거로 아직 볼 것이 남았는가. 판정은 Fire의 무시 조건과 같아야 한다 —
    /// UI가 규칙을 복제하지 않도록 "띄울지"의 답을 여기서만 낸다(데이터 미주입이면 false).</summary>
    public static bool HasPending(EOutgameTutorialTrigger _trigger)
    {
        if (_trigger == EOutgameTutorialTrigger.None) return false;
        if (!IsGuidedOpen) return false;
        if (OutgameTutorialProgress.IsTriggerDone(_trigger)) return false;
        if (s_deferred.Contains(_trigger)) return false;
        if (!TryGetGuidedChapter(_trigger, out _, out var t_chapter) || t_chapter.StepCount == 0) return false;

        return OutgameFeatureLock.IsUnlocked(t_chapter.Prerequisite);
    }

    /// <summary>자율 안내 발화. 무대가 비었는지는 묻지 않는다 — 그 판정은 UI를 아는 GuidanceCoordinator.TryFire 몫이다.
    /// 아래 무시 조건은 전부 정상 경로라 경고하지 않는다.</summary>
    public static void Fire(EOutgameTutorialTrigger _trigger)
    {
        if (s_data == null) return;
        if (IsGuidedRunning) return;
        if (AdventureTutorialRunner.IsRunning) return;
        if (!HasPending(_trigger)) return;

        TryGetGuidedChapter(_trigger, out s_guidedChapter, out _);
        s_guidedStep = 0;

        OnGuidedActivated?.Invoke();
        OnGuidedChanged?.Invoke();
    }

    // 자율 커서가 가리키는 스텝(미실행·범위 밖·빈 칸이면 false)
    public static bool TryGetGuidedStep(out TutorialStepDef _step)
    {
        _step = null;
        if (!IsGuidedRunning) return false;

        return TryGetChapterRaw(s_guidedChapter, out var t_chapter) && t_chapter.TryGetStep(s_guidedStep, out _step);
    }

    public static bool IsGuidedAction(EOutgameTutorialAction _action)
        => TryGetGuidedStep(out var t_step) && t_step.Action == _action;

    // 자율 스텝 진입 — 결말은 반환값이 말한다(EnterCurrentStep과 같은 규약)
    public static EOutgameTutorialStepResult EnterGuidedStep()
    {
        if (!TryGetGuidedStep(out var t_step))
        {
            Debug.LogWarning($"[OutgameTutorialRunner] Guided step {s_guidedChapter}-{s_guidedStep}({GuidedTrigger}) is empty — closing it as finished.");
            FinishGuided();
            return EOutgameTutorialStepResult.Advanced;
        }

        bool t_isLast = !TryGetChapterRaw(s_guidedChapter, out var t_chapter) || s_guidedStep + 1 >= t_chapter.StepCount;

        return TutorialStepExecutor.Enter(t_step,
            new OutgameTutorialStepContext(s_guidedChapter, s_guidedStep, s_guidedChapter, s_guidedStep + 1, t_isLast,
                                           GuidedProgressSink.Instance));
    }

    // 자율 스텝 완료를 감지한 브리지가 호출 — 마지막이었으면 완주 낙인까지 찍는다
    public static void NotifyGuidedStepSatisfied()
    {
        if (!IsGuidedRunning) return;

        s_guidedStep++;
        if (!TryGetChapterRaw(s_guidedChapter, out var t_chapter) || s_guidedStep >= t_chapter.StepCount) FinishGuided();
    }

    /// <summary>자율 안내를 낙인 없이 끊는다. 트리거를 주면 그 안내가 도는 중일 때만, 그리고 이번 세션은 미뤄 둔다(화면 이탈 = 미루기).
    /// 인자 없이 부르면 무조건 끊고 미루기도 전부 걷는다(세이브 재로드·디버그 리셋용).</summary>
    public static void AbortGuided(EOutgameTutorialTrigger _onlyIf = EOutgameTutorialTrigger.None)
    {
        if (_onlyIf != EOutgameTutorialTrigger.None)
        {
            if (!IsGuidedRunning || GuidedTrigger != _onlyIf) return;
            s_deferred.Add(_onlyIf);
        }
        else
        {
            s_deferred.Clear();

            // 되감기로 온보딩이 다시 진행 중이 되면 승급으로 연 문도 함께 닫혀야 한다 — 남으면 튜토 도중에 점이 뜬다.
            s_openedAtRankPromotion = false;
        }

        s_guidedChapter = -1;
        s_guidedStep    = 0;

        OnGuidedChanged?.Invoke();
    }

    /// <summary>자율 안내 완주(낙인). 트리거를 주면 그 안내가 도는 중일 때만 — 안내 밖 경로로 목적을 이룬 화면이 부른다.</summary>
    public static void FinishGuided(EOutgameTutorialTrigger _onlyIf = EOutgameTutorialTrigger.None)
    {
        if (!IsGuidedRunning) return;

        var t_trigger = GuidedTrigger;
        if (_onlyIf != EOutgameTutorialTrigger.None && t_trigger != _onlyIf) return;

        OutgameTutorialProgress.MarkTriggerDone(t_trigger);

        s_guidedChapter = -1;
        s_guidedStep    = 0;

        OnGuidedChanged?.Invoke();
    }

    // 자율 런의 진행 좌표를 메모리에만 두는 싱크(챕터는 세션 시작 때 정해져 _chapter는 무시)
    sealed class GuidedProgressSink : ITutorialProgressSink
    {
        public static readonly ITutorialProgressSink Instance = new GuidedProgressSink();

        GuidedProgressSink() { }

        public void Commit(int _chapter, int _step) => s_guidedStep = _step;

        public void Complete() => FinishGuided();
    }

    // 씬마다 브리지가 호출하는 멱등 주입(첫 주입만 유효)
    public static void EnsureData(OutgameTutorialData _data)
    {
        if (_data == null) return;
        if (s_data == _data) return;

        if (s_data != null)
        {
            Debug.LogWarning($"[OutgameTutorialRunner] Attempted to inject different tutorial data ('{_data.name}' != existing '{s_data.name}') — keeping the existing one.");
            return;
        }

        s_data = _data;
        s_forcedCount = CountForcedPrefix();
        AdventureTutorialRunner.EnsureData(_data);
        WarnOnMisauthoredChapters();
    }

    /// <summary>트리거가 깨우는 자율 챕터. 같은 트리거가 여럿이면 먼저 나온 챕터가 이긴다(검증기가 중복을 잡는다).</summary>
    public static bool TryGetGuidedChapter(EOutgameTutorialTrigger _trigger, out int _index, out OutgameTutorialChapter _chapter)
    {
        _index   = -1;
        _chapter = null;
        if (_trigger == EOutgameTutorialTrigger.None) return false;

        for (int t_c = ForcedChapterCount; t_c < ChapterCount; t_c++)
        {
            if (!TryGetChapterRaw(t_c, out var t_candidate) || !t_candidate.IsGuided || t_candidate.Trigger != _trigger) continue;

            _index   = t_c;
            _chapter = t_candidate;
            return true;
        }

        return false;
    }

    static int CountForcedPrefix()
    {
        int t_count = 0;
        while (TryGetChapterRaw(t_count, out var t_chapter) && !t_chapter.IsGuided) t_count++;
        return t_count;
    }

    /// <summary>초기화가 1회 부르는 재개 정정(EnsureData 이후). 대본 전투가 연 화면(덱 게이트) 안의 좌표에 서 있는데
    /// TutorialConfig가 꺼져 있으면 그 전제를 다시 세울 길이 없다 — 시나리오는 휘발성이라 초기화에 사라지고
    /// 복원 지점이 없다. 그 자리에 남으면 안내 앵커가 등록되지 않아 영구 정지고, 억지로 이어 붙여도 대본 아닌
    /// 일반 전투가 된다. 그래서 좌표를 전투 진입 스텝으로 되감아 저작된 경로를 처음부터 다시 태운다
    /// (Begin은 그 스텝의 실행자가 부른다). 스캔은 <b>같은 챕터 안</b>으로 한정한다 — 게이트 구간은 그 진입
    /// 스텝과 같은 챕터에 저작된다는 전제이고, 전투를 마친 좌표는 이미 다음 챕터라 루프가 돌지 않는다.</summary>
    public static void RewindToPendingBattleEntry()
    {
        // 세션 내 진행은 건드리지 않는다 — 전제가 살아 있으면 되감을 이유가 없다.
        if (!IsRunning || TutorialConfig.IsActive) return;

        int t_chapter = OutgameTutorialProgress.ChapterIndex;
        int t_step    = OutgameTutorialProgress.StepIndex;

        for (int t_i = t_step - 1; t_i >= 0; t_i--)
        {
            if (!TryGetStepAt(t_chapter, t_i, out var t_def)) continue;

            // 전투를 이미 치른 뒤의 좌표다 — 되감을 대본이 남아 있지 않다.
            if (t_def.Action == EOutgameTutorialAction.BattleStart ||
                t_def.Action == EOutgameTutorialAction.AutoBattle) return;

            if (t_def.Action != EOutgameTutorialAction.BattleEntry) continue;

            // 게이트를 거치지 않는 진입은 그 자리에서 씬을 떠난다(LeavesScene) — 되감아 재생할 화면이 없다.
            // 시나리오 유무는 묻지 않는다: 모험 구간의 진입은 대본 없이 덱 게이트만 켜므로,
            // 그 항이 남으면 그 구간에서 앱을 다시 켰을 때 되감을 대상을 못 찾아 영구 정지한다.
            if (!t_def.ShowDeckGate) return;

            Debug.LogWarning($"[OutgameTutorialRunner] The app closed before the scripted battle — rewinding position {t_chapter}-{t_step} to the battle entry step {t_chapter}-{t_i}.");

            // 초기화에서 UI 구독보다 먼저 도는 자리라 OnStepChanged는 쏘지 않는다(들을 구독자가 아직 없다).
            OutgameTutorialProgress.CommitStep(t_chapter, t_i);

            // 매 초기화 복구가 도는 좌표는 막힌 좌표가 아니다 — 정지 판정을 새 좌표에서 다시 세지 않으면
            // 게이트 구간에서 세 번 껐다 켜는 것만으로 fail-open이 오발동한다.
            // 낙인까지 함께 걷는다: 카운터만 0으로 돌리면 이미 선 s_stalled가 남아 이번 세션은 전 기능이 열린 채다.
            OutgameTutorialProgress.ResetStallWatch();
            OutgameFeatureLock.ClearStall();
            OutgameFeatureLock.Refresh();
            return;
        }
    }

    /// <summary>세이브가 붙잡아 둔 스텝 번호로 진행 좌표를 되찾는다. 초기화에서 <b>EnsureData 직후,
    /// RewindToPendingBattleEntry 직전</b>에 1회 부른다 — 되감기는 현재 좌표에서 같은 챕터를
    /// 역방향으로 훑으므로, 좌표가 낡은 채로 그쪽이 먼저 돌면 엉뚱한 챕터를 뒤진다.
    ///
    /// 세션 가드를 두지 않는 이유는 필요가 없어서다 — 호출처가 초기화 1곳뿐이고, 좌표가 이미 맞으면
    /// 조기 반환이라 어디서 다시 불려도 무해하다.</summary>
    public static void ResolveProgressAnchor()
    {
        if (s_data == null || OutgameTutorialProgress.IsCompleted) return;

        int t_id = OutgameTutorialProgress.StepId;

        // 앵커가 없는 세이브(옛 세이브·되감기가 새로 만든 슬롯·ID 미부여 시퀀스)는 좌표가 정본이다.
        if (t_id <= 0) { StampAnchorAtCoord(); return; }

        if (!TryFindStepId(t_id, out int t_chapter, out int t_step))
        {
            // 스텝이 삭제됐다 — 좌표 기준으로 되돌아간다(ID 도입 이전과 같은 동작).
            Debug.LogWarning($"[OutgameTutorialRunner] Step #{t_id} that the save points at is not in the sequence — using position {OutgameTutorialProgress.ChapterIndex}-{OutgameTutorialProgress.StepIndex} as is.");
            StampAnchorAtCoord();
            return;
        }

        if (t_chapter == OutgameTutorialProgress.ChapterIndex && t_step == OutgameTutorialProgress.StepIndex) return;

        Debug.Log($"[OutgameTutorialRunner] Step #{t_id} moved from {OutgameTutorialProgress.ChapterIndex}-{OutgameTutorialProgress.StepIndex} to {t_chapter}-{t_step} — following the position.");

        OutgameTutorialProgress.CommitStep(t_chapter, t_step);

        // 정지 감시는 카운터와 낙인 둘 다 걷어야 한다. Progress.Init()의 DetectStall이 **낡은 좌표로 먼저** 돌아
        // 임계치를 넘겼다면 s_stalled가 이미 서 있는데, 그건 static이라 카운터만 0으로 돌려서는 안 걷힌다
        // — 그대로 두면 좌표를 바로잡고도 이번 세션 내내 전 기능이 열린 채다.
        OutgameTutorialProgress.ResetStallWatch();   // 저작이 옮긴 좌표는 "막힌 좌표"가 아니다
        OutgameFeatureLock.ClearStall();
        OutgameFeatureLock.Refresh();                // 해금은 좌표에서 파생된다
    }

    // 좌표가 가리키는 스텝의 번호(미주입·범위 밖·빈 칸이면 0)
    public static int StepIdAt(int _chapter, int _step)
        => TryGetStepAt(_chapter, _step, out var t_def) ? t_def.StepId : 0;

    // 지금 좌표의 스텝 번호를 앵커로 도장한다. 좌표에 스텝이 없으면(졸업 직전의 끝 좌표 센티넬 등)
    // 아무 것도 쓰지 않는다 — 0으로 남아야 CloseOrWarnOnMissingStep의 졸업 확정이 그대로 산다.
    static void StampAnchorAtCoord()
    {
        int t_chapter = OutgameTutorialProgress.ChapterIndex;
        int t_step    = OutgameTutorialProgress.StepIndex;

        if (StepIdAt(t_chapter, t_step) <= 0) return;

        OutgameTutorialProgress.CommitStep(t_chapter, t_step);   // 좌표는 그대로, 앵커만 채워진다
    }

    // 번호로 좌표 찾기. 33칸을 초기화에 한 번 훑을 뿐이라 사전도 캐시도 만들지 않는다.
    // 번호가 겹치면 먼저 나온 칸이 이긴다(CardCatalog.SetSource와 같은 규칙).
    static bool TryFindStepId(int _id, out int _chapter, out int _step)
    {
        for (int t_c = 0; t_c < ForcedChapterCount; t_c++)
        {
            if (!TryGetChapter(t_c, out var t_chapter)) continue;

            for (int t_s = 0; t_s < t_chapter.StepCount; t_s++)
            {
                if (!t_chapter.TryGetStep(t_s, out var t_def) || t_def.StepId != _id) continue;

                _chapter = t_c;
                _step    = t_s;
                return true;
            }
        }

        _chapter = 0;
        _step    = 0;
        return false;
    }

    // 저작된 챕터의 스텝 수(범위 밖·빈 챕터는 0)
    public static int StepCountOf(int _chapter) => TryGetChapter(_chapter, out var t_chapter) ? t_chapter.StepCount : 0;

    // 임의 좌표의 스텝 조회(진행도와 무관 — 되감기 재생이 좌표째 훑는 창구)
    public static bool TryGetStepAt(int _chapter, int _step, out TutorialStepDef _def)
    {
        _def = null;

        return TryGetChapter(_chapter, out var t_chapter) && t_chapter.TryGetStep(_step, out _def);
    }

    // 현재 좌표가 가리키는 스텝(미주입·완료·범위 밖·빈 칸이면 false)
    public static bool TryGetCurrentStep(out TutorialStepDef _step)
    {
        _step = null;
        if (!IsRunning) return false;

        return TryGetChapter(OutgameTutorialProgress.ChapterIndex, out var t_chapter)
            && t_chapter.TryGetStep(OutgameTutorialProgress.StepIndex, out _step);
    }

    // 현재 스텝 진입 — 결말은 반환값이 말한다(Gated=게이트를 걸어야 함 / Advanced=좌표가 넘어감 / Failed=그 자리에 막힘)
    public static EOutgameTutorialStepResult EnterCurrentStep()
    {
        if (!TryGetCurrentStep(out var t_step))
            return CloseOrWarnOnMissingStep();

        int t_chapter = OutgameTutorialProgress.ChapterIndex;
        int t_index   = OutgameTutorialProgress.StepIndex;

        bool t_hasNext = TryGetNext(t_chapter, t_index, out int t_nextChapter, out int t_nextStep);

        return TutorialStepExecutor.Enter(t_step,
            new OutgameTutorialStepContext(t_chapter, t_index, t_nextChapter, t_nextStep, !t_hasNext,
                                           PersistentTutorialProgressSink.Instance));
    }

    // 지금 서 있는 스텝이 _action인가. 화면이 튜토 좌표를 직접 해석하지 않게 하는 조회 창구
    // (강화 화면이 "지금이 튜토 강화 스텝인가"를 묻는 데 쓴다)
    public static bool IsCurrentAction(EOutgameTutorialAction _action)
        => TryGetCurrentStep(out var t_step) && t_step.Action == _action;

    // 이번 스텝이 상점 진열·판매 대상을 지정했으면 true(미지정이면 상점 기본 진열)
    // 가격 자리에 띄울 문구도 함께 준다 — 저작이 비면 null이고, 그러면 팩의 실제 가격을 쓴다
    public static bool TryGetForcedPack(out string _packId, out string _priceLabel)
    {
        _packId     = null;
        _priceLabel = null;

        return TryGetCurrentStep(out var t_step) && t_step.TryGetForcedPack(out _packId, out _priceLabel);
    }

    // 이번 스텝이 자동 편성으로 채울 카드를 지정했으면 true(미지정이면 일반 편성 규칙)
    public static bool TryGetForcedDeck(out IReadOnlyList<int> _cardIds)
    {
        _cardIds = null;

        return TryGetCurrentStep(out var t_step) && t_step.TryGetForcedDeck(out _cardIds);
    }

    /// <summary>덱 편집을 열 때 빼 둘 카드 — 지금 좌표부터 같은 챕터 앞쪽에서 첫 <see cref="EOutgameTutorialAction.WaitDeckEquip"/>가
    /// 지목한 카드다(전투 스텝을 만나면 중단).
    ///
    /// <b>"현재 스텝"을 묻지 않는 이유</b>: 편집 화면을 여는 버튼은 자기 리스너를 게이트보다 먼저 걸어서,
    /// 패널이 다 세워진 <b>뒤에야</b> 좌표가 장착 스텝으로 넘어간다. 현재 스텝만 보면 그 순간엔 아직 이전 스텝이라
    /// 빈 칸 없이 6/6으로 열리고, 끼울 자리가 없어 그 자리에서 영영 멈춘다.
    /// 앞을 보면 어느 쪽 좌표에서 물어도 같은 답이 나온다(되감기로 다시 흘러도 멱등).</summary>
    public static bool TryGetPendingEquipCard(out int _cardId)
    {
        _cardId = 0;

        // 정지 fail-open으로 덱 탭이 좌표보다 먼저 열리면, 한참 앞 스텝의 카드가 빠진 5/6 덱이 떠 저장이 막힌다.
        if (OutgameFeatureLock.AllUnlocked) return false;

        if (!IsRunning) return false;

        int t_chapter = OutgameTutorialProgress.ChapterIndex;

        for (int t_i = OutgameTutorialProgress.StepIndex; t_i < StepCountOf(t_chapter); t_i++)
        {
            if (!TryGetStepAt(t_chapter, t_i, out var t_def)) continue;

            // 전투로 나가는 스텝을 넘어서면 이번 덱 화면의 일이 아니다.
            if (t_def.Action == EOutgameTutorialAction.BattleStart) return false;
            if (t_def.Action != EOutgameTutorialAction.WaitDeckEquip) continue;

            _cardId = t_def.AnchorCardId;
            return _cardId > 0;
        }

        return false;
    }

    // 스텝 완료를 감지한 브리지가 호출 — 다음 좌표 커밋, 시퀀스를 넘어서면 완료 처리
    public static void NotifyStepSatisfied()
    {
        if (!IsRunning) return;

        // 졸업 보류 판정에 쓸 "방금 끝낸 스텝" — 커밋하면 좌표가 넘어가므로 먼저 떠 둔다.
        TryGetCurrentStep(out var t_satisfied);

        bool t_hasNext = TryGetNext(OutgameTutorialProgress.ChapterIndex, OutgameTutorialProgress.StepIndex,
                                    out int t_nextChapter, out int t_nextStep);

        OutgameTutorialProgress.CommitStep(t_nextChapter, t_nextStep);

        // 마지막 스텝이 전투로 나가면 졸업은 그 전투가 끝난 뒤로 미룬다 — 여기서 낙인을 찍으면 첫 티어 진입이
        // 그 판보다 앞서서 승점이 튜토리얼 천장에 걸려 통째로 사라진다(RankManager.ApplyBattleResult).
        // 미뤄 둔 졸업은 돌아온 씬의 브리지가 끝 좌표를 보고 확정한다(CloseOrWarnOnMissingStep).
        if (!t_hasNext && (t_satisfied == null || !t_satisfied.LeavesScene)) CompleteSequence();

        OnStepChanged?.Invoke();
    }

    /// <summary>덱 게이트에서 전투가 시작됐다 — 좌표를 그 전투를 여는 스텝 뒤로 옮긴다.
    ///
    /// 안내가 짠 순서(덱 선택 → 뒤로가기 → 전투 시작) 말고도 전투로 나가는 길이 있다(덱 편집 화면의 전투 버튼).
    /// 그 길로 나가면 남은 안내 스텝의 앵커가 전부 사라진 화면으로 돌아와 등록을 영영 기다린다 —
    /// 전투는 이미 치렀는데 좌표만 그 앞에 남아, 다음 챕터가 시작되지 않는다.
    ///
    /// 전투 스텝에 이미 서 있으면 아무 일도 하지 않는다 — 그 자리는 게이트가 스스로 넘긴다(이중 전진 방지).</summary>
    public static void NotifyDeckGateBattleLaunched()
    {
        if (!IsRunning) return;

        int t_chapter = OutgameTutorialProgress.ChapterIndex;
        int t_step    = OutgameTutorialProgress.StepIndex;

        for (int t_i = t_step + 1; t_i < StepCountOf(t_chapter); t_i++)
        {
            if (!TryGetStepAt(t_chapter, t_i, out var t_def)) continue;
            if (t_def.Action != EOutgameTutorialAction.BattleStart) continue;

            Debug.LogWarning($"[OutgameTutorialRunner] The battle started without going through the guide — moving position {t_chapter}-{t_step} to after the battle step {t_chapter}-{t_i}.");

            TryGetNext(t_chapter, t_i, out int t_nextChapter, out int t_nextStep);
            OutgameTutorialProgress.CommitStep(t_nextChapter, t_nextStep);

            // 건너뛴 스텝들의 unlocks도 좌표에서 파생되므로 여기서 한 번 반영한다(잠김 룩이 옛 상태에 고착되지 않게).
            OutgameFeatureLock.Refresh();

            OnStepChanged?.Invoke();
            return;
        }
    }

    // 시퀀스 처음부터 지정 좌표까지(그 칸 포함) 스텝을 순서대로 훑는다
    public static IEnumerable<TutorialStepDef> EnumerateUpTo(int _chapter, int _step)
    {
        for (int t_c = 0; t_c <= _chapter && t_c < ForcedChapterCount; t_c++)
        {
            if (!TryGetChapter(t_c, out var t_chapter)) continue;

            int t_last = t_c < _chapter ? t_chapter.StepCount - 1 : Mathf.Min(_step, t_chapter.StepCount - 1);

            for (int t_s = 0; t_s <= t_last; t_s++)
                if (t_chapter.TryGetStep(t_s, out var t_asset)) yield return t_asset;
        }
    }

    // 강제 커서가 쓰는 챕터 조회 — 자율 챕터는 범위 밖으로 취급한다
    static bool TryGetChapter(int _index, out OutgameTutorialChapter _chapter)
    {
        _chapter = null;
        if (_index >= ForcedChapterCount) return false;

        return TryGetChapterRaw(_index, out _chapter);
    }

    static bool TryGetChapterRaw(int _index, out OutgameTutorialChapter _chapter)
    {
        _chapter = null;
        if (s_data == null || s_data.chapters == null) return false;
        if (_index < 0 || _index >= s_data.chapters.Count) return false;

        _chapter = s_data.chapters[_index];
        return _chapter != null;
    }

    // 반환 false = 강제 시퀀스 끝(그때도 out은 끝 좌표를 준다 — 그대로 커밋되어야 하므로)
    static bool TryGetNext(int _chapter, int _step, out int _nextChapter, out int _nextStep)
    {
        _nextChapter = _chapter;
        _nextStep    = _step + 1;
        if (_nextStep < StepCountOf(_chapter)) return true;

        _nextStep    = 0;
        _nextChapter = _chapter + 1;
        while (_nextChapter < ForcedChapterCount && StepCountOf(_nextChapter) == 0) _nextChapter++;

        return _nextChapter < ForcedChapterCount;
    }

    // 좌표가 가리키는 스텝이 없는 경우의 수습. 좌표를 정정하거나 졸업으로 닫았으면 Advanced,
    // 진행할 길이 없으면 Failed — 호출자가 그 둘을 구분해야 fail-open이 필요한 자리에만 선다.
    static EOutgameTutorialStepResult CloseOrWarnOnMissingStep()
    {
        if (!IsRunning) return EOutgameTutorialStepResult.Advanced;

        if (TotalStepCount == 0)
        {
            Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}' has no authored forced step ({ForcedChapterCount} forced chapter(s)) — cannot proceed.");
            return EOutgameTutorialStepResult.Failed;
        }

        int t_chapter = OutgameTutorialProgress.ChapterIndex;
        int t_index   = OutgameTutorialProgress.StepIndex;

        if (t_chapter >= ForcedChapterCount)
        {
            // 끝 좌표(마지막 강제 스텝 바로 다음 자리)는 정상이다 — 전투로 나간 마지막 스텝이 미뤄 둔 졸업을 여기서 확정한다.
            // 브리지 Start에서 도는 자리라 로비 랭크 연출 디렉터의 캐리어 소비(다음 프레임)보다 앞선다.
            // 저작이 강제 챕터를 줄여 좌표가 자율 챕터 안에 남은 세이브도 여기로 온다 — 그 안내는 낙인이 없으니 알림 점이 다시 부른다.
            if (t_chapter > ForcedChapterCount || t_index != 0)
                Debug.LogWarning($"[OutgameTutorialRunner] Position {t_chapter}-{t_index} is outside the {ForcedChapterCount} forced chapter(s) of '{s_data.name}' — closing it as complete.");

            CompleteSequence();
            return EOutgameTutorialStepResult.Advanced;
        }

        if (t_index < StepCountOf(t_chapter))
        {
            Debug.LogWarning($"[OutgameTutorialRunner] Chapter {t_chapter} step {t_index} of '{s_data.name}' is empty — cannot proceed.");
            return EOutgameTutorialStepResult.Failed;
        }

        if (TryGetNext(t_chapter, StepCountOf(t_chapter) - 1, out int t_nextChapter, out int t_nextStep))
        {
            Debug.LogWarning($"[OutgameTutorialRunner] Chapter {t_chapter} of '{s_data.name}' is shorter than {t_index} slots — correcting the position to {t_nextChapter}-{t_nextStep} (resumes in the next scene).");
            OutgameTutorialProgress.CommitStep(t_nextChapter, t_nextStep);
            return EOutgameTutorialStepResult.Advanced;
        }

        Debug.LogWarning($"[OutgameTutorialRunner] There is no step left after the last chapter {t_chapter} of '{s_data.name}' — closing it as complete.");
        CompleteSequence();
        return EOutgameTutorialStepResult.Advanced;
    }

    static void WarnOnMisauthoredChapters()
    {
#if UNITY_EDITOR
        for (int i = 0; i < ChapterCount; i++)
        {
            if (!TryGetChapterRaw(i, out var t_chapter) || t_chapter.StepCount == 0)
            {
                Debug.LogWarning($"[OutgameTutorialRunner] Chapter {i} of '{s_data.name}' has no step — progress stops until the authoring is finished.");
                continue;
            }

            // Halt는 좌표를 되돌려 재시도를 노리는 정책인데, 앵커도 완료 신호도 없는 스텝은
            // 되돌려 봐야 이 초기화에서 다시 세울 수단이 없다 — 그 자리에서 안내가 끝난다.
            for (int t_s = 0; t_s < t_chapter.StepCount; t_s++)
            {
                if (!t_chapter.TryGetStep(t_s, out var t_def) || t_def.OnFailure != EOutgameTutorialFailure.Halt) continue;
                if (t_def.Anchor != EOutgameTutorialAnchor.None || t_def.Completion != EOutgameTutorialCompletion.Auto) continue;

                Debug.LogWarning($"[OutgameTutorialRunner] Step {i}-{t_s}({t_def.Action}) of '{s_data.name}' is Halt but has neither an anchor nor a completion signal — even after a rewind there is no way to resume in this initialization.");
            }

        }

        WarnOnBadStepIds();
#endif
    }

#if UNITY_EDITOR
    // 세이브 앵커가 성립하지 않는 저작을 초기화에서 소리내어 잡는다. 부여 도구는 사람이 눌러야 도는데,
    // 안 누른 채로 두면 복제본에 서 있던 세이브가 앞 원본으로 되감겨 지급이 다시 실행된다.
    static void WarnOnBadStepIds()
    {
        var t_seen = new Dictionary<int, string>();

        for (int t_c = 0; t_c < ChapterCount; t_c++)
        {
            if (!TryGetChapterRaw(t_c, out var t_chapter)) continue;

            for (int t_s = 0; t_s < t_chapter.StepCount; t_s++)
            {
                if (!t_chapter.TryGetStep(t_s, out var t_def)) continue;

                if (t_def.StepId <= 0)
                {
                    Debug.LogWarning($"[OutgameTutorialRunner] Step {t_c}-{t_s}({t_def.Action}) of '{s_data.name}' has no id — run [Assign step ids] on the sequence SO. For now it is addressed only by position, so it shifts when the authoring changes.");
                    continue;
                }

                if (t_seen.TryGetValue(t_def.StepId, out string t_first))
                {
                    Debug.LogWarning($"[OutgameTutorialRunner] Step {t_c}-{t_s} of '{s_data.name}' has the same id #{t_def.StepId} as {t_first} (duplicated row?) — run [Assign step ids]. For now the earlier slot wins and progress rewinds to it.");
                    continue;
                }

                t_seen[t_def.StepId] = $"{t_c}-{t_s}";
            }
        }
    }
#endif
}
