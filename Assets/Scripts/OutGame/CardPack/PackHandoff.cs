// 개봉 세션 컨텍스트(구매 티켓·팩·목적지)를 개봉 화면에 넘기는 씬 캐리어
public static class PackHandoff
{
    public static PackPurchaseTicket Ticket { get; private set; }
    public static CardPackData Pack { get; private set; }
    public static string NextScene { get; private set; }
    public static bool StartTutorial { get; private set; }

    public static bool HasPending => Ticket != null;

    // 개봉 세션 컨텍스트를 싣는다 — 결과가 아니라 왕복 중인 티켓을 넘긴다
    public static void Set(PackPurchaseTicket _ticket, CardPackData _pack, string _nextScene, bool _startTutorial)
    {
        Ticket = _ticket;
        Pack = _pack;
        NextScene = _nextScene;
        StartTutorial = _startTutorial;
    }

    // 티켓을 꺼내고 홀더를 통째로 비운다 (1회 소비) — Pack·NextScene은 Consume 전에 읽을 것
    public static PackPurchaseTicket Consume()
    {
        PackPurchaseTicket t_ticket = Ticket;
        Ticket = null;
        Pack = null;
        NextScene = null;
        StartTutorial = false;
        return t_ticket;
    }
}
