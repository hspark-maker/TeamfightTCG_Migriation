using System;
using System.Collections.Generic;
using UnityEngine;

// 아웃게임 첫시작 튜토리얼의 시퀀스 해석 static 코어(씬 오브젝트·UI를 모른다).
// 진행 좌표는 (챕터, 챕터 안 스텝) 2차원이지만 챕터 넘김은 좌표의 자리 올림일 뿐이라
// 스텝·브리지·상점은 챕터 경계를 눈치채지 못한다 — 스텝이 "무엇을 하는지"는 스텝 SO가 안다.
public static class OutgameTutorialRunner
{
    static OutgameTutorialData s_data;

    /// <summary>진행도가 다음 스텝으로 넘어갈 때 발화. 진열 대상이 스텝에 따라 달라지는 화면(상점)이 갱신 시점을 잡는다.</summary>
    public static event Action OnStepChanged;

    /// <summary>데이터가 주입됐고 아직 완료 전이면 진행 중. 완료는 항상 진행도의 스칼라가 우선한다.</summary>
    public static bool IsRunning => s_data != null && !OutgameTutorialProgress.IsCompleted;

    /// <summary>저작된 챕터("N편") 수. 미주입·빈 시퀀스는 0.</summary>
    public static int ChapterCount => s_data != null && s_data.chapters != null ? s_data.chapters.Count : 0;

    // 챕터를 가로지른 총 스텝 수. 완료 낙인을 찍어도 되는지의 유일한 근거다 —
    // 챕터 수로 대신하면 "챕터만 만들고 스텝은 아직 안 꽂은" 저작 중간 상태가 완료로 닫힌다.
    static int TotalStepCount
    {
        get
        {
            int t_total = 0;
            for (int i = 0; i < ChapterCount; i++) t_total += StepCountOf(i);
            return t_total;
        }
    }

    /// <summary>씬마다 브리지가 호출하는 멱등 주입. 첫 주입만 유효하다(에셋이 갈리면 진행 좌표가 다른 시퀀스를 가리킨다).</summary>
    public static void EnsureData(OutgameTutorialData _data)
    {
        if (_data == null) return;          // 미배선 브리지가 기존 주입을 지우지 않게.
        if (s_data == _data) return;

        if (s_data != null)
        {
            Debug.LogWarning($"[OutgameTutorialRunner] 다른 튜토리얼 데이터 주입 시도('{_data.name}' ≠ 기존 '{s_data.name}') — 기존 유지.");
            return;
        }

        s_data = _data;
        WarnOnMisauthoredChapters();
    }

    /// <summary>현재 좌표가 가리키는 스텝. 미주입·완료·범위 밖·빈 칸이면 false.</summary>
    public static bool TryGetCurrentStep(out OutgameTutorialStep _step)
    {
        _step = null;
        if (!IsRunning) return false;

        return TryGetChapter(OutgameTutorialProgress.ChapterIndex, out var t_chapter)
            && t_chapter.TryGetStep(OutgameTutorialProgress.StepIndex, out _step);
    }

    /// <summary>현재 스텝을 진입시킨다. 반환 true = 이 씬에서 앵커에 게이트를 걸어야 함(false면 자동 처리·씬 전환).</summary>
    public static bool EnterCurrentStep()
    {
        if (!TryGetCurrentStep(out var t_step))
        {
            CloseOrWarnOnMissingStep();
            return false;
        }

        int t_chapter = OutgameTutorialProgress.ChapterIndex;
        int t_index   = OutgameTutorialProgress.StepIndex;

        // 다음 좌표를 러너가 미리 계산해 실어준다 — 스텝은 자기가 챕터 끝인지도 모른 채 커밋한다.
        bool t_hasNext = TryGetNext(t_chapter, t_index, out int t_nextChapter, out int t_nextStep);

        return t_step.Enter(new OutgameTutorialStepContext(t_chapter, t_index, t_nextChapter, t_nextStep, !t_hasNext,
                                                          PersistentTutorialProgressSink.Instance));
    }

    /// <summary>튜토리얼이 이번 스텝에서 팔 팩을 지정했으면 true. 상점은 진열·가격·구매 대상을 이걸로 덮어써
    /// 튜토리얼 중 구매 결과가 저작대로 고정되게 한다. 미지정이면 false → 상점 기본 진열.</summary>
    public static bool TryGetForcedPack(out CardPackData _pack, out long _refundGold)
    {
        _pack       = null;
        _refundGold = 0;

        return TryGetCurrentStep(out var t_step) && t_step.TryGetForcedPack(out _pack, out _refundGold);
    }

    /// <summary>튜토리얼이 이번 스텝에서 편성할 덱을 지정했으면 true. 덱 자동 편성은 이걸로 채워
    /// 튜토리얼 중 편성 결과가 저작대로 고정되게 한다. 미지정이면 false → 일반 편성 규칙.</summary>
    public static bool TryGetForcedDeck(out IReadOnlyList<CardData> _cards)
    {
        _cards = null;

        return TryGetCurrentStep(out var t_step) && t_step.TryGetForcedDeck(out _cards);
    }

    /// <summary>스텝 완료를 감지한 브리지가 호출. 다음 좌표를 커밋하고 시퀀스를 넘어서면 완료 처리한다.</summary>
    public static void NotifyStepSatisfied()
    {
        if (!IsRunning) return;

        bool t_hasNext = TryGetNext(OutgameTutorialProgress.ChapterIndex, OutgameTutorialProgress.StepIndex,
                                    out int t_nextChapter, out int t_nextStep);

        OutgameTutorialProgress.CommitStep(t_nextChapter, t_nextStep);

        // 완료를 좌표 비교로 파생시키지 않고 여기서 한 번만 확정한다(챕터를 나중에 추가해도 완료 유저가 되살아나지 않게).
        if (!t_hasNext) OutgameTutorialProgress.Complete();

        // 게이트를 걸기 전에 알린다 — 구매 스텝의 진열 교체가 끝난 뒤 게이트가 그 버튼 상태를 읽어야 한다.
        OnStepChanged?.Invoke();
    }

    // 범위 밖·빈 칸이면 false. 미배선 챕터는 스텝이 없는 것과 같다.
    static bool TryGetChapter(int _index, out OutgameTutorialChapter _chapter)
    {
        _chapter = null;
        if (s_data == null || s_data.chapters == null) return false;
        if (_index < 0 || _index >= s_data.chapters.Count) return false;

        _chapter = s_data.chapters[_index];
        return _chapter != null;
    }

    static int StepCountOf(int _chapter) => TryGetChapter(_chapter, out var t_chapter) ? t_chapter.StepCount : 0;

    // 다음 좌표. 반환 false = 시퀀스 끝(그때도 out은 끝 좌표를 준다 — CommitAdvance가 그대로 커밋해야 하므로).
    // 완료 판정과 진입 시 isLast 판정이 이 하나를 공유한다 — 갈라 놓으면 빈 챕터가 뒤에 붙었을 때 완료가 영영 안 찍힌다.
    static bool TryGetNext(int _chapter, int _step, out int _nextChapter, out int _nextStep)
    {
        _nextChapter = _chapter;
        _nextStep    = _step + 1;
        if (_nextStep < StepCountOf(_chapter)) return true;

        // 자리 올림. 빈 챕터는 건너뛴다 — 저작 미완 챕터가 진행을 영구히 막지 않게(단조 증가라 종료 보장).
        _nextStep    = 0;
        _nextChapter = _chapter + 1;
        while (_nextChapter < ChapterCount && StepCountOf(_nextChapter) == 0) _nextChapter++;

        return _nextChapter < ChapterCount;
    }

    // 실행할 스텝이 없는 상태를 정리한다. 좌표가 시퀀스 밖(저작에서 챕터를 줄인 경우)이면 완료로 닫는다 —
    // 안 그러면 IsRunning은 true인데 실행할 스텝이 없는 림보가 영구히 남는다.
    static void CloseOrWarnOnMissingStep()
    {
        if (!IsRunning) return;

        // 단, 빈 시퀀스는 저작 미완일 뿐이라 완료로 낙인찍지 않는다(스텝을 채우면 그대로 재개돼야 한다).
        // 판정 근거가 챕터 수가 아니라 총 스텝 수인 게 핵심 — 챕터만 만들어 둔 중간 상태도 여기서 걸러야 한다.
        if (TotalStepCount == 0)
        {
            Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'에 저작된 스텝이 없습니다(챕터 {ChapterCount}개) — 진행할 수 없습니다.");
            return;
        }

        int t_chapter = OutgameTutorialProgress.ChapterIndex;
        int t_index   = OutgameTutorialProgress.StepIndex;

        if (t_chapter >= ChapterCount)
        {
            // 되돌릴 수 없는 낙인이라 근거를 남긴다(저작에서 챕터를 줄이면 정상적으로 여기로 온다).
            Debug.LogWarning($"[OutgameTutorialRunner] 좌표 {t_chapter}-{t_index}이(가) '{s_data.name}'의 챕터 {ChapterCount}개 밖입니다 — 완료로 닫습니다.");
            OutgameTutorialProgress.Complete();
            return;
        }

        // 범위 안인데 못 꺼냈다 = 그 칸이 비어 있다. 저작 실수라 완료로 닫지 않고 드러낸다.
        if (t_index < StepCountOf(t_chapter))
        {
            Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'의 챕터 {t_chapter} 스텝 {t_index}이(가) 비어 있습니다 — 진행할 수 없습니다.");
            return;
        }

        // 챕터가 짧아졌다(저작에서 스텝을 줄인 경우) → 다음 챕터 첫 스텝으로 정정해 림보를 끊는다.
        // 정정만 하고 이 씬에서 진입시키지는 않는다 — 다음 씬 진입 때 브리지가 재개한다.
        if (TryGetNext(t_chapter, StepCountOf(t_chapter) - 1, out int t_nextChapter, out int t_nextStep))
        {
            Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'의 챕터 {t_chapter}이(가) {t_index}칸보다 짧습니다 — 좌표를 {t_nextChapter}-{t_nextStep}로 정정합니다(다음 씬에서 재개).");
            OutgameTutorialProgress.CommitStep(t_nextChapter, t_nextStep);
            return;
        }

        Debug.LogWarning($"[OutgameTutorialRunner] '{s_data.name}'의 마지막 챕터 {t_chapter} 뒤에 남은 스텝이 없습니다 — 완료로 닫습니다.");
        OutgameTutorialProgress.Complete();
    }

    // 저작 검증(주입 1회). 챕터 경계 = 씬 전환 경계라, 편의 마지막이 씬을 떠나지 않으면
    // 다음 편의 첫 스텝이 같은 씬에서 곧장 이어져 버린다. 저작자만 보면 되므로 에디터 전용.
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
