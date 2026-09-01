using System.Collections.Generic;

// 덱·카드의 파워(전력) 환산 단일 지점
// 여기서 나오는 수는 전부 화면에 그릴 값이다 — 그래서 내 카드의 성장은 CardGrowthManager.DisplayGrowthOf 로 읽는다
// (서버가 아직 확정하지 않은 한계돌파까지 얹힌 값이라, 서버로 나가는 스냅샷은 이 창구를 지나지 않는다).
public static class DeckPower
{
    /// <summary>상대 카드 _card가 서 있는 레벨. 늘 바닥이다 — 적을 랭크로 강화하던 축은 제거됐고,
    /// 멀티는 스탯을 와이어로 보내지 않는 lockstep이라 상대 강화분을 추측하면 표시가 거짓말이 된다.
    /// 명시 레벨이 있는 모험 정점만 예외이고, 그쪽은 OfAtLevel로 레벨을 직접 넘긴다.</summary>
    public static int OpponentLevelOf(int _cardId) => CardGrowth.BaseLevel;

    /// <summary>표시용 레벨. _mine=false는 상대 덱이고, 적은 강화되지 않아 바닥으로 나온다.</summary>
    public static int LevelOf(int _cardId, bool _mine = true)
    {
        if (_cardId <= 0) return CardGrowth.BaseLevel;

        return _mine ? CardGrowthManager.DisplayGrowthOf(_cardId).Level : OpponentLevelOf(_cardId);
    }

    public static int EvolutionStageOf(int _cardId, bool _mine = true)
        => CardGrowthManager.GrowthAtLevel(_cardId, LevelOf(_cardId, _mine)).EvolutionStage;

    /// <summary>표시용 시너지 해금 여부. 시너지는 1차 진화 레벨에서 열린다(GrowthRules.SynergyUnlockedAt) →
    /// 그 전 카드는 실제로 시너지에 참여하지 않으므로 카드 위에 배지·시너지용 배경판을 띄우면 오정보다.
    /// 전투 인스턴스에는 이미 같은 게이트가 CardInstance.synergyEnabled로 박혀 있다 — 그쪽이 있으면 그쪽이 이긴다.</summary>
    public static bool SynergyUnlockedOf(int _cardId, bool _mine = true)
        => _cardId > 0 && CardGrowthManager.GrowthAtLevel(_cardId, LevelOf(_cardId, _mine)).SynergyUnlocked;

    // 표시용 최대 체력. 내 카드는 내 강화 진행도, 상대 카드는 상대 레벨 기준이다.
    public static int MaxHpOf(int _cardId, bool _mine = true)
    {
        if (_cardId <= 0) return 0;

        CardGrowth t_growth = _mine
            ? CardGrowthManager.DisplayGrowthOf(_cardId)
            : CardGrowthManager.GrowthAtLevel(_cardId, LevelOf(_cardId, false));
        return CardCatalog.RequireSpec(_cardId).MaxHp + t_growth.HpBonus;
    }

    // 카드 1장의 파워(null은 0)
    public static int Of(int _cardId, bool _mine = true)
        => _cardId > 0 ? MaxHpOf(_cardId, _mine) : 0;

    /// <summary>지정한 레벨에 선 카드의 표시용 최대 체력. 레벨을 랭크 티어가 아니라 <b>저작이</b> 정하는
    /// 상대(모험 정점)를 위한 길이다 — LevelOf는 내 랭크를 읽으므로 그쪽으로 가면 정점마다 같은 수가 나온다.</summary>
    public static int MaxHpAtLevel(int _cardId, int _level)
        => _cardId > 0 ? CardCatalog.RequireSpec(_cardId).MaxHp + CardGrowthManager.GrowthAtLevel(_cardId, _level).HpBonus : 0;

    /// <summary>덱 전체가 지정한 레벨에 섰을 때의 파워 합. 정점의 권장 전투력이 여기서 나온다 —
    /// 난이도를 SO에 따로 저작하면 적 덱을 고칠 때마다 두 수가 어긋난다.</summary>
    public static int OfAtLevel(IReadOnlyList<int> _deck, int _level)
    {
        if (_deck == null) return 0;

        int t_sum = 0;
        for (int t_i = 0; t_i < _deck.Count; t_i++)
        {
            int t_card = _deck[t_i];
            if (t_card <= 0) continue;

            t_sum += MaxHpAtLevel(t_card, _level);
        }

        return t_sum;
    }

    // 덱 전체 파워 합
    public static int Of(IReadOnlyList<int> _deck, bool _mine = true)
    {
        if (_deck == null) return 0;

        int t_sum = 0;
        for (int t_i = 0; t_i < _deck.Count; t_i++)
            t_sum += Of(_deck[t_i], _mine);

        return t_sum;
    }
}
