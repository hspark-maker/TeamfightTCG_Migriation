using Firebase.Firestore;

[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public sealed class SynergyIntroductionSaveData
{
    [FirestoreProperty("completed")] public bool Completed { get; set; }
    [FirestoreProperty("deckSlot")] public int DeckSlot { get; set; } = -1;
    [FirestoreProperty("synergyId")] public string SynergyId { get; set; } = "";
}
