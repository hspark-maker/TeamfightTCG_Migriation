using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 도감 배치의 단일 진실원(배치 오버레이). 자리 번호 = slots 인덱스 순서(위→아래·좌→우).
/// 소유·미소유 카드 전부가 사전 정의된 고정 자리에 박혀 있고, 카드 획득은 예약된 자리를 채울 뿐이다.
/// 카드 마스터를 복제하지 않는다 — CardData 참조(배치)만 담고 스탯/키는 CardCatalog가 소유한다.
/// raw 값은 [SerializeField] private, 노출은 읽기전용 프로퍼티만. (BattleTimingConfig 관용구)
/// 실제 .asset 인스턴스 생성·배치 데이터 입력은 에디터에서 사용자가 authoring — 비어도 안전해야 한다.
/// </summary>
[CreateAssetMenu(fileName = "CollectionLayoutConfig", menuName = "Card Battle/Collection Layout Config")]
public class CollectionLayoutConfig : ScriptableObject
{
    // 배치 순서(= 자리 번호). 미소유 카드도 자리 예약용으로 포함한다. 3의 배수가 아니어도 됨(부분행 허용).
    [Header("도감 배치 (순서 = 자리 번호, 위→아래·좌→우). 미소유 카드도 자리 예약용으로 포함.")]
    [SerializeField] List<CardData> slots = new List<CardData>();

    // 배치 자리 총 개수(부분행 포함). null 방어.
    public int SlotCount => slots != null ? slots.Count : 0;

    // 읽기 전용 배치 순서. null이면 빈 목록(미authoring 상태 안전 처리).
    public IReadOnlyList<CardData> Slots => slots != null ? slots : (IReadOnlyList<CardData>)System.Array.Empty<CardData>();
}
