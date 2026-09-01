public static class PackUnlockRules
{
    public static bool IsUnlocked(string _packId)
        => IsUnlocked(_packId, RankManager.IsRanked, RankManager.CurrentGrade);

    public static bool IsUnlocked(string _packId, bool _isRanked, ERankGrade _currentGrade)
    {
        if (!PackSpec.TryGetPack(_packId, out _)) return false;
        if (!PackSpec.TryGetMinRankGrade(_packId, out ERankGrade t_required)) return true;
        return _isRanked && _currentGrade >= t_required;
    }

    public static string UnlockLabel(string _packId)
    {
        if (!PackSpec.TryGetMinRankGrade(_packId, out ERankGrade t_required)) return string.Empty;
        return $"{GradeLabel(t_required)} 랭크에서 해금";
    }

    public static string GradeLabel(ERankGrade _grade)
    {
        switch (_grade)
        {
            case ERankGrade.Bronze:   return "브론즈";
            case ERankGrade.Silver:   return "실버";
            case ERankGrade.Gold:     return "골드";
            case ERankGrade.Platinum: return "플래티넘";
            case ERankGrade.Diamond:  return "다이아몬드";
            default:                  return _grade.ToString();
        }
    }
}
