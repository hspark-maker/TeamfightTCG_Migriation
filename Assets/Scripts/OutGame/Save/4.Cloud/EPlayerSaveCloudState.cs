// 클라우드 세이브 세션 상태
internal enum EPlayerSaveCloudState
{
    Disabled,
    Loading,
    Ready,
    Offline,
    Uploading,
    Failed,

    // 초기화를 통과한 뒤 클라우드만 끊긴 상태. Failed와 갈라 두는 이유는 표면이 다르기 때문이다 —
    // Failed는 복구 화면, Blocked는 재시작 모달이 받는다.
    Blocked
}
