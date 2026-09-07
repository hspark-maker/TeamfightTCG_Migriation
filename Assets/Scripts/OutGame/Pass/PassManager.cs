using System;
using System.Collections.Generic;

/// <summary>배틀패스 상태의 메모리 진실원. 시즌 정의·곡선·보상은 전부 서버가 준 값이고
/// 이 클래스는 사본을 만들지 않는다 — 레벨 판정도 서버가 준 currentLevel 을 그대로 쓴다.
///
/// <para>시즌이 끝나면 exp·수령 낙인이 리셋되는데 그 판정도 서버 몫이다. 클라가 기간을 재면
/// 기기 시계가 앞선 유저에게 다음 시즌 화면이 먼저 뜬다.</para>
/// </summary>
internal static class PassManager
{
    static PassGetResponse s_snapshot;

    internal static event Action OnChanged;

    /// <summary>서버 응답을 한 번이라도 채택했는가. false면 화면은 조회 중 상태로 그린다.</summary>
    internal static bool IsReady => s_snapshot != null;

    /// <summary>활성 시즌이 있는가. 시즌 공백기에는 false다(응답은 왔지만 season 이 null).</summary>
    internal static bool HasSeason => s_snapshot?.Season != null;

    internal static PassSeasonDefinition Season => s_snapshot?.Season;

    internal static IReadOnlyList<PassLevelDefinition> Levels
        => (IReadOnlyList<PassLevelDefinition>)s_snapshot?.Levels ?? Array.Empty<PassLevelDefinition>();

    internal static long Exp => s_snapshot?.Progress?.Exp ?? 0L;

    internal static int CurrentLevel => s_snapshot?.CurrentLevel ?? 0;

    /// <summary>다음 레벨의 누적 문턱. 최대 레벨이면 null이다.</summary>
    internal static long? NextRequiredExp => s_snapshot?.NextRequiredExp;

    internal static void Adopt(PassGetResponse _response)
    {
        if (_response == null) return;

        s_snapshot = _response;
        NotifyChanged();
    }

    /// <summary>수령 응답이 돌려준 진행 상태만 갈아끼운다. 정의·곡선은 마지막 getPass 결과를 유지한다.</summary>
    internal static void Adopt(PassProgress _progress)
    {
        if (_progress == null || s_snapshot == null) return;

        s_snapshot.Progress = _progress;
        s_snapshot.CurrentLevel = LevelOf(_progress.Exp);
        s_snapshot.NextRequiredExp = NextThresholdOf(s_snapshot.CurrentLevel);
        NotifyChanged();
    }

    internal static bool IsClaimed(int _level)
    {
        Dictionary<string, bool> t_claimed = s_snapshot?.Progress?.Claimed;
        return t_claimed != null && t_claimed.TryGetValue(_level.ToString(), out bool t_value) && t_value;
    }

    /// <summary>수령 가능한가. 서버가 다시 판정하므로 이 값은 버튼 표시용 낙관 검사다.</summary>
    internal static bool CanClaim(PassLevelDefinition _level)
        => _level != null && !PassCommands.IsInFlight(_level.Level) &&
           Exp >= _level.RequiredExp && !IsClaimed(_level.Level);

    internal static void NotifyCommandStateChanged() => NotifyChanged();

    internal static void Clear()
    {
        s_snapshot = null;
        NotifyChanged();
    }

    // 수령 응답에는 레벨이 실려 오지 않는다 — 곡선은 이미 있으므로 경험치로 되짚는다.
    static int LevelOf(long _exp)
    {
        int t_reached = 0;
        foreach (PassLevelDefinition t_level in Levels)
        {
            if (t_level == null || _exp < t_level.RequiredExp) break;
            t_reached = t_level.Level;
        }
        return t_reached;
    }

    static long? NextThresholdOf(int _currentLevel)
    {
        foreach (PassLevelDefinition t_level in Levels)
            if (t_level != null && t_level.Level > _currentLevel) return t_level.RequiredExp;

        return null;
    }

    static void NotifyChanged() => OnChanged?.Invoke();
}
