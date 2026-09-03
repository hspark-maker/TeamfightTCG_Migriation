using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 스펙시트 색인 선로드. 파싱 자체는 ContentProfileStep의 SpecSource.Init이 이미 1회 했으므로
// 여기 비용은 키 색인뿐이다. 지연 로드도 되지만 상점·모험 진입 프레임에 걸리지 않게 당긴다.
// 팩 드롭 조회가 CardCatalog를 읽으므로 카탈로그 구성 뒤여야 한다.
public sealed class SpecSheetPreloadStep : MainInitializer
{
    public override UniTask Initialize(InitializationContext _context)
    {
        InitializeRequiredSpecs(_context);
        return UniTask.CompletedTask;
    }

    // 필수 표 실패를 두 표면으로 가른다.
    // 앱이 콘텐츠보다 낡은 것이면 재시도가 무의미하므로 업데이트 안내로 보내고,
    // 데이터 손상이면 재시도 가능한 복구 화면으로 보낸다.
    void FailRequiredSpec(InitializationContext _context, bool _updateRequired, string _error)
    {
        if (!_updateRequired)
        {
            FailToRecovery(_context, new InvalidOperationException(_error));
            return;
        }
        Debug.LogError($"[SpecSheetPreloadStep] {_error}");
        GameInitialization.MarkUpdateRequired();
        Destroy(_context.Root);
        _context.Abort();
    }

    void InitializeRequiredSpecs(InitializationContext _context)
    {
        PackSpec.Init();
        AIDeckSpec.Init();
        RewardSpec.Init();
        GrowthSpec.Init();

        if (!RankGradeSpec.TryValidateRequired(out string t_rankError))
        {
            FailRequiredSpec(_context, RankGradeSpec.UpdateRequired, t_rankError);
            return;
        }

        if (!RankGradeSpec.TryBuildRuntime(out RankConfig t_runtimeRank, out t_rankError))
        {
            FailRequiredSpec(_context, RankGradeSpec.UpdateRequired, t_rankError);
            return;
        }

        if (!AIDeckSpec.TryValidateRequired(out string t_aiDeckError))
        {
            FailRequiredSpec(_context, AIDeckSpec.UpdateRequired, t_aiDeckError);
            return;
        }

        if (!AdventureNodeSpec.TryValidateRequired(out string t_adventureError))
        {
            FailRequiredSpec(_context, AdventureNodeSpec.UpdateRequired, t_adventureError);
            return;
        }

        // 필수 표가 모두 유효한 뒤에만 전역에 공개한다. 판정과 보상은 같은 서버 스냅샷을 본다.
        RankManager.SetConfig(t_runtimeRank);
        RankRewardManager.SetConfig(t_runtimeRank);

        // 보상 표가 비면 앨범·모험·랭크는 조용히 0을 표시하고, 전투는 매판 0을 지급한다.
        // 그 사고는 유저 신고 전에는 드러나지 않으므로 초기화에서 세운다.
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
    }
}
