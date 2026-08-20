using System;
using System.Collections.Generic;

// 보상 토너먼트 정점 클리어 낙인 세이브 값 객체 — 해금 진행은 저장하지 않는다(낙인 파생)
[Serializable]
public class TournamentSaveData
{
    public const int VERSION = 1;

    public int version = VERSION;

    // 클리어한 정점의 안정 키(TournamentNodeDef.nodeId). 순서는 의미 없다
    public List<string> clearedNodeIds = new List<string>();
}
