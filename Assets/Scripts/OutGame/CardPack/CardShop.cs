using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 상점 진열 데이터(에디터 authoring, SO). 판매 중인 카드팩 목록과 중복 환급 전역값을 담는다.
/// CardPackOpener에 주입되며(SetShop), 미배선 시 빈 상점 fallback으로 동작한다.
/// </summary>
[CreateAssetMenu(fileName = "CardShop", menuName = "Card Battle/Card Shop")]
public class CardShop : ScriptableObject
{
    [Header("진열 카드팩 목록 (순서 = 상점 표시 순서)")]
    [SerializeField] List<CardPackData> packs = new List<CardPackData>();

    [Header("중복 환급")]
    [Tooltip("이미 소유한 카드를 뽑았을 때(중복) 되돌려주는 Gold. 전역값.")]
    [Min(0)] [SerializeField] long duplicateRefundGold = 10;

    // 진열 팩 총 개수. null 방어.
    public int PackCount => packs != null ? packs.Count : 0;

    // 읽기 전용 팩 목록. null이면 빈 목록(미authoring 상태 안전 처리).
    public IReadOnlyList<CardPackData> Packs
        => packs != null ? packs : (IReadOnlyList<CardPackData>)System.Array.Empty<CardPackData>();

    public long DuplicateRefundGold => duplicateRefundGold;
}
