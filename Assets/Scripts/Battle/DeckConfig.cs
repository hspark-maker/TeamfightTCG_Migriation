using System.Collections.Generic;
using System.Linq;

public static class DeckConfig
{
    public static List<CardData> PlayerDeck { get; private set; }

    public static void Set(IEnumerable<CardData> _deck)
    {
        PlayerDeck = new List<CardData>(_deck.Where(d => d != null));
    }

    public static bool HasDeck => PlayerDeck != null && PlayerDeck.Count == DeckSaveManager.DECK_SIZE && PlayerDeck.All(d => d != null);

    // ── 상대(AI) 덱 ──────────────────────────────────────────────────────────
    // 싱글 전투에서 상대 덱을 전투 씬 진입 전(로비 매칭)에 확정해 넘기기 위한 홀더.
    // 미설정이면 GameInitializer가 기존대로 aiDeckConfig.GetRandomDeck()로 폴백한다.
    public static List<CardData> EnemyDeck { get; private set; }

    public static void SetEnemyDeck(IEnumerable<CardData> _deck)
    {
        EnemyDeck = new List<CardData>(_deck.Where(d => d != null));
    }

    public static bool HasEnemyDeck => EnemyDeck != null && EnemyDeck.Count > 0;

    /// <summary>다음 전투에 상대 덱을 넘기지 않도록 홀더를 비운다(폴백 경로로 되돌림).</summary>
    public static void ClearEnemyDeck() => EnemyDeck = null;

    public static bool IsMultiplayer { get; private set; }

    public static void SetMultiplayer(bool _value) => IsMultiplayer = _value;

    /// <summary>씬 종료 시 모드 플래그 해제. 진입점마다 SetMultiplayer(false)를 부르는 규율은
    /// 진입점 하나를 빠뜨리면 이전 판의 멀티 플래그가 다음 판으로 새어든다 —
    /// TutorialConfig.End()와 같은 자리(TurnRunner.Cleanup)에서 함께 끄는 것이 단일 규율.</summary>
    public static void ResetMode() => IsMultiplayer = false;
}
