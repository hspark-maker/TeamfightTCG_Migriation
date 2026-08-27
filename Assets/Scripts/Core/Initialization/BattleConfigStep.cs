using Cysharp.Threading.Tasks;
using UnityEngine;

// 전투 튜닝 SO를 전역 static에 꽂는 단일 창구. 전부 null 허용 시그니처라 미배선이 예외가 되지는 않는다
// (대신 각 static이 기본값을 처음 꺼내 쓸 때 세션당 1회 경고한다).
// 다른 Awake보다 먼저 꽂혀야 해서 러너 순서 앞쪽에 선다.
public sealed class BattleConfigStep : MainInitializer
{
    [SerializeField] BattleTimingConfig battleTimingConfig;
    // 티어 테이블과 랭크 보상 표는 같은 SO를 읽는다 — 둘로 나누면 승급 기준과 보상 기준이 갈린다.
    [SerializeField] RankConfig rankConfig;
    [SerializeField] BattleVfxLibrary battleVfxLibrary;

    public override UniTask Initialize(InitializationContext _context)
    {
        GameTiming.SetConfig(battleTimingConfig);
        RankManager.SetConfig(rankConfig);
        RankRewardManager.SetConfig(rankConfig);
        BattleVfx.SetLibrary(battleVfxLibrary);
        return UniTask.CompletedTask;
    }
}
