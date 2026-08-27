using System.Collections.Generic;
using UnityEngine;

// 카드 강화 튜닝 데이터
[CreateAssetMenu(fileName = "CardGrowthConfig", menuName = "Card Battle/Card Growth Config")]
public class CardGrowthConfig : ScriptableObject
{
    [Header("전역 기본식 (시트·레벨 오버라이드가 모두 없을 때 적용)")]
    [Tooltip("강화 상한 레벨. 미강화가 Lv1이므로 강화 횟수는 이 값 - 1이다.")]
    [Min(CardGrowth.BaseLevel)] [SerializeField] int maxLevel = 4;
    [Min(0)] [SerializeField] int hpPerLevel = 4;

    [Tooltip("첫 강화(Lv2로 올릴 때)의 비용. 단위는 기본 재화(골드)다.")]
    [UnityEngine.Serialization.FormerlySerializedAs("baseGoldCost")]
    [SerializeField] long baseEnhanceCost = 25;

    [Tooltip("레벨마다 늘어나는 비용. 레벨 N 비용 = baseEnhanceCost + (N-2) * 이 값.")]
    [SerializeField] long costGrowthPerLevel = 50;
    [Range(0f, 1f)] [SerializeField] float baseSuccessRate = 1f;
    [Range(0f, 1f)] [SerializeField] float rateDropPerLevel = 0f;

    [Header("진화 레벨 (전역 — 카드 SO에 적지 않는다)")]
    // 키워드 해금 레벨은 여기 없다. 카드마다 다르므로 CardSpec.KeywordUnlockLevel이 소유한다.
    [Tooltip("1차 진화 레벨. 도달하면 진화 단계 1 + 시너지 기능이 열린다.")]
    [Min(CardGrowth.BaseLevel)] [SerializeField] int firstEvolutionLevel = 3;

    [Tooltip("2차 진화 레벨. 도달하면 진화 단계 2 + 키워드 강화.")]
    [Min(CardGrowth.BaseLevel)] [SerializeField] int secondEvolutionLevel = 4;

    [Header("레벨별 상세 (비어 있는 레벨은 위 기본식으로 계산)")]
    [Tooltip("레벨 하나하나의 체력 증가·비용·성공률. " +
             "비용·재화·성공률의 진실원은 CardEnhance 시트다 — 시트에 그 레벨 행이 있으면 여기 값은 무시된다. " +
             "여기는 시트를 못 읽었거나 시트에 없는 레벨의 폴백이다. " +
             "체력은 Card 시트의 hp2~hp4가 이긴다. 여기 hpGain은 그 세 칸이 전부 0인 카드에만 쓰인다.")]
    [SerializeField] List<GrowthLevelStep> levelSteps = new List<GrowthLevelStep>();

    // 한계돌파는 강화와 별개 축의 **덤**이다 — 단계당 +1로 얕게 둔다(주 성장 수단은 강화 곡선).
    [Header("한계돌파")]
    [Min(0)] [SerializeField] int maxLimitBreak = 3;
    [SerializeField] List<LimitBreakStep> limitBreakSteps = new List<LimitBreakStep>
    {
        new LimitBreakStep(1, 1, 1),
        new LimitBreakStep(2, 1, 2),
        new LimitBreakStep(3, 1, 3),
    };

    // 강화 상한 레벨(바닥 아래 오설정은 바닥으로 보정 = 강화 없음)
    public int MaxLevel => maxLevel < CardGrowth.BaseLevel ? CardGrowth.BaseLevel : maxLevel;

    public int FirstEvolutionLevel  => firstEvolutionLevel;
    public int SecondEvolutionLevel => secondEvolutionLevel;
    public int MaxLimitBreak => Mathf.Max(0, maxLimitBreak);

    public bool TryGetLimitBreakStep(int _stage, out LimitBreakStep _step)
    {
        _step = default;
        if (_stage <= 0 || _stage > MaxLimitBreak) return false;

        if (limitBreakSteps != null)
            for (int t_i = 0; t_i < limitBreakSteps.Count; t_i++)
            {
                LimitBreakStep t_row = limitBreakSteps[t_i];
                if (t_row.Stage != _stage) continue;

                _step = new LimitBreakStep(_stage, Mathf.Max(0, t_row.HpGain), Mathf.Max(1, t_row.SnackCost));
                return true;
            }

        // 기존 설정 에셋에 신규 필드가 없어도 기본 곡선으로 동작한다(위 저작 기본값과 같은 +1).
        _step = new LimitBreakStep(_stage, 1, _stage);
        return true;
    }

    public int LimitBreakHpBonusAt(int _stage)
    {
        int t_top = Mathf.Clamp(_stage, 0, MaxLimitBreak);
        int t_sum = 0;
        for (int t_i = 1; t_i <= t_top; t_i++)
            if (TryGetLimitBreakStep(t_i, out LimitBreakStep t_step)) t_sum += t_step.HpGain;

        return t_sum;
    }

    /// <summary>레벨 _level에서의 진화 단계(0=미진화). 관문을 거꾸로 저작해도 도달한 것 중 높은 단계를 준다.</summary>
    public int EvolutionStageAt(int _level)
    {
        int t_stage = 0;
        if (_level >= firstEvolutionLevel)  t_stage = 1;
        if (_level >= secondEvolutionLevel) t_stage = 2;
        return t_stage > CardSpec.MaxEvolutionStage ? CardSpec.MaxEvolutionStage : t_stage;
    }

    /// <summary>레벨 _level로 올리는 것이 곧 진화인가 — 관문 숫자를 화면이 다시 적지 않게 여기서 답한다.</summary>
    public bool IsEvolutionLevel(int _level) => EvolutionStageAt(_level) > EvolutionStageAt(_level - 1);

    /// <summary>레벨 _level에서 실제로 켜져 있는 카드 키워드. 기본 키워드에 더하는 값이 아니라 대체하는 값이다 —
    /// 키워드는 해금 전까지 아예 없는 것으로 친다(해금 레벨 미지정이면 처음부터 열려 있다).</summary>
    public CardKeyword UnlockedKeywordsAt(int _cardId, int _level)
    {
        if (_cardId <= 0) return CardKeyword.None;
        CardSpec t_spec = CardCatalog.RequireSpec(_cardId);
        return _level >= t_spec.KeywordUnlockLevel ? t_spec.Keywords : CardKeyword.None;
    }

    /// <summary>1차 진화(= 시너지 기능 해금) 도달 여부.</summary>
    public bool SynergyUnlockedAt(int _level) => _level >= firstEvolutionLevel;

    // 레벨 _level로 올리는 한 스텝(범위 밖이면 false). 바닥 레벨은 강화로 도달하는 레벨이 아니다.
    public bool TryGetStep(int _cardId, int _level, out GrowthStep _step)
    {
        _step = default;
        if (_level <= CardGrowth.BaseLevel || _level > MaxLevel) return false;

        _step = StepAt(_cardId, _level);
        return true;
    }

    // 레벨 _level까지의 누적 HP 보너스
    public int HpBonusAt(int _cardId, int _level)
    {
        if (_level <= CardGrowth.BaseLevel) return 0;

        int t_top = _level > MaxLevel ? MaxLevel : _level;
        int t_sum = 0;
        for (int t_i = CardGrowth.BaseLevel + 1; t_i <= t_top; t_i++)
        {
            t_sum += StepAt(_cardId, t_i).HpGain;
        }
        return t_sum;
    }

    GrowthStep StepAt(int _cardId, int _level)
    {
        // 첫 강화(바닥 바로 위)가 곡선의 0번째 칸이다 — 그래야 baseEnhanceCost·baseSuccessRate가 첫 강화의 값이 된다.
        int t_step = _level - CardGrowth.BaseLevel - 1;

        int           t_hp       = hpPerLevel;
        long          t_cost     = baseEnhanceCost + costGrowthPerLevel * t_step;
        float         t_rate     = baseSuccessRate - rateDropPerLevel * t_step;
        ECurrencyType t_currency = ECurrencyType.Gold;       // 기본식에는 재화 축이 없다

        // 체력·재화는 행이 있으면 무조건 그 값이고, 비용·성공률만 미지정(0 이하 / 음수)을 기본식으로 되돌린다.
        if (TryGetLevelStep(_level, out var t_row))
        {
            t_hp       = t_row.hpGain;
            t_currency = t_row.costCurrency;
            if (t_row.cost        > 0)    t_cost = t_row.cost;
            if (t_row.successRate >= 0f)  t_rate = t_row.successRate;
        }

        // 비용·성공률의 진실원은 시트다 — 위 저작값은 시트에 없는 레벨의 폴백으로만 남는다.
        if (EnhanceSpec.TryGetStep(_level, out EnhanceStepSpec t_sheet))
        {
            t_currency = t_sheet.Currency;
            t_cost     = t_sheet.Cost;
            t_rate     = t_sheet.SuccessRate;
        }

        if (_cardId > 0 && CardCatalog.RequireSpec(_cardId).TryGetHpGain(_level, out int t_cardHp))
            t_hp = t_cardHp;

        if (t_cost < 0) t_cost = 0;
        if (t_hp < 0)   t_hp   = 0;

        return new GrowthStep(_level, t_hp, t_currency, t_cost, Mathf.Clamp01(t_rate));
    }

    bool TryGetLevelStep(int _level, out GrowthLevelStep _row)
    {
        _row = default;
        if (levelSteps == null) return false;

        for (int t_i = 0; t_i < levelSteps.Count; t_i++)
        {
            if (levelSteps[t_i].level != _level) continue;

            _row = levelSteps[t_i];
            return true;
        }
        return false;
    }
}

/// <summary>레벨 하나의 저작 값. 한 칸씩 손으로 정하는 게 목적이라 override 체크를 두지 않는다 —
/// 행이 있으면 체력·재화는 무조건 이 값이고, 비용·성공률만 미지정을 허용한다.</summary>
[System.Serializable]
public struct GrowthLevelStep
{
    [Tooltip("대상 레벨(2 = 첫 강화). 바닥 레벨(1) 이하는 어떤 스텝에도 적용되지 않는다.")]
    public int level;

    [Min(0)] [Tooltip("이 레벨업으로 얻는 최대 체력 가산분.")]
    public int hpGain;

    [Tooltip("이 레벨업에 소모할 재화. 비용과 달리 '미지정'이 없다 — 행이 있으면 이 값이 그대로 쓰인다. " +
             "레벨마다 결제 재화를 손으로 갈라 놓고 싶을 때 쓰는 칸이다.")]
    public ECurrencyType costCurrency;

    [Tooltip("이 레벨업의 비용. 0 이하면 기본식을 쓴다. 단위는 위 재화다(기본식은 항상 골드).")]
    public long cost;

    [Range(-1f, 1f)] [Tooltip("이 레벨업의 성공률(0~1). 음수면 기본식을 쓴다. 실패해도 비용만 소모되고 레벨은 유지된다.")]
    public float successRate;
}

// 레벨 하나의 파생 스냅샷(CardGrowthConfig가 기본식+오버라이드로 계산해 내주는 값)
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

[System.Serializable]
public struct LimitBreakStep
{
    [Min(1)] [SerializeField] int stage;
    [Min(0)] [SerializeField] int hpGain;
    [Min(1)] [SerializeField] int snackCost;

    public int Stage => stage;
    public int HpGain => hpGain;
    public int SnackCost => snackCost;

    public LimitBreakStep(int _stage, int _hpGain, int _snackCost)
    {
        stage = _stage;
        hpGain = _hpGain;
        snackCost = _snackCost;
    }
}
