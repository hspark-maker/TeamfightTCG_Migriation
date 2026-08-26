using System;
using System.Collections.Generic;
using UnityEngine;

// 카드 성장(강화 레벨)의 static 단일 창구. 간식은 같은 세이브 항목을 써서 partial 조각(.Snack.cs)이 맡는다.
public static partial class CardGrowthManager
{
    static readonly Dictionary<int, CardGrowthEntry> s_growth = new Dictionary<int, CardGrowthEntry>();

    static readonly System.Random s_rng = new System.Random();

    static CardGrowthConfig s_config;
    static bool s_configInjected;
    static bool s_missingConfigLogged;

    static bool s_initialized;

    // 성장 변경 통지(강화 실패도 통지 — 재화가 줄었다)
    public static event Action OnGrowthChanged;

    // 강화 곡선 설정(미배선이면 표시 호환용 기본 인스턴스, 실제 강화는 차단)
    public static CardGrowthConfig Config
    {
        get
        {
            if (s_config == null)
                s_config = ScriptableObject.CreateInstance<CardGrowthConfig>();

            if (!s_configInjected && !s_missingConfigLogged)
            {
                Debug.LogError("[CardGrowth] CardGrowthConfig가 주입되지 않아 기본 표시값을 사용합니다. 강화는 차단됩니다.");
                s_missingConfigLogged = true;
            }

            return s_config;
        }
    }

    public static bool IsConfigReady => s_configInjected;

    public static int MaxLevel => Config.MaxLevel;
    public static int MaxStar => GrowthStar.FromLevel(MaxLevel);

    // 레벨 _level로 올리는 한 방이 진화인가(관문 레벨은 곡선이 소유한다)
    public static bool IsEvolutionLevel(int _level) => Config.IsEvolutionLevel(_level);

    // Init()으로 세이브를 캐싱했는지(false면 Save가 no-op)
    public static bool IsReady => s_initialized;

    // 부트스트랩에서 실제 애셋 주입. null이면 이전 설정을 버리고 미주입 상태로 되돌린다.
    public static void SetConfig(CardGrowthConfig _config)
    {
        s_config              = _config;
        s_configInjected      = _config != null;
        s_missingConfigLogged = false;
    }

    // 부트에서 클라우드 세이브 채택 이후 1회 호출
    public static void Init()
    {
        s_growth.Clear();

        KeywordGrowthManager.OnChanged -= NotifyGrowthChanged;
        KeywordGrowthManager.OnChanged += NotifyGrowthChanged;

        var t_data = DataSaveManager.Data.CardGrowth;
        if (t_data != null && t_data.Entries != null)
        {
            foreach (var t_pair in t_data.Entries)
            {
                if (t_pair.Value == null) continue;
                if (!int.TryParse(t_pair.Key, out int t_id)) continue;
                if (t_id <= 0 || s_growth.ContainsKey(t_id)) continue;

                s_growth[t_id] = t_pair.Value;
            }
        }

        s_initialized = true;
    }

    static void NotifyGrowthChanged() => OnGrowthChanged?.Invoke();

    /// <summary>캐시를 세이브 슬롯에 반영만 한다(디스크 쓰기 없음) — 여러 도메인을 건드리는 흐름에서
    /// 같은 파일을 반복해 쓰지 않도록 디스크 쓰기는 마지막 한 곳이 맡는다.</summary>
    internal static void FlushToData()
    {
        if (!s_initialized) return;

        var t_data = DataSaveManager.Data.CardGrowth ?? (DataSaveManager.Data.CardGrowth = new CardGrowthSaveData());

        var t_entries = new Dictionary<string, CardGrowthEntry>(s_growth.Count);
        foreach (var t_pair in s_growth)
        {
            var t_entry = t_pair.Value;
            if (t_entry == null) continue;
            if (t_entry.Level <= CardGrowth.BaseLevel && t_entry.Snack <= 0 && t_entry.LimitBreak <= 0) continue;

            t_entries[t_pair.Key.ToString()] = t_entry;
        }
        t_data.Entries = t_entries;
    }

    // 메모리 캐시를 세이브 슬롯에 flush 후 영속화(미초기화면 no-op)
    public static void Save()
    {
        if (!s_initialized) return;

        FlushToData();
        DataSaveManager.Save();
    }

    public static CardGrowth GrowthAtLevel(int _id, int _level)
        => Snapshot(_id, ClampLevel(_level), false);

    // 카드 번호의 성장 스냅샷(기록이 없으면 미강화). HP 보너스·해금 상태는 저장값이 아니라 레벨에서 파생
    public static CardGrowth GrowthOf(int _id) => Snapshot(_id, LevelOf(_id), true);

    // 카드의 현재 강화 레벨(기록 없음 = 미강화)
    public static int LevelOf(int _id)
    {
        if (_id <= 0) return CardGrowth.BaseLevel;
        if (!s_growth.TryGetValue(_id, out var t_entry) || t_entry == null) return CardGrowth.BaseLevel;

        // 바닥 아래 값은 미강화로 읽는다 — 레벨을 0부터 세던 시절의 세이브가 그렇다.
        return ClampLevel(t_entry.Level);
    }

    static int ClampLevel(int _level)
        => Mathf.Clamp(_level, CardGrowth.BaseLevel, MaxLevel);

    public static int HpBonusOf(int _cardId) => GrowthOf(_cardId).HpBonus;

    // 다음 레벨의 비용·성공률·HP 증가분(만렙이면 false)
    public static bool TryGetNextStep(int _cardId, out GrowthStep _step)
    {
        _step = default;
        if (_cardId <= 0) return false;

        return TryGetStepAt(_cardId, GrowthOf(_cardId).Level + 1, out _step);
    }

    /// <summary>무료 한 방의 조건이 바뀌었다고 알린다 — 레벨도 잔액도 그대로지만 낼 값이 달라져
    /// 이미 그려 둔 화면이 비용을 다시 읽어야 한다.</summary>
    public static void NotifyCostRuleChanged() => OnGrowthChanged?.Invoke();

    // 강화 1회 시도(실패해도 비용은 소모, 레벨 하락 없음)
    public static EnhanceResult TryEnhance(int _cardId)
    {
        if (!s_initialized) return new EnhanceResult(EEnhanceOutcome.NotReady, CardGrowth.BaseLevel);
        if (!s_configInjected)
        {
            _ = Config; // 누락 로그는 Config 접근 경로에서 한 번만 남긴다.
            return new EnhanceResult(EEnhanceOutcome.NotReady, CardGrowth.BaseLevel);
        }

        int t_id = _cardId;
        if (t_id <= 0) return new EnhanceResult(EEnhanceOutcome.MaxLevel, CardGrowth.BaseLevel);

        CardGrowthConfig t_config = Config;
        CardGrowth       t_growth = GrowthOf(t_id);
        int              t_level  = t_growth.Level;

        if (t_level >= t_config.MaxLevel) return new EnhanceResult(EEnhanceOutcome.MaxLevel, t_level);

        if (!TryGetStepAt(t_id, t_level + 1, out var t_step))
            return new EnhanceResult(EEnhanceOutcome.MaxLevel, t_level);

        // 재화는 스텝이 들고 온다 — 성급별로 무엇을 무는지 여기 적으면 곡선과 이중 진실원이 된다.
        if (!CurrencyManager.CanAfford(t_step.Currency, t_step.Cost))
            return new EnhanceResult(EEnhanceOutcome.NotAffordable, t_level);

        if (!CurrencyManager.Spend(t_step.Currency, t_step.Cost))
            return new EnhanceResult(EEnhanceOutcome.NotAffordable, t_level);

        bool t_success = s_rng.NextDouble() < t_step.SuccessRate;
        if (t_success)
        {
            t_level = t_growth.Level + 1;
            Entry(t_id).Level = t_level;
            Save();

            // 실패에는 걸지 않는다 — 닫아 버리면 안내가 시키는 강화를 유저 돈으로 다시 해야 한다.
            if (OutgameTutorialGuide.HasFreeShot(EOutgameTutorialAction.WaitEnhance))
                OutgameTutorialGuide.ConsumeFreeShot();
        }

        CurrencyManager.Save();
        OnGrowthChanged?.Invoke();

        return new EnhanceResult(t_success ? EEnhanceOutcome.Success : EEnhanceOutcome.Failed, t_level);
    }

    /// <summary>전 카드를 만렙으로 올린다(디버그 전용). 반환값은 실제로 레벨이 오른 카드 수.
    /// TryEnhance를 안 타는 이유는 재화·성공률에 걸려 "전부 만렙"을 못 채우기 때문이다.</summary>
    public static int DebugMaxAll()
    {
        if (!s_initialized) return 0;

        int t_max     = Config.MaxLevel;
        int t_changed = 0;

        var t_cards = CardCatalog.AllIds;
        for (int t_i = 0; t_i < t_cards.Count; t_i++)
        {
            int t_id = t_cards[t_i];
            if (t_id <= 0) continue;
            if (LevelOf(t_id) >= t_max) continue;

            Entry(t_id).Level = t_max;
            t_changed++;
        }

        if (t_changed == 0) return 0;

        Save();
        OnGrowthChanged?.Invoke();

        return t_changed;
    }

    // 성장 전체 초기화(디버그 전용, 진행도 손실)
    public static void DebugResetAll()
    {
        s_growth.Clear();
        OutgameTutorialGuide.ResetFreeShotForDebug();   // 강화를 처음부터 다시 보는 상태다
        Save();
        OnGrowthChanged?.Invoke();
    }

    // 튜토리얼 무료 보정을 여기 하나로 모은다 — 조회가 갈리면 표시·활성 판정·소모가 서로 다른 값을 본다.
    static bool TryGetStepAt(int _cardId, int _level, out GrowthStep _step)
    {
        if (!Config.TryGetStep(_cardId, _level, out _step)) return false;

        if (OutgameTutorialGuide.HasFreeShot(EOutgameTutorialAction.WaitEnhance))
            _step = new GrowthStep(_step.Level, _step.HpGain, _step.Currency, 0, _step.SuccessRate);

        return true;
    }

    // _card가 null이면(카탈로그 미초기화·미등록) 키워드 해금만 비고 나머지는 그대로 — 레벨까지 잃지 않는다.
    static CardGrowth Snapshot(int _cardId, int _level, bool _includeKeywordGrowth)
    {
        CardGrowthConfig t_config = Config;
        CardKeyword t_unlockedKeywords = t_config.UnlockedKeywordsAt(_cardId, _level);
        int t_hpBonus = t_config.HpBonusAt(_cardId, _level);
        if (_includeKeywordGrowth)
        {
            t_hpBonus += KeywordGrowthManager.HpBonusFor(t_unlockedKeywords);
            t_hpBonus += t_config.LimitBreakHpBonusAt(LimitBreakOf(_cardId));
        }

        return new CardGrowth(
            _level,
            t_hpBonus,
            t_config.EvolutionStageAt(_level),
            t_unlockedKeywords,
            t_config.SynergyUnlockedAt(_level));
    }

    static CardGrowthEntry Entry(int _id)
    {
        if (s_growth.TryGetValue(_id, out var t_entry) && t_entry != null) return t_entry;

        t_entry = new CardGrowthEntry { Level = CardGrowth.BaseLevel };
        s_growth[_id] = t_entry;
        return t_entry;
    }
}
