using Cysharp.Threading.Tasks;

public abstract class TurnBase
{
    protected readonly TurnContext ctx;

    protected TurnBase(TurnContext _ctx)
    {
        ctx = _ctx;
    }

    public virtual void OnEnter() { }
    public abstract UniTask Execute();
    public virtual void OnExit() { }
}

public interface IAiTakeoverContinuable
{
    void ContinueAfterAiTakeover();
}
