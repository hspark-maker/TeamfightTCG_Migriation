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
    [Tooltip("결제 재화. 중복 카드 환급도 같은 재화로 돌려준다.")]
    [SerializeField] ECurrencyType priceType = ECurrencyType.Gold;
    [Min(0)] [SerializeField] long price = 100;
    [Min(1)] [SerializeField] int drawCount = 3;

    [Tooltip("켜면 한 팩 안에서 같은 카드를 두 번 뽑지 않는다(비복원 추출). 풀이 뽑을 장수보다 작으면 풀 크기만큼만 나온다.")]
    [SerializeField] bool uniqueDraw;

    [Header("드로우 풀 (이 팩 전용 지정 카드셋)")]
    [Tooltip("이 팩에서 뽑을 수 있는 카드셋. 마스터 전체가 아닌 큐레이션된 부분집합. 균등 확률로 drawCount회 뽑는다.")]
    [SerializeField] List<CardData> pool = new List<CardData>();

    public string PackId => packId;
    public string DisplayName => displayName;
    public Sprite PackArt => packArt;
    public ECurrencyType PriceType => priceType;
    public long Price => price;
    public int DrawCount => drawCount;
    public bool UniqueDraw => uniqueDraw;

    public int PoolCount => pool != null ? pool.Count : 0;

    // 읽기 전용 풀 — 미authoring이면 빈 목록
    public IReadOnlyList<CardData> Pool
        => pool != null ? pool : (IReadOnlyList<CardData>)System.Array.Empty<CardData>();
}
