using System.Collections.Generic;
using Firebase.Firestore;

/// <summary>콘텐츠 영구 해금과 아직 보여주지 않은 연출 이력.</summary>
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public class ContentUnlockSaveData
{
    [FirestoreProperty("version")] public int Version { get; set; }
    [FirestoreProperty("unlocked")] public List<string> Unlocked { get; set; } = new List<string>();
    [FirestoreProperty("pending")] public List<string> Pending { get; set; } = new List<string>();
}
