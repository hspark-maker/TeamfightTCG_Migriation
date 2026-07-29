using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 카드팩 1종의 정의 데이터(에디터 authoring, SO). 구매=즉시 개봉이라 재고 상태는 없다.
/// packId는 안정 문자열 키(상점 진열·구매 요청의 식별자 — 인덱스 금지 규약).
/// pool은 이 팩 전용 지정 카드셋(드로우 대상). CardData 마스터의 부분집합 참조일 뿐 복제가 아니다.
/// </summary>
[CreateAssetMenu(fileName = "CardPackData", menuName = "Card Battle/Card Pack Data")]
public class CardPackData : ScriptableObject
{
    [Header("식별 (packId = 안정 키, 변경 금지)")]
    [SerializeField] string packId;              // 상점·구매 요청의 안정 문자열 키. 에셋 리네임과 무관하게 고정.
    [SerializeField] string displayName;         // 표시명(CardData.displayName과 동일 규약 — 정본 표시명).

    [Header("표시")]
    [Tooltip("진열·개봉에 쓰는 팩 아트. 미지정이면 진열 뷰가 자기 기본 이미지를 유지한다.")]
    [SerializeField] Sprite packArt;             // 축소 아이콘이 아니라 진열 본체 아트(개봉 씬도 같은 필드를 소비할 여지).

    [Header("가격·드로우")]
    [Min(0)] [SerializeField] long price = 100;   // Gold 가격. 음수 오설정 방지.
    [Min(1)] [SerializeField] int drawCount = 3;  // 개봉 시 뽑는 장수. 최소 1.

    [Tooltip("켜면 한 팩 안에서 같은 카드를 두 번 뽑지 않는다(비복원 추출). 풀이 뽑을 장수보다 작으면 풀 크기만큼만 나온다.")]
    [SerializeField] bool uniqueDraw;             // 팩 내 중복 금지. '이미 소유한 카드 제외'와는 다른 축.

    [Header("드로우 풀 (이 팩 전용 지정 카드셋)")]
    [Tooltip("이 팩에서 뽑을 수 있는 카드셋. 마스터 전체가 아닌 큐레이션된 부분집합. 균등 확률로 drawCount회 뽑는다.")]
    [SerializeField] List<CardData> pool = new List<CardData>();

    // ── 읽기 전용 노출 ─────────────────────────────────────────
    public string PackId => packId;
    public string DisplayName => displayName;
    public Sprite PackArt => packArt;
    public long Price => price;
    public int DrawCount => drawCount;
    public bool UniqueDraw => uniqueDraw;

    // 풀 총 장수. null 방어.
    public int PoolCount => pool != null ? pool.Count : 0;

    // 읽기 전용 풀. null이면 빈 목록(미authoring 상태 안전 처리).
    public IReadOnlyList<CardData> Pool
        => pool != null ? pool : (IReadOnlyList<CardData>)System.Array.Empty<CardData>();
}
