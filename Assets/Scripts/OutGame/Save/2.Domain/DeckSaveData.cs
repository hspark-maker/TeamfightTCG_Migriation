using System.Collections.Generic;
using Firebase.Firestore;

// 덱 세이브 값 객체 — 카드는 고유 번호(CardCatalog.IdOf)로 식별
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public class DeckSaveData
{
    [FirestoreProperty("slots")] public List<DeckSlotSaveData> Slots { get; set; } = new List<DeckSlotSaveData>();
}

// 덱 슬롯 하나의 저장 내용
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public class DeckSlotSaveData
{
    [FirestoreProperty("name")] public string Name { get; set; } = "";
    [FirestoreProperty("cardIds")] public List<int> CardIds { get; set; } = new List<int>();

    // 덱 대표 이미지 키(DeckImageCatalog 스프라이트 이름)
    [FirestoreProperty("imageKey")] public string ImageKey { get; set; } = "";
}
