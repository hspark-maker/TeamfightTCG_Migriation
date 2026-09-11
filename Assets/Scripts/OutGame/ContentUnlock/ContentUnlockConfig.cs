using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>튜토리얼 SO에서 저작하는 콘텐츠별 AND 조건.</summary>
[Serializable]
public sealed class ContentUnlockDef
{
    [Tooltip("Mission, Adventure, Roulette 중 하나. 콘텐츠당 한 항목만 등록한다.")]
    public EOutgameFeature feature;
    [Tooltip("강제 선형 FTUE 전체 완료를 요구한다. 자율 안내 완료는 포함하지 않는다.")]
    public bool requireFtue;
    [Tooltip("체크하면 최소 랭크 조건을 사용한다. 최고 도달 랭크로 평가한다.")]
    public bool requireRank;
    [Tooltip("랭크 조건을 사용할 때 요구하는 최소 등급.")]
    public ERankGrade minRankGrade = ERankGrade.Bronze;
    [Tooltip("최소 랭크 단계. 랭크 조건을 사용하지 않으면 0.")]
    public int minRankDivision;
    [Tooltip("최소 계정 레벨. 0이면 계정 레벨 조건을 사용하지 않는다.")]
    public int minAccountLevel;
}

/// <summary>저작값에서 복사한 콘텐츠 해금 조건.</summary>
public readonly struct ContentUnlockRule
{
    public string ContentKey { get; }
    public bool RequireFtue { get; }
    public bool RequireRank { get; }
    public ERankGrade MinRankGrade { get; }
    public int MinRankDivision { get; }
    public int MinAccountLevel { get; }

    public ContentUnlockRule(ContentUnlockDef _row)
    {
        ContentUnlockManager.TryGetKey(_row.feature, out string t_key);
        ContentKey = t_key;
        RequireFtue = _row.requireFtue;
        RequireRank = _row.requireRank;
        MinRankGrade = _row.minRankGrade;
        MinRankDivision = _row.minRankDivision;
        MinAccountLevel = _row.minAccountLevel;
    }
}

/// <summary>초기화에서 주입받은 해금 설정. 튜토리얼 실행 상태와 독립적으로 조회한다.</summary>
public static class ContentUnlockConfig
{
    static readonly string[] s_requiredKeys = { "Mission", "Adventure", "Roulette" };
    static IReadOnlyList<ContentUnlockRule> s_rules = Array.Empty<ContentUnlockRule>();
    public static bool IsReady { get; private set; }
    public static IReadOnlyList<ContentUnlockRule> Rules => s_rules;

    public static bool TrySetSource(IReadOnlyList<ContentUnlockDef> _rows, out string _error)
    {
        IsReady = false;
        s_rules = Array.Empty<ContentUnlockRule>();
        if (!TryValidate(_rows, out _error)) return false;
        var t_rules = new List<ContentUnlockRule>();
        foreach (ContentUnlockDef t_row in _rows) t_rules.Add(new ContentUnlockRule(t_row));
        s_rules = t_rules.AsReadOnly();
        IsReady = true;
        return true;
    }

    public static bool TryGet(string _key, out ContentUnlockRule _rule)
    {
        foreach (ContentUnlockRule t_rule in s_rules)
            if (t_rule.ContentKey == _key) { _rule = t_rule; return true; }
        _rule = default;
        return false;
    }

    public static bool TryGet(IReadOnlyList<ContentUnlockDef> _rows, string _key, out ContentUnlockRule _rule)
    {
        if (_rows != null)
            foreach (ContentUnlockDef t_row in _rows)
                if (t_row != null && ContentUnlockManager.TryGetKey(t_row.feature, out string t_key) && t_key == _key)
                { _rule = new ContentUnlockRule(t_row); return true; }
        _rule = default;
        return false;
    }

    public static bool TryValidate(IReadOnlyList<ContentUnlockDef> _rows, out string _error)
    {
        _error = null;
        if (_rows == null || _rows.Count == 0) return Fail("SO에 해금 조건이 없습니다.", out _error);
        var t_keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (ContentUnlockDef t_row in _rows)
        {
            if (t_row == null || !ContentUnlockManager.TryGetKey(t_row.feature, out string t_key) || !t_keys.Add(t_key))
                return Fail("미등록 또는 중복 콘텐츠입니다.", out _error);
            if (!t_row.requireRank)
            {
                if (t_row.minRankDivision != 0) return Fail($"{t_key}: 랭크 미사용 단계는 0이어야 합니다.", out _error);
            }
            else if (!Enum.IsDefined(typeof(ERankGrade), t_row.minRankGrade)
                || t_row.minRankDivision < 1 || t_row.minRankDivision > RankConfig.DivisionsPerGrade)
                return Fail($"{t_key}: 잘못된 랭크 조건입니다.", out _error);
            if (t_row.minAccountLevel < 0) return Fail($"{t_key}: 계정 레벨은 0 이상이어야 합니다.", out _error);
        }
        foreach (string t_key in s_requiredKeys)
            if (!t_keys.Contains(t_key)) return Fail($"필수 콘텐츠 누락: {t_key}", out _error);
        return true;
    }

    /// <summary>원격 성장 표가 준비된 뒤 SO 조건의 실제 도달 가능 범위를 확인한다.</summary>
    public static bool TryValidateProgression(out string _error)
    {
        _error = null;
        if (!IsReady) return Fail("해금 설정이 초기화되지 않았습니다.", out _error);
        foreach (ContentUnlockRule t_rule in s_rules)
        {
            if (t_rule.RequireRank)
            {
                bool t_found = false;
                var t_ranks = SpecSource.Manager?.RankGrade?.All;
                if (t_ranks != null)
                    foreach (RankGrade t_rank in t_ranks)
                        if (t_rank != null && Enum.TryParse(t_rank.gradeKey, out ERankGrade t_grade)
                            && t_grade == t_rule.MinRankGrade) { t_found = true; break; }
                if (!t_found) return Fail($"{t_rule.ContentKey}: 랭크 표에 조건 등급이 없습니다.", out _error);
            }
            if (t_rule.MinAccountLevel > (SpecSource.Manager?.AccountLevel?.All?.Count ?? 0))
                return Fail($"{t_rule.ContentKey}: 계정 레벨 조건이 표 범위를 벗어납니다.", out _error);
        }
        return true;
    }

    static bool Fail(string _message, out string _error)
    {
        _error = "ContentUnlock: " + _message;
        return false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        IsReady = false;
        s_rules = Array.Empty<ContentUnlockRule>();
    }
}
