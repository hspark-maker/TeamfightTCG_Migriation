# 온보딩 개편 서버 배포 목록

2026-09-16 배포 완료. `bm-cardbattle` / `asia-northeast3`의 아래 17개 함수를 선별 배포했다. 신규 1개 생성·기존 16개 갱신 성공, 배포 명령 종료 코드 0 및 원격 17개 모두 `ACTIVE`를 확인했다. live/test 공통 반영이다.

## 대상

- 프로젝트 설정: `bm-cardbattle` (`.firebaserc`).
- Cloud Functions 코드베이스: `default`, 소스 `functions/`.
- 리전: `asia-northeast3`. Firestore 명명 데이터베이스: `cardbattle`.
- `live` / `test`는 요청의 env와 문서 경로 구분이다. 함수 배포 자체가 test 전용 배포로 격리되는 구조는 아니다.

## 직접 기능 변경: 7개 함수

| 함수 | 배포 내용 |
|---|---|
| `getOnboardingOperation` | 신규. 거래 ID·명령·인자를 대조하고 불변 결과와 최신 save/wallet/missions를 반환 |
| `grantTutorialCards` | 카드 지급과 온보딩 처리 기록을 같은 트랜잭션에 저장 |
| `openPack` | 팩 구매·차감·지급의 응답 유실 복구 지원 |
| `enhanceCard` | 카드 강화의 응답 유실 복구 지원 |
| `enhanceSynergyIntroduction` | 시너지 입문 강화의 응답 유실 복구 지원. 구현 파일은 enhanceCard.ts |
| `enhanceKeyword` | 키워드 강화의 응답 유실 복구 지원 |
| `limitBreakCard` | 한계 돌파의 응답 유실 복구 지원 |

명령 6개는 `onboarding: true`와 유효한 `txId`를 받으면 영구 처리 기록을 생성한다. 같은 거래 ID로 다른 명령·인자를 보내거나 온보딩 표시를 제거해 재실행하는 경로를 차단한다. 일반 명령의 revision 순서 검증은 유지한다.

추가 파일: `functions/src/save/onboardingOperation.ts`, `functions/src/commands/getOnboardingOperation.ts`.
공통 변경: `functions/src/save/saveDocument.ts`, 함수 export 등록 `functions/src/index.ts`.

## 공통 저장 모듈 반영 범위

위 7개만 배포하면 다른 함수는 기존 mutateSave 구현을 유지한다. 공통 거래 ID 충돌 검사를 일관되게 적용하려면 아래 소비자도 재배포한다.

- `claimReward`, `claimMission`, `claimAttendance`
- `claimPassReward`, `claimPassRepeatReward`
- `spinRoulette`, `reportAdventureWin`
- `devBumpRevision`, `devResetSave`, `devSetRank`

총 17개를 선별 배포했다. 원격에만 존재하는 `claimBattleExperience`를 보존하기 위해 `default` 전체 배포는 하지 않았다. `currency` 코드베이스(`functions-currency/`)도 배포하지 않았다. CLI가 두 코드베이스의 predeploy lint/build를 실행했지만 실제 생성·갱신 대상은 위 17개뿐이다.

## 데이터·설정

새 경로: `envs/{env}/users/{uid}/onboardingOperations/{txId}`.

- 첫 해당 요청에서 자동 생성. 사전 컬렉션 생성·기존 데이터 일괄 이관 없음.
- 경제 변경과 처리 기록을 원자 저장. 일반 영수증 TTL과 분리되며 새 기록에는 만료 필드가 없다. 복구 근거이므로 별도 TTL/삭제 작업을 붙이지 않는다.
- 문서 ID 직접 조회이므로 새 복합 인덱스 불필요.
- Admin SDK로만 접근. 현재 rules의 기본 거부가 클라이언트 직접 접근을 차단하므로 rules 변경 불필요.
- `tutorial.execution`, `tutorial.onboardingCommand`는 기존 tutorial map의 선택 필드. 현재 rules·세이브 버전 변경 불필요.
- 신규 Secret·환경 변수·Remote Config·시트·SpecData.bytes·Hosting 배포 없음.
- 공통 `mutateSave` 요청에는 처리 기록 문서 읽기 1회가 추가된다. 온보딩 성공 요청에는 처리 기록 쓰기도 추가된다. 복구 조회는 save/wallet/operation/missions와 미션 카탈로그를 읽는다.

## 반영 순서

1. 배포 리비전 확정. 신규 TS 파일 2개가 누락되지 않았는지 확인한다.
2. 배포 전 전체 lint/build 실행. `firebase.json`의 predeploy는 전체 lint와 build를 실행하며, build에는 콘텐츠 버전 검사도 포함된다. 배포 산출물은 `functions/lib`이므로 최신 빌드가 필요하다.
3. 신규 조회 함수와 변경 명령·공통 모듈 소비자를 서버에 반영한다. 함수별 배포는 일괄 원자 전환이 아니므로 모두 완료되기 전 새 클라이언트를 배포하지 않는다.
4. test 계정으로 정상 요청 및 응답 유실·동일 거래 재시도·인자 불일치 거부를 확인한다. test 검증과 별개로 함수 코드는 live에도 반영되어 있다는 점을 구분한다.
5. 검증 후 새 클라이언트 배포. 서버보다 클라이언트가 먼저 나가면 조회 함수가 없거나 처리 기록이 남지 않아 안전 복구가 성립하지 않는다.

기존 클라이언트의 `onboarding` 없는 요청 형식은 유지한다. 단, 구버전 앱이 새 클라이언트의 선택적 세이브 필드를 다시 저장하면서 보존하는지는 별도 호환 QA가 필요하다. 새 클라이언트 배포 후 서버만 구버전으로 롤백하거나 처리 기록을 삭제하지 않는다.

## 검증 상태와 배포 후 확인

- 통과: 전체 lint/build와 콘텐츠 버전 검사, 인메모리 처리 기록 테스트, 시너지 입문 서버 회귀. 배포 직전 `test-onboarding-operation.js` PASS 및 `test-synergy-introduction-grant.js` 3개 테스트 통과.
- 원격 확인: 17개 함수 모두 `ACTIVE`. 신규 `getOnboardingOperation`에 인증 없는 callable 요청을 보내 HTTP 401 / `UNAUTHENTICATED` / `Sign-in is required.` 응답 확인. 계정 데이터 변경 없음.
- 미완료: 실제 계정의 정상 조회·Firestore 동시성·네트워크 응답 유실 시험, 앱의 8-3/8-4 및 배틀→로비 복귀 플레이 회귀. 인증 없는 요청 거부 확인만으로 이 항목들의 통과를 의미하지 않는다.
- 배포 이력: 첫 시도는 로컬 함수 분석 서버의 `fetch failed`로 업로드 전에 종료. 재시도는 성공. 중단 요청 당시 이미 접수된 원격 배포가 이후 완료됐으며, 이후 조회로 최종 상태를 확인했다.
- 기존 `test-guide-mission-growth` 실패: 현재 CSV 미션 순서와 테스트 기대값 불일치. 이번 작업에서 CSV 수정하지 않음.
- 배포 후: 동일 거래 재요청이 경제 효과·revision을 중복 증가시키지 않는지, 일반 영수증 만료 후에도 재지급되지 않는지, 조회가 다른 계정 기록을 노출하지 않는지 확인한다.
- 운영 로그: `OnboardingRecoveryRequired`는 최신 결과 조회로 이어져야 한다. `TxIdReused`는 다른 인자 재사용 거부다. 반복 복구 실패·세션 충돌·저장 실패는 따로 확인한다.

## 서버 배포가 필요 없는 최근 변경

Canvas/GraphicRaycaster 수명 수정, 준비 문구 제거, 진행 미루기 제거, BattleReturnLoadingCover의 로비 준비 대기 통합은 클라이언트 변경이다. 이들 자체 때문에 서버 함수를 추가 배포할 필요는 없다. 온보딩 응답 유실 복구를 함께 사용하는 새 클라이언트에는 위 서버 변경이 먼저 필요하다.
