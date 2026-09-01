using System;
using System.Collections.Generic;

/// <summary>서버 검증과 결정론 셔플이 공유하는 덱 순서 규약.</summary>
public static class DeckOrder
{
    public static void SortInPlace(List<int> _cardIds)
    {
        if (_cardIds == null) throw new ArgumentNullException(nameof(_cardIds));
        _cardIds.Sort();
    }

    public static void SortInPlace(int[] _cardIds, CardGrowth[] _growth)
    {
        if (_cardIds == null) throw new ArgumentNullException(nameof(_cardIds));
        if (_growth == null) throw new ArgumentNullException(nameof(_growth));
        if (_cardIds.Length != _growth.Length)
            throw new ArgumentException("Card IDs and growth snapshots must have the same length.");
        Array.Sort(_cardIds, _growth);
    }

    /// <summary>정규화된 덱을 소유자별 파생 스트림으로 Fisher-Yates 셔플한다.</summary>
    public static List<int> Derive(IReadOnlyList<int> _sortedIds, int _ownerIndex)
    {
        if (_sortedIds == null) throw new ArgumentNullException(nameof(_sortedIds));

        var t_result = new List<int>(_sortedIds.Count);
        for (int i = 0; i < _sortedIds.Count; i++) t_result.Add(_sortedIds[i]);

        MatchRandom.DerivedStream t_rng = MatchRandom.DeriveDeckStream(_ownerIndex);
        for (int i = t_result.Count - 1; i > 0; i--)
        {
            int t_j = t_rng.Range(i + 1);
            (t_result[i], t_result[t_j]) = (t_result[t_j], t_result[i]);
        }
        return t_result;
    }
}
