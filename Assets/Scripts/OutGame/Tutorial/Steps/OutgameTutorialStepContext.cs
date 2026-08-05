// 스텝이 진행도를 건드리는 유일한 창구(러너가 자기 좌표·다음 좌표·시퀀스 끝 여부를 담아 넘긴다)
public readonly struct OutgameTutorialStepContext
{
    public int ChapterIndex { get; }
    public int StepIndex { get; }

    readonly int m_nextChapter;
    readonly int m_nextStep;
    readonly bool m_isLast;

    readonly ITutorialProgressSink m_sink;

    public OutgameTutorialStepContext(int _chapter, int _step, int _nextChapter, int _nextStep, bool _isLast, ITutorialProgressSink _sink)
    {
        ChapterIndex  = _chapter;
        StepIndex     = _step;
        m_nextChapter = _nextChapter;
        m_nextStep    = _nextStep;
        m_isLast      = _isLast;
        m_sink        = _sink;
    }

    // 다음 좌표 커밋 — 불변식: 커밋이 실행보다 앞선다(실행 중 강제종료돼도 스텝이 되풀이되지 않게)
    public void CommitAdvance() => m_sink?.Commit(m_nextChapter, m_nextStep);

    // 실행 실패 시 커밋을 되돌린다(다음 부트에 재시도)
    public void Rollback() => m_sink?.Commit(ChapterIndex, StepIndex);

    // 시퀀스 전체의 마지막 스텝이면 완료를 확정한다(되돌릴 수 없으므로 성공 확정 뒤에만)
    public void CompleteIfLast()
    {
        if (m_isLast) m_sink?.Complete();
    }
}
