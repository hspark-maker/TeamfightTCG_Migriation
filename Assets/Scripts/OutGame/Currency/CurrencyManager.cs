using System;

// 재화를 관리하는 static 단일 창구. 종류별 금액을 배열로 캐싱한다.
// 세이브 슬롯(CurrencySaveData) 매핑은 여기서만 안다. 잔액 음수 방지도 여기서 일원화.
public static class CurrencyManager
{
    // 종류별 금액. ECurrencyType을 인덱스로 사용(크기 = Count).
    static readonly long[] s_currencies = new long[(int)ECurrencyType.Count];

    // 잔액 변경 통지 (종류, 변경 후 금액) — UI 갱신용.
    public static event Action<ECurrencyType, long> OnCurrencyChanged;

    public static long Gold    => s_currencies[(int)ECurrencyType.Gold];
    public static long Diamond => s_currencies[(int)ECurrencyType.Diamond];

    public static long GetBalance(ECurrencyType _type) => s_currencies[(int)_type];

    public static bool CanAfford(ECurrencyType _type, long _cost) => s_currencies[(int)_type] >= _cost;

    // 부트에서 DataSaveManager.Load() 이후 1회 호출 — 세이브를 메모리에 캐싱.
    public static void Init()
    {
        var t_data = DataSaveManager.Data.currency;
        s_currencies[(int)ECurrencyType.Gold]    = t_data.gold;
        s_currencies[(int)ECurrencyType.Diamond] = t_data.diamond;
    }

    // 메모리 금액을 세이브 슬롯에 flush 후 영속화.
    public static void Save()
    {
        var t_data = DataSaveManager.Data.currency;
        t_data.gold    = s_currencies[(int)ECurrencyType.Gold];
        t_data.diamond = s_currencies[(int)ECurrencyType.Diamond];
        DataSaveManager.Save();
    }

    // 적립(음수는 무시).
    public static void Earn(ECurrencyType _type, long _amount)
    {
        if (_amount <= 0) return;

        s_currencies[(int)_type] += _amount;
        OnCurrencyChanged?.Invoke(_type, s_currencies[(int)_type]);
    }

    // 사용. 잔액 부족이면 아무것도 하지 않고 false.
    // 비용 0은 무료(0원) 결제로 허용 — 잔액 변경·이벤트 없이 성공(true). 음수만 거부.
    public static bool Spend(ECurrencyType _type, long _cost)
    {
        if (_cost < 0) return false;
        if (_cost == 0) return true;
        if (s_currencies[(int)_type] < _cost) return false;

        s_currencies[(int)_type] -= _cost;
        OnCurrencyChanged?.Invoke(_type, s_currencies[(int)_type]);
        return true;
    }
}
