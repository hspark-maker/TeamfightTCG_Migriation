using Cysharp.Threading.Tasks;

/// <summary>세이브 초기화 후 패스를 미리 조회한다. 응답을 기다리며 로비 진입을 막지 않는다.</summary>
public sealed class PassPreloadStep : MainInitializer
{
    public override UniTask Initialize(InitializationContext _context)
    {
        PassCommands.RefreshAsync().Forget();
        return UniTask.CompletedTask;
    }
}
