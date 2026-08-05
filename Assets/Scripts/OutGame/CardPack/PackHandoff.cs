// 개봉 세션 컨텍스트(결과·팩·목적지)를 개봉 화면에 넘기는 씬 캐리어
public static class PackHandoff
{
    // 열어야 할 개봉 결과 (이미 차감·소유 완료된 스냅샷)
    public static OpenedPack Opened { get; private set; }
    // 그 결과를 낳은 팩 정의 — 개봉 뷰의 팩 아트 소스
    public static CardPackData Pack { get; private set; }
    // 획득 후 이동할 목적지 씬
    public static string NextScene { get; private set; }
    // 획득 후 목적지 진입 전 튜토리얼 시작 여부
    public static bool StartTutorial { get; private set; }

    // 개봉 세션 컨텍스트를 싣는다 (구매 성공한 호출자만)
    public static void Set(OpenedPack _opened, CardPackData _pack, string _nextScene, bool _startTutorial)
    {
        Opened = _opened;
        Pack = _pack;
        NextScene = _nextScene;
        StartTutorial = _startTutorial;
    }

    // 넘겨진 세션이 있는지
    public static bool HasPending => Opened != null;

    // 결과를 꺼내고 홀더를 통째로 비운다 (1회 소비) — Pack·NextScene은 Consume 전에 읽을 것
    public static OpenedPack Consume()
    {
        var t_opened = Opened;
        Opened = null;
        Pack = null;
        NextScene = null;
        StartTutorial = false;
        return t_opened;
    }
}
