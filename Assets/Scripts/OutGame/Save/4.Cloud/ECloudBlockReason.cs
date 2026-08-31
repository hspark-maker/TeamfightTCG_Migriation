// 클라우드 업로드가 이 세션에서 멈춘 이유. 재시작 모달이 유저에게 뭐라 말할지를 이 값이 정한다.
internal enum ECloudBlockReason
{
    None,

    // 우리가 모르는 쓰기가 원격 문서를 먼저 올렸다. 재시작하면 그쪽 기록을 채택한다.
    RemoteAhead,

    // 세이브 문서가 한도를 넘었다. 재시작해도 내용이 그대로면 다시 막힌다.
    DocumentTooLarge,

    // 룰 거부·배선 오류·세션 중 계정 교체 — 이 클라이언트로는 더 쓸 수 없다.
    SessionUnusable
}
