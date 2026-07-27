using System;

// 랭크(표시용 티어 진행도)의 세이브 값 객체.
// 필드는 추가만(하위호환) — 의미 변경·삭제·리네임 금지. 구 세이브엔 노드가 없어 기본값(0)으로 읽힌다.
[Serializable]
public class RankSaveData
{
    // 티어는 이 값의 순수 파생이다 — 도달 티어를 따로 저장하면 둘이 어긋날 때 어느 쪽이 진실인지 알 수 없다.
    public long points;
}
