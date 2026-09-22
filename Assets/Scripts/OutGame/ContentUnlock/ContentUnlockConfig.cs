using System;
using System.Collections.Generic;
using UnityEngine;

public enum EContentUnlockCondition { AccountLevel = 0, Rank = 1, FtueCompleted = 2 }

/// <summary>콘텐츠마다 하나만 선택하는 해금 조건.</summary>
[Serializable]
public sealed class ContentUnlockDef
{
    [Tooltip("Mission, Adventure, Roulette, CardEnhance 중 하나. 콘텐츠당 한 항목만 등록한다.")]
    public EOutgameFeature feature;
    public EContentUnlockCondition condition;
    [Tooltip("콘텐츠가 해금되는 최소 계정 레벨. 1부터 계정 레벨 표의 최대 레벨까지 설정한다.")]
    public int minAccountLevel = 1;
    public ERankGrade minRankGrade = ERankGrade.Bronze;
    [Tooltip("최고 도달 랭크로 판정한다. 등급 내 단계는 1부터 시작한다.")]
    public int minRankDivision = 1;
}

/// <summary>저작값에서 복사한 콘텐츠 해금 조건.</summary>
public readonly struct ContentUnlockRule
{
    public string ContentKey { get; }
    public EContentUnlockCondition Condition { get; }
    public int MinAccountLevel { get; }
    public ERankGrade MinRankGrade { get; }
    public int MinRankDivision { get; }

    public ContentUnlockRule(ContentUnlockDef _row)
    {
        ContentUnlockManager.TryGetKey(_row.feature, out string t_key);
        ContentKey = t_key;
        Condition = _row.condition;
        MinAccountLevel = _row.minAccountLevel;
        MinRankGrade = _row.minRankGrade;
        MinRankDivision = _row.minRankDivision;
    }
}

/// <summary>초기화에서 주입받은 해금 설정. 튜토리얼 실행 상태와 독립적으로 조회한다.</summary>
public static class ContentUnlockConfig
{
    static readonly string[] s_requiredKeys = { "Mission", "Adventure", "Roulette", "CardEnhance" };
    static IReadOnlyList<ContentUnlockRule> s_rules = Array.Empty<ContentUnlockRule>();
    public static bool IsReady { get; private set; }
    public static ContentUnlockData Data { get; private set; }
    public static IReadOnlyList<ContentUnlockRule> Rules => s_rules;

    public static bool TrySetSource(ContentUnlockData _data, out string _error)
    {
        IsReady = false;
        Data = null;
        s_rules = Array.Empty<ContentUnlockRule>();
        var t_rows = _data != null ? _data.contentUnlocks : null;
        if (!TryValidate(t_rows, out _error)) return false;
        var t_rules = new List<ContentUnlockRule>();
        foreach (ContentUnlockDef t_row in t_rows) t_rules.Add(new ContentUnlockRule(t_row));
        s_rules = t_rules.AsReadOnly();
        Data = _data;
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
            if (!Enum.IsDefined(typeof(EContentUnlockCondition), t_row.condition))
                return Fail($"{t_key}: 해금 조건을 선택하세요.", out _error);
            if (t_row.condition == EContentUnlockCondition.AccountLevel && t_row.minAccountLevel < 1)
                return Fail($"{t_key}: 계정 레벨은 1 이상이어야 합니다.", out _error);
            if (t_row.condition == EContentUnlockCondition.Rank
                && (!Enum.IsDefined(typeof(ERankGrade), t_row.minRankGrade)
                    || t_row.minRankDivision < 1 || t_row.minRankDivision > RankConfig.DivisionsPerGrade))
                return Fail($"{t_key}: 랭크 등급·단계를 확인하세요.", out _error);
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
            if (t_rule.Condition == EContentUnlockCondition.AccountLevel && t_rule.MinAccountLevel > AccountLevelSpec.MaxLevel)
                return Fail($"{t_rule.ContentKey}: 계정 레벨 조건이 표 범위를 벗어납니다.", out _error);
            if (t_rule.Condition == EContentUnlockCondition.Rank && !TryGetRequiredTier(t_rule, out _))
                return Fail($"{t_rule.ContentKey}: 랭크 표에 요구 등급·단계가 없습니다.", out _error);
        }
        return true;
    }

    public static bool TryGetRequiredTier(ContentUnlockRule _rule, out int _tierIndex)
    {
        _tierIndex = -1;
        if (!RankManager.IsConfigured) return false;
        for (int t_i = 0; RankManager.TryGetTier(t_i, out RankTier t_tier); t_i++)
            if (t_tier.Grade == _rule.MinRankGrade && t_tier.Division == _rule.MinRankDivision)
            { _tierIndex = t_tier.Index; return true; }
        return false;
    }

    static bool Fail(string _message, out string _error)
    {
        _error = "ContentUnlock: " + _message;
        return false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Data = null;
        IsReady = false;
        s_rules = Array.Empty<ContentUnlockRule>();
    }
}
