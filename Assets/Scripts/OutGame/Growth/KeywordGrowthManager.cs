using System;
using System.Collections.Generic;
using UnityEngine;

public static class KeywordGrowthManager
{
    static readonly Dictionary<CardKeyword, KeywordGrowthEntry> s_growth =
        new Dictionary<CardKeyword, KeywordGrowthEntry>();

    static KeywordGrowthConfig s_config;
    static bool s_initialized;

    public static event Action OnChanged;

    public static KeywordGrowthConfig Config
        => s_config != null ? s_config : (s_config = ScriptableObject.CreateInstance<KeywordGrowthConfig>());

    public static bool IsReady => s_initialized;

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
    }

    public static void Save()
    {
        if (!s_initialized) return;

        SyncSaveData();
        DataSaveManager.Save();
    }

    public static int LevelOf(CardKeyword _keyword)
    {
        if (!Config.Supports(_keyword)) return 0;
        return s_growth.TryGetValue(_keyword, out var t_entry) && t_entry != null ? t_entry.level : 0;
    }

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
        => Config.TryGetNextStep(_keyword, LevelOf(_keyword), out _step);

    public static EnhanceResult TryEnhance(CardKeyword _keyword)
    {
        int t_level = LevelOf(_keyword);
        if (!s_initialized) return new EnhanceResult(EEnhanceOutcome.NotReady, t_level);
        if (!Config.TryGetNextStep(_keyword, t_level, out var t_step))
            return new EnhanceResult(EEnhanceOutcome.MaxLevel, t_level);
        if (!CurrencyManager.Spend(t_step.Currency, t_step.Cost))
            return new EnhanceResult(EEnhanceOutcome.NotAffordable, t_level);

        t_level++;
        Entry(_keyword).level = t_level;
        SyncSaveData();
        CurrencyManager.Save();
        OnChanged?.Invoke();

        return new EnhanceResult(EEnhanceOutcome.Success, t_level);
    }

    static void SyncSaveData()
    {
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
