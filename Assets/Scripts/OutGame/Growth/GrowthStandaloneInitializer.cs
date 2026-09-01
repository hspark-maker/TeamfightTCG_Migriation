/// <summary>초기화(InitializationRunner)가 없는 독립 테스트 씬에서 성장 매니저를 준비한다.
/// 곡선·비용은 스펙시트가 소유하고 GrowthSpec이 지연 로드로 스스로 읽으므로 여기서 주입할 설정이 없다 —
/// 여기서 하는 일은 세이브 캐싱(Init) 순서를 설치기와 똑같이 맞추는 것뿐이다.</summary>
public static class GrowthStandaloneInitializer
{
    public static bool Ensure()
    {
        if (CardGrowthManager.IsReady && KeywordGrowthManager.IsReady) return true;

        // Init은 세이브 채택 이후에 부른다 — DataSaveManager.Data를 그대로 캐싱한다.
        KeywordGrowthManager.Init();
        CardGrowthManager.Init();
        return true;
    }
}
