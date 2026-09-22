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

/// <summary>선택한 해금 조건 하나만 평가한다.</summary>
public static class ContentUnlockRules
{
    public static ContentUnlockEvaluation Evaluate(ContentUnlockRule _rule, bool _levelReady, int _level,
        bool _ftueCompleted = false, bool _rankReady = false, bool _isRanked = false,
        int _bestTier = -1, int _requiredTier = -1)
    {
        EContentUnlockRequirement t_missing = _rule.Condition switch
        {
            EContentUnlockCondition.AccountLevel => !_levelReady ? EContentUnlockRequirement.Data
                : _level < _rule.MinAccountLevel ? EContentUnlockRequirement.AccountLevel : EContentUnlockRequirement.None,
            EContentUnlockCondition.Rank => !_rankReady || _requiredTier < 0 ? EContentUnlockRequirement.Data
                : !_isRanked || _bestTier < _requiredTier ? EContentUnlockRequirement.Rank : EContentUnlockRequirement.None,
            EContentUnlockCondition.FtueCompleted => !_ftueCompleted ? EContentUnlockRequirement.Ftue : EContentUnlockRequirement.None,
            _ => EContentUnlockRequirement.Data,
        };
        return new ContentUnlockEvaluation(t_missing);
    }
}
