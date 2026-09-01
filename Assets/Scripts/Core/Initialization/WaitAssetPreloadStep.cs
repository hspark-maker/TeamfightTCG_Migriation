using Cysharp.Threading.Tasks;

// AssetPreloadKickStep이 건 선로드가 끝나기를 기다린다. 실패는 복구 화면으로 보낸다 —
// 아트·UI 프리팹이 없으면 로비가 그려지긴 해도 빈 그림이라 진단이 안 된다.
public sealed class WaitAssetPreloadStep : MainInitializer
{
    public override async UniTask Initialize(InitializationContext _context)
    {
        GameInitialization.SetState(EGameInitState.LoadingAssets);

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
