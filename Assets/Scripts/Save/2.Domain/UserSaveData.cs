using System;

// 아웃게임 세이브 값 객체 — 전투 밖 유저 상태의 스냅샷.
// JsonUtility로 직렬화되므로 필드는 public. 필드는 추가만(하위호환) — 의미 변경·삭제·리네임 금지.
[Serializable]
public class UserSaveData
{
    public const int VERSION = 1;   // 구조를 바꿔야 할 때만 올린다.

    public int version = VERSION;

    // 도메인 값 객체 조립 지점. 각 도메인은 자기 값 객체를 2.Domain에 정의하고 여기 얹는다.
    public CurrencySaveData currency = new CurrencySaveData();       // A-3 재화
    public OwnershipSaveData ownership = new OwnershipSaveData();    // B-5 소유
    public DeckSaveData deck = new DeckSaveData();                   // B-6 덱
    // public CollectionSaveData collection = new CollectionSaveData(); // C 도감
}
