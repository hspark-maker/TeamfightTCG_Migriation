using Cysharp.Threading.Tasks;

public abstract class TurnBase
{
    protected TurnContext ctx;

    protected TurnBase(TurnContext _ctx)
    {
        this.ctx = _ctx;
    }

    public virtual void OnEnter() { }
    public abstract UniTask Execute();
    public virtual void OnExit() { }
}
