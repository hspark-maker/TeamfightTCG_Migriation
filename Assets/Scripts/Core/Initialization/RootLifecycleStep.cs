using Cysharp.Threading.Tasks;
using UnityEngine;

// 초기화 루트의 수명만 담당한다. 프리팹 사본이 로딩 씬·로비 씬 둘이라 먼저 깬 쪽이 초기화를 선점하고,
// 늦게 깬 쪽은 자식 매니저가 각자 자폭하기 전에 루트째 걷힌다(빈 루트가 씬에 남지 않게).
// 선점 판정(InitClaimed)은 러너가 들고 CardCatalogStep이 세운다.
public sealed class RootLifecycleStep : MainInitializer
{
    public override UniTask Initialize(InitializationContext _context)
    {
        if (InitializationRunner.InitClaimed)
        {
            Destroy(_context.Root);
            _context.Abort();
            return UniTask.CompletedTask;
        }

        DontDestroyOnLoad(_context.Root);
        return UniTask.CompletedTask;
    }
}
