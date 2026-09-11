# 결과 제출 재시도 회귀 테스트

`powershell -ExecutionPolicy Bypass -File Tools/MatchResultSubmissionTests/run.ps1`

실제 `Assets/Scripts/Network/MatchResultSubmission.cs` 전체를 독립 실행 파일로 컴파일한다. Unity에 포함된 Roslyn·Mono를 사용하며 `-UnityData`로 설치 경로를 변경할 수 있다. 산출물은 시스템 임시 디렉터리에만 생성한다. Unity 실행, 에셋 임포트, 실제 Firebase 요청은 없다.

가상 callable 응답·시계·인증으로 앱 복귀, flush, 신규 결과 제출의 재시도 예약 교체와 동시 전송 방지, 백오프·큐 보존, 확정·영구 거절, 초기화·종료·폐기 이후 오래된 응답을 검증한다. 취소는 기본적으로 다음 시계 진행에서 감지하여 UniTask의 기본 PlayerLoop 취소 처리를 모사하고, 즉시 취소 재진입도 별도 검증한다. 잊힌 비동기 작업의 예외는 테스트 실패로 처리한다.

`Stubs.cs`의 `REAL_UNITY` 정의는 Unity/UniTask 대체 구현을 제외한다. Unity `UnityReferenceAssemblies/unity-4.8-api`(netstandard 2.1 포함), `UnityEngine.CoreModule.dll`, `UnityEngine.JSONSerializeModule.dll`, 프로젝트 `Library/ScriptAssemblies/UniTask.dll`을 참조하여 생산 파일과 도메인 스텁의 컴파일도 확인할 수 있다. 독립 테스트는 실제 Unity PlayerLoop·Firebase SDK 통합 실행을 대체하지 않는다.
