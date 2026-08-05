using System;

// 랭크(표시용 티어 진행도) 세이브 값 객체
[Serializable]
public class RankSaveData
{
    // 티어는 이 값에서 파생 — 도달 티어는 저장하지 않는다
    public long points;

    // 수령 완료한 티어 개수(단조 증가 커서)
    public int claimedCount;
}
