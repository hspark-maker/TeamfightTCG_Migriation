// 전투 씬이 확정한 모험 정점 결과를 로비 씬까지 실어 나르는 씬 캐리어(자체 세이브 없음)
public static class AdventureResultHandoff
{
    static string s_nodeId;
    static bool   s_won;
    static bool   s_pending;

    // 결과 싣기(BattleOutcome.TryCapture 한 곳에서만). 정점 키가 없으면 낙인할 대상이 없어 버린다.
    public static void Set(string _nodeId, bool _won)
    {
        if (string.IsNullOrEmpty(_nodeId)) return;

        s_nodeId  = _nodeId;
        s_won     = _won;
        s_pending = true;
    }

    // 결과를 꺼내고 홀더를 비운다(1회 소비 — 보상 지급이 두 번 돌면 안 된다)
    public static bool TryConsume(out string _nodeId, out bool _won)
    {
        _nodeId = s_nodeId;
        _won    = s_won;

        bool t_had = s_pending;
        Clear();
        return t_had;
    }

    public static void Clear()
    {
        s_nodeId  = null;
        s_won     = false;
        s_pending = false;
    }
}
