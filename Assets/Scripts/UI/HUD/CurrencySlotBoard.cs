using System.Collections.Generic;

/// <summary>
/// 상단바 재화 칸의 배차 창구. 획득 연출이 요구하는 재화 중 <b>지금 어느 칸도 맡고 있지 않은 것</b>만
/// 남는 칸에 잠시 갈아입힌다 — 바뀌는 칸 수 = 결핍 재화 수이고, 나머지 칸은 손대지 않는다.
///
/// 에너지처럼 대표 HUD가 없는 재화는 도착지가 없어 연출이 통째로 스킵됐다. 그 재화가 들어오는 동안만
/// 칸 하나를 빌려 주고, 로비 탭을 넘어갈 때 고향 재화로 돌아간다.
///
/// 성공/실패 판정은 여기 없다 — 빌리지 못하면 기존 CurrencyHud.TryGet 실패 경로가 그대로 그 재화를 건너뛴다.
/// </summary>
public static class CurrencySlotBoard
{
    static readonly List<CurrencyHud> s_slots = new List<CurrencyHud>();
    static readonly bool[] s_requested = new bool[(int)ECurrencyType.Count];

    // 빌려준 순서. 다음에 또 결핍이 나면 가장 오래된 대여부터 회수한다(방금 준 칸을 도로 뺏지 않게).
    static int s_lendSerial;

    /// <summary>대표 칸을 배차 목록에 올린다. CurrencyHud.OnEnable에서 registerAsPrimary일 때만 부른다.</summary>
    public static void Register(CurrencyHud _hud)
    {
        if (_hud == null || s_slots.Contains(_hud)) return;

        s_slots.Add(_hud);
    }

    public static void Unregister(CurrencyHud _hud)
    {
        s_slots.Remove(_hud);
    }

    /// <summary>이번 획득이 요구하는 재화 집합만큼 칸을 빌린다.</summary>
    public static void Lend(CurrencyGainBucket _gains)
    {
        if (_gains == null || _gains.IsEmpty) return;

        for (int t_i = 0; t_i < s_requested.Length; t_i++)
            s_requested[t_i] = _gains[(ECurrencyType)t_i] > 0L;

        LendRequested();
    }

    /// <summary>단일 획득판. 요청 집합이 그 재화 하나일 뿐 몸통은 같다.</summary>
    public static void Lend(ECurrencyType _type)
    {
        for (int t_i = 0; t_i < s_requested.Length; t_i++)
            s_requested[t_i] = t_i == (int)_type;

        LendRequested();
    }

    static void LendRequested()
    {
        Compact();

        for (int t_i = 0; t_i < s_requested.Length; t_i++)
        {
            if (!s_requested[t_i]) continue;

            var t_type = (ECurrencyType)t_i;
            if (CurrencyHud.TryGet(t_type, out _)) continue;

            // 고향 칸이 있는 재화는 남의 칸을 빌리지 않는다 — 자기 칸을 먼저 되찾는다.
            // 이걸 건너뛰면 그 재화가 두 칸에 동시에 뜬다(고향 칸이 나중에 스스로 반납하므로).
            CurrencyHud t_home = FindHome(t_type);
            if (t_home != null)
            {
                if (!t_home.IsBusy) t_home.Return();
                continue;
            }

            // 그림이 없는 재화는 칸을 주지 않는다 — "골드 아이콘 아래 에너지 숫자"를 내보내지 않기 위함.
            if (CurrencyLook.BarIconOf(t_type) == null) continue;

            CurrencyHud t_victim = PickVictim();
            if (t_victim == null) continue;

            t_victim.Lend(t_type, ++s_lendSerial);
        }
    }

    // 그 재화를 고향으로 삼는 칸. 지금 다른 재화를 맡고 있어도(대여 중) 여기서 잡힌다.
    static CurrencyHud FindHome(ECurrencyType _type)
    {
        for (int t_i = 0; t_i < s_slots.Count; t_i++)
            if (s_slots[t_i].DefaultType == _type) return s_slots[t_i];

        return null;
    }

    // 희생 후보 중 가장 앞선 하나. 이번 요청이 필요로 하는 재화를 맡은 칸은 후보에서 빠진다.
    static CurrencyHud PickVictim()
    {
        CurrencyHud t_best = null;

        for (int t_i = 0; t_i < s_slots.Count; t_i++)
        {
            CurrencyHud t_slot = s_slots[t_i];
            if (!t_slot.IsLendable || t_slot.IsBusy) continue;
            if (s_requested[(int)t_slot.Type]) continue;

            if (t_best == null || IsBetterVictim(t_slot, t_best)) t_best = t_slot;
        }

        return t_best;
    }

    // 이미 빌려간 칸이 먼저(오래된 대여부터), 그 다음이 고향 칸(Shard→Diamond→Gold 순으로 내준다).
    static bool IsBetterVictim(CurrencyHud _candidate, CurrencyHud _best)
    {
        if (_candidate.IsLent != _best.IsLent) return _candidate.IsLent;

        if (_candidate.IsLent) return _candidate.LendSerial < _best.LendSerial;

        return (int)_candidate.DefaultType > (int)_best.DefaultType;
    }

    // 파괴됐는데 Unregister가 오지 않은 잔재를 걷는다.
    static void Compact()
    {
        for (int t_i = s_slots.Count - 1; t_i >= 0; t_i--)
            if (s_slots[t_i] == null) s_slots.RemoveAt(t_i);
    }
}
