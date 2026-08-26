using System.Collections.Generic;

// 카드팩 구매·즉시 개봉의 static 파사드
public static class CardPackOpener
{
    static readonly System.Random s_rng = new System.Random();

    // 중복 1장이 주는 간식 수. 1:1로 묶어야 "한계돌파 N회 = 중복 몇 장"이 그대로 읽힌다.
    public const int SnackPerDuplicate = 1;

    // 팩 구매·즉시 개봉 — 차감 → 드로우 → 소유 부여 → 중복 간식 적립, 실패 시 차감 없이 사유 반환
    public static OpenedPack TryPurchase(CardPackData _pack)
    {
        if (_pack == null) return OpenedPack.CreateFailure(EPackOpenResult.PackNotFound);
        if (!PackUnlockRules.IsUnlocked(_pack))
            return OpenedPack.CreateFailure(EPackOpenResult.RankLocked);
        if (!CardCatalog.IsReady || !CardGrowthManager.IsReady)
            return OpenedPack.CreateFailure(EPackOpenResult.NotReady);

        IReadOnlyList<WeightedCard> t_resolvedPool = _pack.ResolvePool(RankManager.CurrentGrade);
        var t_pool = new List<WeightedCard>(t_resolvedPool.Count);
        for (int t_i = 0; t_i < t_resolvedPool.Count; t_i++)
        {
            WeightedCard t_entry = t_resolvedPool[t_i];
            if (t_entry.card != null && CardCatalog.Contains(CardCatalog.IdOf(t_entry.card)))
                t_pool.Add(t_entry);
        }
        if (t_pool.Count == 0) return OpenedPack.CreateFailure(EPackOpenResult.EmptyPool);

        ECurrencyType t_priceCurrency = _pack.PriceType;
        long t_price = _pack.Price;

        if (!CurrencyManager.CanAfford(t_priceCurrency, t_price))
            return OpenedPack.CreateFailure(EPackOpenResult.InsufficientGold);

        if (!CurrencyManager.Spend(t_priceCurrency, t_price))
            return OpenedPack.CreateFailure(EPackOpenResult.SpendFailed);

        // 재화 환급은 나가지 않는다(중복 보상 = 간식). 팩의 환급 저작값은 되돌릴 여지를 두려고 남긴다.
        ECurrencyType t_refundType = _pack.RefundType;
        List<DrawnCard> t_drawn = Draw(_pack, t_pool);

        // 전부 중복이면 소유가 안 변해 지급 쪽 커밋이 걸리지 않는다 — 간식·차감을 여기서 확정한다.
        SaveTransaction.Request();

        return OpenedPack.CreateSuccess(t_drawn, t_refundType);
    }

    static List<DrawnCard> Draw(CardPackData _pack, IReadOnlyList<WeightedCard> _pool)
    {
        bool t_unique = _pack.UniqueDraw;
        int t_drawCount = _pack.DrawCount;
        if (t_unique && t_drawCount > _pool.Count) t_drawCount = _pool.Count;

        var t_candidates = new List<int>(_pool.Count);
        for (int t_i = 0; t_i < _pool.Count; t_i++) t_candidates.Add(t_i);

        var t_pendingNew = new HashSet<int>();
        var t_drawn = new List<DrawnCard>(t_drawCount);
        for (int t_i = 0; t_i < t_drawCount; t_i++)
        {
            int t_pick = PickWeightedCandidate(_pool, t_candidates);
            CardData t_card = _pool[t_candidates[t_pick]].card;
            if (t_unique) t_candidates.RemoveAt(t_pick);

            // null 풀 항목은 건너뛴다 — 신규 판정에 걸리지 않아 간식이 새어나간다
            if (t_card == null) continue;

            t_drawn.Add(GrantAndReward(t_card, t_pendingNew));
        }

        OwnershipManager.GrantAll(t_pendingNew);
        return t_drawn;
    }

    /// <summary>신규면 지급 목록에 모으고, 중복이면 간식을 적립한다.
    /// 소유 반영을 개봉이 끝난 뒤로 미루는 이유는 장당 지급이 곧 장당 저장이기 때문이다.
    /// 이미 뽑은 번호를 <paramref name="_pendingNew"/>가 기억해야 같은 팩에서 두 번 나온 카드가 중복으로 잡힌다.</summary>
    static DrawnCard GrantAndReward(CardData _card, HashSet<int> _pendingNew)
    {
        int t_id = CardCatalog.IdOf(_card);
        if (t_id > 0 && !OwnershipManager.IsOwned(t_id) && _pendingNew.Add(t_id))
            return new DrawnCard(_card, true);

        bool t_added = CardGrowthManager.AddSnack(t_id, SnackPerDuplicate);
        return new DrawnCard(_card, false, t_added ? SnackPerDuplicate : 0);
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
