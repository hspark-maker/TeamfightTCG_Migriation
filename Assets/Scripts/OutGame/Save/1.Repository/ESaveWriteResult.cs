// 쓰기 1회의 결과. 업로드 큐의 지속 상태(ESaveUploadState)와는 축이 다르다.
public enum ESaveWriteResult
{
    Success,

    // 로드된 세이브가 이 클라이언트보다 상위 버전이라 쓰기가 봉쇄된 상태
    Blocked,

    IoFailed,
}
