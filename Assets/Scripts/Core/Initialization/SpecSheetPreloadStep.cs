using Cysharp.Threading.Tasks;

// 스펙시트 색인 선로드. 파싱 자체는 ContentProfileStep의 SpecSource.Init이 이미 1회 했으므로
// 여기 비용은 키 색인뿐이다. 지연 로드도 되지만 상점·토너먼트 진입 프레임에 걸리지 않게 당긴다.
// 팩 드롭 조회가 CardCatalog를 읽으므로 카탈로그 구성 뒤여야 한다.
public sealed class SpecSheetPreloadStep : MainInitializer
{
    public override UniTask Initialize(InitializationContext _context)
    {
        PackSpec.Init();
        TournamentSpec.Init();
        AlbumSpec.Init();
        return UniTask.CompletedTask;
    }
}
