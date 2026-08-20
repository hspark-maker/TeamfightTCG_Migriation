using System;

// 정점 전투 결과를 로비에서 소비해 "떠났던 화면으로 돌아간다"로 잇는 진입점.
//
// 화면 복원과 선물 등장은 박자가 다르다 — 복원은 로비가 서는 즉시(첫 프레임부터 맵이 떠 있어야 한다),
// 등장은 골드 흡입이 끝난 뒤다(두 사건이 겹치면 어느 쪽이 무엇인지 읽히지 않는다).
// 보상 팝업은 여기서 열지 않는다. 맵에 선 선물을 눌러야 열린다.
public static class TournamentReturnFlow
{
    /// <summary>화면 복원 요청(탭 + 맵). (정점 키, 승리 여부)</summary>
    public static event Action<string, bool> ReturnRequested;

    /// <summary>선물 등장 요청(승리만). 골드 흡입 뒤에 온다.</summary>
    public static event Action<string> GiftRevealRequested;

    // 구독 멱등 가드. 로비를 드나들 때마다 Arm이 불리므로, 없으면 구독이 쌓여 등장이 그만큼 돈다.
    static bool s_armed;

    // 등장을 기다리는 정점(빈 값 = 없음). 복원 때 캐리어를 비우므로 여기 옮겨 든다.
    static string s_giftNodeId;

    public static void Arm()
    {
        if (s_armed) return;
        s_armed = true;

        // 구독을 풀지 않는다 — static 이벤트 + static 구독자라 씬 수명과 무관하고 죽은 오브젝트도 남지 않는다.
        LobbyGainEffectDirector.OnAnyFinished += OnGainEffectFinished;
    }

    /// <summary>로비가 선 직후 1회. 캐리어를 비우고 화면을 되돌린다(선물 등장은 뒤로 미룬다).</summary>
    public static void Restore()
    {
        if (!TournamentResultHandoff.TryConsume(out string t_nodeId, out bool t_won)) return;

        s_giftNodeId = t_won ? t_nodeId : null;

        ReturnRequested?.Invoke(t_nodeId, t_won);
    }

    // 보여줄 것이 없어 지나간 경우에도 오는 신호다(골드가 0이어도 여기까지 온다).
    static void OnGainEffectFinished()
    {
        if (string.IsNullOrEmpty(s_giftNodeId)) return;

        string t_nodeId = s_giftNodeId;
        s_giftNodeId = null;   // 남기면 다음 팩 개봉의 신호에 엉뚱하게 터진다

        // 조건 없이 낸다 — 이 신호가 맵의 등장 예약을 푸는 유일한 열쇠라, 걸러 버리면 선물이 감춰진 채 남는다.
        GiftRevealRequested?.Invoke(t_nodeId);
    }
}
