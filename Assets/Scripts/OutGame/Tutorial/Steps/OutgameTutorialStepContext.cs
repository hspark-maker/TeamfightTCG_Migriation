/// <summary>스텝이 진행도를 건드리는 유일한 창구. 러너가 자기 좌표·다음 좌표·시퀀스 끝 여부를 담아 넘긴다
/// — 스텝 SO는 자기가 몇 편 몇 번째 칸인지, 챕터 끝인지조차 모른 채 커밋·롤백할 수 있다(같은 에셋을 여러 칸에 꽂는 전제).
/// 진행도 싱크를 주입받으므로 온보딩 시퀀스 밖(트리거 튜토리얼)에서도 같은 스텝 SO를 안전하게 실행할 수 있다.</summary>
public readonly struct OutgameTutorialStepContext
{
    public int ChapterIndex { get; }
    public int StepIndex { get; }

    // 챕터 자리 올림은 러너가 이미 계산해 실어준다.
    readonly int m_nextChapter;
    readonly int m_nextStep;
    readonly bool m_isLast;

    // 진행도를 어디에 쓸지도 러너가 정한다(온보딩 = 영속 세이브, 트리거 = 메모리).
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

    /// <summary>다음 좌표를 커밋한다. 불변식: 커밋이 실행보다 앞선다 — 실행 도중 강제종료돼도 스텝이 되풀이되지 않게.</summary>
    public void CommitAdvance() => m_sink?.Commit(m_nextChapter, m_nextStep);

    /// <summary>실행이 실패했을 때 커밋을 되돌린다(다음 부트에 재시도).</summary>
    public void Rollback() => m_sink?.Commit(ChapterIndex, StepIndex);

    /// <summary>시퀀스 전체의 마지막 스텝이면(챕터 마지막이 아니다) 완료를 확정한다.
    /// 되돌릴 수 없으므로 실행 성공이 확정된 뒤에만 부른다.</summary>
    public void CompleteIfLast()
    {
        if (m_isLast) m_sink?.Complete();
    }
}
