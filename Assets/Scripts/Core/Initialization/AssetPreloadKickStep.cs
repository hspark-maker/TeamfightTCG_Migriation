using Cysharp.Threading.Tasks;

// 아트·UI 프리팹 선로드를 건다(완료는 기다리지 않는다 — 대기는 WaitAssetPreloadStep 몫).
// 시작 시점이 컴포넌트 실행 순서에 끌려다니지 않게 초기화 소유자가 명시적으로 건다.
public sealed class AssetPreloadKickStep : MainInitializer
{
    public override UniTask Initialize(InitializationContext _context)
    {
        // 로컬 UI·팩 아트만 먼저 적재한다. 카드 아트는 WaitAssetPreloadStep이
        // 원격 번들 다운로드를 마친 뒤 적재해 진행률과 재시도를 한곳에서 관리한다.
        StartCoroutine(PackArtCache.Preload());

        UiPrefabCache.Preload().Forget();
        return UniTask.CompletedTask;
    }
}
