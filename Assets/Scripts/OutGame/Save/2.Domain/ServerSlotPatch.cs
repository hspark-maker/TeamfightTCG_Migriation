using Newtonsoft.Json;

// 서버가 다시 써 준 세이브 슬롯 묶음(callable 응답 DTO). Firestore 문서 매핑이 아니라서 [FirestoreData]를 붙이지 않는다.
//
// 불변식: null == 서버가 그 슬롯을 건드리지 않았다.
// 그래서 프로퍼티 이니셜라이저를 하나도 두지 않는다 — UserSaveData처럼 = new Xxx()를 달면
// 한 슬롯만 담긴 응답을 역직렬화해도 나머지 8개가 non-null이 되어 로컬 진행도를 기본값으로 덮는다.
public class ServerSlotPatch
{
    [JsonProperty("ownership")] public OwnershipSaveData Ownership { get; set; }
    [JsonProperty("deck")] public DeckSaveData Deck { get; set; }
    [JsonProperty("cardGrowth")] public CardGrowthSaveData CardGrowth { get; set; }
    [JsonProperty("keywordGrowth")] public KeywordGrowthSaveData KeywordGrowth { get; set; }
    [JsonProperty("rank")] public RankSaveData Rank { get; set; }
    [JsonProperty("albumReward")] public AlbumRewardSaveData AlbumReward { get; set; }
    [JsonProperty("tournament")] public TournamentSaveData Tournament { get; set; }
    [JsonProperty("tutorial")] public TutorialSaveData Tutorial { get; set; }
    [JsonProperty("profile")] public ProfileSaveData Profile { get; set; }
}
