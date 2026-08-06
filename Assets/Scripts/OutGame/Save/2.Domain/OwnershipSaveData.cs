using System;
using System.Collections.Generic;

// 카드 소유권 세이브 값 객체 — 카드는 고유 번호(CardCatalog.IdOf)로 식별
[Serializable]
public class OwnershipSaveData
{
    public List<int> ownedCardIds = new List<int>();

    // 구 세이브 이관용(에셋 이름 키). 로드 때 번호로 한 번 옮기고 비운다 — 새로 쓰지 않는다.
    public List<string> ownedCardKeys = new List<string>();

    // 미사용 — 구 세이브 호환용으로만 남긴 기본지급 플래그
    public bool defaultsGranted = false;
}
