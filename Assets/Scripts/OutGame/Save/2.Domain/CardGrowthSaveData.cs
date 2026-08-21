using System;
using System.Collections.Generic;

// 카드 성장(강화 레벨) 세이브 값 객체 — 카드는 고유 번호(CardCatalog.IdOf)로 식별
[Serializable]
public class CardGrowthSaveData
{
    public const int VERSION = 6;

    public int version = VERSION;

    // 진행도가 있는 카드만 담는다(강화 또는 간식). 빈 항목은 CardGrowthManager가 저장 때 거른다.
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

    // 간식 보유량(카드팩 중복으로만 쌓인다). 카드별 재화라 전역 잔액 배열에 못 넣어 여기 얹었다.
    public int snack;

    // 카드별 한계돌파 단계.
    public int limitBreak;
}
