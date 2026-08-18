using System.Collections.Generic;

// 카드팩 구매·즉시 개봉의 static 파사드
public static class CardPackOpener
{
    static readonly System.Random s_rng = new System.Random();

    // 팩 구매·즉시 개봉 — 차감 → 드로우 → 소유 부여 → 중복 환급, 실패 시 차감 없이 사유 반환
    public static OpenedPack TryPurchase(CardPackData _pack)
    {
        if (_pack == null) return OpenedPack.CreateFailure(EPackOpenResult.PackNotFound);

        IReadOnlyList<WeightedCard> t_pool = _pack.ResolvePool(RankManager.CurrentGrade);
        if (t_pool.Count == 0) return OpenedPack.CreateFailure(EPackOpenResult.EmptyPool);

        ECurrencyType t_priceCurrency = _pack.PriceType;
        long t_price = _pack.Price;

        if (!CurrencyManager.CanAfford(t_priceCurrency, t_price))
            return OpenedPack.CreateFailure(EPackOpenResult.InsufficientGold);

        if (!CurrencyManager.Spend(t_priceCurrency, t_price))
            return OpenedPack.CreateFailure(EPackOpenResult.SpendFailed);

        ECurrencyType t_refundType = _pack.RefundType;
        List<DrawnCard> t_drawn = Draw(_pack, t_pool, t_refundType);

        CurrencyManager.Save();

        return OpenedPack.CreateSuccess(t_drawn, t_refundType);
    }

    static List<DrawnCard> Draw(CardPackData _pack, IReadOnlyList<WeightedCard> _pool, ECurrencyType _refundType)
    {
        long t_refundEach = _pack.RefundAmount;

        bool t_unique = _pack.UniqueDraw;
        int t_drawCount = _pack.DrawCount;
        if (t_unique && t_drawCount > _pool.Count) t_drawCount = _pool.Count;

        var t_candidates = new List<int>(_pool.Count);
        for (int t_i = 0; t_i < _pool.Count; t_i++) t_candidates.Add(t_i);

        var t_drawn = new List<DrawnCard>(t_drawCount);
        for (int t_i = 0; t_i < t_drawCount; t_i++)
        {
            int t_pick = PickWeightedCandidate(_pool, t_candidates);
            CardData t_card = _pool[t_candidates[t_pick]].card;
            if (t_unique) t_candidates.RemoveAt(t_pick);

            // null 풀 항목은 건너뛴다 — Grant(null)=false가 중복으로 오판돼 환급이 새어나간다
            if (t_card == null) continue;

            t_drawn.Add(GrantAndRefund(t_card, _refundType, t_refundEach));
        }
        return t_drawn;
    }

    static DrawnCard GrantAndRefund(CardData _card, ECurrencyType _refundType, long _refundEach)
    {
        if (OwnershipManager.Grant(CardCatalog.IdOf(_card))) return new DrawnCard(_card, true, 0, _refundType);

        CurrencyManager.Earn(_refundType, _refundEach);
        return new DrawnCard(_card, false, _refundEach, _refundType);
    }

    static int PickWeightedCandidate(IReadOnlyList<WeightedCard> _pool, List<int> _candidates)
    {
        int t_sum = 0;
        for (int t_i = 0; t_i < _candidates.Count; t_i++)
            t_sum += _pool[_candidates[t_i]].EffectiveWeight;

        int t_roll = s_rng.Next(t_sum);
        for (int t_i = 0; t_i < _candidates.Count; t_i++)
        {
            t_roll -= _pool[_candidates[t_i]].EffectiveWeight;
            if (t_roll < 0) return t_i;
        }
        return _candidates.Count - 1;
    }
}
