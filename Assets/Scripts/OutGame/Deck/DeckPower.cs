using System.Collections.Generic;

// 덱·카드의 파워(전력) 환산 단일 지점
public static class DeckPower
{
    /// <summary>상대 카드 _card가 서 있는 레벨. 싱글은 랭크 티어가 정한 AI 레벨(카드마다 다르다)이고,
    /// 멀티는 바닥이다 — 스탯을 와이어로 보내지 않는 lockstep이라 상대 강화분을 추측하면 표시가 거짓말이 된다.
    /// 튜토리얼도 바닥이다 — 전투가 적에게 성장값을 안 태우므로(GameInitializer) 미리보기만 강화된 적을 보여주면 실전과 갈린다.</summary>
    public static int OpponentLevelOf(CardData _card)
        => DeckConfig.IsMultiplayer || TutorialConfig.IsActive ? CardGrowth.BaseLevel : RankManager.AiCardLevelOf(_card);

    /// <summary>표시용 레벨. _mine=false는 상대 덱 — **성장 없음이 아니라 상대의 레벨**이다.
    /// AI가 티어 레벨을 갖기 전에는 둘이 같았지만 지금은 다르다(상대도 강화된 카드로 나온다).</summary>
    public static int LevelOf(CardData _card, bool _mine = true)
    {
        if (_card == null) return CardGrowth.BaseLevel;

        return _mine ? CardGrowthManager.GrowthOf(_card).Level : OpponentLevelOf(_card);
    }

    public static int EvolutionStageOf(CardData _card, bool _mine = true)
        => CardGrowthManager.GrowthAtLevel(_card, LevelOf(_card, _mine)).EvolutionStage;

    // 표시용 최대 체력. 내 카드는 내 강화 진행도, 상대 카드는 상대 레벨 기준이다.
    public static int MaxHpOf(CardData _card, bool _mine = true)
    {
        if (_card == null) return 0;

        return _card.maxHp + CardGrowthManager.GrowthAtLevel(_card, LevelOf(_card, _mine)).HpBonus;
    }

    // 카드 1장의 파워(null은 0)
    public static int Of(CardData _card, bool _mine = true)
        => _card != null ? MaxHpOf(_card, _mine) + _card.bonusHp : 0;

    // 덱 전체 파워 합
    public static int Of(IReadOnlyList<CardData> _deck, bool _mine = true)
    {
        if (_deck == null) return 0;

        int t_sum = 0;
        for (int t_i = 0; t_i < _deck.Count; t_i++)
            t_sum += Of(_deck[t_i], _mine);

        return t_sum;
    }
}
