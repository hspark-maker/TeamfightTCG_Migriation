using System;
using System.Collections.Generic;
using UnityEngine;

// 카드 성장(강화 레벨)의 static 단일 창구
public static class CardGrowthManager
{
    static readonly Dictionary<string, CardGrowthEntry> s_growth = new Dictionary<string, CardGrowthEntry>();

    static readonly System.Random s_rng = new System.Random();

    static CardGrowthConfig s_config;

    static bool s_initialized;

    // 성장 변경 통지(강화 실패도 통지 — 재화가 줄었다)
    public static event Action OnGrowthChanged;

    // 강화 곡선 설정(미배선이면 기본 인스턴스)
    public static CardGrowthConfig Config
        => s_config != null ? s_config : (s_config = ScriptableObject.CreateInstance<CardGrowthConfig>());

    public static int MaxLevel => Config.MaxLevel;

    // Init()으로 세이브를 캐싱했는지(false면 Save가 no-op)
    public static bool IsReady => s_initialized;

    // 부트스트랩에서 실제 애셋 주입(선택). null이면 기본 유지
    public static void SetConfig(CardGrowthConfig _config)
    {
        if (_config != null) s_config = _config;
    }

    // 부트에서 DataSaveManager.Load() 이후 1회 호출
    public static void Init()
    {
        s_growth.Clear();

        var t_data = DataSaveManager.Data.cardGrowth;
        if (t_data != null && t_data.entries != null)
        {
            foreach (var t_entry in t_data.entries)
            {
                if (t_entry == null || string.IsNullOrEmpty(t_entry.cardKey)) continue;
                if (s_growth.ContainsKey(t_entry.cardKey)) continue;
                s_growth[t_entry.cardKey] = t_entry;
            }
        }

        s_initialized = true;
    }

    // 메모리 캐시를 세이브 슬롯에 flush 후 영속화(미초기화면 no-op)
    public static void Save()
    {
        if (!s_initialized) return;

        var t_data = DataSaveManager.Data.cardGrowth ?? (DataSaveManager.Data.cardGrowth = new CardGrowthSaveData());
        t_data.version = CardGrowthSaveData.VERSION;
        t_data.entries = new List<CardGrowthEntry>(s_growth.Values);
        DataSaveManager.Save();
    }

    public static CardGrowth GrowthOf(CardData _card) => Snapshot(_card, LevelOf(CardCatalog.KeyOf(_card)));

    // 카드 키의 성장 스냅샷(기록이 없으면 미강화). HP 보너스·해금 상태는 저장값이 아니라 레벨에서 파생
    public static CardGrowth GrowthOf(string _key) => Snapshot(CardCatalog.Get(_key), LevelOf(_key));

    // 카드의 현재 강화 레벨(기록 없음 = 미강화)
    public static int LevelOf(string _key)
    {
        if (string.IsNullOrEmpty(_key)) return CardGrowth.BaseLevel;
        if (!s_growth.TryGetValue(_key, out var t_entry) || t_entry == null) return CardGrowth.BaseLevel;

        // 바닥 아래 값은 미강화로 읽는다 — 레벨을 0부터 세던 시절의 세이브가 그렇다.
        return t_entry.level < CardGrowth.BaseLevel ? CardGrowth.BaseLevel : t_entry.level;
    }

    public static int HpBonusOf(CardData _card) => GrowthOf(_card).HpBonus;

    // 다음 레벨의 비용·성공률·HP 증가분(만렙이면 false)
    public static bool TryGetNextStep(CardData _card, out GrowthStep _step)
    {
        _step = default;
        if (_card == null) return false;

        return Config.TryGetStep(GrowthOf(_card).Level + 1, out _step);
    }

    // 강화 1회 시도(실패해도 골드는 소모, 레벨 하락 없음)
    public static EnhanceResult TryEnhance(CardData _card)
    {
        if (!s_initialized) return new EnhanceResult(EEnhanceOutcome.NotReady, CardGrowth.BaseLevel);

        string t_key = CardCatalog.KeyOf(_card);
        if (string.IsNullOrEmpty(t_key)) return new EnhanceResult(EEnhanceOutcome.MaxLevel, CardGrowth.BaseLevel);

        CardGrowthConfig t_config = Config;
        CardGrowth       t_growth = GrowthOf(t_key);
        int              t_level  = t_growth.Level;

        if (t_level >= t_config.MaxLevel) return new EnhanceResult(EEnhanceOutcome.MaxLevel, t_level);

        if (!t_config.TryGetStep(t_level + 1, out var t_step))
            return new EnhanceResult(EEnhanceOutcome.MaxLevel, t_level);

        if (!CurrencyManager.CanAfford(ECurrencyType.Gold, t_step.Cost))
            return new EnhanceResult(EEnhanceOutcome.NotAffordable, t_level);

        if (!CurrencyManager.Spend(ECurrencyType.Gold, t_step.Cost))
            return new EnhanceResult(EEnhanceOutcome.NotAffordable, t_level);

        bool t_success = s_rng.NextDouble() < t_step.SuccessRate;
        if (t_success)
        {
            t_level = t_growth.Level + 1;
            Entry(t_key).level = t_level;
            Save();
        }

        CurrencyManager.Save();
        OnGrowthChanged?.Invoke();

        return new EnhanceResult(t_success ? EEnhanceOutcome.Success : EEnhanceOutcome.Failed, t_level);
    }

    // 성장 전체 초기화(디버그 전용, 진행도 손실)
    public static void DebugResetAll()
    {
        s_growth.Clear();
        Save();
        OnGrowthChanged?.Invoke();
    }

    // 레벨 하나에서 전투가 쓸 파생값을 전부 만든다(곡선·관문을 아는 것은 OutGame뿐이라는 규약).
    // _card가 null이면(카탈로그 미초기화·미등록) 키워드 해금만 비고 나머지는 그대로 — 조용히 레벨까지 잃지 않는다.
    static CardGrowth Snapshot(CardData _card, int _level)
    {
        CardGrowthConfig t_config = Config;
        return new CardGrowth(
            _level,
            t_config.HpBonusAt(_level),
            t_config.EvolutionStageAt(_level),
            t_config.UnlockedKeywordsAt(_card, _level),
            t_config.SynergyUnlockedAt(_level));
    }

    static CardGrowthEntry Entry(string _key)
    {
        if (s_growth.TryGetValue(_key, out var t_entry) && t_entry != null) return t_entry;

        t_entry = new CardGrowthEntry { cardKey = _key, level = CardGrowth.BaseLevel };
        s_growth[_key] = t_entry;
        return t_entry;
    }
}
