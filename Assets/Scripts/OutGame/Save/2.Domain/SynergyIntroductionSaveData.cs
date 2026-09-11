using System.Collections.Generic;
using Firebase.Firestore;

[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public sealed class SynergyIntroductionSaveData
{
    // 옛 판: 시너지 하나만 소개하고 completed 로 끝냈다. 지금은 completedIds 가 정본이고 이 값은 마이그레이션 읽기용이다.
    [FirestoreProperty("completed")] public bool Completed { get; set; }
    [FirestoreProperty("deckSlot")] public int DeckSlot { get; set; } = -1;
    [FirestoreProperty("synergyId")] public string SynergyId { get; set; } = "";
    [FirestoreProperty("completedIds")] public List<string> CompletedIds { get; set; } = new List<string>();

    const string LEGACY_DEFAULT_ID = "Caretaker";

    /// <summary>해당 시너지 소개를 이미 끝냈는지.</summary>
    public bool IsDone(string _synergyId)
    {
        this.Migrate();
        return !string.IsNullOrEmpty(_synergyId) && this.CompletedIds.Contains(_synergyId);
    }

    /// <summary>소개 완주 낙인. 같은 시너지를 두 번 찍지 않는다.</summary>
    public void MarkDone(string _synergyId)
    {
        this.Migrate();
        if (string.IsNullOrEmpty(_synergyId) || this.CompletedIds.Contains(_synergyId)) return;
        this.CompletedIds.Add(_synergyId);
    }

    // 옛 completed 는 "어떤 시너지든 하나 봤다"라 정확한 대상을 모른다. 저장된 id 가 있으면 그것, 없으면 돌보미로 친다.
    void Migrate()
    {
        if (this.CompletedIds == null) this.CompletedIds = new List<string>();
        if (this.Completed && this.CompletedIds.Count == 0)
            this.CompletedIds.Add(string.IsNullOrEmpty(this.SynergyId) ? LEGACY_DEFAULT_ID : this.SynergyId);
    }
}
