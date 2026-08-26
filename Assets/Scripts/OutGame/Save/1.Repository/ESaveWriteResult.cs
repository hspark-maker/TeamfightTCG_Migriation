// 쓰기 1회의 결과. 업로드 큐의 지속 상태(ESaveUploadState)와는 축이 다르다.
public enum ESaveWriteResult
{
    Success,

    // 로드된 세이브가 이 클라이언트보다 상위 버전이라 쓰기가 봉쇄된 상태
    Blocked,

    IoFailed,

    // 서버 문서가 내가 아는 revision보다 앞서 있어 쓰기 선조건이 깨진 상태
    Conflict,

    // 서버에 도달하지 못한 상태(타임아웃·네트워크 단절). 재시도로 풀릴 수 있다는 점이 IoFailed와 다르다
    Offline,
}
