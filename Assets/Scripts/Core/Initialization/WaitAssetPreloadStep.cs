using Cysharp.Threading.Tasks;

// 원격 다운로드 후 카드·팩·UI를 선로드하고 완료를 기다린다. 실패는 복구 화면으로 보낸다 —
// 아트·UI 프리팹이 없으면 로비가 그려지긴 해도 빈 그림이라 진단이 안 된다.
public sealed class WaitAssetPreloadStep : MainInitializer
{
    public override async UniTask Initialize(InitializationContext _context)
    {
        GameInitialization.SetState(EGameInitState.LoadingAssets);

        await RemoteCardArtDownload.PrepareAsync(this.GetCancellationTokenOnDestroy());
        if (GameInitialization.IsTerminated) return;
        PackArtCache.ResetIfFailed();
        StartCoroutine(PackArtCache.Preload());
        UiPrefabCache.ResetIfFailed();
        UiPrefabCache.Preload().Forget();
        StartCoroutine(CardArtCache.Preload(CardCatalog.AllSpecs));

        await UniTask.WaitUntil(() =>
            (CardArtCache.IsComplete && PackArtCache.IsComplete &&
             (UiPrefabCache.IsComplete || UiPrefabCache.HasFailed)) ||
            GameInitialization.IsTerminated);

        if (GameInitialization.IsTerminated)
        {
            _context.Abort();
            return;
        }

        if (CardArtCache.HasFailed || PackArtCache.HasFailed || UiPrefabCache.HasFailed)
        {
            GameInitialization.MarkRecoveryRequired();
            _context.Abort();
        }
    }
}
