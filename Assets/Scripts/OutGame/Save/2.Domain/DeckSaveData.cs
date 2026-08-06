using System;

// 덱 세이브 값 객체 — 카드는 고유 번호(CardCatalog.IdOf)로 식별
[Serializable]
public class DeckSaveData
{
    public DeckSlotSaveData[] slots;
}

// 덱 슬롯 하나의 저장 내용
[Serializable]
public class DeckSlotSaveData
{
    public string name;
    public int[] cardIds;

    // 구 세이브 이관용(에셋 이름 키). 로드 때 번호로 한 번 옮기고 비운다 — 새로 쓰지 않는다.
    public string[] cardKeys;

    // 덱 대표 이미지 키(DeckImageCatalog 스프라이트 이름)
    public string imageKey;
}
