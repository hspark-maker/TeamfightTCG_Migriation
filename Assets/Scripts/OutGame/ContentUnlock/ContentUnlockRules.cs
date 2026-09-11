using System;

[Flags]
public enum EContentUnlockRequirement
{
    None = 0,
    Data = 1,
    Ftue = 2,
    Rank = 4,
    AccountLevel = 8,
}

/// <summary>콘텐츠 접근 가능 여부와 아직 충족하지 못한 조건.</summary>
public readonly struct ContentUnlockEvaluation
{
    public EContentUnlockRequirement Missing { get; }
    public bool IsUnlocked => Missing == EContentUnlockRequirement.None;

    public ContentUnlockEvaluation(EContentUnlockRequirement _missing) => Missing = _missing;
}

/// <summary>진행 사실과 표의 AND 조건을 비교한다.</summary>
public static class ContentUnlockRules
{
    public static ContentUnlockEvaluation Evaluate(ContentUnlockRule _rule, bool _ftueCompleted,
        bool _rankReady, bool _isRanked, int _bestTier, int _requiredTier, bool _levelReady, int _level)
    {
        EContentUnlockRequirement t_missing = EContentUnlockRequirement.None;
        if (_rule.RequireFtue && !_ftueCompleted) t_missing |= EContentUnlockRequirement.Ftue;
        if (_rule.RequireRank)
        {
            if (!_rankReady || _requiredTier < 0) t_missing |= EContentUnlockRequirement.Data;
            else if (!_isRanked || _bestTier < _requiredTier) t_missing |= EContentUnlockRequirement.Rank;
        }
        if (_rule.MinAccountLevel > 0)
        {
            if (!_levelReady) t_missing |= EContentUnlockRequirement.Data;
            else if (_level < _rule.MinAccountLevel) t_missing |= EContentUnlockRequirement.AccountLevel;
        }
        return new ContentUnlockEvaluation(t_missing);
    }
}
