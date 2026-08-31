// 성장 규칙. 수치의 진실원은 스펙시트(CardEnhanceRule / CardEnhance / CardLimitBreak)고 GrowthSpec이 읽는다 —
// 서버 functions/src/growth 가 같은 표를 재계산하므로 코드에 값을 박으면 저작이 바뀌는 순간 조용히 갈린다.
// 표를 못 읽으면 상한은 0, TryGet 계열은 false다(임의 기본값으로 버튼을 열어 주지 않는다).
public static class GrowthRules
{
    /// <summary>강화 상한 레벨. 카드 곡선(CardSpec.hp2~hp4)이 저작된 데까지만 열린다.</summary>
    public static int MaxLevel => GrowthSpec.CardMaxLevel;

    /// <summary>1차 진화 레벨. 도달하면 진화 단계 1 + 시너지 기능이 열린다.</summary>
    public const int FirstEvolutionLevel = 3;

    /// <summary>2차 진화 레벨. 도달하면 진화 단계 2 + 키워드 강화가 열린다.</summary>
    public const int SecondEvolutionLevel = 4;

    /// <summary>한계돌파 최대 단계. 표의 곡선이 단계 1부터 이어지는 데까지만 연다.</summary>
    public static int MaxLimitBreak => GrowthSpec.MaxLimitBreak;

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

        // 저작 상한이 아니라 코드 천장에서 자른다 — 서버 expectedHpBonus가 min(레벨, 천장)까지 합산하므로
        // 여기서 저작값으로 자르면 저장된 레벨과 체력이 서로 어긋난 스냅샷이 나온다.
        int t_top = _level > GrowthSpec.CardMaxLevelCeiling ? GrowthSpec.CardMaxLevelCeiling : _level;
        int t_sum = 0;
        for (int t_i = CardGrowth.BaseLevel + 1; t_i <= t_top; t_i++)
            t_sum += HpGainAt(_cardId, t_i);

        return t_sum;
    }

    /// <summary>한계돌파 _stage까지의 누적 체력 가산분.</summary>
    public static int LimitBreakHpBonusAt(int _stage)
        => _stage < 0 ? 0 : GrowthSpec.LimitBreakHpBonusAt(_stage);

    /// <summary>한계돌파 한 단계의 비용·가산분(곡선 밖이면 false).</summary>
    public static bool TryGetLimitBreakStep(int _stage, out LimitBreakStep _step)
        => GrowthSpec.TryGetLimitBreakStep(_stage, out _step);

    /// <summary>레벨 _level로 올리는 한 스텝(범위 밖이면 false). 바닥 레벨은 강화로 도달하는 레벨이 아니다.</summary>
    public static bool TryGetStep(int _cardId, int _level, out GrowthStep _step)
    {
        _step = default;
        if (!GrowthSpec.TryGetCardEnhanceCost(_level, out EnhanceCost t_cost)) return false;

        _step = new GrowthStep(_level, HpGainAt(_cardId, _level), t_cost.Currency, t_cost.Cost, t_cost.SuccessRate);
        return true;
    }

    static int HpGainAt(int _cardId, int _level)
        => _cardId > 0 && CardCatalog.RequireSpec(_cardId).TryGetHpGain(_level, out int t_hp) ? t_hp : 0;
}

// 키워드 강화 규칙. 상한·비용·재화는 스펙시트(KeywordEnhance)가 답하고 GrowthSpec이 읽는다.
// 레벨당 체력만 코드에 남는다 — 서버 덱 검증이 레벨당 1로 하드코딩돼 있어 표로 옮기면 그 계약까지 같이 바뀐다.
public static class KeywordGrowthRules
{
    public const int HpPerLevel = 1;

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

    /// <summary>키워드 _keyword의 강화 상한 레벨(행이 없으면 0 — 그 키워드는 강화가 열리지 않는다).</summary>
    public static int MaxLevelOf(CardKeyword _keyword)
        => Supports(_keyword) ? GrowthSpec.KeywordMaxLevelOf(_keyword) : 0;

    /// <summary>세이브·서버 응답에서 읽은 레벨을 캐시에 담기 전에 조인다. 저작 상한이 아니라 늘 코덱 천장으로만
    /// 조인다 — 서버 readKeywordLevels도 같은 자리에서 표를 보지 않는다. 저작이 상한을 낮췄다고 클라만 레벨을
    /// 깎으면 서버가 아는 진행도와 갈린다.</summary>
    public static int ClampSavedLevel(CardKeyword _keyword, int _level)
    {
        if (_level <= 0 || !Supports(_keyword)) return 0;

        return _level > GrowthSpec.KeywordMaxLevelCeiling ? GrowthSpec.KeywordMaxLevelCeiling : _level;
    }

    public static bool TryGetNextStep(CardKeyword _keyword, int _level, out GrowthStep _step)
    {
        _step = default;
        if (!Supports(_keyword) || !GrowthSpec.TryGetKeywordEnhanceCost(_keyword, _level, out EnhanceCost t_cost))
            return false;

        _step = new GrowthStep(_level + 1, HpPerLevel, t_cost.Currency, t_cost.Cost, t_cost.SuccessRate);
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
