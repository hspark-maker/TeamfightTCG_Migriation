using Cysharp.Threading.Tasks;

// 인증·스펙 동기화 뒤, 설정과 카드 카탈로그를 조립하기 전에 원격 콘텐츠를 준비한다.
public sealed class RuntimeContentPreloadStep : MainInitializer
{
    public override async UniTask Initialize(InitializationContext _context)
    {
        GameInitialization.SetState(EGameInitState.LoadingAssets);
        var t_token = this.GetCancellationTokenOnDestroy();
        await RemoteCardArtDownload.PrepareAsync(t_token);
        if (GameInitialization.IsTerminated)
        {
            _context.Abort();
            return;
        }
        await RuntimeContentCache.PreloadAsync(t_token);
        DataLibrary.instance.keywordIconConfig = RuntimeContentCache.Config.keywordIconConfig;
        DataLibrary.instance.cardFrameConfig = RuntimeContentCache.Config.cardFrameConfig;
    }
}
