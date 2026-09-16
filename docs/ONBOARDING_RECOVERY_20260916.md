# 온보딩 진행 오류 검사·개편

## 검사 결과

대상: `Assets/SO/TutorialConfig/Outgame/OutgameTutorial.asset` 전체 실행 경로. 기존 스텝 ID·액션 enum 값·문구·보상 수치는 유지한다.

| 위치/경계 | 코드에서 확인한 문제 | 변경 |
|---|---|---|
| 시너지 실전 8-3, ID 76 | 설명 완료 뒤 화면/게이트 재진입과 커서·저장 수명이 분리됨 | 단일 실행 수명과 완료 경로, 앵커 재복원·명시적 재시도 |
| 8-4, ID 77 `WaitSynergyDeck` 이후 | 모든 자율 스텝에 저장 확인. 안내 전용 5초 제한이 실제 저장·callable 제한보다 짧음. 덱 완료 폴링은 일반 완료 경로 우회 | 설명 스텝 저장 대기 제거, 덱 확정은 공통 확인 경로. 저장 계층의 제한을 사용 |
| 카드 지급 | 지급 요청을 기다리지 않고 연출·진행 가능 | 서버 확정 뒤 표시·진행 |
| 앱 이탈/탭 이동 | 편집 화면 복원이 작업 덱을 다시 로드할 수 있음. 자동 트리거가 즉시 재시작 가능 | 같은 계정·슬롯의 메모리 초안 복원. 사용자 미루기는 제거하고 안내 외 이동 제한 |
| 경제 요청 응답 유실 | 서버 적용 여부와 다음 재시도 구분 불가 | 명령 의도 선저장, 영구 처리 기록 조회, 동일 거래 ID 재전송 |
| 구매 후 재시작 | 개봉/획득 커서는 저장됐지만 화면 전달값은 휘발됨 | 같은 챕터의 해당 구매 결과만 연출 재생. 구버전 결과가 없으면 재구매 없이 연출 스텝 생략 |
| 전투 진입 직후 종료 | 다음 스텝 저장 후 전투를 완료하지 않아도 이후 스텝으로 재시작 | 미완료 대본 전투 진입 ID 기록·복원, 결과 시 해제 |
| 계정/기기 변경 중 복구 | 이전 요청이 새 세션에서 재전송될 가능성 | 각 비동기 경계에서 계정·환경·세대 확인, 재전송 전 원격 충돌 검사 |

이는 정적 경로 분석과 회귀 검사 결과다. 최초 제보의 기기 로그가 없으므로 실제 장애 원인을 하나로 확정한 것은 아니다.

## 원자성의 의미

UI 전체를 하나의 데이터베이스 트랜잭션으로 묶지는 않는다. 서버 경제 변경과 처리 기록은 같은 트랜잭션이다. 클라이언트는 실행 의도 저장 → 서버 확정/결과 조회 → 결과 표시 → 진행 위치 저장 순서로 복구 가능하게 만든다. 설명은 서버 왕복 없이 진행해 앱 강제 종료 시 일부 설명이 반복될 수 있다.

기존 일반 명령의 revision 검증을 완화하지 않는다. 온보딩 복구만 별도 최신 스냅샷 채택 경로를 사용한다. 원격 충돌·Blocked/Failed/Loading 상태를 성공으로 바꾸지 않는다.

## 변경·추가 범위

경로는 `Assets/Scripts/` 기준. 아래는 이번 작업분이며 작업 시작 전부터 있던 다른 UI·스펙·랭크 변경은 제외한다.

| 구분 | 파일/영역 | 내용 |
|---|---|---|
| 추가 | `OutGame/Tutorial/OnboardingSession.cs` | 실행 단계·세대·완료 확인·저장 DTO |
| 추가 | `OutGame/Save/4.Cloud/OnboardingCommands.cs` | 영속 명령 복구·구매 연출 복원 |
| 변경 | `OutGame/Save/2.Domain/TutorialSaveData.cs` | 선택적 실행/명령 필드 |
| 변경 | `OutGame/Save/4.Cloud/{ServerSaveCommands,PlayerSaveCloud}.cs` | 직렬화·복구·세션 검증 |
| 변경 | `OutGame/CardPack/CardPackOpener.cs` | 결과 재생 시 구매 사전검사·낙관 차감 반복 방지 |
| 변경 | `OutGame/Tutorial/{GuideResume,OutgameTutorialRunner,OutgameTutorialProgress}.cs` | 저장 확인·전투 재개·반복 기동만으로 FTUE 우회 방지 |
| 변경 | `OutGame/Tutorial/Steps/{TutorialActionMeta,TutorialStepExecutor}.cs` | 액션 계약·비동기 실행 |
| 변경 | `UI/Tutorial/{OutgameTutorialBridge,OutgameTutorialGateUI}.cs` | 공통 완료·복구·안내 대상 입력 제한 |
| 변경 | `UI/Tutorial/GuidanceCoordinator.{Missions,Recovery}.cs` | 오류·앱 이탈 복구·표면 복원 |
| 변경 | `UI/{Deck/DeckEditController,Deck/DeckTabController,Lobby/LobbyTabController,CardDetail/CardDetailOverlayView,Album/AlbumPageOverlayView}.cs` | 초안 보관·이탈 연결 |
| 변경 | `UI/Common/LoadingCoverView.cs`, `UI/Lobby/LobbyMatchLauncher.cs` | 초기 전투 오류 재시도·씬 전환 전 저장 확인 |
| 변경 | `Battle/{TutorialConfig,TurnRunner}.cs` | 대본 전투 결과 통지 훅 |
| 변경/추가 | `Editor/Tutorial/{TutorialValidator,GuideResumeValidation,OnboardingExecutionValidation}.cs` | 계약·실행·UI 재개 회귀 검증 |
| 추가 | `functions/src/save/onboardingOperation.ts`, `commands/getOnboardingOperation.ts` | 거래 인자 결합·처리 결과 조회 |
| 변경 | `functions/src/save/saveDocument.ts`, `commands/{openPack,grantTutorialCards,enhanceCard,enhanceKeyword,limitBreakCard}.ts`, `index.ts` | 처리 기록 원자 저장·callable 등록. 시너지 입문 강화는 enhanceCard 파일의 별도 command 경유 |
| 추가 | `functions/scripts/test-onboarding-operation.js` | 인메모리 트랜잭션 회귀 |
| 추가 | `docs/OutGamePlan/STRUCTURE.md`, 이 문서 | 구조·변경 범위·검증 한계 |

TypeScript 컴파일에 따른 추적 중 `functions/lib` 산출물도 갱신된다. `SpecData.bytes`, 시트 값, SO 진행 순서, 전투 규칙, Photon 프로토콜은 이번 개편에서 변경하지 않는다.

## 검증과 반영 조건

- 통과 — 서버 TypeScript 컴파일·변경 파일 ESLint, 처리 기록 원자성·TTL 독립성·인자 결합·응답 유실 복구·자기 계정 격리 회귀, 시너지 입문 서버 회귀 10건.
- 통과 — 런타임/에디터 C# 컴파일, Unity 편집기의 `GuideResumeValidation` 및 `OnboardingExecutionValidation` 실제 실행. 중복 완료·취소 세대·저장 호환·팩 복원·안내 이동 제한·덱 초안·강제/자율 세션 소유권 포함.
- 통과 — 이번 변경 파일의 `git diff --check`. 저장소 전체 검사는 다른 작업의 `PackOpenOverlay.prefab` 공백을 보고한다.
- 기존 `test-guide-mission-growth`는 현재 CSV 미션 순서와 테스트 기대값 불일치로 실패했다. 이 작업에서 CSV를 바꾸지는 않았다.
- 로컬 검증은 실제 Firestore/기기 생명주기 시험을 대체하지 않는다. 서버 신규 callable과 처리 기록 지원은 2026-09-16 `bm-cardbattle`의 17개 함수에 배포 완료했다. 전부 `ACTIVE`, 신규 조회의 비인증 요청 거부를 확인했다. 상세 범위와 남은 실환경 검증은 `docs/ONBOARDING_SERVER_DEPLOYMENT.md` 참고.
- 실기기 QA: 8-3 설명 연속 탭, 8-4 저장 중 지연/망 단절, 지급·구매·강화 응답 직전 종료, 개봉/획득 중 종료, 전투 진입/전투 중 종료, 같은 프로세스 덱 초안 재개, 다른 계정·기기 충돌을 확인한다.

## 배틀 → 로비 복귀 대기 통합

`BattleReturnLoadingCover.prefab`의 기존 `LoadingCoverView`가 로비 활성화 뒤 첫 안내 준비까지 화면을 덮는다. 순서는 전투 결과 확인 → 로비 활성화 → 첫 안내의 지급·저장·랭크 확인 → 커버 해제다. 사용자 확인과 보상 연출 종료는 기다리지 않는다. 이후 스텝의 지급을 미리 실행하지 않는다.

- `LoadingCoverView.cs`: 복귀 준비 완료 조건, 오류 표시, 씬 재로드 없는 준비 재시도 추가. 일반 전환·첫 초기화 동작은 기존 경로다.
- `OutgameTutorialBridge.cs`: 준비 비동기 진입점, 복귀 커버와 시작 순서 조정, 해당 요청의 별도 서버 대기·실패 팝업 억제.
- `PackPurchaseFlow.cs`, `TutorialStepExecutor.cs`: 복귀 준비 중 자동 구매의 대기·실패 표시를 커버에 위임. 일반 사용자 구매는 기존 표시 유지.
- 프리팹 저작은 변경하지 않는다. 기존 복귀 커버의 회복 패널·재시도·종료 버튼을 재사용한다.
- 검증: 런타임·에디터 C# 컴파일 통과. 신규 `LobbyReturnPreparationValidation`을 Unity에서 실행해 PASS 확인(복귀 소유권, 실행·랭크·저장 대기, 사용자/연출 대기 제외, 실패·같은 Bridge 재시도·취소 정리). 실제 서버 왕복과 실제 씬 전환 재시도는 이 인메모리 검사의 대상이 아니다.
