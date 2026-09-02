using System.Collections.Generic;
using Firebase.Firestore;

// 모험 낙인 세이브 값 객체 — 해금 진행은 저장하지 않는다(낙인 파생)
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public class TournamentSaveData
{
    // 클리어한 정점의 안정 키(TournamentNodeDef.nodeId). 순서는 의미 없다
    [FirestoreProperty("clearedNodeIds")] public List<string> ClearedNodeIds { get; set; } = new List<string>();

    // 완주 보상을 수령한 챕터의 안정 키(TournamentChapterDef.chapterId). 순서는 의미 없다
    [FirestoreProperty("claimedChapterIds")] public List<string> ClaimedChapterIds { get; set; } = new List<string>();

    // 깼지만 아직 보상을 받지 않은 정점(빈 문자열 = 없음).
    // 목록이 아니라 한 칸이다 — 미수령이 있으면 다음 정점이 잠기고 그 정점 자신도 재진입이 막혀 둘이 동시에 생기지 않는다
    [FirestoreProperty("pendingRewardNodeId")] public string PendingRewardNodeId { get; set; } = "";

    // 해금 연출을 이미 보여준 챕터·정점의 안정 키. 진행 낙인이 아니라 연출 이력이라 파생값이 아니다
    [FirestoreProperty("seenUnlockIds")] public List<string> SeenUnlockIds { get; set; } = new List<string>();
}
