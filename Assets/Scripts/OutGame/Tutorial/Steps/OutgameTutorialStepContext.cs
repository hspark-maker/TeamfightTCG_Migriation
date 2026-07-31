/// <summary>스텝이 진행도를 건드리는 유일한 창구. 러너가 자기 인덱스·시퀀스 끝 여부를 담아 넘긴다
/// — 스텝 SO는 자기가 몇 번째 칸인지 모른 채 커밋·롤백할 수 있다(같은 에셋을 여러 칸에 꽂는 전제).</summary>
public readonly struct OutgameTutorialStepContext
{
    public int Index { get; }

    readonly bool m_isLast;

    public OutgameTutorialStepContext(int _index, bool _isLast)
    {
        Index    = _index;
        m_isLast = _isLast;
    }

    /// <summary>다음 인덱스를 커밋한다. 불변식: 커밋이 실행보다 앞선다 — 실행 도중 강제종료돼도 스텝이 되풀이되지 않게.</summary>
    public void CommitAdvance() => OutgameTutorialProgress.CommitStep(Index + 1);

    /// <summary>실행이 실패했을 때 커밋을 되돌린다(다음 부트에 재시도).</summary>
    public void Rollback() => OutgameTutorialProgress.CommitStep(Index);

    /// <summary>마지막 스텝이면 완료를 확정한다. 되돌릴 수 없으므로 실행 성공이 확정된 뒤에만 부른다.</summary>
    public void CompleteIfLast()
    {
        if (m_isLast) OutgameTutorialProgress.Complete();
    }
}
