using System;
using System.Collections.Generic;
using UnityEngine;

// 아웃게임 첫시작 튜토리얼의 시퀀스 해석 static 코어(씬 오브젝트·UI를 모른다)
public static class OutgameTutorialRunner
{
    static OutgameTutorialData s_data;

    // 진행도가 다음 스텝으로 넘어갈 때 발화
    public static event Action OnStepChanged;

    // 데이터가 주입됐고 아직 완료 전인가
    public static bool IsRunning => s_data != null && !OutgameTutorialProgress.IsCompleted;

    // 저작된 챕터("N편") 수(미주입·빈 시퀀스는 0)
    public static int ChapterCount => s_data != null && s_data.chapters != null ? s_data.chapters.Count : 0;

    static int TotalStepCount
    {
        get
        {
            int t_total = 0;
            for (int i = 0; i < ChapterCount; i++) t_total += StepCountOf(i);
            return t_total;
        }
    }

    /// <summary>온보딩 졸업 처리의 유일한 창구(멱등).
    ///
    /// 첫 랭크 티어 진입은 <b>여기가 아니라 시퀀스가 저작한 자리</b>(EnterFirstRank 스텝)에서 일어난다 —
    /// 진입 연출은 마지막 전투에서 로비로 돌아온 그 순간에 서야 하고, 졸업은 그보다 뒤로 밀릴 수 있기 때문이다.
    /// 여기 남은 호출은 그 스텝을 거치지 않고 닫히는 경로(디버그 스킵·좌표 이탈)의 안전망이다(TryEnterFirstTier는 멱등).</summary>
    public static void CompleteSequence()
    {
        if (OutgameTutorialProgress.IsCompleted) return;

        OutgameTutorialProgress.Complete();

        if (RankManager.TryEnterFirstTier(out var t_entry)) RankResultHandoff.Set(t_entry);

        // 졸업으로 전 기능이 열린다. 게이트를 거치지 않고 닫히는 경로(전투에서 돌아와 확정하는 졸업·디버그 스킵)에도
        // 잠김 룩이 따라오게 여기서 알린다 — FeatureLockView는 OnChanged로만 다시 그린다.
        OutgameFeatureLock.Refresh();

        // 트리거 튜토리얼도 졸업과 함께 풀린다 — 그 답이 뒤집힌 것은 여기서만 알 수 있다.
        TriggeredTutorialRunner.NotifyOnboardingCompleted();
    }

    // 씬마다 브리지가 호출하는 멱등 주입(첫 주입만 유효)
    public static void EnsureData(OutgameTutorialData _data)
    {
        if (_data == null) return;
        if (s_data == _data) return;

        if (s_data != null)
        {
            Debug.LogWarning($"[OutgameTutorialRunner] 다른 튜토리얼 데이터 주입 시도('{_data.name}' ≠ 기존 '{s_data.name}') — 기존 유지.");
            return;
        }

        s_data = _data;
        WarnOnMisauthoredChapters();
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
            // 시나리오 유무는 묻지 않는다: 토너먼트 구간의 진입은 대본 없이 덱 게이트만 켜므로,
            // 그 항이 남으면 그 구간에서 앱을 다시 켰을 때 되감을 대상을 못 찾아 영구 정지한다.
            if (!t_def.ShowDeckGate) return;

            Debug.LogWarning($"[OutgameTutorialRunner] 대본 전투 전에 앱이 닫혔습니다 — 좌표 {t_chapter}-{t_step}을(를) 전투 진입 스텝 {t_chapter}-{t_i}로 되감습니다.");

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
            Debug.LogWarning($"[OutgameTutorialRunner] 세이브가 가리키는 스텝 #{t_id}이(가) 시퀀스에 없습니다 — 좌표 {OutgameTutorialProgress.ChapterIndex}-{OutgameTutorialProgress.StepIndex}를 그대로 씁니다.");
            StampAnchorAtCoord();
            return;
        }

        if (t_chapter == OutgameTutorialProgress.ChapterIndex && t_step == OutgameTutorialProgress.StepIndex) return;

        Debug.Log($"[OutgameTutorialRunner] 스텝 #{t_id}이(가) {OutgameTutorialProgress.ChapterIndex}-{OutgameTutorialProgress.StepIndex} → {t_chapter}-{t_step}로 옮겨졌습니다 — 좌표를 따라갑니다.");

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
        for (int t_c = 0; t_c < ChapterCount; t_c++)
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

            Debug.LogWarning($"[OutgameTutorialRunner] 안내를 거치지 않고 전투가 시작됐습니다 — 좌표 {t_chapter}-{t_step}을(를) 전투 스텝 {t_chapter}-{t_i} 뒤로 옮깁니다.");

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
        for (int t_c = 0; t_c <= _chapter && t_c < ChapterCount; t_c++)
        {
            if (!TryGetChapter(t_c, out var t_chapter)) continue;

            int t_last = t_c < _chapter ? t_chapter.StepCount - 1 : Mathf.Min(_step, t_chapter.StepCount - 1);

            for (int t_s = 0; t_s <= t_last; t_s++)
                if (t_chapter.TryGetStep(t_s, out var t_asset)) yield return t_asset;
        }
    }

    static bool TryGetChapter(int _index, out OutgameTutorialChapter _chapter)
    {
        _chapter = null;
        if (s_data == null || s_data.chapters == null) return false;
        if (_index < 0 || _index >= s_data.chapters.Count) return false;

        _chapter = s_data.chapters[_index];
        return _chapter != null;
    }

    // 반환 false = 시퀀스 끝(그때도 out은 끝 좌표를 준다 — 그대로 커밋되어야 하므로)
    static bool TryGetNext(int _chapter, int _step, out int _nextChapter, out int _nextStep)
    {
        _nextChapter = _chapter;
        _nextStep    = _step + 1;
        if (_nextStep < StepCountOf(_chapter)) return true;

        _nextStep    = 0;
        _nextChapter = _chapter + 1;
        while (_nextChapter < ChapterCount && StepCountOf(_nextChapter) == 0) _nextChapter++;

        return _nextChapter < ChapterCount;
    }

    // 좌표가 가리키는 스텝이 없는 경우의 수습. 좌표를 정정하거나 졸업으로 닫았으면 Advanced,
    // 진행할 길이 없으면 Failed — 호출자가 그 둘을 구분해야 fail-open이 필요한 자리에만 선다.
    static EOutgameTutorialStepResult CloseOrWarnOnMissingStep()
    {
        if (!IsRunning) return EOutgameTutorialStepResult.Advanced;

        if (TotalStepCount == 0)
        {
            Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'에 저작된 스텝이 없습니다(챕터 {ChapterCount}개) — 진행할 수 없습니다.");
            return EOutgameTutorialStepResult.Failed;
        }

        int t_chapter = OutgameTutorialProgress.ChapterIndex;
        int t_index   = OutgameTutorialProgress.StepIndex;

        if (t_chapter >= ChapterCount)
        {
            // 끝 좌표(마지막 스텝 바로 다음 자리)는 정상이다 — 전투로 나간 마지막 스텝이 미뤄 둔 졸업을 여기서 확정한다.
            // 브리지 Start에서 도는 자리라 로비 랭크 연출 디렉터의 캐리어 소비(다음 프레임)보다 앞선다.
            if (t_chapter > ChapterCount || t_index != 0)
                Debug.LogWarning($"[OutgameTutorialRunner] 좌표 {t_chapter}-{t_index}이(가) '{s_data.name}'의 챕터 {ChapterCount}개 밖입니다 — 완료로 닫습니다.");

            CompleteSequence();
            return EOutgameTutorialStepResult.Advanced;
        }

        if (t_index < StepCountOf(t_chapter))
        {
            Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'의 챕터 {t_chapter} 스텝 {t_index}이(가) 비어 있습니다 — 진행할 수 없습니다.");
            return EOutgameTutorialStepResult.Failed;
        }

        if (TryGetNext(t_chapter, StepCountOf(t_chapter) - 1, out int t_nextChapter, out int t_nextStep))
        {
            Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'의 챕터 {t_chapter}이(가) {t_index}칸보다 짧습니다 — 좌표를 {t_nextChapter}-{t_nextStep}로 정정합니다(다음 씬에서 재개).");
            OutgameTutorialProgress.CommitStep(t_nextChapter, t_nextStep);
            return EOutgameTutorialStepResult.Advanced;
        }

        Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'의 마지막 챕터 {t_chapter} 뒤에 남은 스텝이 없습니다 — 완료로 닫습니다.");
        CompleteSequence();
        return EOutgameTutorialStepResult.Advanced;
    }

    static void WarnOnMisauthoredChapters()
    {
#if UNITY_EDITOR
        for (int i = 0; i < ChapterCount; i++)
        {
            if (!TryGetChapter(i, out var t_chapter) || t_chapter.StepCount == 0)
            {
                Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'의 챕터 {i}에 스텝이 없습니다 — 저작을 마치기 전엔 진행이 멈춥니다.");
                continue;
            }

            // Halt는 좌표를 되돌려 재시도를 노리는 정책인데, 앵커도 완료 신호도 없는 스텝은
            // 되돌려 봐야 이 초기화에서 다시 세울 수단이 없다 — 그 자리에서 안내가 끝난다.
            for (int t_s = 0; t_s < t_chapter.StepCount; t_s++)
            {
                if (!t_chapter.TryGetStep(t_s, out var t_def) || t_def.OnFailure != EOutgameTutorialFailure.Halt) continue;
                if (t_def.Anchor != EOutgameTutorialAnchor.None || t_def.Completion != EOutgameTutorialCompletion.Auto) continue;

                Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'의 스텝 {i}-{t_s}({t_def.Action})가 Halt인데 앵커도 완료 신호도 없습니다 — 되돌려도 이 초기화에서 재개할 수단이 없습니다.");
            }

            // 마지막 챕터는 면제한다 — 그 끝은 다음 챕터로의 인계가 아니라 졸업이라 씬을 떠날 이유가 없다.
            if (i == ChapterCount - 1) continue;

            if (!t_chapter.TryGetStep(t_chapter.StepCount - 1, out var t_last) || !t_last.LeavesScene)
                Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'의 챕터 {i}('{t_chapter.Label}') 마지막 스텝이 씬을 떠나지 않습니다 — 챕터는 전투 스텝으로 끝나야 합니다.");
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
            if (!TryGetChapter(t_c, out var t_chapter)) continue;

            for (int t_s = 0; t_s < t_chapter.StepCount; t_s++)
            {
                if (!t_chapter.TryGetStep(t_s, out var t_def)) continue;

                if (t_def.StepId <= 0)
                {
                    Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'의 스텝 {t_c}-{t_s}({t_def.Action})에 ID가 없습니다 — 시퀀스 SO의 [스텝 ID 부여]를 돌리세요. 지금은 좌표로만 지목되어 저작이 바뀌면 밀립니다.");
                    continue;
                }

                if (t_seen.TryGetValue(t_def.StepId, out string t_first))
                {
                    Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'의 스텝 {t_c}-{t_s}가 {t_first}과(와) 같은 ID #{t_def.StepId}입니다(행 복제?) — [스텝 ID 부여]를 돌리세요. 지금은 앞 칸이 이겨 진행이 그리로 되감깁니다.");
                    continue;
                }

                t_seen[t_def.StepId] = $"{t_c}-{t_s}";
            }
        }
    }
#endif
}
