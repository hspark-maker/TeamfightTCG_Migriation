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

    // 강화 "성공"만 알린다 — OnChanged는 값이 변했다는 갱신 신호라 완료 판정에 쓰면 의미가 갈린다
    public static event Action<CardKeyword> OnEnhanced;

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
        => TryGetStepAt(_keyword, LevelOf(_keyword), out _step);

    /// <summary>무료 한 방의 조건이 바뀌었다고 알린다(안내가 강화 스텝에 들어선 순간).
    /// 레벨도 잔액도 그대로지만 **낼 값**이 달라지므로, 이미 그려 둔 화면이 비용을 다시 읽어야 한다.</summary>
    public static void NotifyCostRuleChanged() => OnChanged?.Invoke();

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
        SyncSaveData();

        // 안내가 대준 한 방은 성공한 자리에서만 소진한다. OnEnhanced보다 앞이어야 한다 —
        // 뒤로 밀면 그 신호를 받은 안내가 이미 다음 스텝에 들어선 뒤라 소진 표식이 엉뚱한 스텝에 찍힌다.
        if (OutgameTutorialGuide.HasFreeShot(EOutgameTutorialAction.WaitKeywordEnhance))
            OutgameTutorialGuide.ConsumeFreeShot();

        CurrencyManager.Save();
        OnChanged?.Invoke();
        OnEnhanced?.Invoke(_keyword);

        return new EnhanceResult(EEnhanceOutcome.Success, t_level);
    }

    // 레벨 _level에서 한 단계 올리는 스텝. 곡선 조회를 여기 하나로 모으는 이유는 튜토리얼 보정 때문이다 —
    // 조회가 갈리면 화면엔 비용이 뜨는데 실제로는 0이 나가고, 잔액이 모자란 유저는 버튼이 비활성으로 굳는다.
    static bool TryGetStepAt(CardKeyword _keyword, int _level, out GrowthStep _step)
    {
        if (!Config.TryGetNextStep(_keyword, _level, out _step)) return false;

        if (OutgameTutorialGuide.HasFreeShot(EOutgameTutorialAction.WaitKeywordEnhance))
            _step = new GrowthStep(_step.Level, _step.HpGain, _step.Currency, 0, _step.SuccessRate);

        return true;
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
