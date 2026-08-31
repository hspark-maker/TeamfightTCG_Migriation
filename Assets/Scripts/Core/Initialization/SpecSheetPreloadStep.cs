using Cysharp.Threading.Tasks;
using UnityEngine;

// 스펙시트 색인 선로드. 파싱 자체는 ContentProfileStep의 SpecSource.Init이 이미 1회 했으므로
// 여기 비용은 키 색인뿐이다. 지연 로드도 되지만 상점·토너먼트 진입 프레임에 걸리지 않게 당긴다.
// 팩 드롭 조회가 CardCatalog를 읽으므로 카탈로그 구성 뒤여야 한다.
public sealed class SpecSheetPreloadStep : MainInitializer
{
    public override UniTask Initialize(InitializationContext _context)
    {
        PackSpec.Init();
        RewardSpec.Init();
        GrowthSpec.Init();

        // 보상 표가 비면 앨범·토너먼트·랭크는 조용히 0을 표시하고, 전투는 매판 0을 지급한다.
        // 그 사고는 유저 신고 전에는 드러나지 않으므로 부팅에서 세운다.
        if (!RewardSpec.TryValidateRequired(out string t_rewardError))
        {
            Debug.LogError($"[SpecSheetPreloadStep] {t_rewardError}");
            GameInitialization.MarkRecoveryRequired();
        }

        // 카드 강화 규칙·한계돌파 곡선이 비면 서버 lockDeck이 덱 잠금을 통째로 거절해 전투 진입이 막힌다.
        // 키워드 강화 표는 여기서 보지 않는다 — 그 축만 조용히 닫히고 전투는 선다.
        if (!GrowthSpec.TryValidateRequired(out string t_growthError))
        {
            Debug.LogError($"[SpecSheetPreloadStep] {t_growthError}");
            GameInitialization.MarkRecoveryRequired();
        }
        return UniTask.CompletedTask;
    }
}
