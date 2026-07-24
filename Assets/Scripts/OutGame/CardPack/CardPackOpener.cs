using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 카드팩 구매·개봉의 static 파사드(오케스트레이터). 자체 영속 상태가 없다 —
/// 골드 차감은 CurrencyManager가, 카드 소유는 OwnershipManager가 이미 영속하므로 E는 둘을 잇기만 한다.
/// 상점 SO 미배선 시 빈 상점(Packs 비어있음) fallback으로 동작(RewardService/CatalogRows 관용구).
///
/// 전제: TryPurchase는 CardCatalog·OwnershipManager가 Init된 상태를 가정한다(부트 후 호출).
/// Grant는 KeyOf(=CardData.name) 기준이라 카탈로그 준비와 무관히 소유 집합에 키를 넣지만,
/// 소유 UI가 카탈로그로 카드를 되찾으므로 정상 흐름에선 부트 완료 후 사용한다.
/// </summary>
public static class CardPackOpener
{
    // 주입된 상점 SO(선택). null이면 빈 상점 fallback(lazy 인스턴스, Packs 비어있음).
    static CardShop s_shop;
    static CardShop s_fallbackShop;

    // 아웃게임 최초 랜덤. Battle/MatchRandom 재사용 금지(경계 위반) — 서비스 내부 System.Random.
    // 비결정론 무방(오프라인 단일 유저 개봉).
    static readonly System.Random s_rng = new System.Random();

    // 상점 소스. 배선 SO 우선, 없으면 코드 기본 빈 상점 lazy 인스턴스(CatalogRows.Tuning 관용구).
    static CardShop Shop
        => s_shop != null
            ? s_shop
            : (s_fallbackShop != null ? s_fallbackShop : (s_fallbackShop = ScriptableObject.CreateInstance<CardShop>()));

    /// <summary>상점 SO 주입(선택). 부트/배선에서 호출. null이면 fallback(빈 상점) 복귀.</summary>
    public static void SetShop(CardShop _shop)
    {
        s_shop = _shop;
    }

    // ── 공개 조회 API ─────────────────────────────────────────

    /// <summary>진열 팩 목록(읽기 전용). 미배선 시 빈 목록.</summary>
    public static IReadOnlyList<CardPackData> Packs => Shop.Packs;

    /// <summary>packId로 팩 조회. 미존재·빈 키면 null(예외 없음).</summary>
    public static CardPackData GetPack(string _packId)
    {
        if (string.IsNullOrEmpty(_packId)) return null;

        var t_packs = Shop.Packs;
        for (int t_i = 0; t_i < t_packs.Count; t_i++)
        {
            var t_pack = t_packs[t_i];
            if (t_pack != null && t_pack.PackId == _packId) return t_pack;
        }
        return null;
    }

    // ── 구매·개봉 ─────────────────────────────────────────────

    /// <summary>
    /// 팩 구매·즉시 개봉. 성공 시 Gold 차감 → 지정 풀 균등 드로우 → 소유 부여 → 중복 환급 → 1회 Save 후
    /// OpenedPack 반환. 실패(팩 없음/잔액 부족/빈 풀/방어)는 차감 없이 실패 결과 반환(예외 없음).
    /// </summary>
    public static OpenedPack TryPurchase(string _packId)
    {
        // 1. 팩 조회 — 없으면 차감 없이 실패.
        var t_pack = GetPack(_packId);
        if (t_pack == null) return OpenedPack.CreateFailure(EPackOpenResult.PackNotFound, _packId);

        // 빈 풀은 드로우 불가 — 차감 전에 방어(잘못된 결제 방지).
        if (t_pack.PoolCount == 0) return OpenedPack.CreateFailure(EPackOpenResult.EmptyPool, _packId);

        long t_price = t_pack.Price;

        // 2. 잔액 확인 — 부족하면 차감 없이 실패.
        if (!CurrencyManager.CanAfford(ECurrencyType.Gold, t_price))
            return OpenedPack.CreateFailure(EPackOpenResult.InsufficientGold, _packId);

        // 3. 차감. CanAfford 통과 후에도 false면 방어 실패(차감 없음).
        if (!CurrencyManager.Spend(ECurrencyType.Gold, t_price))
            return OpenedPack.CreateFailure(EPackOpenResult.SpendFailed, _packId);

        // 4. drawCount회 균등 드로우 → Grant → 중복 시 환급.
        var t_pool = t_pack.Pool;
        long t_refundEach = Shop.DuplicateRefundGold;
        int t_drawCount = t_pack.DrawCount;

        var t_drawn = new List<DrawnCard>(t_drawCount);
        for (int t_i = 0; t_i < t_drawCount; t_i++)
        {
            var t_card = t_pool[s_rng.Next(t_pool.Count)]; // 로컬 랜덤 균등.

            // 오설정 방어: null 풀 항목은 뽑지 않고 건너뛴다.
            // (Grant(null)==false가 '중복'으로 오판돼 받지도 않은 카드에 환급이 나가는 것을 차단.)
            if (t_card == null) continue;

            bool t_isNew = OwnershipManager.Grant(CardCatalog.KeyOf(t_card));

            long t_refund = 0;
            if (!t_isNew)
            {
                // 중복 = 소액 골드 환급. Spend와 같은 트랜잭션(루프 후 1회 Save로 영속).
                CurrencyManager.Earn(ECurrencyType.Gold, t_refundEach);
                t_refund = t_refundEach;
            }

            t_drawn.Add(new DrawnCard(t_card, t_isNew, t_refund));
        }

        // 5. Spend+Earn 트랜잭션 즉시 영속(1회). 소유는 Grant가 이미 자체 Save했다.
        CurrencyManager.Save();

        // 6. 결과 조립(총 환급액은 카드 Refund 합으로 파생).
        return OpenedPack.CreateSuccess(_packId, t_drawn);
    }
}
