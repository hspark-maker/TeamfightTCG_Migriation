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

    // 서버가 채택한 프로필을 직접 읽는다. 경험치 지급은 서버 명령에서만 확정한다.
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

    internal static void NotifyRehydrated() => OnChanged?.Invoke();
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
