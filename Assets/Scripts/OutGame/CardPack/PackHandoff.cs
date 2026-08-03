// 구매한 쪽(상점/부트)이 "이 팩을 열고, 획득 후 이 씬으로 가라(+튜토리얼이면 시작)"를 개봉 화면에 넘기는 캐리어.
// DeckConfig 관용구(static 프로퍼티 + Set/Has/Consume)를 따른다. 자체 세이브 없음 —
// 구매·소유·재화는 CardPackOpener.TryPurchase가 이미 원자 영속했으므로, 여기선 "이번 개봉 세션"의
// 휘발 컨텍스트(어떤 결과를 열고, 어디로 갈지)만 잠시 실어 나른다.
public static class PackHandoff
{
    // 열어야 할 개봉 결과(이미 소유·차감 완료된 스냅샷). Consume 전까지만 유효.
    public static OpenedPack Opened { get; private set; }
    // 그 결과를 낳은 팩 정의. 개봉 뷰가 팩 외형(아트)을 이 참조로 갈아끼운다 —
    // 결과 스냅샷은 packId 문자열만 쥐고 있고 팩 레지스트리가 없어(상점 SO 미개입) 역참조가 불가하다.
    public static CardPackData Pack { get; private set; }
    // 획득 후 이동할 목적지 씬. 첫시작=배틀, 일반=로비. 목적지 분기는 이 값으로만 한다(첫시작 재판정 안 함).
    public static string NextScene { get; private set; }
    // 획득 후 목적지 진입 전에 튜토리얼을 시작할지. 첫시작만 true.
    public static bool StartTutorial { get; private set; }

    /// <summary>개봉 세션 컨텍스트를 싣는다. 구매 성공한 호출자만 호출(opened는 Success 전제).
    /// pack은 방금 산 그 팩 — 결과와 외형이 갈리지 않게 결과를 낸 쪽이 함께 넘긴다.</summary>
    public static void Set(OpenedPack _opened, CardPackData _pack, string _nextScene, bool _startTutorial)
    {
        Opened = _opened;
        Pack = _pack;
        NextScene = _nextScene;
        StartTutorial = _startTutorial;
    }

    /// <summary>넘겨진 세션이 있는지. 오버레이가 열기 전에, 브레인이 정상 진입 판별에 쓴다.</summary>
    public static bool HasPending => Opened != null;

    /// <summary>개봉 결과를 꺼내고 홀더를 통째로 비운다(1회 소비). Pack/NextScene/StartTutorial은 Consume 전에 읽을 것.</summary>
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
