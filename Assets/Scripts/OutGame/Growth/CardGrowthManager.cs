using System;
using System.Collections.Generic;
using UnityEngine;

// 카드 성장(강화 레벨)의 static 단일 창구
public static class CardGrowthManager
{
    static readonly Dictionary<int, CardGrowthEntry> s_growth = new Dictionary<int, CardGrowthEntry>();

    static readonly System.Random s_rng = new System.Random();

    static CardGrowthConfig s_config;

    static bool s_initialized;

    // 성장 변경 통지(강화 실패도 통지 — 재화가 줄었다)
    public static event Action OnGrowthChanged;

    // 강화 곡선 설정(미배선이면 기본 인스턴스)
    public static CardGrowthConfig Config
        => s_config != null ? s_config : (s_config = ScriptableObject.CreateInstance<CardGrowthConfig>());

    public static int MaxLevel => Config.MaxLevel;
    public static int MaxStar => GrowthStar.FromLevel(MaxLevel);

    // 레벨 _level로 올리는 한 방이 진화인가(관문 레벨은 곡선이 소유한다)
    public static bool IsEvolutionLevel(int _level) => Config.IsEvolutionLevel(_level);

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

        KeywordGrowthManager.OnChanged -= NotifyGrowthChanged;
        KeywordGrowthManager.OnChanged += NotifyGrowthChanged;

        var t_data = DataSaveManager.Data.cardGrowth;
        bool t_migrated = false;
        if (t_data != null && t_data.entries != null)
        {
            foreach (var t_entry in t_data.entries)
            {
                if (t_entry == null) continue;

                int t_id = t_entry.cardId;
                if (t_id <= 0)
                {
                    // 구 세이브(이름 키) 이관 — 카탈로그 미준비면 이번 부트는 건너뛰고 값을 보존한다.
                    if (!CardCatalog.IsReady) continue;

                    t_id = CardCatalog.LegacyIdOfName(t_entry.cardKey);
                    if (t_id <= 0) continue;                 // 사라진 카드의 진행도는 버린다

                    t_entry.cardId  = t_id;
                    t_entry.cardKey = null;
                    t_migrated      = true;
                }

                if (s_growth.ContainsKey(t_id)) continue;
                s_growth[t_id] = t_entry;
            }
        }

        s_initialized = true;

        if (t_migrated) Save();
    }

    static void NotifyGrowthChanged() => OnGrowthChanged?.Invoke();

    // 메모리 캐시를 세이브 슬롯에 flush 후 영속화(미초기화면 no-op)
    public static void Save()
    {
        if (!s_initialized) return;

        var t_data = DataSaveManager.Data.cardGrowth ?? (DataSaveManager.Data.cardGrowth = new CardGrowthSaveData());
        t_data.version = CardGrowthSaveData.VERSION;
        t_data.entries = new List<CardGrowthEntry>(s_growth.Values);
        DataSaveManager.Save();
    }

    public static CardGrowth GrowthOf(CardData _card) => Snapshot(_card, LevelOf(CardCatalog.IdOf(_card)), true);

    /// <summary>세이브와 무관하게 **지정 레벨**의 성장 스냅샷. AI 난이도처럼 소유 진행도가 없는 쪽이 쓴다 —
    /// 곡선 해석(체력·진화·키워드·시너지 해금)을 한 곳에 두려고 여기서 내준다.</summary>
    public static CardGrowth GrowthAtLevel(CardData _card, int _level)
        => Snapshot(_card, ClampLevel(_level), false);

    // 카드 번호의 성장 스냅샷(기록이 없으면 미강화). HP 보너스·해금 상태는 저장값이 아니라 레벨에서 파생
    public static CardGrowth GrowthOf(int _id) => Snapshot(CardCatalog.Get(_id), LevelOf(_id), true);

    // 카드의 현재 강화 레벨(기록 없음 = 미강화)
    public static int LevelOf(int _id)
    {
        if (_id <= 0) return CardGrowth.BaseLevel;
        if (!s_growth.TryGetValue(_id, out var t_entry) || t_entry == null) return CardGrowth.BaseLevel;

        // 바닥 아래 값은 미강화로 읽는다 — 레벨을 0부터 세던 시절의 세이브가 그렇다.
        return ClampLevel(t_entry.level);
    }

    static int ClampLevel(int _level)
        => Mathf.Clamp(_level, CardGrowth.BaseLevel, MaxLevel);

    public static int HpBonusOf(CardData _card) => GrowthOf(_card).HpBonus;

    // 다음 레벨의 비용·성공률·HP 증가분(만렙이면 false)
    public static bool TryGetNextStep(CardData _card, out GrowthStep _step)
    {
        _step = default;
        if (_card == null) return false;

        return TryGetStepAt(_card, GrowthOf(_card).Level + 1, out _step);
    }

    /// <summary>무료 한 방의 조건이 바뀌었다고 알린다(안내가 강화 스텝에 들어선 순간).
    /// 레벨도 잔액도 그대로지만 **낼 값**이 달라지므로, 이미 그려 둔 화면이 비용을 다시 읽어야 한다 —
    /// 안 알리면 상세가 옛 비용을 띄운 채로 굳고, 잔액이 그에 못 미치는 유저는 강화 버튼이 비활성인 채
    /// 안내가 시킨 일을 하지 못한다(표시·활성 판정·소모가 같은 값을 봐야 한다는 <see cref="TryGetStepAt"/> 규약).</summary>
    public static void NotifyCostRuleChanged() => OnGrowthChanged?.Invoke();

    // 강화 1회 시도(실패해도 골드는 소모, 레벨 하락 없음)
    public static EnhanceResult TryEnhance(CardData _card)
    {
        if (!s_initialized) return new EnhanceResult(EEnhanceOutcome.NotReady, CardGrowth.BaseLevel);

        int t_id = CardCatalog.IdOf(_card);
        if (t_id <= 0) return new EnhanceResult(EEnhanceOutcome.MaxLevel, CardGrowth.BaseLevel);

        CardGrowthConfig t_config = Config;
        CardGrowth       t_growth = GrowthOf(t_id);
        int              t_level  = t_growth.Level;

        if (t_level >= t_config.MaxLevel) return new EnhanceResult(EEnhanceOutcome.MaxLevel, t_level);

        if (!TryGetStepAt(_card, t_level + 1, out var t_step))
            return new EnhanceResult(EEnhanceOutcome.MaxLevel, t_level);

        // 재화는 곡선이 정한다 — 1·2성은 조각, 최종 3성은 다이아를 쓴다.
        if (!CurrencyManager.CanAfford(t_step.Currency, t_step.Cost))
            return new EnhanceResult(EEnhanceOutcome.NotAffordable, t_level);

        if (!CurrencyManager.Spend(t_step.Currency, t_step.Cost))
            return new EnhanceResult(EEnhanceOutcome.NotAffordable, t_level);

        bool t_success = s_rng.NextDouble() < t_step.SuccessRate;
        if (t_success)
        {
            t_level = t_growth.Level + 1;
            Entry(t_id).level = t_level;
            Save();

            // 안내가 대준 한 방을 여기서 소진한다. 실패에는 걸지 않는다 —
            // 실패로 닫아 버리면 안내가 시키는 강화를 유저 돈으로 다시 해야 한다.
            if (OutgameTutorialGuide.HasFreeShot(EOutgameTutorialAction.WaitEnhance))
                OutgameTutorialGuide.ConsumeFreeShot();
        }

        CurrencyManager.Save();
        OnGrowthChanged?.Invoke();

        return new EnhanceResult(t_success ? EEnhanceOutcome.Success : EEnhanceOutcome.Failed, t_level);
    }

    /// <summary>전 카드를 만렙으로 올린다(디버그 전용). 반환값은 실제로 레벨이 오른 카드 수.
    ///
    /// TryEnhance를 돌리지 않는다 — 재화·성공률을 타면 골드가 마르거나 실패로 레벨이 들쭉날쭉해져
    /// "전부 만렙"이라는 목적을 못 채운다. 여기서는 곡선이 정한 상한을 레벨에 직접 쓴다.
    /// 진화 단계·키워드 해금은 레벨에서 파생되므로(GrowthOf) 따로 손대지 않아도 같이 열린다.</summary>
    public static int DebugMaxAll()
    {
        if (!s_initialized) return 0;

        int t_max     = Config.MaxLevel;
        int t_changed = 0;

        var t_cards = CardCatalog.All;
        for (int t_i = 0; t_i < t_cards.Count; t_i++)
        {
            CardData t_card = t_cards[t_i];
            if (t_card == null) continue;               // CardRegistry의 ID 보존용 빈 칸

            int t_id = CardCatalog.IdOf(t_card);
            if (t_id <= 0) continue;
            if (LevelOf(t_id) >= t_max) continue;

            Entry(t_id).level = t_max;
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

    // 레벨 _level로 올리는 한 스텝. 곡선 조회를 여기 하나로 모으는 이유는 튜토리얼 보정 때문이다 —
    // 안내가 강화를 시키는 동안은 비용을 0으로 눕히는데, 조회가 갈리면 화면엔 100골드가 뜨는데
    // 실제로는 0이 나가고 잔액이 모자란 유저는 버튼이 비활성으로 굳는다(표시·활성 판정·소모가 같은 값을 봐야 한다).
    static bool TryGetStepAt(CardData _card, int _level, out GrowthStep _step)
    {
        if (!Config.TryGetStep(_card, _level, out _step)) return false;

        // 무료 한 방의 원장은 OutgameTutorialGuide가 쥔다(키워드 강화도 같은 원장을 본다 — 축으로 갈린다).
        if (OutgameTutorialGuide.HasFreeShot(EOutgameTutorialAction.WaitEnhance))
            _step = new GrowthStep(_step.Level, _step.HpGain, _step.Currency, 0, _step.SuccessRate);

        return true;
    }

    // 레벨 하나에서 전투가 쓸 파생값을 전부 만든다(곡선·관문을 아는 것은 OutGame뿐이라는 규약).
    // _card가 null이면(카탈로그 미초기화·미등록) 키워드 해금만 비고 나머지는 그대로 — 조용히 레벨까지 잃지 않는다.
    static CardGrowth Snapshot(CardData _card, int _level, bool _includeKeywordGrowth)
    {
        CardGrowthConfig t_config = Config;
        CardKeyword t_unlockedKeywords = t_config.UnlockedKeywordsAt(_card, _level);
        int t_hpBonus = t_config.HpBonusAt(_card, _level);
        if (_includeKeywordGrowth) t_hpBonus += KeywordGrowthManager.HpBonusFor(t_unlockedKeywords);

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

        t_entry = new CardGrowthEntry { cardId = _id, level = CardGrowth.BaseLevel };
        s_growth[_id] = t_entry;
        return t_entry;
    }
}
