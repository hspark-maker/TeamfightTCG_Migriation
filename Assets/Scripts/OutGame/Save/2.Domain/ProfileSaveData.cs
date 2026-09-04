using Firebase.Firestore;

// 프로필(닉네임·아바타·프레임) 세이브 값 객체
[FirestoreData(UnknownPropertyHandling = UnknownPropertyHandling.Ignore)]
public class ProfileSaveData
{
    // 빈 값 = 미저작. 기본값을 여기 박지 않는다 — 박으면 ProfileConfig 0번을 갈아도 신규 유저가 옛 id로 굳는다.
    [FirestoreProperty("nickname")] public string Nickname { get; set; }
    [FirestoreProperty("avatarId")] public string AvatarId { get; set; }
    [FirestoreProperty("frameId")] public string FrameId { get; set; }

    // 계정 레벨은 이 누적치의 파생이라 레벨 자체는 저장하지 않는다(AccountLevelManager).
    [FirestoreProperty("accountExp")] public long AccountExp { get; set; }
}
