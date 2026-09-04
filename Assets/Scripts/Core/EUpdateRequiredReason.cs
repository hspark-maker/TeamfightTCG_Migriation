/// <summary>업데이트 화면이 뜬 사유. 종료 상태는 하나(UpdateRequired)지만 유저가 볼 문구와
/// 로그가 갈려야 한다 — 뭉치면 표 배포 사고와 앱 버전 정책 사고를 로그로 구분할 수 없다.</summary>
internal enum EUpdateRequiredReason
{
    /// <summary>이 앱이 해석 못 하는 콘텐츠 표 세대가 배포됐다(ContentVersion 축).</summary>
    Content,

    /// <summary>서버 정책이 이 빌드 버전을 더 받지 않는다(AppVersionGate 축).</summary>
    AppVersion,
}
