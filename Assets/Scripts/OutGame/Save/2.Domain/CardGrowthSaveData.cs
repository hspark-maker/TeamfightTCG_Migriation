using System;
using System.Collections.Generic;

// 카드 성장(강화 레벨) 세이브 값 객체 — 카드는 안정 문자열 키(CardCatalog.KeyOf)로 식별
[Serializable]
public class CardGrowthSaveData
{
    public const int VERSION = 1;

    public int version = VERSION;

    // 성장한 카드만 담는다(Lv0은 항목 없음)
    public List<CardGrowthEntry> entries = new List<CardGrowthEntry>();
}

// 카드 한 장의 성장 진행도
[Serializable]
public class CardGrowthEntry
{
    public string cardKey;

    public int level;
}
