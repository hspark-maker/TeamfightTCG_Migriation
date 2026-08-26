using System;

// 재화 잔액 변경의 단일 창구 (static)
public static class CurrencyManager
{
    // 신규 유저 최초 지급 골드
    const long STARTING_GOLD = 100;

    static readonly long[] s_currencies = new long[(int)ECurrencyType.Count];

    // 잔액 변경 통지 (종류, 변경 후 금액)
    public static event Action<ECurrencyType, long> OnCurrencyChanged;
    public static event Action<ECurrencyType, long, long> OnCurrencySpent;

    public static long Gold    => s_currencies[(int)ECurrencyType.Gold];
    public static long Diamond => s_currencies[(int)ECurrencyType.Diamond];
    public static long Energy => s_currencies[(int)ECurrencyType.Energy];
    public static long Shard   => s_currencies[(int)ECurrencyType.Shard];

    public static long GetBalance(ECurrencyType _type) => s_currencies[(int)_type];

    public static bool CanAfford(ECurrencyType _type, long _cost) => s_currencies[(int)_type] >= _cost;

    // 부트에서 클라우드 세이브 채택 이후 1회 호출 — 세이브를 메모리에 캐싱한다.
    // _freshAccount는 "원격 문서가 없는 신규 계정"이다. 오프라인 폴백 세션은 false로 들어온다.
    public static void Init(bool _freshAccount)
    {
        var t_data = DataSaveManager.Data.Currency;

        // 잔액 맵이 비어 있는 세이브도 첫실행으로 본다 — 튜토리얼 되감기가 슬롯을 갈아 끼웠거나
        // 콘솔에서 currency 맵을 통째로 지운 문서가 여기 걸린다. Normalize가 0을 채우기 전에 판정해야 한다.
        bool t_firstRun = _freshAccount || t_data.Balances == null || t_data.Balances.Count == 0;
        t_data.Normalize();

        if (t_firstRun) t_data.Balances[KeyOf(ECurrencyType.Gold)] = STARTING_GOLD;

        for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++)
            s_currencies[t_i] = t_data.Balances[KeyOf((ECurrencyType)t_i)];

        for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++)
            OnCurrencyChanged?.Invoke((ECurrencyType)t_i, s_currencies[t_i]);
    }

    // 메모리 금액을 세이브 슬롯에 flush 후 영속화
    public static void Save()
    {
        var t_data = DataSaveManager.Data.Currency;
        t_data.Normalize();

        for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++)
            t_data.Balances[KeyOf((ECurrencyType)t_i)] = s_currencies[t_i];

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

    static string KeyOf(ECurrencyType _type) => _type.ToString();
}
