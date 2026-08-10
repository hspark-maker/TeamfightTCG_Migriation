using System.Collections.Generic;
using UnityEngine;

// 카드팩 1종의 정의 데이터 SO
[CreateAssetMenu(fileName = "CardPackData", menuName = "Card Battle/Card Pack Data")]
public class CardPackData : ScriptableObject
{
    [Header("식별 (packId = 안정 키, 변경 금지)")]
    [SerializeField] string packId;
    [SerializeField] string displayName;

    [Header("표시")]
    [Tooltip("진열·개봉에 쓰는 팩 아트. 미지정이면 진열 뷰가 자기 기본 이미지를 유지한다.")]
    [SerializeField] Sprite packArt;

    [Header("가격·드로우")]
    [Tooltip("결제 재화.")]
    [SerializeField] ECurrencyType priceType = ECurrencyType.Gold;
    [Min(0)] [SerializeField] long price = 100;
    [Min(1)] [SerializeField] int drawCount = 3;

    [Header("중복 환급")]
    [Tooltip("이미 소유한 카드를 뽑았을 때 되돌려줄 재화. 결제 재화와 달라도 된다 — 다이아로 산 팩이 골드를 환급해도 무방하다.")]
    [SerializeField] ECurrencyType refundType = ECurrencyType.Gold;
    [Tooltip("중복 카드 1장당 환급량. 0이면 환급하지 않는다.")]
    [Min(0)] [SerializeField] long refundAmount = 10;

    [Tooltip("켜면 한 팩 안에서 같은 카드를 두 번 뽑지 않는다(비복원 추출). 풀이 뽑을 장수보다 작으면 풀 크기만큼만 나온다.")]
    [SerializeField] bool uniqueDraw;

    [Header("드로우 풀 (이 팩 전용 지정 카드셋)")]
    [Tooltip("이 팩에서 뽑을 수 있는 카드셋. 마스터 전체가 아닌 큐레이션된 부분집합. 균등 확률로 drawCount회 뽑는다.")]
    [SerializeField] List<CardData> pool = new List<CardData>();

    [Header("랭크별 풀 오버라이드")]
    [Tooltip("랭크 등급별 풀 오버라이드. 비워두면 기본 pool에서 균등 추첨(기존 동작).")]
    [SerializeField] List<RankPackPool> rankPools = new List<RankPackPool>();

    public string PackId => packId;
    public string DisplayName => displayName;
    public Sprite PackArt => packArt;
    public ECurrencyType PriceType => priceType;
    public long Price => price;
    public int DrawCount => drawCount;
    public bool UniqueDraw => uniqueDraw;
    public ECurrencyType RefundType => refundType;
    public long RefundAmount => refundAmount;

    public int PoolCount => pool != null ? pool.Count : 0;

    // 읽기 전용 풀 — 미authoring이면 빈 목록
    public IReadOnlyList<CardData> Pool
        => pool != null ? pool : (IReadOnlyList<CardData>)System.Array.Empty<CardData>();

    public IReadOnlyList<RankPackPool> RankPools
        => rankPools != null ? rankPools : (IReadOnlyList<RankPackPool>)System.Array.Empty<RankPackPool>();

    // 랭크 해석 후의 실제 추첨 풀 — 매치 없거나 빈 오버라이드면 기본 pool을 weight 1 취급으로 폴백
    public IReadOnlyList<WeightedCard> ResolvePool(ERankGrade _grade)
    {
        RankPackPool t_best = null;
        if (rankPools != null)
            foreach (RankPackPool t_entry in rankPools)
                if (t_entry != null && t_entry.minGrade <= _grade && (t_best == null || t_entry.minGrade > t_best.minGrade))
                    t_best = t_entry;

        if (t_best != null && t_best.cards != null && t_best.cards.Count > 0)
            return t_best.cards;

        var t_fallback = new List<WeightedCard>(PoolCount);
        for (int t_i = 0; t_i < PoolCount; t_i++)
            t_fallback.Add(new WeightedCard { card = pool[t_i], weight = 1 });
        return t_fallback;
    }
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
