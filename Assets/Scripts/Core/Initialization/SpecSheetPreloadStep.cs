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

        // 보상 표가 비면 앨범·토너먼트·랭크는 조용히 0을 표시하고, 전투는 매판 0을 지급한다.
        // 그 사고는 유저 신고 전에는 드러나지 않으므로 부팅에서 세운다.
        if (!RewardSpec.TryValidateRequired(out string t_rewardError))
        {
            Debug.LogError($"[SpecSheetPreloadStep] {t_rewardError}");
            GameInitialization.MarkRecoveryRequired();
        }
        return UniTask.CompletedTask;
    }
}
