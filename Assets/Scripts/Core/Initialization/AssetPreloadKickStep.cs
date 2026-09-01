using Cysharp.Threading.Tasks;

// 아트·UI 프리팹 선로드를 건다(완료는 기다리지 않는다 — 대기는 WaitAssetPreloadStep 몫).
// 시작 시점이 컴포넌트 실행 순서에 끌려다니지 않게 초기화 소유자가 명시적으로 건다.
public sealed class AssetPreloadKickStep : MainInitializer
{
    public override UniTask Initialize(InitializationContext _context)
    {
        // 카드 아트는 CardData가 직접 물지 않고 Addressables로 따로 온다(CardArtCache).
        // 그리는 코드는 여전히 동기라 화면에 나가기 전에 여기서 채워 둔다.
        // 지금은 전 카드를 받는다 — 범위를 덱·도감 단위로 좁히는 건 그 다음 단계다.
        StartCoroutine(CardArtCache.Preload(CardCatalog.AllSpecs));
        StartCoroutine(PackArtCache.Preload());

        UiPrefabCache.Preload().Forget();
        return UniTask.CompletedTask;
    }
}
