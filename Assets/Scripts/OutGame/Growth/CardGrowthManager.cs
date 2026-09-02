using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

// 카드 성장(강화 레벨)의 static 단일 창구. 간식은 같은 세이브 항목을 써서 partial 조각(.Snack.cs)이 맡는다.
public static partial class CardGrowthManager
{
    static readonly Dictionary<int, CardGrowthEntry> s_growth = new Dictionary<int, CardGrowthEntry>();

    static bool s_initialized;

    // 성장 변경 통지(강화 실패도 통지 — 재화가 줄었다)
    public static event Action OnGrowthChanged;

    /// <summary>곡선·비용이 실제로 실렸는가. 스펙시트(CardEnhanceRule)가 진실원이 된 뒤로 "항상 준비"가 아니다 —
    /// 표를 못 읽으면 상한도 스텝도 비어 성장값이 통째로 어긋난 스냅샷이 나간다.</summary>
    public static bool IsConfigReady => GrowthSpec.CardMaxLevel > 0;

    public static int MaxLevel => GrowthRules.MaxLevel;
    public static int MaxStar => GrowthStar.FromLevel(MaxLevel);

    // 레벨 _level로 올리는 한 방이 진화인가(관문 레벨은 곡선이 소유한다)
    public static bool IsEvolutionLevel(int _level) => GrowthRules.IsEvolutionLevel(_level);

    // Init()으로 세이브를 캐싱했는지(false면 Save가 no-op)
    public static bool IsReady => s_initialized;

    // 초기화에서 클라우드 세이브 채택 이후 1회 호출
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

    /// <summary>메모리 캐시를 세이브 슬롯에 flush 후 영속화(미초기화면 no-op).
    /// <b>정상 플레이 호출부 0 — 디버그 전용이다.</b> cardGrowth 슬롯을 쓰는 쪽은 서버뿐이라,
    /// 정상 경로에서 부르면 응답 채택이 세운 업로드 기준선을 클라 값으로 덮는다.</summary>
    public static void Save()
    {
        if (!s_initialized) return;

        FlushToData();
        DataSaveManager.SaveCoalesced();
    }

    public static CardGrowth GrowthAtLevel(int _id, int _level)
        => Snapshot(_id, ClampLevel(_level), false, 0);

    /// <summary>카드 번호의 성장 스냅샷(기록이 없으면 미강화). HP 보너스·해금 상태는 저장값이 아니라 레벨에서 파생된다.
    /// 화면도 전투도 이 길 하나로 읽는다 — 서버가 확정하지 않은 값이 여기 얹히면 서버가 모르는 체력이
    /// lockDeck 에 실려 덱 잠금이 거절된다.</summary>
    public static CardGrowth GrowthOf(int _id) => Snapshot(_id, LevelOf(_id), true, LimitBreakOf(_id));

    // 카드의 현재 강화 레벨(기록 없음 = 미강화)
    public static int LevelOf(int _id)
    {
        if (_id <= 0) return CardGrowth.BaseLevel;
        if (!s_growth.TryGetValue(_id, out var t_entry) || t_entry == null) return CardGrowth.BaseLevel;

        // 바닥 아래 값은 미강화로 읽는다 — 레벨을 0부터 세던 시절의 세이브가 그렇다.
        return ClampLevel(t_entry.Level);
    }

    // 저장값은 저작 상한이 아니라 코드 천장으로 조인다 — 표가 상한을 낮춘 순간 클라만 레벨을 깎으면
    // 서버 덱 검증(고정 천장)과 갈려 lockDeck이 덱을 통째로 거절한다.
    // 서버가 내려준 AI 레벨도 같은 이유로 여기를 지난다(BattleGrowthBridge · ServerMatchmaker).
    public static int ClampLevel(int _level)
        => Mathf.Clamp(_level, CardGrowth.BaseLevel, GrowthSpec.CardMaxLevelCeiling);

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

    /// <summary>강화 한 방이 지금 성립하는가의 낙관 검사. 차감도 요청도 하지 않는다.</summary>
    public static EEnhanceOutcome Precheck(int _cardId) => Precheck(_cardId, out _);

    /// <summary>강화 1회를 서버에 요청한다(실패해도 비용은 소모, 레벨 하락 없음).
    /// 성공률·차감·레벨의 진실원은 서버 enhanceCard 다 — <see cref="Precheck"/> 는 왕복을 아끼는 낙관 검사일 뿐이라
    /// 서버가 다른 답을 주면 그쪽이 이긴다.</summary>
    public static async UniTask<EnhanceResult> TryEnhanceAsync(int _cardId)
    {
        int t_id = _cardId;

        EEnhanceOutcome t_precheck = Precheck(t_id, out GrowthStep t_step);
        if (t_precheck != EEnhanceOutcome.Success) return new EnhanceResult(t_precheck, LevelOf(t_id));

        // 무료 한 방의 조건은 클라 안내가 쥐고 있어 요청에 실어 보낸다 — 실제로 먹였는지는 응답이 답한다.
        bool t_freeShot = OutgameTutorialGuide.HasFreeShot(EOutgameTutorialAction.WaitEnhance);

        // 첫 await 이전이어야 유저가 누른 프레임에 잔액이 줄어든다. 걷는 쪽은 InvokeAsync 가 전담한다.
        CurrencyPendingTicket t_pending = CurrencyPendingTicket.Hold(t_step.Currency, -t_step.Cost);

        EnhanceCommandResult t_command = await EnhanceCommand.EnhanceCardAsync(t_id, t_freeShot, t_pending);

        // 결제 전에 막힌 결말은 값이 하나도 안 바뀌었다 — 통지 없이 물러난다(화면이 스스로 되돌린다).
        if (!t_command.Settled) return new EnhanceResult(t_command.Outcome, LevelOf(t_id));

        // 레벨·잔액은 응답 채택이 갈아끼운 슬롯을 ServerSlotRehydrator가 Init으로 다시 태워 이미 캐시에 있다 —
        // 여기서 대입하거나 저장하면 서버와 이중 진실원이 된다.
        int t_level = ClampLevel(t_command.Level);

        // 실패에는 걸지 않는다 — 닫아 버리면 안내가 시키는 강화를 유저 돈으로 다시 해야 한다.
        // 그 판정은 서버 몫이다(무료를 실제로 먹였는지는 차감한 쪽이 안다).
        if (t_command.FreeShotUsed) OutgameTutorialGuide.ConsumeFreeShot();

        OnGrowthChanged?.Invoke();

        return new EnhanceResult(t_command.Outcome, t_level);
    }

    /// <summary>전 카드를 만렙으로 올린다(디버그 전용). 반환값은 실제로 레벨이 오른 카드 수.
    /// TryEnhanceAsync를 안 타는 이유는 재화·성공률에 걸려 "전부 만렙"을 못 채우기 때문이다.</summary>
    public static int DebugMaxAll()
    {
        if (!s_initialized) return 0;

        int t_max     = GrowthRules.MaxLevel;
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

    // 성장 전체 재설정(디버그 전용, 진행도 손실)
    public static void DebugResetAll()
    {
        s_growth.Clear();
        OutgameTutorialGuide.ResetFreeShotForDebug();   // 강화를 처음부터 다시 보는 상태다
        Save();
        OnGrowthChanged?.Invoke();
    }

    // 사유의 순서가 곧 계약이다 — 호출부가 NotReady로 로그를 가르므로 초기화 판정이 만렙·비용보다 앞선다.
    static EEnhanceOutcome Precheck(int _cardId, out GrowthStep _step)
    {
        _step = default;

        if (!s_initialized) return EEnhanceOutcome.NotReady;
        if (_cardId <= 0) return EEnhanceOutcome.MaxLevel;

        int t_level = GrowthOf(_cardId).Level;
        if (t_level >= GrowthRules.MaxLevel) return EEnhanceOutcome.MaxLevel;

        if (!TryGetStepAt(_cardId, t_level + 1, out _step)) return EEnhanceOutcome.MaxLevel;

        // 낙관 델타가 얹힌 표시 잔액을 본다 — 연속 강화에서 아직 안 걷힌 차감분이 이중 결제로 통과하지 않는다.
        if (!CurrencyManager.CanAfford(_step.Currency, _step.Cost)) return EEnhanceOutcome.NotAffordable;

        return EEnhanceOutcome.Success;
    }

    // 튜토리얼 무료 보정을 여기 하나로 모은다 — 조회가 갈리면 표시·활성 판정·소모가 서로 다른 값을 본다.
    static bool TryGetStepAt(int _cardId, int _level, out GrowthStep _step)
    {
        if (!GrowthRules.TryGetStep(_cardId, _level, out _step)) return false;

        if (OutgameTutorialGuide.HasFreeShot(EOutgameTutorialAction.WaitEnhance))
            _step = new GrowthStep(_step.Level, _step.HpGain, _step.Currency, 0, _step.SuccessRate);

        return true;
    }

    // _card가 null이면(카탈로그 미초기화·미등록) 키워드 해금만 비고 나머지는 그대로 — 레벨까지 잃지 않는다.
    // 한계돌파 단계는 부르는 쪽이 골라 넘긴다 — 저장값과 표시값(낙관분 포함)이 갈리는 축이라
    // 여기서 직접 읽으면 어느 스냅샷이든 같은 값을 담게 되고, 그 순간 서버 제출에 확정 전 체력이 섞인다.
    static CardGrowth Snapshot(int _cardId, int _level, bool _includeKeywordGrowth, int _limitBreakStage)
    {
        CardKeyword t_unlockedKeywords = GrowthRules.UnlockedKeywordsAt(_cardId, _level);
        int t_hpBonus = GrowthRules.HpBonusAt(_cardId, _level);
        if (_includeKeywordGrowth)
        {
            t_hpBonus += KeywordGrowthManager.HpBonusFor(t_unlockedKeywords);
            t_hpBonus += GrowthRules.LimitBreakHpBonusAt(_limitBreakStage);
        }

        return new CardGrowth(
            _level,
            t_hpBonus,
            GrowthRules.EvolutionStageAt(_level),
            t_unlockedKeywords,
            GrowthRules.SynergyUnlockedAt(_level));
    }

    static CardGrowthEntry Entry(int _id)
    {
        if (s_growth.TryGetValue(_id, out var t_entry) && t_entry != null) return t_entry;

        t_entry = new CardGrowthEntry { Level = CardGrowth.BaseLevel };
        s_growth[_id] = t_entry;
        return t_entry;
    }
}
