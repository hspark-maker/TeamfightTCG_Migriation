using System;

// 덱 세이브 값 객체. 카드는 안정 문자열 키(CardCatalog.KeyOf)로 저장 — 인덱스 금지.
// slots 길이는 SLOT_COUNT를 기대하지만 누락/초과는 DeckSaveManager가 방어한다.
// 필드는 추가만(하위호환) — 기존 필드 의미 변경·삭제·리네임 금지.
[Serializable]
public class DeckSaveData
{
    public DeckSlotSaveData[] slots;
}

[Serializable]
public class DeckSlotSaveData
{
    public string name;
    public string[] cardKeys;
}
