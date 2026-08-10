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

    /// <summary>온보딩 졸업 처리의 유일한 창구(멱등). 완료 낙인과 함께 첫 랭크 티어에 진입시킨다 —
    /// 튜토리얼 전투로 쌓은 포인트는 첫 티어 임계치에 못 미치므로(승점 × 3전), 진입은 전투가 아니라 졸업이 결정한다.
    /// 진입 결과는 캐리어에 실어 둔다(로비 도달 시 랭크 연출 디렉터가 소비한다).</summary>
    public static void CompleteSequence()
    {
        if (OutgameTutorialProgress.IsCompleted) return;

        OutgameTutorialProgress.Complete();

        if (RankManager.TryEnterFirstTier(out var t_entry)) RankResultHandoff.Set(t_entry);
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

    // 현재 좌표가 가리키는 스텝(미주입·완료·범위 밖·빈 칸이면 false)
    public static bool TryGetCurrentStep(out TutorialStepDef _step)
    {
        _step = null;
        if (!IsRunning) return false;

        return TryGetChapter(OutgameTutorialProgress.ChapterIndex, out var t_chapter)
            && t_chapter.TryGetStep(OutgameTutorialProgress.StepIndex, out _step);
    }

    // 현재 스텝 진입 — 반환 true = 이 씬에서 앵커에 게이트를 걸어야 함
    public static bool EnterCurrentStep()
    {
        if (!TryGetCurrentStep(out var t_step))
        {
            CloseOrWarnOnMissingStep();
            return false;
        }

        int t_chapter = OutgameTutorialProgress.ChapterIndex;
        int t_index   = OutgameTutorialProgress.StepIndex;

        bool t_hasNext = TryGetNext(t_chapter, t_index, out int t_nextChapter, out int t_nextStep);

        return TutorialStepExecutor.Enter(t_step,
            new OutgameTutorialStepContext(t_chapter, t_index, t_nextChapter, t_nextStep, !t_hasNext,
                                           PersistentTutorialProgressSink.Instance));
    }

    // 이번 스텝이 상점 진열·판매 대상을 지정했으면 true(미지정이면 상점 기본 진열)
    public static bool TryGetForcedPack(out CardPackData _pack)
    {
        _pack = null;

        return TryGetCurrentStep(out var t_step) && t_step.TryGetForcedPack(out _pack);
    }

    // 이번 스텝이 자동 편성으로 채울 카드를 지정했으면 true(미지정이면 일반 편성 규칙)
    public static bool TryGetForcedDeck(out IReadOnlyList<CardData> _cards)
    {
        _cards = null;

        return TryGetCurrentStep(out var t_step) && t_step.TryGetForcedDeck(out _cards);
    }

    // 스텝 완료를 감지한 브리지가 호출 — 다음 좌표 커밋, 시퀀스를 넘어서면 완료 처리
    public static void NotifyStepSatisfied()
    {
        if (!IsRunning) return;

        bool t_hasNext = TryGetNext(OutgameTutorialProgress.ChapterIndex, OutgameTutorialProgress.StepIndex,
                                    out int t_nextChapter, out int t_nextStep);

        OutgameTutorialProgress.CommitStep(t_nextChapter, t_nextStep);

        if (!t_hasNext) CompleteSequence();

        OnStepChanged?.Invoke();
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

    static int StepCountOf(int _chapter) => TryGetChapter(_chapter, out var t_chapter) ? t_chapter.StepCount : 0;

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

    static void CloseOrWarnOnMissingStep()
    {
        if (!IsRunning) return;

        if (TotalStepCount == 0)
        {
            Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'에 저작된 스텝이 없습니다(챕터 {ChapterCount}개) — 진행할 수 없습니다.");
            return;
        }

        int t_chapter = OutgameTutorialProgress.ChapterIndex;
        int t_index   = OutgameTutorialProgress.StepIndex;

        if (t_chapter >= ChapterCount)
        {
            Debug.LogWarning($"[OutgameTutorialRunner] 좌표 {t_chapter}-{t_index}이(가) '{s_data.name}'의 챕터 {ChapterCount}개 밖입니다 — 완료로 닫습니다.");
            CompleteSequence();
            return;
        }

        if (t_index < StepCountOf(t_chapter))
        {
            Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'의 챕터 {t_chapter} 스텝 {t_index}이(가) 비어 있습니다 — 진행할 수 없습니다.");
            return;
        }

        if (TryGetNext(t_chapter, StepCountOf(t_chapter) - 1, out int t_nextChapter, out int t_nextStep))
        {
            Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'의 챕터 {t_chapter}이(가) {t_index}칸보다 짧습니다 — 좌표를 {t_nextChapter}-{t_nextStep}로 정정합니다(다음 씬에서 재개).");
            OutgameTutorialProgress.CommitStep(t_nextChapter, t_nextStep);
            return;
        }

        Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'의 마지막 챕터 {t_chapter} 뒤에 남은 스텝이 없습니다 — 완료로 닫습니다.");
        CompleteSequence();
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

            if (!t_chapter.TryGetStep(t_chapter.StepCount - 1, out var t_last) || !t_last.LeavesScene)
                Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'의 챕터 {i}('{t_chapter.Label}') 마지막 스텝이 씬을 떠나지 않습니다 — 챕터는 전투 스텝으로 끝나야 합니다.");
        }
#endif
    }
}
