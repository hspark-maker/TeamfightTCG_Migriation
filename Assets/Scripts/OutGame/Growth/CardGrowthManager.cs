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

    // 메모리 캐시를 세이브 슬롯에 flush 후 영속화(미초기화면 no-op)
    public static void Save()
    {
        if (!s_initialized) return;

        var t_data = DataSaveManager.Data.cardGrowth ?? (DataSaveManager.Data.cardGrowth = new CardGrowthSaveData());
        t_data.version = CardGrowthSaveData.VERSION;
        t_data.entries = new List<CardGrowthEntry>(s_growth.Values);
        DataSaveManager.Save();
    }

    public static CardGrowth GrowthOf(CardData _card) => Snapshot(_card, LevelOf(CardCatalog.IdOf(_card)));

    /// <summary>세이브와 무관하게 **지정 레벨**의 성장 스냅샷. AI 난이도처럼 소유 진행도가 없는 쪽이 쓴다 —
    /// 곡선 해석(체력·진화·키워드·시너지 해금)을 한 곳에 두려고 여기서 내준다.</summary>
    public static CardGrowth GrowthAtLevel(CardData _card, int _level)
        => Snapshot(_card, _level < CardGrowth.BaseLevel ? CardGrowth.BaseLevel : _level);

    // 카드 번호의 성장 스냅샷(기록이 없으면 미강화). HP 보너스·해금 상태는 저장값이 아니라 레벨에서 파생
    public static CardGrowth GrowthOf(int _id) => Snapshot(CardCatalog.Get(_id), LevelOf(_id));

    // 카드의 현재 강화 레벨(기록 없음 = 미강화)
    public static int LevelOf(int _id)
    {
        if (_id <= 0) return CardGrowth.BaseLevel;
        if (!s_growth.TryGetValue(_id, out var t_entry) || t_entry == null) return CardGrowth.BaseLevel;

        // 바닥 아래 값은 미강화로 읽는다 — 레벨을 0부터 세던 시절의 세이브가 그렇다.
        return t_entry.level < CardGrowth.BaseLevel ? CardGrowth.BaseLevel : t_entry.level;
    }

    public static int HpBonusOf(CardData _card) => GrowthOf(_card).HpBonus;

    // 다음 레벨의 비용·성공률·HP 증가분(만렙이면 false)
    public static bool TryGetNextStep(CardData _card, out GrowthStep _step)
    {
        _step = default;
        if (_card == null) return false;

        return TryGetStepAt(_card, GrowthOf(_card).Level + 1, out _step);
    }

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

        // 재화는 곡선이 정한다 — 진화 레벨(Lv5·Lv10)만 다이아를 물고 나머지는 골드다.
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

    // 레벨 _level로 올리는 한 스텝. 곡선 조회를 여기 하나로 모으는 이유는 튜토리얼 보정 때문이다 —
    // 안내가 강화를 시키는 동안은 비용을 0으로 눕히는데, 조회가 갈리면 화면엔 100골드가 뜨는데
    // 실제로는 0이 나가고 잔액이 모자란 유저는 버튼이 비활성으로 굳는다(표시·활성 판정·소모가 같은 값을 봐야 한다).
    static bool TryGetStepAt(CardData _card, int _level, out GrowthStep _step)
    {
        if (!Config.TryGetStep(_card, _level, out _step)) return false;

        if (OutgameTutorialRunner.IsCurrentAction(EOutgameTutorialAction.WaitEnhance))
            _step = new GrowthStep(_step.Level, _step.HpGain, _step.Currency, 0, _step.SuccessRate);

        return true;
    }

    // 레벨 하나에서 전투가 쓸 파생값을 전부 만든다(곡선·관문을 아는 것은 OutGame뿐이라는 규약).
    // _card가 null이면(카탈로그 미초기화·미등록) 키워드 해금만 비고 나머지는 그대로 — 조용히 레벨까지 잃지 않는다.
    static CardGrowth Snapshot(CardData _card, int _level)
    {
        CardGrowthConfig t_config = Config;
        return new CardGrowth(
            _level,
            t_config.HpBonusAt(_card, _level),
            t_config.EvolutionStageAt(_level),
            t_config.UnlockedKeywordsAt(_card, _level),
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
