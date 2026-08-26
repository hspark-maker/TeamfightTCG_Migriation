using System;

// 종료 시점의 세이브를 로컬에 남긴 저널 한 건. 다음 부팅이 서버 revision과 대조해 올리거나 폐기한다.
[Serializable]
public class SaveJournalEntry
{
    public string payload;

    // 풀해시. 서버 캐시 해시와 같으면 종료 직전 푸시가 이미 성공했다는 뜻이다.
    public string payloadHash;

    // 저널을 쓸 때 알고 있던 서버 revision. 이 값이 서버와 어긋나면 저널은 이미 낡았다.
    public long baseRevision;

    public int schemaVersion;

    // 계정이나 프로필이 바뀐 저널을 다음 부팅에 남의 문서로 올리지 않기 위한 대조 키다.
    public string profileId;
    public string uid;

    public long writtenAtUtcTicks;
}
