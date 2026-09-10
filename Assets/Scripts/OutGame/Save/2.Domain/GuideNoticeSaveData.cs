using Firebase.Firestore;
using System.Collections.Generic;

/// <summary>안내 표시 이력만 저장한다. 미션 달성·수령은 서버 미션 문서가 소유한다.</summary>
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public sealed class GuideNoticeSaveData
{
    [FirestoreProperty("version")] public int Version { get; set; }
    [FirestoreProperty("guide1Pending")] public bool Guide1Pending { get; set; }
    [FirestoreProperty("guide1Shown")] public bool Guide1Shown { get; set; }
    [FirestoreProperty("guide2Pending")] public bool Guide2Pending { get; set; }
    [FirestoreProperty("guide2Shown")] public bool Guide2Shown { get; set; }
    [FirestoreProperty("pendingMissionIds")] public List<string> PendingMissionIds { get; set; } = new List<string>();
    [FirestoreProperty("shownMissionIds")] public List<string> ShownMissionIds { get; set; } = new List<string>();
}
