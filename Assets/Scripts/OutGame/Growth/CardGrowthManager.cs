using System;
using System.Collections.Generic;
using UnityEngine;

// 카드 성장(강화 레벨)의 static 단일 창구. 세이브 슬롯(CardGrowthSaveData) 매핑을 여기서만 안다.
// 카드 키는 CardCatalog.KeyOf — 소유·덱 세이브와 정합. OwnershipManager와 동일한 부트/flush 결.
// 성장 상태의 진실원은 이 창구이고 전투는 CardGrowth 값을 받아 읽기만 한다.
public static class CardGrowthManager
{
    // 카드 키 → 성장 진행도(메모리 캐시). 값은 세이브 슬롯에 넣을 값 객체와 동일 참조.
    static readonly Dictionary<string, CardGrowthEntry> s_growth = new Dictionary<string, CardGrowthEntry>();

    // 강화 성공 판정용 로컬 랜덤. Battle/MatchRandom 재사용 금지(경계 위반) — 서비스 내부 System.Random.
    // 비결정론 무방(오프라인 단일 유저 강화).
    static readonly System.Random s_rng = new System.Random();

    static CardGrowthConfig s_config;

    static bool s_initialized;

    // 성장 변경 통지 — UI 갱신용. 강화 실패도 통지한다(재화가 줄었고 표시가 따라와야 한다).
    public static event Action OnGrowthChanged;

    // 미배선이면 기본 인스턴스로 동작한다(씬 배선 없이도 곡선·게이트가 살아 있게 — RewardService 선례).
    public static CardGrowthConfig Config
        => s_config != null ? s_config : (s_config = ScriptableObject.CreateInstance<CardGrowthConfig>());

    public static int MaxLevel => Config.MaxLevel;

    /// <summary>Init()으로 세이브를 캐싱했는지. false면 Save()가 no-op이라 성장 결과를 영속할 수 없다 —
    /// 그래서 결제 경로(TryEnhance)가 이 값을 결제 **전에** 본다.</summary>
    public static bool IsReady => s_initialized;

    /// <summary>부트스트랩에서 실제 애셋 주입(선택). null이면 기본 유지.</summary>
    public static void SetConfig(CardGrowthConfig _config)
    {
        if (_config != null) s_config = _config;
    }

    // 부트에서 DataSaveManager.Load() 이후 1회 호출. 세이브만 읽으므로 CardCatalog와 순서 무관.
    public static void Init()
    {
        s_growth.Clear();

        var t_data = DataSaveManager.Data.cardGrowth;
        if (t_data != null && t_data.entries != null)
        {
            foreach (var t_entry in t_data.entries)
            {
                if (t_entry == null || string.IsNullOrEmpty(t_entry.cardKey)) continue; // 손상/빈 키는 무시(예외 없음).
                if (s_growth.ContainsKey(t_entry.cardKey)) continue;                     // 중복 키는 첫 항목 유지.
                s_growth[t_entry.cardKey] = t_entry;
            }
        }

        s_initialized = true;
    }

    // 메모리 캐시를 세이브 슬롯에 flush 후 영속화. 현재 카탈로그에 없는 키도 그대로 보존(진행도 0 덮어쓰기 금지).
    // 미초기화(Init 전) 상태에서는 빈 캐시로 세이브를 덮지 않도록 no-op.
    public static void Save()
    {
        if (!s_initialized) return;

        var t_data = DataSaveManager.Data.cardGrowth ?? (DataSaveManager.Data.cardGrowth = new CardGrowthSaveData());
        t_data.version = CardGrowthSaveData.VERSION;
        t_data.entries = new List<CardGrowthEntry>(s_growth.Values);
        DataSaveManager.Save();
    }

    // ── 조회 ────────────────────────────────────────────────

    public static CardGrowth GrowthOf(CardData _card) => GrowthOf(CardCatalog.KeyOf(_card));

    // 미성장 카드는 default(Lv0). HP 보너스는 저장하지 않고 레벨에서 파생한다(곡선을 고쳐도 소급 반영).
    public static CardGrowth GrowthOf(string _key)
    {
        if (string.IsNullOrEmpty(_key)) return default;
        if (!s_growth.TryGetValue(_key, out var t_entry) || t_entry == null) return default;

        return new CardGrowth(t_entry.level, Config.HpBonusAt(t_entry.level));
    }

    public static int HpBonusOf(CardData _card) => GrowthOf(_card).HpBonus;

    // 다음 레벨의 비용·성공률·HP 증가분. 만렙이면 false.
    public static bool TryGetNextStep(CardData _card, out GrowthStep _step)
    {
        _step = default;
        if (_card == null) return false;

        return Config.TryGetStep(GrowthOf(_card).Level + 1, out _step);
    }

    // ── 성장 ────────────────────────────────────────────────

    // 강화 1회 시도. 결제(골드)까지 갔으면 확률로 성공/실패가 갈리고, 실패해도 골드는 소모된다(레벨 하락 없음).
    // 카드가 무효(null·빈 키)면 결제 없이 MaxLevel로 닫는다 — 열려 있는 시도 경로를 남기지 않기 위함.
    public static EnhanceResult TryEnhance(CardData _card)
    {
        // 미초기화면 Save()가 no-op이라 골드만 사라지고 레벨은 재시작 시 소실된다 → 결제 전에 닫는다.
        if (!s_initialized) return new EnhanceResult(EEnhanceOutcome.NotReady, 0);

        string t_key = CardCatalog.KeyOf(_card);
        if (string.IsNullOrEmpty(t_key)) return new EnhanceResult(EEnhanceOutcome.MaxLevel, 0);

        CardGrowthConfig t_config = Config;
        CardGrowth       t_growth = GrowthOf(t_key);
        int              t_level  = t_growth.Level;

        if (t_level >= t_config.MaxLevel) return new EnhanceResult(EEnhanceOutcome.MaxLevel, t_level);

        if (!t_config.TryGetStep(t_level + 1, out var t_step))
            return new EnhanceResult(EEnhanceOutcome.MaxLevel, t_level);

        if (!CurrencyManager.CanAfford(ECurrencyType.Gold, t_step.Cost))
            return new EnhanceResult(EEnhanceOutcome.NotAffordable, t_level);

        // CanAfford 통과 후에도 false면 방어 실패(차감 없음).
        if (!CurrencyManager.Spend(ECurrencyType.Gold, t_step.Cost))
            return new EnhanceResult(EEnhanceOutcome.NotAffordable, t_level);

        bool t_success = s_rng.NextDouble() < t_step.SuccessRate;
        if (t_success)
        {
            t_level = t_growth.Level + 1;
            Entry(t_key).level = t_level;
            Save();
        }

        // 실패해도 잔액이 변했으므로 영속·통지는 두 경우 모두 한다.
        CurrencyManager.Save();
        OnGrowthChanged?.Invoke();

        return new EnhanceResult(t_success ? EEnhanceOutcome.Success : EEnhanceOutcome.Failed, t_level);
    }

    // ── 디버그/유지보수 ─────────────────────────────────────

    // 성장 전체 초기화(디버그). 세이브에서도 제거. 진행도 손실 주의 — 정상 흐름에서 호출 금지.
    public static void DebugResetAll()
    {
        s_growth.Clear();
        Save();
        OnGrowthChanged?.Invoke();
    }

    // ── 내부 ────────────────────────────────────────────────

    // 캐시 엔트리 확보(없으면 생성). Lv0은 세이브에 남기지 않으므로 실제로 값이 오르는 직전에만 부른다.
    static CardGrowthEntry Entry(string _key)
    {
        if (s_growth.TryGetValue(_key, out var t_entry) && t_entry != null) return t_entry;

        t_entry = new CardGrowthEntry { cardKey = _key, level = 0 };
        s_growth[_key] = t_entry;
        return t_entry;
    }
}
