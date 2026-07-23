using System;

/// <summary>
/// 아웃게임 세이브 값 객체 — 전투 밖에서 영속되는 유저 상태의 스냅샷.
/// 도메인이 늘면 그 도메인의 값 객체를 필드로 조립한다(예: currency, ownership).
/// JsonUtility로 직렬화되므로 필드는 public이며, 필드는 추가만 한다(하위호환).
/// 기존 필드의 의미 변경·삭제·리네임은 금지(구 세이브가 조용히 기본값으로 읽힘).
/// </summary>
[Serializable]
public class UserSaveData
{
    // 세이브 스키마 버전. 구조를 바꿔야 할 때만 올린다.
    public const int VERSION = 1;

    public int version = VERSION;

    // ── 도메인 값 객체 조립 지점 ──
    // 각 도메인은 자기 값 객체를 2.Domain에 정의하고 여기에 필드로 얹는다.
    // public CurrencySaveData  currency  = new CurrencySaveData();   // A-3 재화
    // public OwnershipSaveData ownership = new OwnershipSaveData();   // B-5 소유
    // public CollectionSaveData collection = new CollectionSaveData(); // C 도감
}
