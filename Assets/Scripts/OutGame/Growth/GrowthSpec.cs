using System;
using System.Collections.Generic;

/// <summary>강화 한 번의 결제 조건. 체력 증가분은 카드 곡선·키워드 규칙이 따로 답하므로 여기 없다.</summary>
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

// 성장 수치(카드 강화·한계돌파·키워드 강화)의 스펙시트 조회 창구.
// 서버 functions/src/growth 가 같은 표를 같은 규약으로 읽는다 — 여기 공식이 저쪽과 다르면 그건 버그다.
// 표를 못 읽으면 "규칙 없음"으로 남는다. 임의 기본값을 내주면 안 된다 — 서버가 RuleUnavailable로 거절하는
// 자리라 클라만 버튼을 열어 주면 눌러도 항상 실패하는 버튼이 된다.
public static class GrowthSpec
{
    /// <summary>카드 강화 상한의 천장. 체력 곡선(hp2~hp4)이 여기까지만 저작돼 표가 더 큰 값을 말해도 자른다.</summary>
    public const int CardMaxLevelCeiling = CardSpec.MaxHpCurveLevel;

    // 한계돌파 단계의 천장(서버 LIMIT_BREAK_STAGE_CEILING과 같다). 상한 자체는 MaxLimitBreak이 답하므로 밖으로 열지 않는다.
    const int LimitBreakStageCeiling = 3;

    /// <summary>키워드 강화 레벨의 천장. 세이브 코덱 상한이라 그 위 레벨은 결제가 헛돈다.</summary>
    public const int KeywordMaxLevelCeiling = 10;

    const int Permille = 1000;
    const ECurrencyType CardFallbackCurrency    = ECurrencyType.Shard;
    const ECurrencyType KeywordFallbackCurrency = ECurrencyType.Energy;

    static bool s_loaded;
    static bool s_hasCardRule;
    static int  s_cardMaxLevel;
    static long s_baseEnhanceCost;
    static long s_costGrowthPerLevel;
    static int  s_baseSuccessPermille;
    static int  s_rateDropPerLevelPermille;
    static int  s_ruleMaxLimitBreak;
    static int  s_maxLimitBreak;

    static readonly Dictionary<int, EnhanceCost> s_cardCosts = new Dictionary<int, EnhanceCost>();
    static readonly Dictionary<int, LimitBreakStep> s_limitBreakSteps = new Dictionary<int, LimitBreakStep>();
    static readonly Dictionary<CardKeyword, KeywordCostCurve> s_keywordCurves = new Dictionary<CardKeyword, KeywordCostCurve>();

    /// <summary>카드 강화 상한 레벨. 규칙을 못 읽었으면 0이다.</summary>
    public static int CardMaxLevel { get { EnsureLoaded(); return s_cardMaxLevel; } }

    /// <summary>한계돌파 최대 단계. 곡선이 끊긴 지점까지만 센다(그 위 단계는 통째로 버린다).</summary>
    public static int MaxLimitBreak { get { EnsureLoaded(); return s_maxLimitBreak; } }

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
        _cost = new EnhanceCost(CardFallbackCurrency, t_amount > 0 ? t_amount : 0,
                                RateOf(s_baseSuccessPermille - t_steps * s_rateDropPerLevelPermille));
        return true;
    }

    /// <summary>한계돌파 _stage 한 단계의 가산분·간식 비용. 곡선 밖이면 false.</summary>
    public static bool TryGetLimitBreakStep(int _stage, out LimitBreakStep _step)
    {
        EnsureLoaded();
        _step = default;
        return _stage > 0 && _stage <= s_maxLimitBreak && s_limitBreakSteps.TryGetValue(_stage, out _step);
    }

    /// <summary>한계돌파 _stage까지의 체력 가산 누적합(표의 hpGain은 그 단계에서 더해지는 몫이다).</summary>
    public static int LimitBreakHpBonusAt(int _stage)
    {
        EnsureLoaded();

        int t_top = _stage > s_maxLimitBreak ? s_maxLimitBreak : _stage;
        int t_sum = 0;
        for (int t_i = 1; t_i <= t_top; t_i++)
            if (s_limitBreakSteps.TryGetValue(t_i, out LimitBreakStep t_step)) t_sum += t_step.HpGain;

        return t_sum;
    }

    /// <summary>키워드 _keyword의 강화 상한 레벨. 행이 없으면 0 — 그 키워드는 강화가 열리지 않는다.</summary>
    public static int KeywordMaxLevelOf(CardKeyword _keyword)
    {
        EnsureLoaded();
        return s_keywordCurves.TryGetValue(_keyword, out KeywordCostCurve t_curve) ? t_curve.MaxLevel : 0;
    }

    /// <summary>레벨 _level에서 한 단계 올리는 키워드 강화의 결제 조건. 확률 실패가 없어 성공률은 항상 1이다.</summary>
    public static bool TryGetKeywordEnhanceCost(CardKeyword _keyword, int _level, out EnhanceCost _cost)
    {
        EnsureLoaded();
        _cost = default;
        if (_level < 0 || !s_keywordCurves.TryGetValue(_keyword, out KeywordCostCurve t_curve) || _level >= t_curve.MaxLevel)
            return false;

        long t_amount = t_curve.BaseCost + _level * t_curve.CostGrowthPerLevel;
        _cost = new EnhanceCost(t_curve.Currency, t_amount > 0 ? t_amount : 0, 1f);
        return true;
    }

    /// <summary>전투가 설 수 있는 최소 저작인가. 초기화가 이걸 보고 복구 화면으로 보낸다 — 서버 lockDeck이
    /// 카드 강화 규칙이나 한계돌파 곡선을 못 읽으면 덱 잠금 전체를 거절하므로, 그 상태로 로비까지 들여보내면
    /// 안내도 없이 전투 진입에서 막힌다. KeywordEnhance는 여기 없다 — 서버도 키워드 강화만 거절하고 전투는 세운다.</summary>
    public static bool TryValidateRequired(out string _error)
    {
        EnsureLoaded();

        if (!s_hasCardRule)
        {
            _error = "CardEnhanceRule 표에서 규칙 행(maxLevel > 1)을 읽지 못했다 — 카드 강화·진화·한계돌파가 전부 막히고 덱 잠금도 거절된다.";
            return false;
        }

        if (s_ruleMaxLimitBreak <= 0)
        {
            _error = "CardEnhanceRule.maxLimitBreak가 0이다 — 서버 lockDeck이 한계돌파 규칙을 쓸 수 없다며 덱 잠금을 거절한다.";
            return false;
        }

        if (s_maxLimitBreak <= 0)
        {
            _error = "CardLimitBreak 표에서 단계 1을 읽지 못했다(곡선 없음) — 서버 lockDeck이 덱 잠금을 거절한다.";
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
        LoadLimitBreakCurve(t_manager);
        LoadKeywordCurves(t_manager);
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
        s_baseSuccessPermille      = ClampPermille(t_rule.baseSuccessPermille);
        s_rateDropPerLevelPermille = t_rule.rateDropPerLevelPermille;

        // 한계돌파 열이 비었다고 규칙 전체를 버리지 않는다 — 0은 "그 축이 닫혀 있다"이지 강화 불가가 아니다.
        int t_maxLimitBreak = t_rule.maxLimitBreak > 0 ? t_rule.maxLimitBreak : 0;
        s_ruleMaxLimitBreak = t_maxLimitBreak > LimitBreakStageCeiling ? LimitBreakStageCeiling : t_maxLimitBreak;
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
                RateOf(t_row.successPermille));
        }
    }

    static void LoadLimitBreakCurve(SpecDataManager _manager)
    {
        if (s_ruleMaxLimitBreak <= 0) return;

        IReadOnlyList<CardLimitBreak> t_source = _manager.CardLimitBreak?.All;
        foreach (CardLimitBreak t_row in ByIdAscending(t_source, _row => _row.id))
        {
            if (t_row.stage <= 0 || t_row.stage > s_ruleMaxLimitBreak || s_limitBreakSteps.ContainsKey(t_row.stage)) continue;

            s_limitBreakSteps[t_row.stage] = new LimitBreakStep(
                t_row.stage,
                t_row.hpGain > 0 ? t_row.hpGain : 0,
                t_row.snackCost > 1 ? t_row.snackCost : 1);
        }

        // 연속성 검사가 fail-closed다: hpGain이 누적이라 중간에 구멍이 나면 그 위 단계의 합이 뜻을 잃는다.
        int t_continuous = 0;
        while (s_limitBreakSteps.ContainsKey(t_continuous + 1)) t_continuous++;

        for (int t_stage = t_continuous + 1; t_stage <= s_ruleMaxLimitBreak; t_stage++)
            s_limitBreakSteps.Remove(t_stage);

        s_maxLimitBreak = t_continuous;
    }

    static void LoadKeywordCurves(SpecDataManager _manager)
    {
        IReadOnlyList<KeywordEnhance> t_source = _manager.KeywordEnhance?.All;
        foreach (KeywordEnhance t_row in ByIdAscending(t_source, _row => _row.id))
        {
            if (!TryParseKeyword(t_row.keyword, out CardKeyword t_keyword)) continue;

            // 지원 목록 밖 키워드 행은 버린다 — 클라가 못 읽는 키에 레벨이 붙으면 다음 저장에서 진행도가 사라진다.
            if (!KeywordGrowthRules.Supports(t_keyword) || s_keywordCurves.ContainsKey(t_keyword)) continue;

            int t_maxLevel = t_row.maxLevel < 1 ? 1 : t_row.maxLevel;
            s_keywordCurves[t_keyword] = new KeywordCostCurve(
                t_maxLevel > KeywordMaxLevelCeiling ? KeywordMaxLevelCeiling : t_maxLevel,
                t_row.baseCost,
                t_row.costGrowthPerLevel,
                ParseCurrency(t_row.costCurrency, KeywordFallbackCurrency));
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

    static bool TryParseKeyword(string _value, out CardKeyword _keyword)
    {
        _keyword = CardKeyword.None;

        string t_token = _value == null ? string.Empty : _value.Trim();
        if (t_token.Length == 0 || char.IsDigit(t_token[0])) return false;

        return Enum.TryParse(t_token, true, out _keyword)
               && _keyword != CardKeyword.None && Enum.IsDefined(typeof(CardKeyword), _keyword);
    }

    static int ClampPermille(int _permille) => _permille < 0 ? 0 : _permille > Permille ? Permille : _permille;

    static float RateOf(int _permille) => ClampPermille(_permille) / (float)Permille;

    readonly struct KeywordCostCurve
    {
        public readonly int MaxLevel;
        public readonly long BaseCost;
        public readonly long CostGrowthPerLevel;
        public readonly ECurrencyType Currency;

        public KeywordCostCurve(int _maxLevel, long _baseCost, long _costGrowthPerLevel, ECurrencyType _currency)
        {
            MaxLevel           = _maxLevel;
            BaseCost           = _baseCost;
            CostGrowthPerLevel = _costGrowthPerLevel;
            Currency           = _currency;
        }
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_loaded                   = false;
        s_hasCardRule              = false;
        s_cardMaxLevel             = 0;
        s_baseEnhanceCost          = 0;
        s_costGrowthPerLevel       = 0;
        s_baseSuccessPermille      = 0;
        s_rateDropPerLevelPermille = 0;
        s_ruleMaxLimitBreak        = 0;
        s_maxLimitBreak            = 0;
        s_cardCosts.Clear();
        s_limitBreakSteps.Clear();
        s_keywordCurves.Clear();
    }
}
