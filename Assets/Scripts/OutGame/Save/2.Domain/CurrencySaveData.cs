using System;

// 재화 세이브 값 객체. 재화가 늘면 필드 추가(하위호환) — 기존 필드 의미 변경·삭제 금지.
[Serializable]
public class CurrencySaveData
{
    public long gold;
}
