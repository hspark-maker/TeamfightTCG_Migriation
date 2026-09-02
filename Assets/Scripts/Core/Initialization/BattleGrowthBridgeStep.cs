using Cysharp.Threading.Tasks;

// 전투에 성장값을 흘리는 배선을 기동 순서에 끼운다. 배선 자체는 BattleGrowthBridge가 소유한다 —
// 초기화를 거치지 않는 독립 테스트 씬(GrowthStandaloneInitializer)도 같은 한 벌을 써야 하기 때문이다.
// 곡선 조회가 Config를 쓰므로 OutgameConfigStep 뒤다.
public sealed class BattleGrowthBridgeStep : MainInitializer
{
    public override UniTask Initialize(InitializationContext _context)
    {
        BattleGrowthBridge.Install();
        return UniTask.CompletedTask;
    }
}
