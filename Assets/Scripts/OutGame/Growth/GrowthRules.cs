// 성장 규칙 상수. 예전에는 CardGrowthConfig(SO)가 들고 있었는데, 카드별 곡선이 스펙시트(hp2~hp4)로
// 옮겨간 뒤로 SO에 남은 건 카드와 무관한 상수뿐이라 코드로 내렸다.
// 서버(매치 검증)가 같은 값을 재계산해야 하므로, 인스펙터에서 흔들릴 수 있는 자리에 두면 안 된다 —
// 값이 갈리는 순간 정상 플레이가 검증 실패로 뜬다.
public static class GrowthRules
{
    /// <summary>강화 상한 레벨. 카드 곡선(CardSpec.hp2~hp4)이 여기까지만 저작된다.</summary>
    public const int MaxLevel = CardSpec.MaxHpCurveLevel;

    /// <summary>1차 진화 레벨. 도달하면 진화 단계 1 + 시너지 기능이 열린다.</summary>
    public const int FirstEvolutionLevel = 3;

    /// <summary>2차 진화 레벨. 도달하면 진화 단계 2 + 키워드 강화가 열린다.</summary>
    public const int SecondEvolutionLevel = 4;

    /// <summary>한계돌파 최대 단계. 강화와 별개 축의 덤이라 단계당 체력 +1로 얕게 둔다.</summary>
    public const int MaxLimitBreak = 3;

    const int LimitBreakHpPerStage = 1;

    // 강화 비용(레벨 N으로 올릴 때). 재화는 조각이고 성공률은 1이다.
    const int EnhanceCostStep = 25;

    /// <summary>레벨 _level에서의 진화 단계(0 = 미진화).</summary>
    public static int EvolutionStageAt(int _level)
    {
        int t_stage = 0;
        if (_level >= FirstEvolutionLevel) t_stage = 1;
        if (_level >= SecondEvolutionLevel) t_stage = 2;
        return t_stage > CardSpec.MaxEvolutionStage ? CardSpec.MaxEvolutionStage : t_stage;
    }

    /// <summary>레벨 _level로 올리는 것이 곧 진화인가 — 관문 숫자를 화면이 다시 적지 않게 여기서 답한다.</summary>
    public static bool IsEvolutionLevel(int _level) => EvolutionStageAt(_level) > EvolutionStageAt(_level - 1);

    /// <summary>1차 진화(= 시너지 기능 해금) 도달 여부.</summary>
    public static bool SynergyUnlockedAt(int _level) => _level >= FirstEvolutionLevel;

    /// <summary>레벨 _level에서 실제로 켜져 있는 카드 키워드. 기본 키워드에 더하는 값이 아니라 대체하는 값이다 —
    /// 키워드는 해금 전까지 아예 없는 것으로 친다(해금 레벨 미지정이면 처음부터 열려 있다).</summary>
    public static CardKeyword UnlockedKeywordsAt(int _cardId, int _level)
    {
        if (_cardId <= 0) return CardKeyword.None;

        CardSpec t_spec = CardCatalog.RequireSpec(_cardId);
        return _level >= t_spec.KeywordUnlockLevel ? t_spec.Keywords : CardKeyword.None;
    }

    /// <summary>레벨 _level까지의 누적 체력 가산분. 카드 곡선(hp2~hp4)이 유일한 출처라
    /// 미저작 카드는 강화해도 체력이 오르지 않는다.</summary>
    public static int HpBonusAt(int _cardId, int _level)
    {
        if (_level <= CardGrowth.BaseLevel) return 0;

        int t_top = _level > MaxLevel ? MaxLevel : _level;
        int t_sum = 0;
        for (int t_i = CardGrowth.BaseLevel + 1; t_i <= t_top; t_i++)
            t_sum += HpGainAt(_cardId, t_i);

        return t_sum;
    }

    /// <summary>한계돌파 _stage까지의 누적 체력 가산분.</summary>
    public static int LimitBreakHpBonusAt(int _stage)
    {
        int t_top = _stage < 0 ? 0 : _stage > MaxLimitBreak ? MaxLimitBreak : _stage;
        return t_top * LimitBreakHpPerStage;
    }

    /// <summary>한계돌파 한 단계의 비용·가산분. 간식 비용은 단계 수와 같다.</summary>
    public static bool TryGetLimitBreakStep(int _stage, out LimitBreakStep _step)
    {
        _step = default;
        if (_stage <= 0 || _stage > MaxLimitBreak) return false;

        _step = new LimitBreakStep(_stage, LimitBreakHpPerStage, _stage);
        return true;
    }

    /// <summary>레벨 _level로 올리는 한 스텝(범위 밖이면 false). 바닥 레벨은 강화로 도달하는 레벨이 아니다.</summary>
    public static bool TryGetStep(int _cardId, int _level, out GrowthStep _step)
    {
        _step = default;
        if (_level <= CardGrowth.BaseLevel || _level > MaxLevel) return false;

        _step = new GrowthStep(_level, HpGainAt(_cardId, _level), ECurrencyType.Shard, CostAt(_level), 1f);
        return true;
    }

    // 레벨 N으로 올리는 비용: 25 / 75 / 150 (= 25 × 1·3·6, 계단 누적)
    static long CostAt(int _level)
    {
        int t_step = _level - CardGrowth.BaseLevel;      // 1부터
        int t_units = t_step * (t_step + 1) / 2;         // 1, 3, 6 …
        return (long)EnhanceCostStep * t_units;
    }

    static int HpGainAt(int _cardId, int _level)
        => _cardId > 0 && CardCatalog.RequireSpec(_cardId).TryGetHpGain(_level, out int t_hp) ? t_hp : 0;
}

// 키워드 강화 규칙. 레벨당 체력 +1 고정이라 표가 필요 없다(재화는 에너지).
public static class KeywordGrowthRules
{
    public const int MaxLevel = 10;
    public const int HpPerLevel = 1;

    const long BaseCost = 5;
    const long CostGrowthPerLevel = 5;

    static readonly CardKeyword[] s_supported =
    {
        CardKeyword.Ranged,
        CardKeyword.Peerless,
        CardKeyword.Execution,
        CardKeyword.Taunt,
        CardKeyword.Cunning,
        CardKeyword.Healer,
    };

    public static CardKeyword[] SupportedKeywords => s_supported;

    public static bool Supports(CardKeyword _keyword)
    {
        if (!IsSingleKeyword(_keyword)) return false;

        for (int t_i = 0; t_i < s_supported.Length; t_i++)
            if (s_supported[t_i] == _keyword) return true;

        return false;
    }

    public static bool TryGetNextStep(CardKeyword _keyword, int _level, out GrowthStep _step)
    {
        _step = default;
        if (!Supports(_keyword) || _level < 0 || _level >= MaxLevel) return false;

        int t_nextLevel = _level + 1;
        long t_cost = BaseCost + CostGrowthPerLevel * (t_nextLevel - 1);
        if (t_cost < 0) t_cost = 0;

        _step = new GrowthStep(t_nextLevel, HpPerLevel, ECurrencyType.Energy, t_cost, 1f);
        return true;
    }

    static bool IsSingleKeyword(CardKeyword _keyword)
    {
        int t_value = (int)_keyword;
        return t_value > 0 && (t_value & (t_value - 1)) == 0;
    }
}

// 레벨 하나의 파생 스냅샷(GrowthRules가 곡선·비용에서 계산해 내주는 값)
public readonly struct GrowthStep
{
    public readonly int Level;
    public readonly int HpGain;
    // 성공·실패 무관하게 소모되는 재화와 그 양
    public readonly ECurrencyType Currency;
    public readonly long Cost;
    public readonly float SuccessRate;

    public GrowthStep(int _level, int _hpGain, ECurrencyType _currency, long _cost, float _successRate)
    {
        Level       = _level;
        HpGain      = _hpGain;
        Currency    = _currency;
        Cost        = _cost;
        SuccessRate = _successRate;
    }
}

// 한계돌파 한 단계의 값
public readonly struct LimitBreakStep
{
    public readonly int Stage;
    public readonly int HpGain;
    public readonly int SnackCost;

    public LimitBreakStep(int _stage, int _hpGain, int _snackCost)
    {
        Stage     = _stage;
        HpGain    = _hpGain;
        SnackCost = _snackCost;
    }
}
