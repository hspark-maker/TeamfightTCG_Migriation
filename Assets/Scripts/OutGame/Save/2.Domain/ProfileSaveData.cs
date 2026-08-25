using System;

// 프로필(닉네임·아바타·프레임) 세이브 값 객체
[Serializable]
public class ProfileSaveData
{
    // 빈 값 = 미저작. 기본값을 여기 박지 않는다 — 박으면 ProfileConfig 0번을 갈아도 신규 유저가 옛 id로 굳는다.
    public string nickname;
    public string avatarId;
    public string frameId;
}
