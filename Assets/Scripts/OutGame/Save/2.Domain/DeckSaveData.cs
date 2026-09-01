using System.Collections.Generic;
using Firebase.Firestore;

// 덱 세이브 값 객체 — 카드는 고유 번호(CardSpec.Id)로 식별
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public class DeckSaveData
{
    [FirestoreProperty("slots")] public List<DeckSlotSaveData> Slots { get; set; } = new List<DeckSlotSaveData>();

    // 전투에 내보낼 덱의 슬롯 좌표. 신규 계정은 슬롯 0이 스타터 덱이라 기본값 0이 곧 정답이다 —
    // 서버 신규 문서(freshAccount.ts)가 이 필드를 싣지 않아도 되는 이유.
    [FirestoreProperty("selectedSlot")] public int SelectedSlot { get; set; }
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
