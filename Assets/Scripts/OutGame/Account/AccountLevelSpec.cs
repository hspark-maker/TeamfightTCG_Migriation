using System.Collections.Generic;
using UnityEngine;

// 계정 레벨 곡선(AccountLevel 표)의 조회 창구.
// 설정 SO를 두지 않는다 — 이 축에는 그림이 없어 저작본에 담을 것이 없고, 표가 곧 수치의 전부다
// (그림이 있는 랭크만 RankConfig를 스킨으로 들고 표가 그 복제본을 덮는다).
// 표를 못 읽으면 MaxLevel이 0으로 남는다. 임의 기본값을 내주면 안 된다 — 조용히 곡선을 지어내면
// 화면에 뜬 레벨이 시트와 다른 채로 굳는다.
public static class AccountLevelSpec
{
    static bool s_loaded;
    static long s_winExp;
    static long s_loseExp;

    // 인덱스 i = 레벨 (i+1)에 도달하는 데 필요한 누적 경험치. 엄격 증가이고 [0]은 항상 0이다.
    static readonly List<long> s_requiredExp = new List<long>();

    /// <summary>만렙(= 표 행 수). 표를 못 읽었으면 0이다.</summary>
    public static int MaxLevel { get { EnsureLoaded(); return s_requiredExp.Count; } }

    /// <summary>승리 1회 획득 경험치. 표를 못 읽었으면 0이다.</summary>
    public static long WinExp { get { EnsureLoaded(); return s_winExp; } }

    /// <summary>패배·무승부 1회 획득 경험치. 무승부가 패배와 같은 값을 받는 것은 골드 규칙과 같다.</summary>
    public static long LoseExp { get { EnsureLoaded(); return s_loseExp; } }

    // 초기화에서 1회. 지연 로드도 되지만 로비 첫 프레임에 파싱이 걸리지 않게 미리 당긴다.
    public static void Init() => EnsureLoaded();

    /// <summary>누적 경험치가 가리키는 레벨(1..MaxLevel). 표가 없으면 1.</summary>
    public static int ResolveLevel(long _exp)
    {
        EnsureLoaded();
        if (s_requiredExp.Count == 0) return 1;

        // 뒤에서 훑는다 — 도달한 가장 높은 행이 곧 레벨이다.
        for (int t_i = s_requiredExp.Count - 1; t_i >= 0; t_i--)
            if (_exp >= s_requiredExp[t_i]) return t_i + 1;

        return 1;
    }

    /// <summary>_level에 도달하는 데 필요한 누적 경험치. 범위 밖이면 false.</summary>
    public static bool TryGetRequiredExp(int _level, out long _exp)
    {
        EnsureLoaded();
        _exp = 0;
        if (_level < 1 || _level > s_requiredExp.Count) return false;

        _exp = s_requiredExp[_level - 1];
        return true;
    }

    /// <summary>레벨 표시가 설 수 있는 최소 저작인가. 초기화가 이걸 보고 복구 표시를 세운다 —
    /// 조용히 0으로 두면 만렙 1짜리 계정이 정상처럼 보인다.</summary>
    public static bool TryValidateRequired(out string _error)
    {
        EnsureLoaded();

        if (s_requiredExp.Count < 2)
        {
            _error = "AccountLevel 표를 읽지 못했거나 행이 2개 미만이다 — 계정 레벨이 오르지 않는다.";
            return false;
        }

        if (s_winExp <= 0)
        {
            _error = "AccountLevel.winExp가 0 이하다 — 이기고도 경험치가 오르지 않는다.";
            return false;
        }

        _error = null;
        return true;
    }

    static void EnsureLoaded()
    {
        if (s_loaded) return;
        s_loaded = true;   // 실패해도 매 조회마다 재파싱하지 않는다(곡선 없음으로 계속 돈다).

        IReadOnlyList<AccountLevel> t_source = SpecSource.Manager?.AccountLevel?.All;
        if (t_source == null || t_source.Count == 0) return;

        var t_rows = new List<AccountLevel>(t_source);
        t_rows.Sort((a, b) => (a?.id ?? 0).CompareTo(b?.id ?? 0));

        long t_previous = -1;
        for (int t_i = 0; t_i < t_rows.Count; t_i++)
        {
            AccountLevel t_row = t_rows[t_i];
            if (t_row == null)
            {
                Debug.LogError("[AccountLevelSpec] AccountLevel 표에 null 행이 있다 — 곡선을 버린다.");
                s_requiredExp.Clear();
                return;
            }
            // id가 곧 레벨이라 1부터 연속이어야 한다. 비면 그 위 레벨의 뜻이 통째로 밀린다.
            if (t_row.id != t_i + 1)
            {
                Debug.LogError($"[AccountLevelSpec] AccountLevel id가 연속이 아니다({t_i + 1}번째 행의 id={t_row.id}) — 곡선을 버린다.");
                s_requiredExp.Clear();
                return;
            }
            if (t_row.requiredExp <= t_previous)
            {
                Debug.LogError($"[AccountLevelSpec] AccountLevel id={t_row.id}의 requiredExp({t_row.requiredExp})가 이전 레벨보다 크지 않다 — 곡선을 버린다.");
                s_requiredExp.Clear();
                return;
            }

            s_requiredExp.Add(t_row.requiredExp);
            t_previous = t_row.requiredExp;
        }

        if (s_requiredExp.Count > 0 && s_requiredExp[0] != 0)
        {
            Debug.LogError($"[AccountLevelSpec] AccountLevel id=1의 requiredExp가 0이 아니다({s_requiredExp[0]}) — 시작 레벨에 도달하지 못한다.");
            s_requiredExp.Clear();
            return;
        }

        // 획득량은 전 행 동일이 규약이라 첫 행을 채택한다(RankGrade의 winPoints·losePoints와 같은 규칙).
        s_winExp  = t_rows[0].winExp;
        s_loseExp = t_rows[0].loseExp;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_loaded  = false;
        s_winExp  = 0;
        s_loseExp = 0;
        s_requiredExp.Clear();
    }
}
