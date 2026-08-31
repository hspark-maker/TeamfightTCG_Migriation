using System.Collections.Generic;
using Firebase.Firestore;

// 카드 소유권 세이브 값 객체 — 카드는 고유 번호(CardSpec.Id)로 식별
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public class OwnershipSaveData
{
    [FirestoreProperty("cardIds")] public List<int> CardIds { get; set; } = new List<int>();
}
