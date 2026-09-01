/// <summary>초기화(InitializationRunner)가 없는 독립 테스트 씬에서 성장 매니저를 준비한다.
/// 곡선·비용은 스펙시트가 소유하고 GrowthSpec이 지연 로드로 스스로 읽으므로 여기서 주입할 설정이 없다 —
/// 여기서 하는 일은 세이브 캐싱(Init) 순서를 설치기와 똑같이 맞추고, 설치기가 꽂는 성장 배선을 같이 세우는 것뿐이다.</summary>
public static class GrowthStandaloneInitializer
{
    public static bool Ensure()
    {
        // 배선은 Init 여부와 무관하게 세운다(대입뿐이라 멱등) — 매니저가 이미 서 있다는 이유로 건너뛰면
        // CardInstance가 마스터 데이터의 키워드를 전부 열린 것으로 취급해, 강화·해금을 바꿔도 표시가 그대로다.
        BattleGrowthBridge.Install();

        if (CardGrowthManager.IsReady && KeywordGrowthManager.IsReady) return true;

        // Init은 세이브 채택 이후에 부른다 — DataSaveManager.Data를 그대로 캐싱한다.
        KeywordGrowthManager.Init();
        CardGrowthManager.Init();
        return true;
    }
}
