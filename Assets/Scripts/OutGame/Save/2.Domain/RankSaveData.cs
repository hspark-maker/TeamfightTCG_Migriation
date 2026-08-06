using System;
using System.Collections.Generic;

// 랭크(표시용 티어 진행도) 세이브 값 객체
[Serializable]
public class RankSaveData
{
    // 티어는 이 값에서 파생 — 도달 티어는 저장하지 않는다
    public long points;

    // 구 세이브 이관용(수령 개수 커서). 낙인 리스트로 한 번 옮기고 비운다 — 새로 쓰지 않는다.
    public int claimedCount;

    // 수령 완료한 티어 인덱스(순서 무관 — 도달한 보상은 아무거나 먼저 받을 수 있다)
    public List<int> claimedTiers = new List<int>();
}
