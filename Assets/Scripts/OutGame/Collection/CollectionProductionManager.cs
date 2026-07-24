using System;
using System.Collections.Generic;
using UnityEngine;

// 도감 방치 생산 매니저 
public static class CollectionProductionManager
{
    // 행 키 → 진행도(메모리 캐시). 값은 세이브 슬롯에 넣을 값 객체와 동일 참조.
    static readonly Dictionary<string, CollectionRowProgress> s_progress = new Dictionary<string, CollectionRowProgress>();

    static bool s_initialized;

    // 도감 전체 완성 1회성 보상 수령 플래그(메모리 캐시). 세이브(CollectionSaveData.completionRewardClaimed)와 동기.
    // 부트에서 세이브값을 읽고, Save() 때 세이브에 함께 flush한다(true를 false로 덮지 않도록 메모리를 단일 진실로 유지).
    static bool s_completionRewardClaimed;

    // 수확·생산 상태 변동 통지 — UI 갱신용(F-18). 누적 성장 자체는 시간 기반이라 통지하지 않는다(폴링 조회).
    public static event Action OnChanged;

    // ── 부트 ────────────────────────────────────────────────

    // 부트에서 DataSaveManager.Load() 이후 1회 호출. CardCatalog/OwnershipManager와 무관하게 세이브만 읽는다
    // (행 해석은 조회 시점 lazy). 재호출 시 메모리 캐시를 세이브로 재구성.
    public static void Init()
    {
        s_progress.Clear();

        var t_data = DataSaveManager.Data.collection;
        if (t_data != null && t_data.rows != null)
        {
            foreach (var t_entry in t_data.rows)
            {
                if (t_entry == null || string.IsNullOrEmpty(t_entry.rowKey)) continue; // 손상/빈 키는 무시(예외 없음).
                if (s_progress.ContainsKey(t_entry.rowKey)) continue;                   // 중복 키는 첫 항목 유지.
                s_progress[t_entry.rowKey] = t_entry;
            }
        }

        // 완성 보상 수령 플래그 복원. 슬롯 미존재/구 세이브는 false(미수령)로 안전 처리 — 진행도 후퇴 아님.
        s_completionRewardClaimed = t_data != null && t_data.completionRewardClaimed;

        s_initialized = true;
    }

    // 메모리 진행도를 세이브 슬롯에 flush 후 영속화. 드리프트로 현재 배치에 없는 키도 그대로 보존(진행도 0 덮어쓰기 금지).
    // 미초기화(Init 전) 상태에서는 빈 캐시로 세이브를 덮지 않도록 no-op(전투 씬 등에서 Flush가 진행도를 지우는 사고 방지).
    public static void Save()
    {
        if (!s_initialized) return;

        var t_data = DataSaveManager.Data.collection ?? (DataSaveManager.Data.collection = new CollectionSaveData());
        t_data.version = CollectionSaveData.VERSION;
        t_data.rows = new List<CollectionRowProgress>(s_progress.Values);
        t_data.completionRewardClaimed = s_completionRewardClaimed; // 메모리 캐시를 함께 flush(rows 재작성에도 플래그 유실 없음).
        DataSaveManager.Save();
    }

    // ── 조회 API (UI/디버그) ────────────────────────────────

    // 현재 수확 가능한 정수 누적량(소수 버림). 미완성·미존재·엔트리 없음이면 0.
    public static long GetAccumulated(string _rowKey)
    {
        return (long)Math.Floor(ResolveByKey(_rowKey));
    }

    // 행 누적 상한. 미존재 키는 0.
    public static long GetCap(string _rowKey)
    {
        return CatalogRows.TryGetRow(_rowKey, out var t_row) ? t_row.Cap : 0;
    }

    // 최소 1 이상 수확할 수 있으면 true. 완성 여부와 무관 — 이미 굳은 누적은 잠겨도 청구 가능.
    public static bool CanHarvest(string _rowKey)
    {
        return GetAccumulated(_rowKey) >= 1;
    }

    // 행 생산 상태(3종 배타). 수확가능은 이 상태와 교차하는 별도 축이므로 CanHarvest로 조회한다.
    //   Locked   : 행 미완성 → 생산 정지(기존 누적은 보존, CanHarvest는 여전히 true일 수 있음)
    //   Producing: 완성 & 누적 < cap → cycleSeconds마다 1단위 누적 중
    //   Capped   : 완성 & 누적 >= cap → 상한 도달, 더 안 쌓임
    public static EProductionState GetState(string _rowKey)
    {
        if (!CatalogRows.TryGetRow(_rowKey, out var t_row)) return EProductionState.Locked;
        return Classify(t_row, Resolve(t_row));
    }

    // 상태 3종 배타 판정(단일 진실원). GetState/GetInfo가 공유해 cap 경계 규칙을 한 곳에서 유지한다.
    static EProductionState Classify(CatalogRow _row, double _raw)
    {
        if (!CatalogRows.IsRowComplete(_row)) return EProductionState.Locked;
        return _raw >= _row.Cap ? EProductionState.Capped : EProductionState.Producing;
    }

    // UI 1회 스냅샷(상태·누적·상한·수확가능·튜닝). 미존재 키는 기본값 스냅샷.
    public static RowProductionInfo GetInfo(string _rowKey)
    {
        if (!CatalogRows.TryGetRow(_rowKey, out var t_row))
            return new RowProductionInfo(_rowKey, EProductionState.Locked, 0, 0.0, 0, false, ECurrencyType.Gold, 0f);

        double t_raw = Resolve(t_row);
        long t_whole = (long)Math.Floor(t_raw);

        EProductionState t_state = Classify(t_row, t_raw);

        return new RowProductionInfo(
            t_row.Key, t_state, t_whole, t_raw, t_row.Cap, t_whole >= 1, t_row.RewardType, t_row.ProductionCycleSeconds);
    }

    // ── 수확 ────────────────────────────────────────────────

    // 행 하나 수확 → 정수 누적분을 CurrencyManager.Earn, 소수 잔여는 보존, 마지막정산=현재로 리셋.
    // 지급 즉시 컬렉션+재화를 함께 영속(크래시 유실 방지). 지급된 정수량 반환(없으면 0).
    public static long Harvest(string _rowKey)
    {
        if (!CatalogRows.TryGetRow(_rowKey, out var t_row)) return 0;

        long t_earned = HarvestCore(t_row);
        if (t_earned <= 0) return 0;

        Save();                 // 컬렉션 진행도 즉시 영속(누적 리셋 굳힘)
        CurrencyManager.Save(); // 재화 즉시 영속(Earn은 지연 flush라 크래시 유실 방지)
        OnChanged?.Invoke();
        return t_earned;
    }

    // 모든 행 일괄 수확. 여러 행/여러 재화 종류를 한 번에 지급하되 영속·통지는 1회로 묶는다.
    public static long HarvestAll()
    {
        long t_total = 0;

        var t_rows = CatalogRows.Rows;
        for (int t_i = 0; t_i < t_rows.Count; t_i++)
        {
            t_total += HarvestCore(t_rows[t_i]);
        }

        if (t_total <= 0) return 0;

        Save();
        CurrencyManager.Save();
        OnChanged?.Invoke();
        return t_total;
    }

    // 수확 코어(영속·통지 없음). Resolve로 정산 후 정수분을 Earn하고 소수만 남긴다.
    // 잠긴 행이라도 이미 굳은 누적은 청구 가능(생산만 멈춰 있을 뿐이므로).
    static long HarvestCore(CatalogRow _row)
    {
        if (_row == null || string.IsNullOrEmpty(_row.Key)) return 0;

        double t_raw = Resolve(_row);
        long t_earned = (long)Math.Floor(t_raw);
        if (t_earned <= 0) return 0;

        if (!s_progress.TryGetValue(_row.Key, out var t_entry)) return 0; // Resolve가 엔트리를 만들었어야 정상.

        t_entry.accumulated = t_raw - t_earned;                 // 정수분 제거, 소수 잔여 보존
        t_entry.lastSettleUtcTicks = GameClock.UtcNow.Ticks;    // 정산 기준점 갱신

        CurrencyManager.Earn(_row.RewardType, t_earned);
        return t_earned;
    }

    // ── 도감 전체 완성 1회성 보상 ───────────────────────────

    // 완성 보상을 이미 수령했는지(플래그 조회).
    public static bool IsCompletionRewardClaimed => s_completionRewardClaimed;

    // 완성 보상 종류(전역 튜닝). UI(F-20) 표시용.
    public static ECurrencyType CompletionRewardType => CatalogRows.CompletionRewardType;

    // 완성 보상량(전역 튜닝). UI(F-20) 표시용.
    public static long CompletionRewardAmount => CatalogRows.CompletionRewardAmount;

    // 지금 수령 가능한가 = 모든 행 완성 & 미수령. 미완성이거나 이미 수령이면 false.
    public static bool CanClaimCompletionReward => CatalogRows.IsAllComplete && !s_completionRewardClaimed;

    // 완성 보상 UI 1회 스냅샷(완성 여부·수령 여부·수령 가능·보상 종류/양). 표시 시점마다 다시 받는다.
    public static CompletionRewardInfo GetCompletionRewardInfo()
    {
        bool t_allComplete = CatalogRows.IsAllComplete;
        return new CompletionRewardInfo(
            t_allComplete,
            s_completionRewardClaimed,
            t_allComplete && !s_completionRewardClaimed,
            CatalogRows.CompletionRewardType,
            CatalogRows.CompletionRewardAmount);
    }

    // 완성 보상 수령: 수령 가능하면 재화 지급 → 플래그 true → 컬렉션+재화 즉시 영속 → 통지 → true.
    // 미초기화/미완성/이미 수령이면 아무것도 하지 않고 false(중복 지급·부트 전 지급 방지).
    public static bool ClaimCompletionReward()
    {
        // 부트 전 지급 방지: Save가 no-op이라 플래그가 안 남으면 재수령으로 중복 지급될 수 있다.
        if (!s_initialized) return false;
        if (!CanClaimCompletionReward) return false;

        CurrencyManager.Earn(CatalogRows.CompletionRewardType, CatalogRows.CompletionRewardAmount);
        s_completionRewardClaimed = true;

        Save();                 // 컬렉션 플래그 즉시 영속(수확과 동일 결)
        CurrencyManager.Save(); // 재화 즉시 영속(Earn은 지연 flush라 크래시 유실 방지)
        OnChanged?.Invoke();
        return true;
    }

    // ── 디버그/유지보수 ─────────────────────────────────────

    // 진행도 전체 초기화(디버그). 세이브에서도 제거. 진행도 손실 주의 — 정상 흐름에서 호출 금지.
    public static void DebugResetAll()
    {
        s_progress.Clear();
        s_completionRewardClaimed = false; // 완성 보상 재수령 가능 상태로 되돌림(디버그 전용).
        Save();
        OnChanged?.Invoke();
    }

    // ── 내부: 생산 정산(순수 시간 기반) ─────────────────────

    // 행 키로 현재 누적 원시값 계산. 미존재 키는 0.
    static double ResolveByKey(string _rowKey)
    {
        return CatalogRows.TryGetRow(_rowKey, out var t_row) ? Resolve(t_row) : 0.0;
    }

    // 행의 현재 누적 원시값을 계산하고, 필요 시 lazy 초기화·정산을 수행한다.
    //  - 미완성(잠김): 생산 정지. 기존 누적 보존. 마지막정산을 현재로 당겨(메모리) 재완성 시 잠금 구간을 계상하지 않는다.
    //  - 완성 & 엔트리 없음: lazy 초기화(마지막정산=현재, 누적=0)하고 즉시 영속(생산 시작점 굳힘). 이번 조회는 0.
    //  - 완성 & 엔트리 있음: 경과분을 누적(cap 클램프)하고 마지막정산=현재로 정산.
    static double Resolve(CatalogRow _row)
    {
        if (_row == null || string.IsNullOrEmpty(_row.Key)) return 0.0;

        string t_key = _row.Key;
        bool t_complete = CatalogRows.IsRowComplete(_row);
        bool t_has = s_progress.TryGetValue(t_key, out var t_entry);

        if (!t_complete)
        {
            if (t_has)
            {
                // 잠금 중: 성장 없음. 마지막정산만 현재로 당겨 재완성 시 잠금 구간이 누적에 섞이지 않게 한다(메모리 전용).
                t_entry.lastSettleUtcTicks = GameClock.UtcNow.Ticks;
                return t_entry.accumulated;
            }
            return 0.0; // 완성된 적 없음 → 엔트리 생성 안 함(생산은 최초 완성부터).
        }

        if (!t_has)
        {
            // 최초 완성 조회 → 지금부터 생산 시작. 시작점을 영속해 크래시에도 시작 시각을 보존한다.
            t_entry = new CollectionRowProgress
            {
                rowKey = t_key,
                lastSettleUtcTicks = GameClock.UtcNow.Ticks,
                accumulated = 0.0,
            };
            s_progress[t_key] = t_entry;
            Save();
            return 0.0;
        }

        // 정산: 마지막정산 이후 경과분을 누적(GameClock.Since가 역행을 0으로 클램프).
        // 주의(디버그 한정): lastSettle을 GameClock.UtcNow(=실제시각+DebugAdvance 오프셋)로 저장한다.
        // 오프셋은 세이브에 안 남으므로, DebugAdvance로 시간을 당긴 뒤 앱을 재시작하면 lastSettle이 미래가 되어
        // 실제시각이 따라잡을 때까지 Since가 0 클램프 → 그 기간 생산 정지. 프로덕션은 오프셋이 없어 무영향이며,
        // 디버그 검증은 한 세션 안에서(재시작 없이) DebugAdvance→조회/수확으로 하면 정확하다.
        var t_since = GameClock.Since(new DateTime(t_entry.lastSettleUtcTicks, DateTimeKind.Utc));
        double t_seconds = t_since.TotalSeconds;
        if (t_seconds > 0.0)
        {
            double t_cap = _row.Cap;
            double t_cycle = _row.ProductionCycleSeconds > 0f ? _row.ProductionCycleSeconds : 0.0; // 0/음수 오설정 방어
            if (t_cycle > 0.0)
            {
                double t_next = t_entry.accumulated + t_seconds / t_cycle; // 초당 1/cycle 단위 누적
                t_entry.accumulated = t_next < t_cap ? t_next : t_cap;     // cap 클램프(상한)
            }
            // cycle이 0/음수(생산 정지)라도 lastSettle은 당겨 시간 낭비 방지.
            t_entry.lastSettleUtcTicks = GameClock.UtcNow.Ticks;
        }
        return t_entry.accumulated;
    }
}

// 행 생산 상태(3종 배타). "수확가능"은 이 축과 교차하므로 CollectionProductionManager.CanHarvest로 별도 조회.
public enum EProductionState
{
    Locked,    // 행 미완성 → 생산 정지
    Producing, // 완성 & 누적 < cap → 누적 중
    Capped,    // 완성 & 누적 >= cap → 상한 도달
}

// 행 생산 상태 1회 스냅샷(UI용). 시간 경과에 따라 값이 바뀌므로 표시 시점마다 GetInfo로 다시 받는다.
public readonly struct RowProductionInfo
{
    public readonly string RowKey;
    public readonly EProductionState State;
    public readonly long Accumulated;        // 수확 가능한 정수 누적량(소수 버림)
    public readonly double AccumulatedRaw;    // 소수 포함 원시 누적량(진행바 비율용)
    public readonly long Cap;                 // 누적 상한
    public readonly bool CanHarvest;          // Accumulated >= 1
    public readonly ECurrencyType RewardType; // 수확 시 지급 재화 종류
    public readonly float ProductionCycleSeconds;  // 생산 사이클 시간(초)

    // 다음 1단위까지의 진행률(0~1). 정수 누적분(수확 가능분)을 뺀 소수부 = 현재 생산 사이클 진행.
    // 한 사이클(=재화 1단위)이 차면 소수부가 1→0으로 돌며 Accumulated가 +1 된다(진행바 리셋·누적 증가).
    // Capped(만땅)는 소수부가 0이라 여기서도 0 — 만땅의 "가득 참" 표시는 뷰에서 처리한다.
    public float CycleProgress01 => (float)(AccumulatedRaw - Accumulated);

    public RowProductionInfo(
        string _rowKey, EProductionState _state, long _accumulated, double _accumulatedRaw,
        long _cap, bool _canHarvest, ECurrencyType _rewardType, float _productionCycleSeconds)
    {
        RowKey = _rowKey;
        State = _state;
        Accumulated = _accumulated;
        AccumulatedRaw = _accumulatedRaw;
        Cap = _cap;
        CanHarvest = _canHarvest;
        RewardType = _rewardType;
        ProductionCycleSeconds = _productionCycleSeconds;
    }
}

// 도감 전체 완성 1회성 보상 1회 스냅샷(UI F-20용). 상태가 바뀌므로 표시 시점마다 GetCompletionRewardInfo로 다시 받는다.
public readonly struct CompletionRewardInfo
{
    public readonly bool AllComplete;         // 모든 행 완성 여부
    public readonly bool Claimed;             // 이미 수령했는지
    public readonly bool CanClaim;            // AllComplete && !Claimed
    public readonly ECurrencyType RewardType; // 수령 시 지급 재화 종류
    public readonly long RewardAmount;        // 수령 시 지급 재화량

    public CompletionRewardInfo(bool _allComplete, bool _claimed, bool _canClaim, ECurrencyType _rewardType, long _rewardAmount)
    {
        AllComplete = _allComplete;
        Claimed = _claimed;
        CanClaim = _canClaim;
        RewardType = _rewardType;
        RewardAmount = _rewardAmount;
    }
}
