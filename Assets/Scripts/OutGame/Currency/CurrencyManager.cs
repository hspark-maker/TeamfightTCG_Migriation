using System;

// 재화 잔액 변경의 단일 창구 (static)
public static class CurrencyManager
{
    static readonly long[] s_currencies = new long[(int)ECurrencyType.Count];

    // 잔액 변경 통지 (종류, 변경 후 금액)
    public static event Action<ECurrencyType, long> OnCurrencyChanged;
    public static event Action<ECurrencyType, long, long> OnCurrencySpent;

    public static long Gold    => s_currencies[(int)ECurrencyType.Gold];
    public static long Diamond => s_currencies[(int)ECurrencyType.Diamond];

    public static long GetBalance(ECurrencyType _type) => s_currencies[(int)_type];

    public static bool CanAfford(ECurrencyType _type, long _cost) => s_currencies[(int)_type] >= _cost;

    // 부트에서 DataSaveManager.Load() 이후 1회 호출 — 세이브를 메모리에 캐싱
    public static void Init()
    {
        var t_data = DataSaveManager.Data.currency;
        s_currencies[(int)ECurrencyType.Gold]    = t_data.gold;
        s_currencies[(int)ECurrencyType.Diamond] = t_data.diamond;
        s_currencies[(int)ECurrencyType.Energy]  = t_data.energy;
    }

    // 메모리 금액을 세이브 슬롯에 flush 후 영속화
    public static void Save()
    {
        var t_data = DataSaveManager.Data.currency;
        t_data.gold    = s_currencies[(int)ECurrencyType.Gold];
        t_data.diamond = s_currencies[(int)ECurrencyType.Diamond];
        t_data.energy  = s_currencies[(int)ECurrencyType.Energy];
        DataSaveManager.Save();
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
