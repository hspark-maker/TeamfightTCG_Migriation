using System.Collections.Generic;

// 카드팩 구매·즉시 개봉의 static 파사드
public static class CardPackOpener
{
    static readonly System.Random s_rng = new System.Random();

    // 팩 구매·즉시 개봉 — 차감 → 드로우 → 소유 부여 → 중복 환급, 실패 시 차감 없이 사유 반환
    public static OpenedPack TryPurchase(CardPackData _pack, long _refundGold)
    {
        if (_pack == null) return OpenedPack.CreateFailure(EPackOpenResult.PackNotFound, null);

        string t_packId = _pack.PackId;

        // 빈 풀 판정은 랭크 해석 후의 실제 추첨 풀 기준
        var t_pool = _pack.ResolvePool(RankManager.GetInfo().Grade);
        if (t_pool.Count == 0) return OpenedPack.CreateFailure(EPackOpenResult.EmptyPool, t_packId);

        long t_price = _pack.Price;
        ECurrencyType t_currency = _pack.PriceType;

        if (!CurrencyManager.CanAfford(t_currency, t_price))
            return OpenedPack.CreateFailure(EPackOpenResult.InsufficientGold, t_packId);

        if (!CurrencyManager.Spend(t_currency, t_price))
            return OpenedPack.CreateFailure(EPackOpenResult.SpendFailed, t_packId);

        long t_refundEach = _refundGold < 0 ? 0 : _refundGold;
        int t_drawCount = _pack.DrawCount;

        bool t_unique = _pack.UniqueDraw;
        if (t_unique && t_drawCount > t_pool.Count) t_drawCount = t_pool.Count;

        var t_candidates = new List<int>(t_pool.Count);
        for (int t_i = 0; t_i < t_pool.Count; t_i++) t_candidates.Add(t_i);

        var t_drawn = new List<DrawnCard>(t_drawCount);
        for (int t_i = 0; t_i < t_drawCount; t_i++)
        {
            int t_pick = PickWeighted(t_pool, t_candidates);
            CardData t_card = t_pool[t_candidates[t_pick]].card;
            if (t_unique) t_candidates.RemoveAt(t_pick);

            // null 풀 항목은 건너뛴다 — Grant(null)=false가 중복으로 오판돼 환급이 새어나간다
            if (t_card == null) continue;

            bool t_isNew = OwnershipManager.Grant(CardCatalog.IdOf(t_card));

            long t_refund = 0;
            if (!t_isNew)
            {
                CurrencyManager.Earn(t_currency, t_refundEach);
                t_refund = t_refundEach;
            }

            t_drawn.Add(new DrawnCard(t_card, t_isNew, t_refund));
        }

        CurrencyManager.Save();

        return OpenedPack.CreateSuccess(t_packId, t_drawn, t_currency);
    }

    // 가중치 추첨 — 합에서 굴린 값을 누적 스캔, uniqueDraw 제거 후에도 매회 합을 재계산
    static int PickWeighted(IReadOnlyList<WeightedCard> _pool, List<int> _candidates)
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
