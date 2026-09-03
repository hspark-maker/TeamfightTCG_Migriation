using Firebase.Firestore;

// 아웃게임 세이브 값 객체 — 전투 밖 유저 상태의 스냅샷
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public class UserSaveData
{
    /// <summary>스키마 버전. 저장 문서 안이 아니라 클라우드 문서의 최상위 schemaVersion 메타 필드가 들고 있다.
    /// 7: Firestore 이관 — 필드 → 프로퍼티, 인덱스 배열 → 문자열 키 맵
    /// 8: 재화 이관 — currency 슬롯이 세이브를 떠나 형제 문서 wallet/current 로 갔다</summary>
    // 승급 정책 본문은 쌍둥이 상수 쪽 하나에 있다 — functions/src/save/saveDocument.ts의 SCHEMA_VERSION 주석.
    // 여기 적을 것은 하나뿐이다: 무중단으로 올리는 순서가 없다.
    // 서버 선행이면 문서가 서버 상수보다 낮아 전 유저 callable이 failed-precondition이고,
    // 클라 선행이면 초기화 게이트가 remote < VERSION으로 Fail이라 아무도 못 들어온다.
    // v8은 그 예외다 — ensureWallet이 초기화 안에서 v7 문서를 승급시켜 준다.
    // 올린다면 firestore.rules.prod의 allow create가 박아 둔 schemaVersion 값도 같이 올린다 —
    // 안 올리면 기존 계정은 멀쩡한데 신규 계정만 안 만들어지는 부분 고장이 된다.
    public const int VERSION = 8;

    [FirestoreProperty("ownership")] public OwnershipSaveData Ownership { get; set; } = new OwnershipSaveData();
    [FirestoreProperty("deck")] public DeckSaveData Deck { get; set; } = new DeckSaveData();
    [FirestoreProperty("cardGrowth")] public CardGrowthSaveData CardGrowth { get; set; } = new CardGrowthSaveData();
    [FirestoreProperty("keywordGrowth")] public KeywordGrowthSaveData KeywordGrowth { get; set; } = new KeywordGrowthSaveData();
    [FirestoreProperty("rank")] public RankSaveData Rank { get; set; } = new RankSaveData();
    [FirestoreProperty("albumReward")] public AlbumRewardSaveData AlbumReward { get; set; } = new AlbumRewardSaveData();
    [FirestoreProperty("adventure")] public AdventureSaveData Adventure { get; set; } = new AdventureSaveData();
    [FirestoreProperty("tutorial")] public TutorialSaveData Tutorial { get; set; } = new TutorialSaveData();
    [FirestoreProperty("profile")] public ProfileSaveData Profile { get; set; } = new ProfileSaveData();
}
