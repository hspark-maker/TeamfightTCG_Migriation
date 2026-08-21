using System;
using System.Collections.Generic;

// 카드 성장(강화 레벨) 세이브 값 객체 — 카드는 고유 번호(CardCatalog.IdOf)로 식별
[Serializable]
public class CardGrowthSaveData
{
    public const int VERSION = 3;

    public int version = VERSION;

    // 강화한 카드만 담는다(미강화 Lv1은 항목 없음)
    public List<CardGrowthEntry> entries = new List<CardGrowthEntry>();
}

// 카드 한 장의 성장 진행도
[Serializable]
public class CardGrowthEntry
{
    public int cardId;

    // 구 세이브 이관용(에셋 이름 키). 로드 때 번호로 한 번 옮기고 비운다 — 새로 쓰지 않는다.
    public string cardKey;

    public int level;
}
