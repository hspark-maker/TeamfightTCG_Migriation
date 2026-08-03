/// <summary>스텝 컨텍스트가 진행도를 쓰는 대상. 컨텍스트가 static 진행도에 하드와이어돼 있으면
/// 트리거 러너가 넘긴 컨텍스트의 CommitAdvance() 한 줄이 온보딩의 영속 좌표를 덮어쓴다.</summary>
public interface ITutorialProgressSink
{
    void Commit(int _chapter, int _step);
    void Complete();
}

/// <summary>온보딩 시퀀스용 영속 싱크. 세이브 좌표를 움직이는 유일한 구현이다.</summary>
sealed class PersistentTutorialProgressSink : ITutorialProgressSink
{
    public static readonly ITutorialProgressSink Instance = new PersistentTutorialProgressSink();

    PersistentTutorialProgressSink() { }

    public void Commit(int _chapter, int _step) => OutgameTutorialProgress.CommitStep(_chapter, _step);

    public void Complete() => OutgameTutorialProgress.Complete();
}
