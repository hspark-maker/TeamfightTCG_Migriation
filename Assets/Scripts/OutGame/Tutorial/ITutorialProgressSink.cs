// 스텝 컨텍스트가 진행도를 쓰는 대상
public interface ITutorialProgressSink
{
    void Commit(int _chapter, int _step);
    void Complete();
}

// 온보딩 시퀀스용 영속 싱크 — 세이브 좌표를 움직이는 유일한 구현
sealed class PersistentTutorialProgressSink : ITutorialProgressSink
{
    public static readonly ITutorialProgressSink Instance = new PersistentTutorialProgressSink();

    PersistentTutorialProgressSink() { }

    public void Commit(int _chapter, int _step) => OutgameTutorialProgress.CommitStep(_chapter, _step);

    public void Complete() => OutgameTutorialProgress.Complete();
}
