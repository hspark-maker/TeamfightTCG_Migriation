using System;

// 덱 세이브 값 객체 — 카드는 안정 문자열 키(CardCatalog.KeyOf)로 식별
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
    public string[] cardKeys;
    // 덱 대표 이미지 키(DeckImageCatalog 스프라이트 이름)
    public string imageKey;
}
