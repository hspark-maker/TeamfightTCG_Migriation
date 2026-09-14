using Cysharp.Threading.Tasks;

// 기존 초기화 프리팹의 스텝 ID·의존성을 유지한다.
// 모든 아트와 UI가 원격 대상이므로 실제 선로드는 WaitAssetPreloadStep에서 다운로드 후 시작한다.
public sealed class AssetPreloadKickStep : MainInitializer
{
    public override UniTask Initialize(InitializationContext _context)
    {
        return UniTask.CompletedTask;
    }
}
