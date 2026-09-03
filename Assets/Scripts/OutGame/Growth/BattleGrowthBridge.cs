/// <summary>전투·표시에 성장값을 흘리는 배선 한 벌. Battle이 OutGame을 참조하지 않도록 값 생산자를 여기서 꽂는다.
///
/// 이 배선은 정식 기동(<see cref="BattleGrowthBridgeStep"/>)과 초기화를 거치지 않는 독립 테스트 씬
/// (<see cref="GrowthStandaloneInitializer"/>) 양쪽이 같이 쓴다 — 한쪽만 꽂으면 그 경로에서
/// <see cref="CardInstance"/>가 마스터 데이터의 키워드를 전부 열린 것으로 취급해
/// 강화·해금이 화면에 반영되지 않는다(표시와 규칙이 갈라진다).</summary>
public static class BattleGrowthBridge
{
    /// <summary>공급자 대입뿐이라 여러 번 불러도 같다(멱등).</summary>
    public static void Install()
    {
        GameInitializer.GrowthProvider = CardGrowthManager.GrowthOf;

        // Firebase 구현이 먼저 주입되지 않은 개발/오프라인 환경에서만 로컬 세이브를 쓴다.
        // 전투와 네트워크는 IMatchGrowthSource만 보므로 이후 공급자 교체가 와이어 계약을 바꾸지 않는다.
        MatchGrowthSource.SetFallback(new LocalSaveMatchGrowthSource());

        // 표시용 해금 키워드도 같은 성장값에서 나온다. 이걸 안 꽂으면 아직 못 쓰는 키워드가
        // 도감·덱편집·정보창에 그대로 떠서 표시와 규칙이 갈라진다.
        CardVisualRules.UnlockedKeywordProvider = _card => CardGrowthManager.GrowthOf(_card).UnlockedKeywords;
        CardVisualRules.EvolutionStageProvider = _card => CardGrowthManager.GrowthOf(_card).EvolutionStage;

        // 싱글 AI 레벨. 모험 정점 저작값만 남았고(랭크 티어로 적을 강화하던 축은 제거) 같은 성장 곡선에 태운다 —
        // 체력뿐 아니라 키워드·시너지 해금까지 플레이어와 동일한 규칙으로 결정된다.
        // 레벨은 전투 시작 시점에 읽어야 한다(초기화에서 굳히면 진행 중 바뀐 정점 값이 안 따라온다).
        GameInitializer.EnemyGrowthProvider = _card => CardGrowthManager.GrowthAtLevel(_card, EnemyCardLevel());
        GameInitializer.EnemyTierProvider = () => RankManager.TierIndex;

        // 튜토리얼 전투용 미강화 기준값. 레벨은 바닥 고정이라 체력은 안 오르고 해금 게이트만 산다 —
        // 진행도(GrowthProvider)를 태우면 저작된 킬 수·턴 수가 깨지고, 아예 안 태우면 키워드가 전부 열린다.
        GameInitializer.BaseGrowthProvider = _card => CardGrowthManager.GrowthAtLevel(_card, CardGrowth.BaseLevel);
        GameInitializer.GrowthAtLevelProvider = CardGrowthManager.GrowthAtLevel;
    }

    // 이번 전투에서 적 카드가 쓸 레벨. 우선순위는 모험 정점 저작값 > AI 덱 저작 레벨 > 바닥이다
    // (랭크 티어로 적 레벨을 올리던 축은 제거됐다). 곡선 밖 레벨은 보너스가 멈추므로 어느 쪽이든 만렙 클램프.
    //
    // 덱 레벨은 여기서 굴리지 않는다 — 이 함수는 카드 1장마다 불리므로 여기서 뽑으면 같은 덱 안에서
    // 카드마다 레벨이 흔들린다. 추첨은 덱을 고르는 자리(AIDeckConfig.TakeDeck)에서 판당 1회다.
    static int EnemyCardLevel()
    {
        int t_level = AdventureRun.IsActive ? AdventureRun.AiCardLevel
                    : DeckConfig.EnemyCardLevel > 0 ? DeckConfig.EnemyCardLevel
                    : CardGrowth.BaseLevel;

        // 저작 상한(CardGrowthManager.MaxLevel)이 아니라 코드 천장으로 조인다. 서버는 AI 레벨을 고정 천장으로
        // 조여 스냅샷을 만들고 재시뮬을 돌리므로, 여기서만 표 상한으로 더 깎으면 실제 필드가 서버보다 약해져
        // 매 판 발산으로 잡힌다(제출 해시도 같은 클램프를 쓴다 — ServerMatchmaker.ComputeEnemyDeckHash).
        return CardGrowthManager.ClampLevel(t_level);
    }
}
