using Firebase.Firestore;

// 아웃게임 세이브 값 객체 — 전투 밖 유저 상태의 스냅샷
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public class UserSaveData
{
    // 스키마 버전. 저장 문서 안이 아니라 클라우드 문서의 최상위 schemaVersion 메타 필드가 들고 있다.
    // 7: Firestore 이관 — 필드 → 프로퍼티, 인덱스 배열 → 문자열 키 맵
    public const int VERSION = 7;

    [FirestoreProperty("currency")] public CurrencySaveData Currency { get; set; } = new CurrencySaveData();
    [FirestoreProperty("ownership")] public OwnershipSaveData Ownership { get; set; } = new OwnershipSaveData();
    [FirestoreProperty("deck")] public DeckSaveData Deck { get; set; } = new DeckSaveData();
    [FirestoreProperty("cardGrowth")] public CardGrowthSaveData CardGrowth { get; set; } = new CardGrowthSaveData();
    [FirestoreProperty("keywordGrowth")] public KeywordGrowthSaveData KeywordGrowth { get; set; } = new KeywordGrowthSaveData();
    [FirestoreProperty("rank")] public RankSaveData Rank { get; set; } = new RankSaveData();
    [FirestoreProperty("albumReward")] public AlbumRewardSaveData AlbumReward { get; set; } = new AlbumRewardSaveData();
    [FirestoreProperty("tournament")] public TournamentSaveData Tournament { get; set; } = new TournamentSaveData();
    [FirestoreProperty("tutorial")] public TutorialSaveData Tutorial { get; set; } = new TutorialSaveData();
    [FirestoreProperty("profile")] public ProfileSaveData Profile { get; set; } = new ProfileSaveData();
}
