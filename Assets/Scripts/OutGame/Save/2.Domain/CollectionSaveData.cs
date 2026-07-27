using System;
using System.Collections.Generic;

// 도감 방치 생산의 세이브 값 객체. 행 진행도는 행 안정 문자열 키(CatalogRow.Key)로 저장 — 인덱스 금지.
// 카드가 추가·삽입돼도 기존 행 키는 불변이라 진행도가 밀리지 않는다.
// 필드는 추가만(하위호환) — 기존 필드 의미 변경·삭제·리네임 금지. 모르는/누락 키는 기본값 처리.
[Serializable]
public class CollectionSaveData
{
    // 도감 스키마를 바꿔야 할 때만 올린다(마이그레이션 진입점).
    // 폐기된 completionRewardClaimed 키가 구 세이브에 남아 있어도 JsonUtility가 무시하므로 버전은 유지.
    public const int VERSION = 1;

    public int version = VERSION;

    // 행별 진행도 목록(순서 무의미 — 키로 조회). null 방어는 소비자(CollectionProductionManager)에서.
    public List<CollectionRowProgress> rows = new List<CollectionRowProgress>();
}

// 행 하나의 방치 생산 진행도. 인덱스가 아닌 행 안정 키로 식별한다.
[Serializable]
public class CollectionRowProgress
{
    // 행 안정 키(CatalogRow.Key = 행 첫 카드의 CardCatalog.KeyOf). 배열 위치가 아님.
    public string rowKey;

    // 마지막 정산 시각(UTC ticks). 이 시점부터의 경과로 현재 누적을 (재)계산한다.
    // long ticks로 저장 — JsonUtility가 DateTime을 직렬화하지 못하므로.
    public long lastSettleUtcTicks;

    // 정산 시점까지 굳어진 누적량(소수 포함). 수확 시 정수분만 지급하고 소수 잔여는 보존한다.
    // 재화 지급은 long이지만 시간당 생산의 소수 손실을 막기 위해 누적은 double로 보관.
    public double accumulated;
}
