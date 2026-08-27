using Cysharp.Threading.Tasks;

// 전투에 성장값을 흘리는 유일한 배선. Battle이 OutGame을 참조하지 않도록 값 생산자를 부트가 꽂는다
// (GameInitializer.GrowthProvider 주석이 지정한 자리). 곡선 조회가 Config를 쓰므로 OutgameConfigStep 뒤다.
public sealed class BattleGrowthBridgeStep : MainInitializer
{
    public override UniTask Initialize(InitializationContext _context)
    {
        GameInitializer.GrowthProvider = CardGrowthManager.GrowthOf;

        // Firebase 구현이 먼저 주입되지 않은 개발/오프라인 환경에서만 로컬 세이브를 쓴다.
        // 전투와 네트워크는 IMatchGrowthSource만 보므로 이후 공급자 교체가 와이어 계약을 바꾸지 않는다.
        MatchGrowthSource.SetFallback(new LocalSaveMatchGrowthSource());

        // 표시용 해금 키워드도 같은 성장값에서 나온다. 이걸 안 꽂으면 아직 못 쓰는 키워드가
        // 도감·덱편집·정보창에 그대로 떠서 표시와 규칙이 갈라진다.
        CardVisualRules.UnlockedKeywordProvider = _card => CardGrowthManager.GrowthOf(_card).UnlockedKeywords;
        CardVisualRules.EvolutionStageProvider = _card => CardGrowthManager.GrowthOf(_card).EvolutionStage;

        // 싱글 AI 레벨. 토너먼트 정점 저작값만 남았고(랭크 티어로 적을 강화하던 축은 제거) 같은 성장 곡선에 태운다 —
        // 체력뿐 아니라 키워드·시너지 해금까지 플레이어와 동일한 규칙으로 결정된다.
        // 레벨은 전투 시작 시점에 읽어야 한다(부트에서 굳히면 진행 중 바뀐 정점 값이 안 따라온다).
        GameInitializer.EnemyGrowthProvider = _card => CardGrowthManager.GrowthAtLevel(_card, EnemyCardLevel());
        GameInitializer.EnemyTierProvider = () => RankManager.TierIndex;

        // 튜토리얼 전투용 미강화 기준값. 레벨은 바닥 고정이라 체력은 안 오르고 해금 게이트만 산다 —
        // 진행도(GrowthProvider)를 태우면 저작된 킬 수·턴 수가 깨지고, 아예 안 태우면 키워드가 전부 열린다.
        GameInitializer.BaseGrowthProvider = _card => CardGrowthManager.GrowthAtLevel(_card, CardGrowth.BaseLevel);
        GameInitializer.GrowthAtLevelProvider = CardGrowthManager.GrowthAtLevel;
        return UniTask.CompletedTask;
    }

    // 이번 전투에서 적 카드가 쓸 레벨. 토너먼트면 정점 저작값(곡선 밖 레벨은 보너스가 멈추므로 만렙 클램프),
    // 아니면 바닥이다 — 랭크 티어로 적 레벨을 올리던 축은 제거됐다.
    static int EnemyCardLevel()
    {
        if (!TournamentRun.IsActive) return CardGrowth.BaseLevel;

        int t_max = CardGrowthManager.MaxLevel;
        return t_max > 0 && TournamentRun.AiCardLevel > t_max ? t_max : TournamentRun.AiCardLevel;
    }
}
