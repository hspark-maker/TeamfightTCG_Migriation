using System;
using System.Collections.Generic;

// 도감 방치 생산 세이브 값 객체 — 행은 대표 카드 번호(CatalogRow.Id)로 식별
[Serializable]
public class CollectionSaveData
{
    public const int VERSION = 1;

    public int version = VERSION;

    public List<CollectionRowProgress> rows = new List<CollectionRowProgress>();
}

// 행 하나의 방치 생산 진행도
[Serializable]
public class CollectionRowProgress
{
    public int rowId;

    // 구 세이브 이관용(행 첫 카드의 에셋 이름). 로드 때 번호로 한 번 옮기고 비운다 — 새로 쓰지 않는다.
    public string rowKey;

    // 마지막 정산 시각(UTC ticks)
    public long lastSettleUtcTicks;

    // 정산 시점까지 굳어진 누적량(소수 포함)
    public double accumulated;
}
