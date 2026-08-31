using System;
using System.Collections.Generic;

// 재화 잔액의 읽기 창구 (static)
// 잔액을 바꾸는 코드는 이 레이어에 없다 — 진실원은 서버 지갑 문서이고, 클라는 WalletCloud가 채택한 값을 비출 뿐이다.
public static class CurrencyManager
{
    static readonly long[] s_currencies = new long[(int)ECurrencyType.Count];

    // 잔액 변경 통지 (종류, 변경 후 금액)
    public static event Action<ECurrencyType, long> OnCurrencyChanged;

    public static long Gold    => s_currencies[(int)ECurrencyType.Gold];
    public static long Diamond => s_currencies[(int)ECurrencyType.Diamond];
    public static long Energy => s_currencies[(int)ECurrencyType.Energy];
    public static long Shard   => s_currencies[(int)ECurrencyType.Shard];

    public static long GetBalance(ECurrencyType _type) => s_currencies[(int)_type];

    public static bool CanAfford(ECurrencyType _type, long _cost) => s_currencies[(int)_type] >= _cost;

    /// <summary>서버 지갑 잔액을 메모리에 세운다. 부르는 곳은 <see cref="WalletCloud"/> 하나다.</summary>
    // 첫실행 지급은 여기 없다 — 스타터 골드의 진실원은 서버(freshAccount.ts)다.
    public static void Adopt(IReadOnlyDictionary<string, long> _balances)
    {
        for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++)
        {
            // 서버는 4키를 다 보내지만, 빠진 키가 예외가 되면 잔액 하나 때문에 초기화가 끊긴다.
            long t_balance = 0;
            if (_balances != null) _balances.TryGetValue(KeyOf((ECurrencyType)t_i), out t_balance);

            s_currencies[t_i] = t_balance;
        }

        for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++)
            OnCurrencyChanged?.Invoke((ECurrencyType)t_i, s_currencies[t_i]);
    }

    static string KeyOf(ECurrencyType _type) => _type.ToString();
}
