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

        if (_pack.PoolCount == 0) return OpenedPack.CreateFailure(EPackOpenResult.EmptyPool, t_packId);

        long t_price = _pack.Price;

        if (!CurrencyManager.CanAfford(ECurrencyType.Gold, t_price))
            return OpenedPack.CreateFailure(EPackOpenResult.InsufficientGold, t_packId);

        if (!CurrencyManager.Spend(ECurrencyType.Gold, t_price))
            return OpenedPack.CreateFailure(EPackOpenResult.SpendFailed, t_packId);

        var t_pool = _pack.Pool;
        long t_refundEach = _refundGold < 0 ? 0 : _refundGold;
        int t_drawCount = _pack.DrawCount;

        bool t_unique = _pack.UniqueDraw;
        if (t_unique && t_drawCount > t_pool.Count) t_drawCount = t_pool.Count;

        List<int> t_remain = null;
        if (t_unique)
        {
            t_remain = new List<int>(t_pool.Count);
            for (int t_i = 0; t_i < t_pool.Count; t_i++) t_remain.Add(t_i);
        }

        var t_drawn = new List<DrawnCard>(t_drawCount);
        for (int t_i = 0; t_i < t_drawCount; t_i++)
        {
            CardData t_card;
            if (t_unique)
            {
                int t_pick = s_rng.Next(t_remain.Count);
                t_card = t_pool[t_remain[t_pick]];
                t_remain.RemoveAt(t_pick);
            }
            else
            {
                t_card = t_pool[s_rng.Next(t_pool.Count)];
            }

            // null 풀 항목은 건너뛴다 — Grant(null)=false가 중복으로 오판돼 환급이 새어나간다
            if (t_card == null) continue;

            bool t_isNew = OwnershipManager.Grant(CardCatalog.IdOf(t_card));

            long t_refund = 0;
            if (!t_isNew)
            {
                CurrencyManager.Earn(ECurrencyType.Gold, t_refundEach);
                t_refund = t_refundEach;
            }

            t_drawn.Add(new DrawnCard(t_card, t_isNew, t_refund));
        }

        CurrencyManager.Save();

        return OpenedPack.CreateSuccess(t_packId, t_drawn);
    }
}
