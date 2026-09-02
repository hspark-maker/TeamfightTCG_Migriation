using System;
using System.Collections.Generic;

// 재화 잔액의 읽기 창구 (static)
// 잔액을 바꾸는 코드는 이 레이어에 없다 — 진실원은 서버 지갑 문서이고, 클라는 WalletCloud가 채택한 값을 비출 뿐이다.
// 화면에 띄우는 값은 그 서버 잔액에 미해소 낙관 델타를 더한 것이다. 델타는 저장되지도 진실로 읽히지도 않고,
// 서버가 확정하는 순간 CurrencyPendingTicket이 걷는다 — 연출 길이와 서버 왕복 길이를 떼어놓기 위한 장치다.
public static class CurrencyManager
{
    static readonly long[] s_currencies = new long[(int)ECurrencyType.Count];

    // 아직 서버가 확정하지 않은 변동의 누계. 티켓이 여러 장 겹칠 수 있어 플래그가 아니라 카운터다.
    static readonly long[] s_pending = new long[(int)ECurrencyType.Count];

    // 낙관분을 통째로 버린 횟수. 그 전에 발행된 티켓은 이미 걷힌 몫을 또 빼게 되므로 이 값으로 걸러낸다.
    static int s_pendingEpoch;

    // 잔액 변경 통지 (종류, 변경 후 표시 금액)
    public static event Action<ECurrencyType, long> OnCurrencyChanged;

    // 소비 통지 (종류, 빠져나간 액수, 소비 후 표시 금액). 서버 왕복이 아니라 낙관 차감을 거는 순간 발화한다 —
    // 소비 롤다운은 "얼마가 빠졌는가"를 알아야 하는데 잔액 통지만으로는 그 폭을 알 수 없다.
    public static event Action<ECurrencyType, long, long> OnCurrencySpent;

    /// <summary>지금 발행되는 티켓이 속할 세대. <see cref="CurrencyPendingTicket"/> 이 걷어도 되는지 가릴 때 쓴다.</summary>
    public static int PendingEpoch => s_pendingEpoch;

    public static long Gold    => GetBalance(ECurrencyType.Gold);
    public static long Diamond => GetBalance(ECurrencyType.Diamond);
    public static long Energy  => GetBalance(ECurrencyType.Energy);
    public static long Shard   => GetBalance(ECurrencyType.Shard);

    /// <summary>화면에 띄울 잔액. 서버 잔액에 아직 확정되지 않은 변동을 더한 값이다.</summary>
    public static long GetBalance(ECurrencyType _type)
    {
        long t_shown = s_currencies[(int)_type] + s_pending[(int)_type];
        return t_shown > 0 ? t_shown : 0;
    }

    /// <summary>서버가 확정한 잔액. 낙관분과 갈라서 봐야 하는 진단·검증에만 쓴다.</summary>
    public static long GetServerBalance(ECurrencyType _type) => s_currencies[(int)_type];

    // 표시값 기준이라, 앞선 구매의 왕복이 끝나기 전에 또 사는 것이 별도 가드 없이 막힌다.
    public static bool CanAfford(ECurrencyType _type, long _cost) => GetBalance(_type) >= _cost;

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

        NotifyAll();
    }

    /// <summary>아직 확정되지 않은 변동을 표시값에 미리 얹는다.
    /// 부르는 곳은 <see cref="CurrencyPendingTicket"/> 하나다 — 걷는 짝이 없으면 표시값이 영영 어긋난다.</summary>
    public static void HoldPending(ECurrencyType _type, long _delta)
    {
        if (_delta == 0) return;

        s_pending[(int)_type] += _delta;

        // 소비를 먼저 알린다 — 잔액 통지가 앞서면 숫자가 새 값으로 툭 떨어져 롤다운이 굴릴 구간을 잃는다.
        if (_delta < 0) OnCurrencySpent?.Invoke(_type, -_delta, GetBalance(_type));

        Notify(_type);
    }

    /// <summary>얹어둔 변동을 걷는다. 부르는 곳은 <see cref="CurrencyPendingTicket"/> 하나다.
    /// 곧바로 <see cref="Adopt"/> 가 뒤따르는 자리에서는 <paramref name="_notify"/> 를 꺼야 한다 —
    /// 걷은 직후의 잔액은 서버 값이 아니라 "차감 전으로 되돌아간" 값이라, 그것이 화면에 닿으면 숫자가 거꾸로 튄다.</summary>
    public static void ReleasePending(ECurrencyType _type, long _delta, bool _notify = true)
    {
        if (_delta == 0) return;

        s_pending[(int)_type] -= _delta;

        if (_notify) Notify(_type);
    }

    /// <summary>남은 낙관분을 전부 버린다. 서버 잔액을 처음부터 다시 세우는 자리(초기화·재로그인)에서만 부른다.</summary>
    public static void ClearPending()
    {
        // 아직 왕복이 살아 있는 티켓이 나중에 자기 몫을 또 빼면 누계가 음수로 굳는다 — 세대를 올려 그것들을 무효로 만든다.
        s_pendingEpoch++;

        for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++) s_pending[t_i] = 0;

        NotifyAll();
    }

    static void Notify(ECurrencyType _type) => OnCurrencyChanged?.Invoke(_type, GetBalance(_type));

    static void NotifyAll()
    {
        for (int t_i = 0; t_i < (int)ECurrencyType.Count; t_i++) Notify((ECurrencyType)t_i);
    }

    static string KeyOf(ECurrencyType _type) => _type.ToString();
}
