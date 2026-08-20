// 정점 전투 결과를 로비에서 소비해 보상 수령으로 잇는 진입점.
// 전투 골드 흡입이 끝난 뒤에 붙는다 — 두 획득이 겹치면 어느 쪽을 받은 것인지 읽히지 않는다.
public static class TournamentReturnFlow
{
    // 구독 멱등 가드. 로비를 드나들 때마다 Arm이 불리므로, 없으면 구독이 쌓여 팝업이 그만큼 뜬다.
    static bool s_armed;

    public static void Arm()
    {
        if (s_armed) return;
        s_armed = true;

        // 구독을 풀지 않는다 — static 이벤트 + static 구독자라 씬 수명과 무관하고 죽은 오브젝트도 남지 않는다.
        LobbyGainEffectDirector.OnAnyFinished += OnGainEffectFinished;
    }

    // 보여줄 것이 없어 지나간 경우에도 오는 신호다(골드가 0이어도 여기까지 온다).
    static void OnGainEffectFinished()
    {
        // 실린 결과가 없으면 조용히 지나간다(일반 전투 복귀·팩 개봉도 같은 신호를 낸다).
        if (!TournamentResultHandoff.TryConsume(out string t_nodeId, out bool t_won)) return;

        // 패배는 아무 일도 없다 — 낙인이 없으니 그 정점은 도전 가능한 채로 남는다.
        if (!t_won) return;

        TournamentRewardFlow.Open(t_nodeId);
    }
}
