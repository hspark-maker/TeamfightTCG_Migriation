# 아웃게임 구조: 온보딩 실행·복구

2026-09-16. 기존 경로에 구조 문서가 없어 이번에 추가했다. 이 문서는 이번에 변경한 온보딩 경계만 다룬다.

## 실행 구조

- `OutGame/Tutorial/OnboardingSession.cs`: 스텝 실행 수명, 세대별 취소, 중복 완료 방지, 효과 확인과 진행 위치 저장의 경계.
- `OutGame/Tutorial/Steps/TutorialActionMeta.cs`: 행동별 진입·완료 확인 정책. 설명은 디바운스 저장, 경제 행동·덱 확정·전투 진입·챕터 종료는 저장 확인.
- `OutGame/Tutorial/Steps/TutorialStepExecutor.cs`: 지급·구매를 기다린 뒤 연출 및 다음 진행. `OutgameTutorialRunner`는 강제/자율 커서와 미완료 대본 전투 위치를 관리한다.
- `UI/Tutorial/OutgameTutorialBridge.cs`: 실행 수명에 UI 이벤트 연결, 공통 완료 창구, 실패 재시도, 앵커 복구. UI 콜백은 현재 스텝과 실행 세대가 유효해야 한다.
- `UI/Tutorial/GuidanceCoordinator.Missions.cs`, `.Recovery.cs`: 안내의 오류·앱 이탈 복구와 화면 복원. 필수 온보딩과 별도의 수명이다.

## 저장·서버 경계

- `TutorialSaveData.execution`: 선택적 실행 기록. 기존 세이브는 필드 없이 읽는다. `battleEntryStepId`는 대본 전투 완료 전 재시작 위치다.
- `TutorialSaveData.onboardingCommand`: 스텝 ID, 명령, 거래 ID, 인자, 확정 결과, 소비 여부. 경제 명령 전 의도를 원격 저장한다.
- `OutGame/Save/4.Cloud/OnboardingCommands.cs`: 미확정 명령의 결과 조회·동일 거래 재전송·연출 재생. UI 취소가 서버 처리를 취소했다고 간주하지 않는다.
- `ServerSaveCommands`: 기존 직렬화 창구를 공유한다. 일반 응답의 `revision + 1` 계약은 유지한다.
- `PlayerSaveCloud`: 복구 전 계정·환경·세션 세대·원격 기기를 확인한다. 최신 서버 스냅샷에서 서버 소유 슬롯을 채택하고 로컬 진행·덱 편집은 보존한다. 차단된 세션은 재초기화가 필요하다.
- `functions/src/save/onboardingOperation.ts`, `saveDocument.ts`: `onboardingOperations/{txId}`에 경제 변경과 같은 트랜잭션으로 처리 기록을 남긴다. 일반 TTL 영수증과 분리한다.
- `functions/src/commands/getOnboardingOperation.ts`: 인증된 자기 계정의 불변 처리 결과와 현재 save/wallet/missions를 읽는다. 다른 인자로 거래 ID를 재사용하면 거절한다.

## UI 경계

진행 미루기 기능은 제거했다. 안내 중에는 지정된 탭·뒤로가기만 허용한다. 오류 팝업은 재시도·종료를 제공하며 앱 이탈 후 진행 복구는 유지한다. 경제 처리·확정 저장 중에는 입력을 차단한다. 준비 중 별도 안내 문구는 표시하지 않는다. 덱 초안은 같은 프로세스·계정·슬롯에서 보존하며 앱 종료 후에는 서버에 저장한 덱을 복원한다.

대본 전투 결과는 `TutorialConfig` 이벤트로 아웃게임에 통지한다. `TurnRunner`의 전투 판정 규칙은 바꾸지 않는다. 로비 전투 진입은 온보딩 저장 확인 이후 씬을 전환한다.

상세 원인·변경 파일·검증 범위: [온보딩 복구 개편](../ONBOARDING_RECOVERY_20260916.md).

## 배틀 복귀 준비

`LoadingCoverView`는 `BattleReturnLoadingCover`를 로비 씬 활성화 뒤에도 유지한다. `OutgameTutorialBridge.PrepareLobbyReturnAsync`로 첫 안내의 지급·진행 저장·첫 랭크 확인을 기다린 뒤 커버를 걷는다. 사용자 입력과 연출 종료는 대기 대상이 아니다. 복귀 준비 실패는 같은 커버의 재시도·종료로 처리하며 로비 씬을 다시 로드하지 않는다.

복귀 준비가 소유한 요청은 `ServerWaitOverlay`를 중복 생성하지 않는다. 자동 구매는 `PackPurchaseFlow`의 호출자 대기 소유 옵션을 사용한다. 로비가 열린 뒤 사용자 구매·강화의 대기 표시는 기존 경로를 유지한다. 미래 스텝의 지급을 앞당겨 실행하지 않는다.
