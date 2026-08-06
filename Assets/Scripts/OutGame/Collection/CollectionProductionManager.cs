using System;
using System.Collections.Generic;

// 도감 방치 생산 매니저
public static class CollectionProductionManager
{
    static readonly Dictionary<int, CollectionRowProgress> s_progress = new Dictionary<int, CollectionRowProgress>();

    static bool s_initialized;

    // 수확·생산 상태 변동 통지 — UI 갱신용(누적 성장 자체는 폴링 조회)
    public static event Action OnChanged;

    // 부트에서 DataSaveManager.Load() 이후 1회 호출
    public static void Init()
    {
        s_progress.Clear();

        var t_data = DataSaveManager.Data.collection;
        bool t_migrated = false;
        if (t_data != null && t_data.rows != null)
        {
            foreach (var t_entry in t_data.rows)
            {
                if (t_entry == null) continue;

                int t_id = t_entry.rowId;
                if (t_id <= 0)
                {
                    // 구 세이브(행 첫 카드 이름) 이관 — 카탈로그 미준비면 이번 부트는 건너뛰고 값을 보존한다.
                    if (!CardCatalog.IsReady) continue;

                    t_id = CardCatalog.LegacyIdOfName(t_entry.rowKey);
                    if (t_id <= 0) continue;                 // 사라진 행의 누적은 버린다

                    t_entry.rowId  = t_id;
                    t_entry.rowKey = null;
                    t_migrated     = true;
                }

                if (s_progress.ContainsKey(t_id)) continue;
                s_progress[t_id] = t_entry;
            }
        }

        s_initialized = true;

        if (t_migrated) Save();
    }

    // 메모리 진행도를 세이브 슬롯에 flush 후 영속화(Init 전이면 no-op — 빈 캐시로 덮어쓰기 방지)
    public static void Save()
    {
        if (!s_initialized) return;

        var t_data = DataSaveManager.Data.collection ?? (DataSaveManager.Data.collection = new CollectionSaveData());
        t_data.version = CollectionSaveData.VERSION;
        t_data.rows = new List<CollectionRowProgress>(s_progress.Values);
        DataSaveManager.Save();
    }

    // 현재 수확 가능한 정수 누적량(소수 버림)
    public static long GetAccumulated(int _rowId)
    {
        return CatalogRows.TryGetRow(_rowId, out var t_row) ? HarvestableOf(t_row) : 0;
    }

    // 전 행의 수확 가능한 정수 누적 합계(일괄 수령 버튼용)
    public static long GetTotalHarvestable()
    {
        long t_total = 0;

        var t_rows = CatalogRows.Rows;
        for (int t_i = 0; t_i < t_rows.Count; t_i++)
        {
            t_total += HarvestableOf(t_rows[t_i]);
        }
        return t_total;
    }

    // 행 누적 상한
    public static long GetCap(int _rowId)
    {
        return CatalogRows.TryGetRow(_rowId, out var t_row) ? t_row.Cap : 0;
    }

    // 최소 1 이상 수확 가능한지 — 잠긴 행이라도 이미 굳은 누적은 청구 가능
    public static bool CanHarvest(int _rowId)
    {
        return GetAccumulated(_rowId) >= 1;
    }

    // 행 생산 상태(수확 가능 여부는 별도 축 — CanHarvest로 조회)
    public static EProductionState GetState(int _rowId)
    {
        if (!CatalogRows.TryGetRow(_rowId, out var t_row)) return EProductionState.Locked;
        return Classify(t_row, Resolve(t_row));
    }

    static EProductionState Classify(CatalogRow _row, double _raw)
    {
        if (!CatalogRows.IsRowComplete(_row)) return EProductionState.Locked;
        return _raw >= _row.Cap ? EProductionState.Capped : EProductionState.Producing;
    }

    // UI 1회 스냅샷(상태·누적·상한·수확가능·튜닝)
    public static RowProductionInfo GetInfo(int _rowId)
    {
        if (!CatalogRows.TryGetRow(_rowId, out var t_row))
            return new RowProductionInfo(_rowId, EProductionState.Locked, 0, 0.0, 0, false, ECurrencyType.Gold, 0f);

        double t_raw = Resolve(t_row);
        long t_whole = (long)Math.Floor(t_raw);

        EProductionState t_state = Classify(t_row, t_raw);

        return new RowProductionInfo(
            t_row.Id, t_state, t_whole, t_raw, t_row.Cap, t_whole >= 1, t_row.RewardType, t_row.ProductionCycleSeconds);
    }

    // 행 하나 수확 — 정수 누적분 지급, 지급 재화·지급량 반환
    public static CurrencyGain Harvest(int _rowId)
    {
        if (!CatalogRows.TryGetRow(_rowId, out var t_row)) return CurrencyGain.None;

        long t_earned = HarvestCore(t_row);
        if (t_earned <= 0) return CurrencyGain.None;

        Save();
        CurrencyManager.Save();
        OnChanged?.Invoke();
        return new CurrencyGain(t_row.RewardType, t_earned);
    }

    // 모든 행 일괄 수확 — 영속·통지는 1회로 묶음. 행마다 재화가 달라도 종류별로 나뉘어 담긴다
    public static CurrencyGainBucket HarvestAll()
    {
        var t_gains = new CurrencyGainBucket();

        var t_rows = CatalogRows.Rows;
        for (int t_i = 0; t_i < t_rows.Count; t_i++)
        {
            t_gains.Add(t_rows[t_i].RewardType, HarvestCore(t_rows[t_i]));
        }

        if (t_gains.IsEmpty) return t_gains;

        Save();
        CurrencyManager.Save();
        OnChanged?.Invoke();
        return t_gains;
    }

    static long HarvestCore(CatalogRow _row)
    {
        long t_earned = HarvestableOf(_row);
        if (t_earned <= 0) return 0;

        if (!s_progress.TryGetValue(_row.Id, out var t_entry)) return 0;

        t_entry.accumulated -= t_earned;
        t_entry.lastSettleUtcTicks = GameClock.UtcNow.Ticks;

        CurrencyManager.Earn(_row.RewardType, t_earned);
        return t_earned;
    }

    static long HarvestableOf(CatalogRow _row)
    {
        if (_row == null || _row.Id <= 0) return 0;
        return (long)Math.Floor(Resolve(_row));
    }

    // 진행도 전체 초기화(디버그 전용 — 정상 흐름 호출 금지)
    public static void DebugResetAll()
    {
        s_progress.Clear();
        Save();
        OnChanged?.Invoke();
    }

    static double Resolve(CatalogRow _row)
    {
        if (_row == null || _row.Id <= 0) return 0.0;

        int t_id = _row.Id;
        bool t_complete = CatalogRows.IsRowComplete(_row);
        bool t_has = s_progress.TryGetValue(t_id, out var t_entry);

        if (!t_complete)
        {
            if (t_has)
            {
                // 잠금 구간이 재완성 후 누적에 섞이지 않도록 마지막정산만 당긴다
                t_entry.lastSettleUtcTicks = GameClock.UtcNow.Ticks;
                return t_entry.accumulated;
            }
            return 0.0;
        }

        if (!t_has)
        {
            t_entry = new CollectionRowProgress
            {
                rowId = t_id,
                lastSettleUtcTicks = GameClock.UtcNow.Ticks,
                accumulated = 0.0,
            };
            s_progress[t_id] = t_entry;
            Save();
            return 0.0;
        }

        var t_since = GameClock.Since(new DateTime(t_entry.lastSettleUtcTicks, DateTimeKind.Utc));
        double t_seconds = t_since.TotalSeconds;
        if (t_seconds > 0.0)
        {
            double t_cap = _row.Cap;
            double t_cycle = _row.ProductionCycleSeconds > 0f ? _row.ProductionCycleSeconds : 0.0;
            if (t_cycle > 0.0)
            {
                double t_next = t_entry.accumulated + t_seconds / t_cycle;
                t_entry.accumulated = t_next < t_cap ? t_next : t_cap;
            }
            t_entry.lastSettleUtcTicks = GameClock.UtcNow.Ticks;
        }
        return t_entry.accumulated;
    }
}

// 행 생산 상태(3종 배타)
public enum EProductionState
{
    Locked,
    Producing,
    Capped,
}

// 행 생산 상태 1회 스냅샷(UI용)
public readonly struct RowProductionInfo
{
    public readonly int RowId;
    public readonly EProductionState State;
    // 수확 가능한 정수 누적량(소수 버림)
    public readonly long Accumulated;
    // 소수 포함 원시 누적량(진행바 비율용)
    public readonly double AccumulatedRaw;
    public readonly long Cap;
    public readonly bool CanHarvest;
    public readonly ECurrencyType RewardType;
    public readonly float ProductionCycleSeconds;

    // 다음 1단위까지의 진행률(0~1)
    public float CycleProgress01 => (float)(AccumulatedRaw - Accumulated);

    public RowProductionInfo(
        int _rowId, EProductionState _state, long _accumulated, double _accumulatedRaw,
        long _cap, bool _canHarvest, ECurrencyType _rewardType, float _productionCycleSeconds)
    {
        RowId = _rowId;
        State = _state;
        Accumulated = _accumulated;
        AccumulatedRaw = _accumulatedRaw;
        Cap = _cap;
        CanHarvest = _canHarvest;
        RewardType = _rewardType;
        ProductionCycleSeconds = _productionCycleSeconds;
    }
}
