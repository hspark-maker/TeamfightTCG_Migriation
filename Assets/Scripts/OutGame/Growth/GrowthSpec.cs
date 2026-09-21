using System;
using System.Collections.Generic;

/// <summary>강화 한 번의 결제 조건. 체력 증가분은 카드 곡선이 따로 답하므로 여기 없다.</summary>
public readonly struct EnhanceCost
{
    public readonly ECurrencyType Currency;
    public readonly long Cost;
    public readonly float SuccessRate;

    public EnhanceCost(ECurrencyType _currency, long _cost, float _successRate)
    {
        Currency    = _currency;
        Cost        = _cost;
        SuccessRate = _successRate;
    }
}

// 카드 강화 수치의 스펙시트 조회 창구.
// 서버 functions/src/growth 가 같은 표를 같은 규약으로 읽는다 — 여기 공식이 저쪽과 다르면 그건 버그다.
// 표를 못 읽으면 "규칙 없음"으로 남는다. 임의 기본값을 내주면 안 된다 — 서버가 RuleUnavailable로 거절하는
// 자리라 클라만 버튼을 열어 주면 눌러도 항상 실패하는 버튼이 된다.
public static class GrowthSpec
{
    /// <summary>카드 강화 상한의 천장. 체력 곡선(hp2~hp4)이 여기까지만 저작돼 표가 더 큰 값을 말해도 자른다.</summary>
    public const int CardMaxLevelCeiling = CardSpec.MaxHpCurveLevel;

    const ECurrencyType CardFallbackCurrency    = ECurrencyType.Shard;

    static bool s_loaded;
    static bool s_hasCardRule;
    static int  s_cardMaxLevel;
    static long s_baseEnhanceCost;
    static long s_costGrowthPerLevel;

    static readonly Dictionary<int, EnhanceCost> s_cardCosts = new Dictionary<int, EnhanceCost>();

    /// <summary>카드 강화 상한 레벨. 규칙을 못 읽었으면 0이다.</summary>
    public static int CardMaxLevel { get { EnsureLoaded(); return s_cardMaxLevel; } }

    // 초기화에서 1회. 지연 로드도 되지만 카드 상세 진입 프레임에 색인이 걸리지 않게 미리 당긴다.
    public static void Init() => EnsureLoaded();

    /// <summary>레벨 _level로 올리는 카드 강화의 결제 조건. 규칙이 없거나 범위 밖이면 false.</summary>
    public static bool TryGetCardEnhanceCost(int _level, out EnhanceCost _cost)
    {
        EnsureLoaded();
        _cost = default;
        if (!s_hasCardRule || _level <= CardGrowth.BaseLevel || _level > s_cardMaxLevel) return false;

        if (s_cardCosts.TryGetValue(_level, out _cost)) return true;

        int t_steps   = _level - CardGrowth.BaseLevel - 1;
        long t_amount = s_baseEnhanceCost + t_steps * s_costGrowthPerLevel;
        _cost = new EnhanceCost(CardFallbackCurrency, t_amount > 0 ? t_amount : 0, 1f);
        return true;
    }

    /// <summary>전투가 설 수 있는 최소 저작인가. 초기화가 이걸 보고 복구 화면으로 보낸다 — 서버 lockDeck이
    /// 카드 강화 규칙을 못 읽으면 덱 잠금 전체를 거절하므로, 그 상태로 로비까지 들여보내면
    /// 안내도 없이 전투 진입에서 막힌다.</summary>
    public static bool TryValidateRequired(out string _error)
    {
        EnsureLoaded();

        if (!s_hasCardRule)
        {
            _error = "CardEnhanceRule 표에서 규칙 행(maxLevel > 1)을 읽지 못했다 — 카드 강화·진화가 막히고 덱 잠금도 거절된다.";
            return false;
        }

        _error = null;
        return true;
    }

    static void EnsureLoaded()
    {
        if (s_loaded) return;
        s_loaded = true;   // 실패해도 매 조회마다 재파싱하지 않는다(규칙 없음으로 계속 돈다).

        SpecDataManager t_manager = SpecSource.Manager;
        if (t_manager == null) return;   // 못 읽은 경고는 SpecSource가 이미 냈다.

        LoadCardRule(t_manager);
        LoadCardCosts(t_manager);
    }

    static void LoadCardRule(SpecDataManager _manager)
    {
        IReadOnlyList<CardEnhanceRule> t_source = _manager.CardEnhanceRule?.All;
        List<CardEnhanceRule> t_rows = ByIdAscending(t_source, _row => _row.id);
        if (t_rows.Count == 0) return;

        CardEnhanceRule t_rule = t_rows[0];
        if (t_rule.maxLevel <= CardGrowth.BaseLevel) return;

        s_hasCardRule              = true;
        s_cardMaxLevel             = t_rule.maxLevel > CardMaxLevelCeiling ? CardMaxLevelCeiling : t_rule.maxLevel;
        s_baseEnhanceCost          = t_rule.baseEnhanceCost;
        s_costGrowthPerLevel       = t_rule.costGrowthPerLevel;
    }

    static void LoadCardCosts(SpecDataManager _manager)
    {
        IReadOnlyList<CardEnhance> t_source = _manager.CardEnhance?.All;
        foreach (CardEnhance t_row in ByIdAscending(t_source, _row => _row.id))
        {
            if (t_row.level <= CardGrowth.BaseLevel || s_cardCosts.ContainsKey(t_row.level)) continue;

            s_cardCosts[t_row.level] = new EnhanceCost(
                ParseCurrency(t_row.costCurrency, CardFallbackCurrency),
                t_row.cost > 0 ? t_row.cost : 0,
                1f); // 카드 강화는 확정 성공이며, 이전 산출물의 확률 열도 읽지 않는다.
        }
    }

    // 행 순서를 가정하지 않는다 — 같은 키가 둘이면 id가 작은 행이 이긴다는 규약(서버와 같다)을 여기서 세운다.
    static List<T> ByIdAscending<T>(IReadOnlyList<T> _rows, Func<T, int> _id) where T : class
    {
        var t_sorted = new List<T>();
        if (_rows == null) return t_sorted;

        for (int t_i = 0; t_i < _rows.Count; t_i++)
            if (_rows[t_i] != null) t_sorted.Add(_rows[t_i]);

        t_sorted.Sort((_a, _b) => _id(_a).CompareTo(_id(_b)));
        return t_sorted;
    }

    // 못 읽으면 축의 기본 재화로만 떨어진다 — 여기서 Gold로 폴백하면 조각으로 표시된 강화가 골드를 문다.
    static ECurrencyType ParseCurrency(string _value, ECurrencyType _fallback)
    {
        string t_token = _value == null ? string.Empty : _value.Trim();
        if (t_token.Length == 0 || char.IsDigit(t_token[0])) return _fallback;

        return Enum.TryParse(t_token, true, out ECurrencyType t_currency)
               && t_currency != ECurrencyType.Count && Enum.IsDefined(typeof(ECurrencyType), t_currency)
            ? t_currency
            : _fallback;
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_loaded                   = false;
        s_hasCardRule              = false;
        s_cardMaxLevel             = 0;
        s_baseEnhanceCost          = 0;
        s_costGrowthPerLevel       = 0;
        s_cardCosts.Clear();
    }
}
