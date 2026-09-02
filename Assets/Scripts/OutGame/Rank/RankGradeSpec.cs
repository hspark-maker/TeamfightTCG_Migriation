using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>서버에서 받은 RankGrade 표를 랭크 런타임 설정으로 조립한다.</summary>
public static class RankGradeSpec
{
    // Functions가 number로 안전하게 읽을 수 있는 정수 상한(Number.MAX_SAFE_INTEGER).
    const long ServerSafeIntegerMax = 9_007_199_254_740_991L;
    static RankConfig s_authoredSkin;
    static RankConfig s_uninitialized;
    static RankConfig s_runtime;

    internal static RankConfig UninitializedConfig
    {
        get
        {
            if (s_uninitialized != null) return s_uninitialized;
            s_uninitialized = ScriptableObject.CreateInstance<RankConfig>();
            s_uninitialized.name = "RankConfig (Uninitialized)";
            s_uninitialized.hideFlags = HideFlags.DontSave;
            s_uninitialized.winPoints = 0;
            s_uninitialized.losePoints = 0;
            s_uninitialized.unrankedDisplayName = "동기화 중";
            s_uninitialized.grades = new List<RankGradeConfig>();
            return s_uninitialized;
        }
    }

    public static void SetAuthoredSkin(RankConfig _authoredSkin) => s_authoredSkin = _authoredSkin;

    public static bool TryValidateRequired(out string _error)
        => TryReadRows(out _, out _error);

    public static bool TryBuildRuntime(out RankConfig _runtime, out string _error)
        => TryBuildRuntime(s_authoredSkin, out _runtime, out _error);

    public static bool TryBuildRuntime(RankConfig _authoredSkin, out RankConfig _runtime, out string _error)
    {
        _runtime = null;
        if (_authoredSkin == null)
        {
            _error = "RankConfig 스킨이 아직 등록되지 않았다 — BattleConfigStep이 이 스텝보다 먼저 서야 한다(프리팹의 스텝 순서·requiredIds 확인).";
            return false;
        }
        if (!TryReadRows(out List<ParsedGrade> t_rows, out _error)) return false;

        var t_badges = new Dictionary<ERankGrade, Sprite>();
        if (_authoredSkin.grades != null)
            foreach (RankGradeConfig t_authored in _authoredSkin.grades)
                if (t_authored != null && !t_badges.ContainsKey(t_authored.grade))
                    t_badges.Add(t_authored.grade, t_authored.badge);

        // 재시도가 여러 번 돌아도 복제본이 쌓이지 않게 직전 것을 버린다.
        if (s_runtime != null) UnityEngine.Object.Destroy(s_runtime);
        RankConfig t_runtime = UnityEngine.Object.Instantiate(_authoredSkin);
        t_runtime.name = _authoredSkin.name + " (ServerSpec)";
        t_runtime.hideFlags = HideFlags.DontSave;
        t_runtime.winPoints = t_rows[0].WinPoints;
        t_runtime.losePoints = t_rows[0].LosePoints;
        t_runtime.grades = new List<RankGradeConfig>(t_rows.Count);
        foreach (ParsedGrade t_row in t_rows)
        {
            t_badges.TryGetValue(t_row.Grade, out Sprite t_badge);
            t_runtime.grades.Add(new RankGradeConfig
            {
                grade = t_row.Grade,
                displayName = t_row.DisplayName,
                badge = t_badge,
                entryPoints = t_row.EntryPoints,
                pointsPerDivision = t_row.PointsPerDivision,
            });
        }

        s_runtime = t_runtime;
        _runtime = t_runtime;
        return true;
    }

    static bool TryReadRows(out List<ParsedGrade> _parsed, out string _error)
    {
        _parsed = new List<ParsedGrade>();
        _error = null;
        IReadOnlyList<RankGrade> t_source = SpecSource.Manager?.RankGrade?.All;
        if (t_source == null || t_source.Count == 0)
        {
            _error = "RankGrade 서버 표가 비어 있다.";
            return false;
        }

        var t_rows = new List<RankGrade>(t_source);
        foreach (RankGrade t_row in t_rows)
            if (t_row == null)
            {
                _error = "RankGrade 서버 표에 null 행이 있다.";
                return false;
            }
        t_rows.Sort((a, b) => a.id.CompareTo(b.id));

        Array t_requiredGrades = Enum.GetValues(typeof(ERankGrade));
        if (t_rows.Count != t_requiredGrades.Length)
        {
            _error = $"RankGrade 행 수 {t_rows.Count}가 앱의 등급 수 {t_requiredGrades.Length}와 다르다.";
            return false;
        }

        long t_winPoints = 0;
        long t_losePoints = 0;
        long t_previousEntry = long.MinValue;
        var t_seen = new HashSet<ERankGrade>();
        for (int i = 0; i < t_rows.Count; i++)
        {
            RankGrade t_row = t_rows[i];
            if (!Enum.TryParse(t_row.gradeKey, false, out ERankGrade t_grade) || !t_seen.Add(t_grade))
            {
                _error = $"RankGrade id={t_row.id}의 gradeKey '{t_row.gradeKey}'가 없거나 중복이다.";
                return false;
            }
            if (!string.Equals(t_row.gradeKey, t_grade.ToString(), StringComparison.Ordinal))
            {
                _error = $"RankGrade id={t_row.id}의 gradeKey '{t_row.gradeKey}'는 정식 enum 이름이 아니다.";
                return false;
            }
            if ((int)t_grade != i)
            {
                _error = $"RankGrade id 오름차순과 등급 순서가 다르다: index={i}, grade={t_grade}.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(t_row.displayName))
            {
                _error = $"RankGrade '{t_grade}' 표시명이 비어 있다.";
                return false;
            }
            if (t_row.entryPoints < 0 || t_row.entryPoints <= t_previousEntry ||
                t_row.entryPoints > ServerSafeIntegerMax ||
                t_row.pointsPerDivision <= 0 || t_row.pointsPerDivision > ServerSafeIntegerMax ||
                t_row.winPoints <= 0 || t_row.winPoints > ServerSafeIntegerMax ||
                t_row.losePoints <= 0 || t_row.losePoints > ServerSafeIntegerMax)
            {
                _error = $"RankGrade '{t_grade}'의 임계치·간격·승패 포인트가 유효하지 않다.";
                return false;
            }
            if (t_row.winPoints > long.MaxValue / RankConfig.WinsPerDivision ||
                t_row.pointsPerDivision != t_row.winPoints * RankConfig.WinsPerDivision)
            {
                _error = $"RankGrade '{t_grade}' pointsPerDivision은 winPoints x {RankConfig.WinsPerDivision}이어야 한다.";
                return false;
            }
            try
            {
                long t_lastDivision = checked(
                    t_row.entryPoints + (RankConfig.DivisionsPerGrade - 1L) * t_row.pointsPerDivision);
                if (t_lastDivision > ServerSafeIntegerMax)
                {
                    _error = $"RankGrade '{t_grade}'의 마지막 단계 임계치가 서버 안전 정수 범위를 넘는다.";
                    return false;
                }
            }
            catch (OverflowException)
            {
                _error = $"RankGrade '{t_grade}'의 단계 임계치 계산이 long 범위를 넘는다.";
                return false;
            }
            if (i == 0)
            {
                t_winPoints = t_row.winPoints;
                t_losePoints = t_row.losePoints;
            }
            else if (t_row.winPoints != t_winPoints || t_row.losePoints != t_losePoints)
            {
                _error = "현재 클라이언트와 서버 판정은 RankGrade 전 등급의 winPoints/losePoints가 같아야 한다.";
                return false;
            }

            _parsed.Add(new ParsedGrade(
                t_grade, t_row.displayName, t_row.entryPoints, t_row.pointsPerDivision,
                t_row.winPoints, t_row.losePoints));
            t_previousEntry = t_row.entryPoints;
        }
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        s_authoredSkin = null;
        s_uninitialized = null;
        s_runtime = null;
    }

    readonly struct ParsedGrade
    {
        public readonly ERankGrade Grade;
        public readonly string DisplayName;
        public readonly long EntryPoints;
        public readonly long PointsPerDivision;
        public readonly long WinPoints;
        public readonly long LosePoints;

        public ParsedGrade(
            ERankGrade _grade, string _displayName, long _entryPoints, long _pointsPerDivision,
            long _winPoints, long _losePoints)
        {
            Grade = _grade;
            DisplayName = _displayName;
            EntryPoints = _entryPoints;
            PointsPerDivision = _pointsPerDivision;
            WinPoints = _winPoints;
            LosePoints = _losePoints;
        }
    }
}
