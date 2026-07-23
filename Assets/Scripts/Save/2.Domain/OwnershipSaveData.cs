using System;
using System.Collections.Generic;

// 카드 소유권 세이브 값 객체. 소유 카드는 안정 문자열 키(CardCatalog.KeyOf)로 저장 — 인덱스 금지.
// 필드는 추가만(하위호환) — 기존 필드 의미 변경·삭제·리네임 금지.
[Serializable]
public class OwnershipSaveData
{
    public List<string> ownedCardKeys = new List<string>();
    public bool defaultsGranted = false;   // 최초 1회 기본 지급 완료 플래그
}
