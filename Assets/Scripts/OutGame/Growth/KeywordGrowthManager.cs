using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 키워드 강화(키워드 한 종류의 레벨)의 static 단일 창구. 그 키워드를 가진 모든 카드에 체력으로 얹힌다.
public static class KeywordGrowthManager
{
    static readonly Dictionary<CardKeyword, int> s_growth = new Dictionary<CardKeyword, int>();

    static bool s_initialized;

    public static event Action OnChanged;

    // 강화 "성공"만 알린다 — OnChanged는 값이 변했다는 갱신 신호라 완료 판정에 쓰면 의미가 갈린다
    public static event Action<CardKeyword> OnEnhanced;

    public static bool IsReady => s_initialized;

    /// <summary>지원 키워드 중 하나라도 다음 스텝이 있고 그 값을 낼 수 있는가(튜토리얼 무료 한 방 보정 포함).</summary>
    public static bool HasAnyAffordableStep
    {
        get
        {
            // 세이브를 읽기 전엔 전 키워드가 레벨 0으로 보인다 — 가드가 없으면 "첫 스텝은 싸다"로 참이 된다.
            if (!s_initialized) return false;

            CardKeyword[] t_supported = KeywordGrowthRules.SupportedKeywords;
            if (t_supported == null) return false;

            for (int t_i = 0; t_i < t_supported.Length; t_i++)
            {
                if (!TryGetNextStep(t_supported[t_i], out GrowthStep t_step)) continue;
                if (CurrencyManager.CanAfford(t_step.Currency, t_step.Cost)) return true;
            }

            return false;
        }
    }

    public static void Init()
    {
        s_growth.Clear();

        var t_data = DataSaveManager.Data.KeywordGrowth;
        if (t_data != null && t_data.Levels != null)
        {
            foreach (var t_pair in t_data.Levels)
            {
                if (!int.TryParse(t_pair.Key, out int t_raw)) continue;

                var t_keyword = (CardKeyword)t_raw;
                if (!KeywordGrowthRules.Supports(t_keyword) || s_growth.ContainsKey(t_keyword)) continue;

                int t_level = Mathf.Clamp(t_pair.Value, 0, KeywordGrowthRules.MaxLevel);
                if (t_level > 0) s_growth[t_keyword] = t_level;
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
        if (!KeywordGrowthRules.Supports(_keyword)) return 0;
        return s_growth.TryGetValue(_keyword, out int t_level) ? t_level : 0;
    }

    // 키워드 비트묶음이 받는 체력 합(같은 키워드를 두 번 세지 않는다)
    public static int HpBonusFor(CardKeyword _keywords)
    {
        int t_bonus = 0;
        CardKeyword t_counted = CardKeyword.None;
        CardKeyword[] t_supported = KeywordGrowthRules.SupportedKeywords;
        if (t_supported == null) return 0;

        for (int t_i = 0; t_i < t_supported.Length; t_i++)
        {
            CardKeyword t_keyword = t_supported[t_i];
            if (!KeywordGrowthRules.Supports(t_keyword) || (t_counted & t_keyword) != 0 || (_keywords & t_keyword) == 0) continue;
            t_counted |= t_keyword;
            t_bonus += LevelOf(t_keyword) * KeywordGrowthRules.HpPerLevel;
        }

        return t_bonus;
    }

    public static bool TryGetNextStep(CardKeyword _keyword, out GrowthStep _step)
        => TryGetStepAt(_keyword, LevelOf(_keyword), out _step);

    /// <summary>무료 한 방의 조건이 바뀌었다고 알린다 — 레벨도 잔액도 그대로지만 낼 값이 달라져
    /// 이미 그려 둔 화면이 비용을 다시 읽어야 한다.</summary>
    public static void NotifyCostRuleChanged() => OnChanged?.Invoke();

    /// <summary>키워드 강화 1회를 서버에 요청한다(카드 강화와 달리 확률 실패가 없다 — 결제되면 반드시 오른다).
    /// 비용·차감·레벨의 진실원은 서버 enhanceKeyword 다 — 아래 선검사는 왕복을 아끼는 낙관 검사일 뿐이다.</summary>
    public static async UniTask<EnhanceResult> TryEnhanceAsync(CardKeyword _keyword)
    {
        int t_level = LevelOf(_keyword);
        if (!s_initialized) return new EnhanceResult(EEnhanceOutcome.NotReady, t_level);
        if (!TryGetStepAt(_keyword, t_level, out _))
            return new EnhanceResult(EEnhanceOutcome.MaxLevel, t_level);

        // 무료 한 방의 조건은 클라 안내가 쥐고 있어 요청에 실어 보낸다 — 실제로 먹였는지는 응답이 답한다.
        bool t_freeShot = OutgameTutorialGuide.HasFreeShot(EOutgameTutorialAction.WaitKeywordEnhance);

        EnhanceCommandResult t_command = await EnhanceCommand.EnhanceKeywordAsync(_keyword, t_freeShot);

        // 결제 전에 막힌 결말은 값이 하나도 안 바뀌었다 — 통지 없이 물러난다.
        if (!t_command.Settled) return new EnhanceResult(t_command.Outcome, LevelOf(_keyword));

        // 레벨은 응답 채택이 갈아끼운 슬롯을 ServerSlotRehydrator가 Init으로 다시 태워 이미 캐시에 있다 —
        // 여기서 대입하거나 저장하면 서버와 이중 진실원이 된다.
        t_level = Mathf.Clamp(t_command.Level, 0, KeywordGrowthRules.MaxLevel);

        // OnChanged보다 앞이어야 한다 — 뒤로 밀면 안내가 이미 다음 스텝에 들어서 소진 표식이 엉뚱한 곳에 찍힌다.
        if (t_command.FreeShotUsed) OutgameTutorialGuide.ConsumeFreeShot();

        OnChanged?.Invoke();
        OnEnhanced?.Invoke(_keyword);

        return new EnhanceResult(t_command.Outcome, t_level);
    }

    // 튜토리얼 무료 보정을 여기 하나로 모은다 — 조회가 갈리면 표시·활성 판정·소모가 서로 다른 값을 본다.
    static bool TryGetStepAt(CardKeyword _keyword, int _level, out GrowthStep _step)
    {
        if (!KeywordGrowthRules.TryGetNextStep(_keyword, _level, out _step)) return false;

        if (OutgameTutorialGuide.HasFreeShot(EOutgameTutorialAction.WaitKeywordEnhance))
            _step = new GrowthStep(_step.Level, _step.HpGain, _step.Currency, 0, _step.SuccessRate);

        return true;
    }

    static void SyncSaveData()
    {
        var t_data = DataSaveManager.Data.KeywordGrowth ??
                     (DataSaveManager.Data.KeywordGrowth = new KeywordGrowthSaveData());

        var t_levels = new Dictionary<string, int>(s_growth.Count);
        foreach (var t_pair in s_growth)
            t_levels[((int)t_pair.Key).ToString()] = t_pair.Value;

        t_data.Levels = t_levels;
    }
}
