using System;

// 재화 잔액 변경의 단일 창구 (static)
public static class CurrencyManager
{
    static readonly long[] s_currencies = new long[(int)ECurrencyType.Count];

    static bool s_initialized;

    // 잔액 변경 통지 (종류, 변경 후 금액)
    public static event Action<ECurrencyType, long> OnCurrencyChanged;
    public static event Action<ECurrencyType, long, long> OnCurrencySpent;

    public static long Gold    => s_currencies[(int)ECurrencyType.Gold];
    public static long Diamond => s_currencies[(int)ECurrencyType.Diamond];
    public static long Energy => s_currencies[(int)ECurrencyType.Energy];
    public static long Shard   => s_currencies[(int)ECurrencyType.Shard];

    public static long GetBalance(ECurrencyType _type) => s_currencies[(int)_type];

    public static bool CanAfford(ECurrencyType _type, long _cost) => s_currencies[(int)_type] >= _cost;

    // 부트에서 DataSaveManager.LoadAsync() 이후 1회 호출 — 세이브를 메모리에 캐싱
    public static void Init()
    {
        var t_data = DataSaveManager.Data.currency;
        t_data.Normalize();

        for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++)
            s_currencies[t_i] = t_data.balances[t_i];

        s_initialized = true;

        for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++)
            OnCurrencyChanged?.Invoke((ECurrencyType)t_i, s_currencies[t_i]);
    }

    /// <summary>캐시를 세이브 슬롯에 반영만 한다(디스크 쓰기 없음).
    /// 부트 전 커밋이 걸리면 빈 캐시가 저장된 잔액을 덮으므로 미초기화면 건너뛴다.</summary>
    internal static void FlushToData()
    {
        if (!s_initialized) return;

        var t_data = DataSaveManager.Data.currency;
        t_data.Normalize();

        for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++)
            t_data.balances[t_i] = s_currencies[t_i];
    }

    // 적립 (음수는 무시)
    public static void Earn(ECurrencyType _type, long _amount)
    {
        if (_amount <= 0) return;

        s_currencies[(int)_type] += _amount;
        OnCurrencyChanged?.Invoke(_type, s_currencies[(int)_type]);
    }

    // 사용 — 잔액 부족이면 변경 없이 false (비용 0은 무료 결제로 성공)
    public static bool Spend(ECurrencyType _type, long _cost)
    {
        if (_cost < 0) return false;
        if (_cost == 0) return true;
        if (s_currencies[(int)_type] < _cost) return false;

        s_currencies[(int)_type] -= _cost;
        long t_balance = s_currencies[(int)_type];
        OnCurrencySpent?.Invoke(_type, _cost, t_balance);
        OnCurrencyChanged?.Invoke(_type, t_balance);
        return true;
    }
}
