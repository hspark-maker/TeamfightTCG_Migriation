using System.Collections.Generic;
using System.Linq;

public static class DeckConfig
{
    public static List<int> PlayerDeck { get; private set; }

    public static void Set(IEnumerable<int> _deck)
    {
        PlayerDeck = new List<int>(_deck.Where(CardCatalog.Contains));
    }


    public static bool HasDeck => PlayerDeck != null && PlayerDeck.Count == DeckSaveManager.DECK_SIZE && PlayerDeck.All(CardCatalog.Contains);

    // ── 상대(AI) 덱 ──────────────────────────────────────────────────────────
    // 싱글 전투에서 상대 덱을 전투 씬 진입 전(로비 매칭)에 확정해 넘기기 위한 홀더.
    // 미설정이면 GameInitializer가 기존대로 aiDeckConfig.GetRandomDeck()로 폴백한다.
    public static List<int> EnemyDeck { get; private set; }

    /// <summary>확정된 상대 덱이 쓸 카드 레벨. 0 = 미저작이며 그때는 전투가 바닥 레벨로 떨어진다.
    /// 값은 덱을 고르는 자리에서 <b>한 번만</b> 굴려 여기 싣는다 — 전투에서 다시 굴리면 카드마다 레벨이 흔들린다.
    /// 토너먼트·튜토리얼은 각자 저작값이 우선이라 이 값을 보지 않는다(BattleGrowthBridgeStep 참고).</summary>
    public static int EnemyCardLevel { get; private set; }

    public static void SetEnemyDeck(IEnumerable<int> _deck, int _cardLevel = 0)
    {
        EnemyDeck = new List<int>(_deck.Where(CardCatalog.Contains));
        EnemyCardLevel = _cardLevel > 0 ? _cardLevel : 0;
    }


    public static bool HasEnemyDeck => EnemyDeck != null && EnemyDeck.Count > 0;

    /// <summary>다음 전투에 상대 덱을 넘기지 않도록 홀더를 비운다(폴백 경로로 되돌림).</summary>
    public static void ClearEnemyDeck()
    {
        EnemyDeck = null;
        EnemyCardLevel = 0;
    }

    public static bool IsMultiplayer { get; private set; }
    public static bool AiTakeover { get; private set; }

    public static void SetMultiplayer(bool _value)
    {
        IsMultiplayer = _value;
        AiTakeover = false;
    }

    /// <summary>멀티로 시작한 전투에서 이탈한 상대를 로컬 AI가 인수했는가.
    /// 멀티 플래그는 유지한다. 재접속은 이 값이 켜지기 전 유예 구간에서만 허용한다.</summary>
    public static void SetAiTakeover(bool _value) => AiTakeover = IsMultiplayer && _value;

    /// <summary>씬 종료 시 모드 플래그 해제. 진입점마다 SetMultiplayer(false)를 부르는 규율은
    /// 진입점 하나를 빠뜨리면 이전 판의 멀티 플래그가 다음 판으로 새어든다 —
    /// TutorialConfig.End()와 같은 자리(TurnRunner.Cleanup)에서 함께 끄는 것이 단일 규율.</summary>
    public static void ResetMode()
    {
        IsMultiplayer = false;
        AiTakeover = false;
    }
}
