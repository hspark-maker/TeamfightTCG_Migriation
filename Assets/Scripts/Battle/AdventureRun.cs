/// <summary>
/// 모험 전투 런타임 단일 진실원. 싱글 경로 전용.
/// 활성 시: 적 카드 레벨이 랭크가 아니라 정점 저작값으로 고정되고(초기화(InitializationRunner)의 EnemyGrowthProvider),
/// 전투 결과가 랭크 대신 <c>AdventureResultHandoff</c>로 나간다(BattleOutcome.TryCapture).
/// 수명 종점은 TurnRunner.Cleanup — 로비에서 진입을 취소한 경로는 씬 전환이 없으므로 진입점이 직접 End한다.
/// </summary>
public static class AdventureRun
{
    public static bool IsActive { get; private set; }

    /// <summary>도전 중인 정점의 안정 키. 결과 캐리어가 이 키로 클리어를 낙인한다.</summary>
    public static string NodeId { get; private set; }

    /// <summary>이 정점의 고정 상대 카드 레벨(랭크 난이도를 대체한다).</summary>
    public static int AiCardLevel { get; private set; } = CardGrowth.BaseLevel;

    /// <summary>정점 전투 시작. 튜토리얼이 켜져 있으면 아무것도 세우지 않고 false —
    /// 두 모드가 겹치면 덱·셔플·입력 게이트 전 경로에서 튜토리얼이 이기므로 애초에 열지 않는다.</summary>
    public static bool Begin(string _nodeId, int _aiCardLevel)
    {
        if (TutorialConfig.IsActive) return false;
        if (string.IsNullOrEmpty(_nodeId)) return false;

        IsActive    = true;
        NodeId      = _nodeId;
        AiCardLevel = _aiCardLevel < CardGrowth.BaseLevel ? CardGrowth.BaseLevel : _aiCardLevel;
        return true;
    }

    public static void End()
    {
        IsActive    = false;
        NodeId      = null;
        AiCardLevel = CardGrowth.BaseLevel;
    }
}
