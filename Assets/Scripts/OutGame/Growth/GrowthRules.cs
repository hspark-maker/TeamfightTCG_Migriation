// 성장 규칙. 수치의 진실원은 스펙시트(CardEnhanceRule / CardEnhance)고 GrowthSpec이 읽는다 —
// 서버 functions/src/growth 가 같은 표를 재계산하므로 코드에 값을 박으면 저작이 바뀌는 순간 조용히 갈린다.
// 표를 못 읽으면 상한은 0, TryGet 계열은 false다(임의 기본값으로 버튼을 열어 주지 않는다).
public static class GrowthRules
{
    /// <summary>강화 상한 레벨. 카드 곡선(CardSpec.hp2~hp4)이 저작된 데까지만 열린다.</summary>
    public static int MaxLevel => GrowthSpec.CardMaxLevel;

    /// <summary>1차 진화 레벨. 도달하면 진화 단계 1 + 시너지 기능이 열린다.</summary>
    public const int FirstEvolutionLevel = 3;

    /// <summary>2차 진화 레벨. 도달하면 진화 단계 2가 열린다.</summary>
    public const int SecondEvolutionLevel = 4;

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

    /// <summary>현재 레벨에서 다음 별까지 필요한 샤드 총량. 마지막 별은 0이다.</summary>
    public static int ShardRequiredAt(int _level)
    {
        if (!GrowthSpec.TryGetCardEnhanceCost(_level + 1, out EnhanceCost t_cost)) return 0;
        return (int)System.Math.Min(int.MaxValue, System.Math.Max(1L, t_cost.Cost));
    }

    public static int ClampShardProgress(int _level, int _progress)
        => System.Math.Max(0, System.Math.Min(_progress, ShardRequiredAt(_level) - 1));

    /// <summary>별 사이에 투자한 비율만큼 체력을 가산한다. 서버와 동일하게 정수 미만은 버린다.</summary>
    public static int ShardHpBonusAt(int _cardId, int _level, int _progress)
    {
        int t_required = ShardRequiredAt(_level);
        if (t_required <= 0) return 0;
        return (int)((long)HpGainAt(_cardId, _level + 1) * ClampShardProgress(_level, _progress) / t_required);
    }

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
