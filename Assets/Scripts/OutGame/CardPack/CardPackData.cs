using System.Collections.Generic;
using UnityEngine;

// 카드팩 1종의 정의 데이터 SO.
//
// **값의 진실원은 스펙시트(CardPack / CardPackDrop)이고, 이 에셋은 packId와 팩 아트만 소유한다.**
// 아래 숫자 필드는 시트를 못 읽을 때의 폴백이다 — 카드(CardData)와 달리 팩은 아트 외에 물려 있는
// 에셋이 없어서 값을 굽지 않고 런타임에 시트를 그대로 읽는다(PackSpec).
// 소비자가 이 프로퍼티만 보게 두는 이유: 시트/폴백 판정이 여기 한 곳에 있어야 "어느 값이 이겼는지"가 갈리지 않는다.
[CreateAssetMenu(fileName = "CardPackData", menuName = "Card Battle/Card Pack Data")]
public class CardPackData : ScriptableObject
{
    [Header("식별 (packId = 안정 키, 변경 금지)")]
    [SerializeField] string packId;
    [SerializeField] string displayName;

    [Header("표시")]
    [Tooltip("진열·개봉에 쓰는 팩 아트. 미지정이면 진열 뷰가 자기 기본 이미지를 유지한다.")]
    [SerializeField] Sprite packArt;

    [Header("가격·드로우 (폴백 — 평소엔 시트 CardPack이 이긴다)")]
    [Tooltip("결제 재화.")]
    [SerializeField] ECurrencyType priceType = ECurrencyType.Gold;
    [Min(0)] [SerializeField] long price = 100;
    [Min(1)] [SerializeField] int drawCount = 3;

    [Tooltip("켜면 한 팩 안에서 같은 카드를 두 번 뽑지 않는다(비복원 추출). 풀이 뽑을 장수보다 작으면 풀 크기만큼만 나온다.")]
    [SerializeField] bool uniqueDraw;

    [Header("중복 환급")]
    [Tooltip("이미 소유한 카드를 뽑았을 때 되돌려줄 재화. 결제 재화와 달라도 된다 — 다이아로 산 팩이 골드를 환급해도 무방하다.")]
    [SerializeField] ECurrencyType refundType = ECurrencyType.Gold;
    [Tooltip("중복 카드 1장당 환급량. 0이면 환급하지 않는다.")]
    [Min(0)] [SerializeField] long refundAmount = 10;

    [Header("드로우 풀 (폴백 — 평소엔 시트 CardPackDrop이 이긴다)")]
    [Tooltip("시트를 못 읽거나 이 packId가 시트에 없을 때만 쓰이는 카드셋. 균등 확률로 drawCount회 뽑는다. "
           + "확률 조정은 여기가 아니라 CardPackDrop 시트의 weight로 한다.")]
    [SerializeField] List<CardData> pool = new List<CardData>();

    [Header("랭크별 풀 오버라이드 (폴백)")]
    [Tooltip("위 pool과 같은 폴백 축. 랭크별 큐레이션도 시트에서 minGrade 행으로 저작한다.")]
    [SerializeField] List<RankPackPool> rankPools = new List<RankPackPool>();

    public string PackId => packId;
    public Sprite PackArt => packArt;   // 시트가 가질 수 없는 유일한 축 — 항상 이 에셋이 소유한다.

    public string DisplayName
        => Spec(out CardPack t_row) ? t_row.displayName : displayName;

    public ECurrencyType PriceType
        => Spec(out CardPack t_row) ? ParseCurrency(t_row.priceType) : priceType;

    public long Price
        => Spec(out CardPack t_row) ? t_row.price : price;

    public int DrawCount
        => Spec(out CardPack t_row) ? Mathf.Max(1, t_row.drawCount) : drawCount;

    public bool UniqueDraw
        => Spec(out CardPack t_row) ? t_row.uniqueDraw != 0 : uniqueDraw;

    public ECurrencyType RefundType
        => Spec(out CardPack t_row) ? ParseCurrency(t_row.refundType) : refundType;

    public long RefundAmount
        => Spec(out CardPack t_row) ? t_row.refundAmount : refundAmount;

    public int PoolCount => Pool.Count;

    // 가중치 없는 카드 목록. 스타터덱 지급·튜토리얼 덱 자동장착처럼 "이 팩에 뭐가 들었나"만 묻는 소비자용.
    // 랭크는 현재 등급으로 해석한다 — 추첨(ResolvePool)과 다른 목록을 보여주지 않기 위해서다.
    public IReadOnlyList<CardData> Pool
    {
        get
        {
            IReadOnlyList<WeightedCard> t_weighted = ResolvePool(RankManager.GetInfo().Grade);
            if (t_weighted.Count == 0)
                return pool != null ? pool : (IReadOnlyList<CardData>)System.Array.Empty<CardData>();

            var t_result = new List<CardData>(t_weighted.Count);
            for (int t_i = 0; t_i < t_weighted.Count; t_i++)
                if (t_weighted[t_i].card != null) t_result.Add(t_weighted[t_i].card);
            return t_result;
        }
    }

    // 인스펙터 폴백 저작 값 그대로(검증 도구용). 실제 추첨이 이걸 쓴다는 보장은 없다 — 그건 ResolvePool이 정한다.
    public IReadOnlyList<RankPackPool> RankPools
        => rankPools != null ? rankPools : (IReadOnlyList<RankPackPool>)System.Array.Empty<RankPackPool>();

    // 랭크 해석 후의 실제 추첨 풀. 시트에 이 팩이 있으면 시트가 이기고, 없으면 인스펙터 폴백으로 떨어진다.
    public IReadOnlyList<WeightedCard> ResolvePool(ERankGrade _grade)
    {
        List<WeightedCard> t_spec = PackSpec.ResolveDrops(packId, _grade);
        if (t_spec.Count > 0) return t_spec;

        return FallbackPool(_grade);
    }

    // 스펙 미로드/미등록 시의 인스펙터 저작 값. 예전 동작 그대로 — rankPools 매치 우선, 없으면 pool 균등.
    IReadOnlyList<WeightedCard> FallbackPool(ERankGrade _grade)
    {
        RankPackPool t_best = null;
        if (rankPools != null)
            foreach (RankPackPool t_entry in rankPools)
                if (t_entry != null && t_entry.minGrade <= _grade && (t_best == null || t_entry.minGrade > t_best.minGrade))
                    t_best = t_entry;

        if (t_best != null && t_best.cards != null && t_best.cards.Count > 0)
            return t_best.cards;

        int t_count = pool != null ? pool.Count : 0;
        var t_fallback = new List<WeightedCard>(t_count);
        for (int t_i = 0; t_i < t_count; t_i++)
            t_fallback.Add(new WeightedCard { card = pool[t_i], weight = 1 });
        return t_fallback;
    }

    bool Spec(out CardPack _row) => PackSpec.TryGetPack(packId, out _row);

    static ECurrencyType ParseCurrency(string _value)
        => System.Enum.TryParse(_value, out ECurrencyType t_type) ? t_type : ECurrencyType.Gold;
}

[System.Serializable]
public struct WeightedCard
{
    public CardData card;
    [Min(0)]
    [Tooltip("추첨 가중치. 0 = 미지정 → 1(균등)로 취급된다. 이 카드를 안 나오게 하려면 0이 아니라 리스트에서 삭제할 것. 예: 가중치 3은 가중치 1 카드보다 3배 잘 나온다.")]
    public int weight;

    // 0 = 인스펙터 리스트 추가 시 기본값 → 균등 취급 (제외는 삭제로)
    public int EffectiveWeight => weight > 0 ? weight : 1;
}

[System.Serializable]
public class RankPackPool
{
    [Tooltip("현재 랭크가 이 등급 이상이면 이 풀 적용. 만족 항목이 여럿이면 가장 높은 등급이 이긴다. 같은 등급 항목이 둘이면 리스트 앞쪽이 이기니 등급당 하나만 둘 것.")]
    public ERankGrade minGrade;
    [Tooltip("이 등급에서 나올 카드 전체를 나열(하위 등급 풀과 합산되지 않음). 비워두면 기본 pool로 폴백.")]
    public List<WeightedCard> cards;
}
