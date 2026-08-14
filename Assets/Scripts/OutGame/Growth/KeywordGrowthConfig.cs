using UnityEngine;

[CreateAssetMenu(fileName = "KeywordGrowthConfig", menuName = "Card Battle/Keyword Growth Config")]
public class KeywordGrowthConfig : ScriptableObject
{
    [SerializeField] CardKeyword[] supportedKeywords =
    {
        CardKeyword.Ranged,
        CardKeyword.Peerless,
        CardKeyword.Execution,
        CardKeyword.Taunt,
        CardKeyword.Cunning,
        CardKeyword.Healer,
    };

    [Min(1)] [SerializeField] int maxLevel = 10;
    [Min(1)] [SerializeField] int hpPerLevel = 1;
    // 키워드 강화의 재화는 에너지다(카드 강화는 골드 — CardGrowthConfig).
    // 이름을 재화 중립으로 바꾸되 FormerlySerializedAs로 기존 에셋 값을 그대로 물려받는다.
    [Min(0)] [UnityEngine.Serialization.FormerlySerializedAs("baseGoldCost")]
    [SerializeField] long baseCost = 5;
    [Min(0)] [SerializeField] long costGrowthPerLevel = 5;

    public int MaxLevel => maxLevel < 1 ? 1 : maxLevel;
    public int HpPerLevel => hpPerLevel < 1 ? 1 : hpPerLevel;
    public CardKeyword[] SupportedKeywords => supportedKeywords;

    public bool Supports(CardKeyword _keyword)
    {
        if (!IsSingleKeyword(_keyword) || supportedKeywords == null) return false;

        for (int t_i = 0; t_i < supportedKeywords.Length; t_i++)
            if (supportedKeywords[t_i] == _keyword) return true;

        return false;
    }

    public bool TryGetNextStep(CardKeyword _keyword, int _level, out GrowthStep _step)
    {
        _step = default;
        if (!Supports(_keyword) || _level < 0 || _level >= MaxLevel) return false;

        int t_nextLevel = _level + 1;
        long t_cost = baseCost + costGrowthPerLevel * (t_nextLevel - 1);
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
