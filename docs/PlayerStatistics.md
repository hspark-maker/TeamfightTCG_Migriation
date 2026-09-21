# 플레이어 통계

통계의 진실원은 `envs/{env}/users/{uid}/statistics/current`다. 서버 정산과 팩 개봉이 이 문서만 집계하며, 업적 진행도는 `lifetime`에서 투영한다. 업적 정의 테이블과 보상 수령 기록은 기존 구조를 유지한다. 새 테이블이나 `SpecData.bytes` 갱신은 필요 없다.

## 저장·조회 계약

`getPlayerStatistics({env})`는 인증된 본인의 `{statistics, achievements}`를 반환한다. `Achievement` 정의 테이블 없이 조회 가능하며, 기존 `AlbumEntry`·`AlbumThemeInfo`와 카드 소유 상태로 완성 앨범을 보정한다. 통계 문서 직접 읽기/쓰기는 클라이언트에 허용하지 않는다.

`statistics`는 다음을 담는다.

- `revision`: 통계 문서의 단조 증가 버전. 세이브 revision과 별개다.
- `achievementRevision`: 이 통계가 반영한 업적 호환 사본 버전.
- `trackedSinceMs`: 해당 계정의 새 전적 집계 시작 시각(UTC epoch ms).
- `lifetime`: `wins`, `cardsDestroyed`, `currentWinStreak`, `bestWinStreak`, `packsOpened`, `albumsCompleted`, `synergyPlays`.
- `battle.all`, `battle.ranked`, `battle.adventure`: 각각 `battles`, `wins`, `losses`, `draws`, `cardsDestroyed`, `attacks`, `damageDealt`, `healed`, `synergyTriggers`, `currentWinStreak`, `bestWinStreak`.

승률은 선택한 `battle` 범위의 `wins / battles`로 계산한다. 무승부도 분모에 포함하고, 0전이면 `—`로 표시한다. 기존 업적의 lifetime 승리를 새 전적 분모에 섞지 않는다. 모드별 연승은 해당 모드의 전투끼리 이어지고, 전체 연승은 모든 집계 대상 전투를 순서대로 따른다. 패배·무승부에서 현재 연승을 0으로 만들며 최고 연승은 유지한다.

클라이언트에서는 프로필 창의 **전적** 버튼으로 연다. 전체·랭크·모험 필터와 집계 시작일을 표시하고, 기존 기록을 포함한 누적 수치는 별도 영역에 표시한다. 조회 실패 시 마지막으로 받은 기록을 유지하며 재시도할 수 있다. 계정 전환 시 캐시를 비우고 이전 계정의 늦은 응답은 버린다.

## 집계 경로

- `submitMatchResult`: 정상 서버 재생으로 검증된 완료 전투만 결과 확정 트랜잭션에서 집계한다. 튜토리얼, 미정산·재생 실패 전투는 제외한다. 중복 제출은 이미 확정된 결과를 반환하여 다시 집계하지 않는다.
- `adventureNodeId`가 있는 전투는 모험, 없는 일반 전투는 랭크다. 랭크 AI 대전도 랭크에 포함한다.
- 시작 덱의 서버 승인 카드와 매치에 고정된 표에서 활성 시너지를 판정하여 시너지별 플레이 횟수를 1씩 올린다.
- `mutateSave`: 구매 팩 및 `claimAttendance`, `claimMission`, `claimReward`, `claimPassReward`, `claimBattleExperience`, `spinRoulette`가 실제 개봉한 보상 팩을 집계한다. 직접 카드 지급은 팩 개봉으로 세지 않는다.
- `getPlayerStatistics`, `getAchievements`, `claimAchievement`, `craftCard`: 잠금 해제된 공개 앨범 테마의 전체 카드 소유 여부로 앨범 완성을 보정한다. 페이지나 보상 수령 개수가 아니다. 소유 감소나 표 변경으로 누적 기록을 낮추지 않는다.
- `claimAchievement`: 통계에서 투영한 진행도와 영구 수령 마커를 지갑 지급과 같은 트랜잭션에서 확인한다.

`getAchievements`, `claimAchievement`, 실제 팩 개봉 응답과 `craftCard` 응답도 `statistics`를 포함한다. 클라이언트는 각 문서의 revision으로 오래된 영수증·조회 응답을 무시해야 한다. 온보딩 영구 영수증 재생은 오래된 `statistics`를 제거한다.

## 기존 기록 이관과 혼합 배포

계정의 첫 통계 읽기/쓰기에서 기존 업적의 승리·파괴·팩·앨범·시너지·현재/최고 연승을 `lifetime`에 이관한다. 수령 마커는 그대로 보존한다. 과거 패배·무승부·모드 정보는 추정하지 않으며, 새 `battle` 범위는 모두 0에서 시작한다.

구 함수와 신 함수가 동시에 동작하거나 롤백될 수 있으므로 `achievements/current.progress`는 일시적으로 남긴다. 새 함수는 통계에서 이 사본을 투영만 한다. 통계 내부 `legacyProgress`·`legacyRevision`이 마지막 투영값을 기억하며, 구 함수가 늘린 누적 수치의 양의 차이는 다음 새 함수 트랜잭션에서 한 번만 흡수한다. 최고 연승·앨범 수는 최댓값으로 합치고 현재 연승은 구 문서의 값을 반영한다. 통계·사본·워터마크는 항상 같은 트랜잭션으로 저장한다.

구 함수가 처리한 전투의 패배·모드·순서를 복원할 수는 없다. 혼합 배포/롤백 기간에 구 revision 변경을 발견하면 새 `battle` 현재 연승은 안전하게 끊고 최고 기록은 보존한다. 구 함수가 팩 개봉이나 업적 수령만 처리했어도 같은 보수적 단절이 생길 수 있다. 이 기간의 구 함수 전투는 `battle` 승률에 포함하지 않으며 lifetime 기록은 보존한다. 전체 집계 함수를 신 버전으로 배포한 뒤부터 새 전적은 연속으로 기록된다.

롤백 시 구 함수가 읽을 업적 사본과 수령 마커를 삭제하지 않는다. 재배포하면 워터마크 이후 증가분을 다시 흡수한다. 호환 사본 삭제와 과거 전투 재구축은 이번 구현 범위에 없다.

## 반영과 검증

서버 반영 상태는 아래 배포 기록을 따른다. 통계 관련 함수는 `getPlayerStatistics`, `getAchievements`, `claimAchievement`, `submitMatchResult`, `openPack`, 위 보상 팩 함수 6개, `getOnboardingOperation`이며, 제작 기능을 배포할 때에는 `craftCard`의 통계 연동도 함께 반영한다. Firestore 규칙 및 통계 UI를 포함한 클라이언트도 반영해야 한다. 새 스펙 테이블 업로드는 없다.

회귀 검증:

- `node --test scripts/test-player-statistics.js scripts/test-achievements.js`: 순수 집계, 승패·무승부 분모, 모드별 연승, 이관·롤백·투영과 DTO 버전.
- 로컬 Firestore 에뮬레이터의 `test-achievements-emulator.js`: 실제 정산·조회·수령·팩 트랜잭션, 동시 구/신 writer, 중복 요청·원자성·계정 격리, 랭크 AI 분류.
- 같은 에뮬레이터의 `test-card-crafting-emulator.js`: 제작·팩 경합, 앨범 집계와 재화 원자성.
- `test-account-progress-rules.js`: 기존 저장 규칙과 통계 직접 접근 금지.
- Unity 런타임·에디터 C# 컴파일, 실제 통계 DTO/매니저 코드 검사 28항목과 실제 `PlayerStatisticsCommands`·UniTask 비동기 검사 13항목: 캐시·중복 조회 병합·늦은 응답·계정 전환·실패 시 기록 유지.

에뮬레이터 테스트는 localhost endpoint와 `demo-*` 프로젝트만 허용한다. 서버 재생 서비스 결과/스펙 읽기는 테스트에서 고정하며 Firestore 트랜잭션과 callable 본문은 실제 코드를 실행한다. 실제 기기 플레이와 원격 배포 검증은 별도다.

2026-09-21 로컬 검증 결과: 서버 순수 검사 17개, Firestore 에뮬레이터 검사 32개, 보안 규칙 검사 10개 통과. TypeScript 컴파일과 lint도 통과했다. 클라이언트는 Unity 런타임·에디터 C# 컴파일, 실제 통계/업적 매니저·DTO를 사용한 검사 28개, 실제 통계 조회 코드와 UniTask를 사용한 비동기 검사 13개를 통과했다. 비동기 검사는 중복 조회 병합, 캐시, 응답 역전, 계정 전환, 실패 후 재시도를 포함한다. UI 프리팹 참조와 필드 배선은 구조 검사했으며 실제 화면 렌더링·기기 플레이는 확인하지 않았다.

## 서버 배포 완료 (2026-09-21)

전적 조회의 `NOT FOUND` 원인은 `bm-cardbattle` / `asia-northeast3`에 `getPlayerStatistics`가 배포되지 않은 것이었다. 운영 서버 소스를 기준으로 통계 변경만 반영한 별도 배포본을 만들고 위 통계 관련 함수 12개와 `cardbattle` Firestore 규칙을 배포했다. 제작 기능·재화 변경은 포함하지 않았다.

배포본 lint·TypeScript 빌드·테이블 세대 일치 검사·통계 및 업적 순수 검사 17개 통과. 배포 후 12개 함수의 ACTIVE 상태와 소스 세대 변경을 확인했다. 앱과 같은 callable 주소로 `getPlayerStatistics`와 `getAchievements`에 인증 없는 요청을 보내 404 대신 정상 인증 거절(401/UNAUTHENTICATED)을 확인했다. 로그인한 실제 계정의 UI 조회는 별도 확인 대상이다.

증거는 `Build/PlayerStatisticsDeployment-20260921-184152/verification.json`과 `source-changes.json`에 보관한다. 이번 배포에는 테이블 업로드·Hosting 리소스 배포·APK 빌드가 포함되지 않았다.
