using System;
using System.Collections.Generic;

// 상점에서 다시 획득할 수 있는 팩만 안내한다. 튜토리얼 지급 팩은 진열 목록에 없다.
internal static class CardAcquisitionSources
{
    internal readonly struct Source
    {
        public readonly string PackId;
        public readonly bool Available;
        public readonly ERankGrade Grade;

        public Source(string _packId, bool _available, ERankGrade _grade)
        {
            PackId = _packId;
            Available = _available;
            Grade = _grade;
        }

        public string Condition => Available ? "현재 획득 가능"
            : $"{PackUnlockRules.GradeLabel(Grade)} 랭크에서 획득 가능";
    }

    internal static List<Source> Resolve(int _card)
        => Resolve(_card, RankManager.IsRanked, RankManager.CurrentGrade);

    internal static List<Source> Resolve(int _card, bool _ranked, ERankGrade _grade)
    {
        var t_result = new List<Source>();
        if (_card <= 0 || !CardCatalog.Contains(_card)) return t_result;

        foreach (string t_pack in PackSpec.ShopPackIds)
        {
            bool t_available = PackUnlockRules.IsUnlocked(t_pack, _ranked, _grade)
                && Contains(t_pack, _grade, _card);
            if (t_available)
            {
                t_result.Add(new Source(t_pack, true, _grade));
                continue;
            }

            // 랭크별 드롭 풀은 합집합이 아니다. 해금 랭크의 실제 풀에 카드가 있어야 안내한다.
            foreach (ERankGrade t_grade in Enum.GetValues(typeof(ERankGrade)))
            {
                if (t_grade < _grade || !PackUnlockRules.IsUnlocked(t_pack, true, t_grade)
                    || !Contains(t_pack, t_grade, _card)) continue;
                t_result.Add(new Source(t_pack, false, t_grade));
                break;
            }
        }

        // 같은 그룹 안에서는 상점의 진열 순서를 보존한다.
        var t_ordered = new List<Source>(t_result.Count);
        foreach (Source t_source in t_result) if (t_source.Available) t_ordered.Add(t_source);
        foreach (Source t_source in t_result) if (!t_source.Available) t_ordered.Add(t_source);
        return t_ordered;
    }

    static bool Contains(string _pack, ERankGrade _grade, int _card)
    {
        foreach (WeightedCard t_drop in PackSpec.ResolveDrops(_pack, _grade))
            if (t_drop.cardId == _card) return true;
        return false;
    }
}
