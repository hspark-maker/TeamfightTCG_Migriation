using System;
using System.Collections.Generic;

// 카드 소유권 세이브 값 객체. 소유 카드는 안정 문자열 키(CardCatalog.KeyOf)로 저장 — 인덱스 금지.
// 필드는 추가만(하위호환) — 기존 필드 의미 변경·삭제·리네임 금지.
[Serializable]
public class OwnershipSaveData
{
    public List<string> ownedCardKeys = new List<string>();

    // [G-23 이후 미사용] 과거 최초 1회 전체 기본지급 완료 플래그. 신규 유저 소유 0 정책으로 자동지급을 제거했다.
    // 하위호환을 위해 필드는 유지(삭제·리네임 금지) — 구 세이브가 이 키를 갖고 있어도 크래시 없이 읽힌다.
    public bool defaultsGranted = false;
}
