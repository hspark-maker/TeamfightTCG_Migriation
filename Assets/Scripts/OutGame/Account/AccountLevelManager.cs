using System;
using UnityEngine;

// 계정 레벨(플레이로 쌓이는 성장)의 static 단일 창구 — 레벨은 누적 경험치의 순수 파생이라 세이브엔 경험치만 둔다.
public static class AccountLevelManager
{
    public static event Action OnChanged;

    /// <summary>곡선을 읽었는가. 표가 없으면 화면은 저작 더미를 그대로 둬야 한다.</summary>
    public static bool IsConfigured => AccountLevelSpec.MaxLevel > 0;

    // 누적 경험치
    public static long Exp => Slot.AccountExp;

    // 현재 레벨(경험치의 순수 파생)
    public static int Level => AccountLevelSpec.ResolveLevel(Exp);

    // 세이브를 캐시하지 않는다 — 서버가 profile 슬롯을 갈아끼울 때 ServerSlotRehydrator가 아직
    // 이 축을 재수화하지 않으므로, 캐시를 두면 채택 뒤에도 화면이 옛 값에 굳는다(AdventureProgress와 같은 처방).
    static ProfileSaveData Slot
    {
        get
        {
            var t_data = DataSaveManager.Data;
            if (t_data.Profile == null) t_data.Profile = new ProfileSaveData();
            return t_data.Profile;
        }
    }

    /// <summary>지금 화면이 그릴 스냅샷.</summary>
    public static AccountLevelInfo GetInfo() => GetInfoAt(Exp);

    /// <summary>임의의 누적 경험치가 그리는 스냅샷. 연출이 '오르기 직전' 화면을 물을 때 쓴다 —
    /// 정산은 이미 끝나 GetInfo는 최종 상태만 돌려준다.</summary>
    public static AccountLevelInfo GetInfoAt(long _exp)
    {
        int t_level = AccountLevelSpec.ResolveLevel(_exp);
        AccountLevelSpec.TryGetRequiredExp(t_level, out long t_levelRequired);

        bool t_hasNext = AccountLevelSpec.TryGetRequiredExp(t_level + 1, out long t_nextRequired);
        if (!t_hasNext) t_nextRequired = _exp;

        return new AccountLevelInfo(t_level, _exp, t_levelRequired, t_nextRequired, !t_hasNext);
    }

    /// <summary>전투 1회 정산 + 즉시 저장. 승리는 winExp, 그 밖(패배·무승부)은 loseExp — 예외가 없다.</summary>
    public static AccountLevelResult ApplyBattleResult(bool _won)
    {
        long t_before = Exp;
        int t_prevLevel = AccountLevelSpec.ResolveLevel(t_before);

        long t_gain = _won ? AccountLevelSpec.WinExp : AccountLevelSpec.LoseExp;
        if (t_gain <= 0) return new AccountLevelResult(0, t_prevLevel, t_prevLevel);

        // 만렙에 닿으면 더 쌓지 않는다 — 넘겨 두면 표를 늘렸을 때 잠자던 경험치가 한꺼번에 터진다.
        long t_ceiling = MaxExp;
        long t_after = t_ceiling > 0 ? Math.Min(t_before + t_gain, t_ceiling) : t_before + t_gain;

        Slot.AccountExp = t_after;
        Save();

        return new AccountLevelResult(t_after - t_before, t_prevLevel, AccountLevelSpec.ResolveLevel(t_after));
    }

    /// <summary>경험치를 더한다(디버그 전용). 만렙 구간은 전승 1,000판대라 이 문 없이는 확인할 수 없다.</summary>
    public static void AddExpForDebug(long _amount)
    {
        long t_ceiling = MaxExp;
        long t_next = Math.Max(Slot.AccountExp + _amount, 0);

        Slot.AccountExp = t_ceiling > 0 ? Math.Min(t_next, t_ceiling) : t_next;
        Save();
    }

    /// <summary>만렙으로 민다(디버그 전용).</summary>
    public static void FillToMaxForDebug()
    {
        long t_ceiling = MaxExp;
        if (t_ceiling <= 0) return;

        Slot.AccountExp = t_ceiling;
        Save();
    }

    /// <summary>레벨 1로 되돌린다(디버그 전용).</summary>
    public static void ResetForDebug()
    {
        Slot.AccountExp = 0;
        Save();
    }

    // 만렙 진입 누적치 = 경험치의 천장. 곡선이 없으면 0(천장 없음으로 다룬다).
    static long MaxExp
        => AccountLevelSpec.TryGetRequiredExp(AccountLevelSpec.MaxLevel, out long t_max) ? t_max : 0;

    static void Save()
    {
        DataSaveManager.Save();
        OnChanged?.Invoke();
    }
}

// 전투 1회 정산 결과
public readonly struct AccountLevelResult
{
    // 천장에 걸리면 요청 획득량보다 작다
    public readonly long Delta;
    public readonly int PrevLevel;
    public readonly int Level;

    public bool IsLevelUp => this.Level > this.PrevLevel;

    public AccountLevelResult(long _delta, int _prevLevel, int _level)
    {
        Delta     = _delta;
        PrevLevel = _prevLevel;
        Level     = _level;
    }
}

// 화면이 그리는 레벨 스냅샷
public readonly struct AccountLevelInfo
{
    public readonly int Level;
    public readonly long Exp;

    // 현재 레벨 진입 누적치 = 레벨 안 진행률의 0% 기준점
    public readonly long LevelRequiredExp;

    // 다음 레벨 진입 누적치(만렙이면 Exp와 같다 — 0 나눗셈·음수 잔여 차단)
    public readonly long NextRequiredExp;

    public readonly bool IsMaxLevel;

    /// <summary>이 레벨 안에서 지금까지 쌓은 경험치.</summary>
    public long ExpInLevel => this.Exp - this.LevelRequiredExp;

    /// <summary>이 레벨을 채우는 데 필요한 총량. 만렙이면 지금까지 쌓은 만큼으로 답해 게이지가 꽉 찬다.</summary>
    public long ExpToNext => this.IsMaxLevel ? this.ExpInLevel : this.NextRequiredExp - this.LevelRequiredExp;

    /// <summary>현재 레벨을 얼마나 채웠는가(0~1). 만렙은 1 — 더 갈 곳이 없어 비워 두면 오해가 된다.</summary>
    public float LevelProgress
    {
        get
        {
            if (this.IsMaxLevel) return 1f;

            long t_span = this.NextRequiredExp - this.LevelRequiredExp;
            return t_span <= 0 ? 1f : Mathf.Clamp01((float)this.ExpInLevel / t_span);
        }
    }

    public AccountLevelInfo(int _level, long _exp, long _levelRequiredExp, long _nextRequiredExp, bool _isMaxLevel)
    {
        Level            = _level;
        Exp              = _exp;
        LevelRequiredExp = _levelRequiredExp;
        NextRequiredExp  = _nextRequiredExp;
        IsMaxLevel       = _isMaxLevel;
    }
}
