using System;
using System.Collections.Generic;

// 카드 소유권 세이브 값 객체 — 카드는 안정 문자열 키(CardCatalog.KeyOf)로 식별
[Serializable]
public class OwnershipSaveData
{
    public List<string> ownedCardKeys = new List<string>();

    // 미사용 — 구 세이브 호환용으로만 남긴 기본지급 플래그
    public bool defaultsGranted = false;
}
