using System;
using System.Collections.Generic;
using UnityEngine;

// 키워드 강화(키워드 한 종류의 레벨)의 static 단일 창구. 그 키워드를 가진 모든 카드에 체력으로 얹힌다.
public static class KeywordGrowthManager
{
    static readonly Dictionary<CardKeyword, KeywordGrowthEntry> s_growth =
        new Dictionary<CardKeyword, KeywordGrowthEntry>();

    static KeywordGrowthConfig s_config;
    static bool s_initialized;

    public static event Action OnChanged;

    // 강화 "성공"만 알린다 — OnChanged는 값이 변했다는 갱신 신호라 완료 판정에 쓰면 의미가 갈린다
    public static event Action<CardKeyword> OnEnhanced;

    public static KeywordGrowthConfig Config
        => s_config != null ? s_config : (s_config = ScriptableObject.CreateInstance<KeywordGrowthConfig>());

    public static bool IsReady => s_initialized;

    /// <summary>지원 키워드 중 하나라도 다음 스텝이 있고 그 값을 낼 수 있는가(튜토리얼 무료 한 방 보정 포함).</summary>
    public static bool HasAnyAffordableStep
    {
        get
        {
            // 세이브를 읽기 전엔 전 키워드가 레벨 0으로 보인다 — 가드가 없으면 "첫 스텝은 싸다"로 참이 된다.
            if (!s_initialized) return false;

            CardKeyword[] t_supported = Config.SupportedKeywords;
            if (t_supported == null) return false;

            for (int t_i = 0; t_i < t_supported.Length; t_i++)
            {
                if (!TryGetNextStep(t_supported[t_i], out GrowthStep t_step)) continue;
                if (CurrencyManager.CanAfford(t_step.Currency, t_step.Cost)) return true;
            }

            return false;
        }
    }

    public static void SetConfig(KeywordGrowthConfig _config)
    {
        if (_config != null) s_config = _config;
    }

    public static void Init()
    {
        s_growth.Clear();

        var t_data = DataSaveManager.Data.keywordGrowth;
        if (t_data != null && t_data.entries != null)
        {
            foreach (var t_entry in t_data.entries)
            {
                if (t_entry == null) continue;

                var t_keyword = (CardKeyword)t_entry.keyword;
                if (!Config.Supports(t_keyword) || s_growth.ContainsKey(t_keyword)) continue;

                t_entry.level = Mathf.Clamp(t_entry.level, 0, Config.MaxLevel);
                if (t_entry.level > 0) s_growth[t_keyword] = t_entry;
            }
        }

        s_initialized = true;

        // 부트 전에 그려진 화면은 키워드 레벨 0으로 굳는다 — 로드 완료도 변경으로 통지해야 따라온다
        OnChanged?.Invoke();
    }

    public static void Save()
    {
        if (!s_initialized) return;

        FlushToData();
        SaveTransaction.Request();
    }

    public static int LevelOf(CardKeyword _keyword)
    {
        if (!Config.Supports(_keyword)) return 0;
        return s_growth.TryGetValue(_keyword, out var t_entry) && t_entry != null ? t_entry.level : 0;
    }

    // 키워드 비트묶음이 받는 체력 합(같은 키워드를 두 번 세지 않는다)
    public static int HpBonusFor(CardKeyword _keywords)
    {
        int t_bonus = 0;
        CardKeyword t_counted = CardKeyword.None;
        CardKeyword[] t_supported = Config.SupportedKeywords;
        if (t_supported == null) return 0;

        for (int t_i = 0; t_i < t_supported.Length; t_i++)
        {
            CardKeyword t_keyword = t_supported[t_i];
            if (!Config.Supports(t_keyword) || (t_counted & t_keyword) != 0 || (_keywords & t_keyword) == 0) continue;
            t_counted |= t_keyword;
            t_bonus += LevelOf(t_keyword) * Config.HpPerLevel;
        }

        return t_bonus;
    }

    public static bool TryGetNextStep(CardKeyword _keyword, out GrowthStep _step)
        => TryGetStepAt(_keyword, LevelOf(_keyword), out _step);

    /// <summary>무료 한 방의 조건이 바뀌었다고 알린다 — 레벨도 잔액도 그대로지만 낼 값이 달라져
    /// 이미 그려 둔 화면이 비용을 다시 읽어야 한다.</summary>
    public static void NotifyCostRuleChanged() => OnChanged?.Invoke();

    // 키워드 강화 1회(카드 강화와 달리 확률 실패가 없다 — 결제되면 반드시 오른다)
    public static EnhanceResult TryEnhance(CardKeyword _keyword)
    {
        int t_level = LevelOf(_keyword);
        if (!s_initialized) return new EnhanceResult(EEnhanceOutcome.NotReady, t_level);
        if (!TryGetStepAt(_keyword, t_level, out var t_step))
            return new EnhanceResult(EEnhanceOutcome.MaxLevel, t_level);
        if (!CurrencyManager.Spend(t_step.Currency, t_step.Cost))
            return new EnhanceResult(EEnhanceOutcome.NotAffordable, t_level);

        t_level++;
        Entry(_keyword).level = t_level;

        // OnEnhanced보다 앞이어야 한다 — 뒤로 밀면 안내가 이미 다음 스텝에 들어서 소진 표식이 엉뚱한 곳에 찍힌다.
        if (OutgameTutorialGuide.HasFreeShot(EOutgameTutorialAction.WaitKeywordEnhance))
            OutgameTutorialGuide.ConsumeFreeShot();

        SaveTransaction.Request();
        OnChanged?.Invoke();
        OnEnhanced?.Invoke(_keyword);

        return new EnhanceResult(EEnhanceOutcome.Success, t_level);
    }

    // 튜토리얼 무료 보정을 여기 하나로 모은다 — 조회가 갈리면 표시·활성 판정·소모가 서로 다른 값을 본다.
    static bool TryGetStepAt(CardKeyword _keyword, int _level, out GrowthStep _step)
    {
        if (!Config.TryGetNextStep(_keyword, _level, out _step)) return false;

        if (OutgameTutorialGuide.HasFreeShot(EOutgameTutorialAction.WaitKeywordEnhance))
            _step = new GrowthStep(_step.Level, _step.HpGain, _step.Currency, 0, _step.SuccessRate);

        return true;
    }

    /// <summary>캐시를 세이브 슬롯에 반영만 한다(디스크 쓰기 없음).</summary>
    internal static void FlushToData()
    {
        if (!s_initialized) return;

        var t_data = DataSaveManager.Data.keywordGrowth ??
                     (DataSaveManager.Data.keywordGrowth = new KeywordGrowthSaveData());
        t_data.version = KeywordGrowthSaveData.VERSION;
        t_data.entries = new List<KeywordGrowthEntry>(s_growth.Values);
    }

    static KeywordGrowthEntry Entry(CardKeyword _keyword)
    {
        if (s_growth.TryGetValue(_keyword, out var t_entry) && t_entry != null) return t_entry;

        t_entry = new KeywordGrowthEntry { keyword = (int)_keyword, level = 0 };
        s_growth[_keyword] = t_entry;
        return t_entry;
    }
}
